using KitWmsIntegrador.Core.Agrupamento;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Orquestrador;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Ponta a ponta: 6 XMLs de 2 transportes + 1 XML inválido, Orquestrador real sobre o
/// FakeClienteWms, relatório real e e-mail gravador. Sem rede.
/// </summary>
public sealed class PacoteEndToEndTests : IDisposable
{
    private readonly AmbientePacote _amb = new();

    public void Dispose() => _amb.Dispose();

    private static RespostaWms Resp(string sucesso, string mensagem) => new()
    {
        TipoEDI = "I50",
        Sucesso = sucesso,
        Retornos = new[] { new RetornoWms { Sucesso = sucesso, Mensagem = mensagem } },
    };

    private static RespostaWms Sucesso() => Resp("S", "Registrado com sucesso.");

    private static RespostaWms Produto() =>
        Resp("N", "Produto Cod\\EAN: \"175886\" da empresa \"3509308\" não ativo ou faltando ou não cadastrado!");

    private static RespostaWms Pendencia() => Resp("N", "Foi importado e esta pendente na interface.");

    private static RespostaWms JaExiste() => Resp("N", "Registro já existe no cadastro.");

    private static Orquestrador CriarOrquestradorReal(FakeClienteWms cliente)
    {
        var op = Options.Create(new OpcoesOperacionais
        {
            Deposito = "JD",
            CnpjArmazem = "14.605.065/0001-75",
            CnpjDepositante = "01.838.723/0098-50",
            CodigoDepositante = "3509308",
        });
        return new Orquestrador(
            cliente,
            new ConstrutorI50(op, Options.Create(new OpcoesI50())),
            new ConstrutorI10(op),
            new ConstrutorI30(op),
            new ConstrutorI60(op),
            new ClassificadorRetorno(),
            Options.Create(new OpcoesI60 { Habilitada = false }),
            new FakeClock(DateTimeOffset.UtcNow),
            NullLogger<Orquestrador>.Instance);
    }

    [Fact]
    public async Task DoisTransportes_FechaPacoteComRelatorioEEmail()
    {
        // Ordem de chegada (criação): a1(T1) b1(T2) a2(T1) b2(T2) a3(T1) b3(T2) + invalido.
        // Ondas: T1 (a1,a2,a3) e depois T2 (b1,b2,b3), contíguas, uma nota por vez.
        // Respostas de I50 na ordem de envio:
        //  a1 -> Sucesso
        //  a2 -> Produto (1ª tentativa; I30 ok) -> Sucesso (2ª)  => integrada, revisar produto (Processados)
        //  a3 -> Pendencia                                       => Processados
        //  b1 -> JaExiste                                        => Processados (idempotência)
        //  b2 -> Produto x3 (persistente)                        => Erro\ProdutoNaoCadastrado
        //  b3 -> Sucesso
        var cliente = new FakeClienteWms()
            .Responder(InterfaceWms.I50,
                Sucesso(),
                Produto(), Sucesso(),
                Pendencia(),
                JaExiste(),
                Produto(), Produto(), Produto(),
                Sucesso())
            .Responder(InterfaceWms.I30, JaExiste());

        var nomes = new[]
        {
            ("a1.xml", "1001", "70001"), ("b1.xml", "2001", "80002"), ("a2.xml", "1002", "70001"),
            ("b2.xml", "2002", "80002"), ("a3.xml", "1003", "70001"), ("b3.xml", "2003", "80002"),
        };
        var baseTempo = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < nomes.Length; i++)
        {
            var caminho = _amb.EscreverXml(nomes[i].Item1, nomes[i].Item2, nomes[i].Item3);
            File.SetCreationTimeUtc(caminho, baseTempo.AddMinutes(i));
        }

        File.WriteAllText(_amb.Caminho("quebrado.xml"), "<nfeProc><NFe>");
        File.SetCreationTimeUtc(_amb.Caminho("quebrado.xml"), baseTempo.AddMinutes(10));

        var gerenciador = _amb.CriarGerenciador(CriarOrquestradorReal(cliente));
        await _amb.RodarAteOciosoAsync(gerenciador);

        // Estado e entrada.
        Assert.Equal(EstadoPacote.Ocioso, gerenciador.Estado);
        Assert.Empty(Directory.GetFiles(_amb.Entrada, "*.xml", SearchOption.TopDirectoryOnly));

        // Pastas.
        var processados = _amb.Arquivos("Processados").Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "a1.xml", "a2.xml", "a3.xml", "b1.xml", "b3.xml" }, processados);
        Assert.Equal(new[] { "b2.xml" },
            _amb.ArquivosErroCategoria("ProdutoNaoCadastrado").Select(Path.GetFileName).ToArray());
        Assert.Equal(new[] { "quebrado.xml" },
            _amb.ArquivosErroCategoria("XmlInvalido").Select(Path.GetFileName).ToArray());

        // Envios contíguos por transporte: I50 de a1..a3 antes de qualquer I50 de b*.
        // a1(1) a2(2 tentativas) a3(1) = 4 envios de T1; depois 5 de T2 (b1:1, b2:3, b3:1).
        var i50 = cliente.Envios.Where(e => e.Identificador == InterfaceWms.I50).ToList();
        Assert.Equal(9, i50.Count);
        var json = i50.Select(e => System.Text.Json.JsonSerializer.Serialize(e.Payload)).ToList();
        var transportes = json.Select(j => j.Contains("70001") ? "70001" : j.Contains("80002") ? "80002" : "?").ToArray();
        Assert.Equal(new[] { "70001", "70001", "70001", "70001", "80002", "80002", "80002", "80002", "80002" }, transportes);

        // Subpasta de pacote em Processados cujo nome bate com o nome-base do relatório (mesmo id).
        var subProcessados = Directory.GetDirectories(Path.Combine(_amb.Entrada, "Processados"));
        var subPacote = Assert.Single(subProcessados);
        var relatorioHtml = Assert.Single(_amb.RelatoriosGerados(".html"));
        Assert.Equal(
            Path.GetFileNameWithoutExtension(relatorioHtml),
            Path.GetFileName(subPacote));

        // Relatório único (HTML + PDF) e e-mail único com PDF anexo.
        Assert.Single(_amb.RelatoriosGerados(".html"));
        Assert.Single(_amb.RelatoriosGerados(".pdf"));
        var email = Assert.Single(_amb.Email.Enviadas);
        var anexo = Assert.Single(email.Anexos);
        Assert.EndsWith(".pdf", anexo.Nome);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(anexo.Conteudo, 0, 4));
        Assert.Contains("fechamento do pacote", email.CorpoHtml);
        Assert.Contains(">7</div>", email.CorpoHtml); // cartão Total
        Assert.Contains("XMLs inválidos", email.CorpoHtml);
        Assert.Contains("Transportes", email.CorpoHtml);

        // Limpeza posterior com relógio +61 dias remove tudo de Processados/Erro.
        _amb.Clock.Avancar(TimeSpan.FromDays(61));
        var limpador = new LimpadorPastas(
            _amb.Clock, Options.Create(_amb.Pastas), Options.Create(new OpcoesLimpeza()),
            NullLogger<LimpadorPastas>.Instance);
        var limpeza = limpador.Limpar();

        Assert.Empty(limpeza.Erros);
        Assert.Empty(_amb.Arquivos("Processados"));
        Assert.Empty(_amb.Arquivos("Erro"));
    }
}
