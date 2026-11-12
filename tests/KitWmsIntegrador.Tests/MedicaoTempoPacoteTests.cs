using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Modelos;
using KitWmsIntegrador.Core.Orquestrador;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// A medição de tempo do pacote (Orquestrador, ProcessadorLote, GerenciadorPacote, FechadorPacote)
/// com o relógio falso: durações determinísticas, sem rede, dados sintéticos.
/// </summary>
public sealed class MedicaoTempoPacoteTests : IDisposable
{
    private readonly AmbientePacote _amb = new();

    public void Dispose() => _amb.Dispose();

    /// <summary>Gerador que repassa ao real e guarda o <see cref="DadosPacote"/> recebido.</summary>
    private sealed class GeradorCapturador : IGeradorRelatorio
    {
        private readonly IGeradorRelatorio _real;

        public GeradorCapturador(IGeradorRelatorio real) => _real = real;

        public DadosPacote? Dados { get; private set; }

        public string GerarHtml(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm, PeriodoColeta? periodo = null, DadosPacote? pacote = null)
        {
            Dados = pacote;
            return _real.GerarHtml(linhas, geradoEm, periodo, pacote);
        }

        public byte[] GerarPdf(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm, PeriodoColeta? periodo = null, DadosPacote? pacote = null) =>
            _real.GerarPdf(linhas, geradoEm, periodo, pacote);

        public (string CaminhoHtml, string CaminhoPdf) GerarArquivos(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm) =>
            _real.GerarArquivos(linhas, geradoEm);
    }

    /// <summary>Cliente WMS que avança o relógio falso a cada chamada (simula a latência da rede).</summary>
    private sealed class ClienteComLatencia : IClienteWms
    {
        private readonly FakeClienteWms _interno;
        private readonly FakeClock _clock;
        private readonly TimeSpan _latencia;

        public ClienteComLatencia(FakeClienteWms interno, FakeClock clock, TimeSpan latencia)
        {
            _interno = interno;
            _clock = clock;
            _latencia = latencia;
        }

        public Task<RespostaWms> EnviarAsync(InterfaceWms identificador, object payload, CancellationToken cancellationToken = default)
        {
            _clock.Avancar(_latencia);
            return _interno.EnviarAsync(identificador, payload, cancellationToken);
        }

        public Task<RespostaWms> EnviarAsync(InterfaceWms identificador, string jsonBruto, CancellationToken cancellationToken = default)
        {
            _clock.Avancar(_latencia);
            return _interno.EnviarAsync(identificador, jsonBruto, cancellationToken);
        }

        public ResultadoWms Classificar(RetornoWms retorno) => _interno.Classificar(retorno);
    }

    private static RespostaWms Resp(string sucesso, string msg) => new()
    {
        TipoEDI = "I50",
        Sucesso = sucesso,
        Transacao = "1",
        Retornos = new[] { new RetornoWms { Sucesso = sucesso, Mensagem = msg, IdDocumento = "9" } },
    };

    [Fact]
    public async Task Orquestrador_SomaAsChamadasAoWms()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var fake = new FakeClienteWms()
            .Responder(InterfaceWms.I50,
                Resp("N", "Destinataria - Empresa não localizada, sigla não informada."),
                Resp("N", "Produto Cod\\EAN: \"A1\" da empresa \"3509308\" não ativo ou faltando ou não cadastrado!"),
                Resp("S", "Registrado com sucesso."))
            .Responder(InterfaceWms.I10, Resp("S", "Registrado com sucesso."))
            .Responder(InterfaceWms.I30, Resp("S", "Registrado com sucesso."));
        var op = Options.Create(new OpcoesOperacionais
        {
            Deposito = "JD",
            CnpjArmazem = "14.605.065/0001-75",
            CnpjDepositante = "01.838.723/0098-50",
            CodigoDepositante = "3509308",
        });
        var orq = new Orquestrador(
            new ClienteComLatencia(fake, clock, TimeSpan.FromMilliseconds(300)),
            new ConstrutorI50(op, Options.Create(new OpcoesI50())),
            new ConstrutorI10(op),
            new ConstrutorI30(op),
            new ConstrutorI60(op),
            new ClassificadorRetorno(),
            Options.Create(new OpcoesI60 { Habilitada = false }),
            clock,
            NullLogger<Orquestrador>.Instance);
        var nota = new NotaFiscal
        {
            Numero = "1",
            Serie = "6",
            DestinatarioCnpj = "14.605.065/0001-75",
            NumeroTransporte = "30288",
            Itens = new[] { new ItemNota { CodigoProduto = "A1", Descricao = "P1", Unidade = "CX", Quantidade = 1, Cfop = "5102" } },
        };

        var r = Assert.Single(await orq.IntegrarAsync(new[] { nota }, "30288"));

        // Três I50 + uma I10 + uma I30 = 5 chamadas de 300 ms.
        Assert.Equal(5, fake.Envios.Count);
        Assert.Equal(1500, r.DuracaoWmsMs);
    }

    [Fact]
    public async Task Puxada_MedeEsperaLeituraEIntegracao()
    {
        var spy = new GeradorCapturador(new GeradorRelatorio(Options.Create(new OpcoesRelatorio { PastaSaida = _amb.Relatorios })));
        _amb.GeradorSubstituto = spy;
        var orq = new OrquestradorFalso
        {
            Comportamento = (nota, transporte) =>
            {
                _amb.Clock.Avancar(TimeSpan.FromSeconds(2));
                return Task.FromResult(OrquestradorFalso.Sucesso(nota.Numero, transporte) with { DuracaoWmsMs = 1500 });
            },
        };
        _amb.EscreverXml("a.xml", "1", "T1");
        _amb.EscreverXml("b.xml", "2", "T1");
        _amb.EscreverXml("c.xml", "3", "T1");

        var g = _amb.CriarGerenciador(orq);
        await _amb.RodarAteOciosoAsync(g);

        var m = Assert.IsType<DadosPacote>(spy.Dados).Medicao!;
        Assert.True(m.EsperaJanelaMs >= _amb.Pacote.JanelaColetaSegundos * 1000L, "espera da janela >= janela configurada");
        Assert.Equal(0, m.LeituraMs); // relógio falso não avança durante o parse
        Assert.Equal(6000, m.IntegracaoFaseMs); // 3 notas x 2 s em sequência
        Assert.Equal(4500, m.ChamadasWmsMs); // 3 notas x 1,5 s
        Assert.Empty(m.Ondas); // PUXADA não tem tempo por onda
        Assert.True(m.DuracaoTotalMs >= m.EsperaJanelaMs + m.IntegracaoFaseMs);
    }

    [Fact]
    public async Task Tsp_DuasOndas_TemTemposDistintosECorretos()
    {
        _amb.Pacote.MaxTransportesConcorrentes = 1; // sequencial: o relógio falso é compartilhado
        var spy = new GeradorCapturador(new GeradorRelatorio(Options.Create(new OpcoesRelatorio { PastaSaida = _amb.Relatorios })));
        _amb.GeradorSubstituto = spy;
        var orq = new OrquestradorFalso
        {
            Comportamento = (nota, onda) =>
            {
                _amb.Clock.Avancar(TimeSpan.FromSeconds(onda.EndsWith("-SJC", StringComparison.Ordinal) ? 2 : 3));
                return Task.FromResult(OrquestradorFalso.Sucesso(nota.Numero, onda));
            },
        };
        _amb.EscreverZipBrf("PuxadaTSP", "XML'S SJC.zip", 1001, "1000000001", "1000000002");
        File.SetCreationTimeUtc(Path.Combine(_amb.Entrada, "PuxadaTSP", "XML'S SJC.zip"), DateTime.UtcNow.AddMinutes(-10));
        _amb.EscreverZipBrf("PuxadaTSP", "XML'S SOROCABA.zip", 2001, "1000000010", "1000000011", "1000000012");

        var g = _amb.CriarGerenciador(orq);
        await _amb.RodarAteOciosoAsync(g);

        var m = Assert.IsType<DadosPacote>(spy.Dados).Medicao!;
        Assert.Equal(2, m.Ondas.Count);
        Assert.Equal(4000, m.Ondas.Single(o => o.Onda.EndsWith("-SJC", StringComparison.Ordinal)).DuracaoMs);
        Assert.Equal(9000, m.Ondas.Single(o => o.Onda.EndsWith("-SOROCABA", StringComparison.Ordinal)).DuracaoMs);
        Assert.Equal(13000, m.IntegracaoFaseMs);
    }

    [Fact]
    public async Task PacoteRetomado_EtapasDeFaseFicamSemValor()
    {
        var spy = new GeradorCapturador(new GeradorRelatorio(Options.Create(new OpcoesRelatorio { PastaSaida = _amb.Relatorios })));
        _amb.GeradorSubstituto = spy;
        var diario = new DiarioPacote(Options.Create(_amb.Pastas), NullLogger<DiarioPacote>.Instance);
        var antes = _amb.Clock.UtcNow.AddMinutes(-5);
        diario.Registrar(new RegistroDiario(
            "antigo.xml", false, "999", "T9", antes.AddSeconds(4), "Registrado com sucesso.", true, 1,
            StatusLinhaRelatorio.Integrada, "", "", Array.Empty<string>(), Array.Empty<string>(), null,
            DestinoArquivo.Processados, "pacote_antigo", "PUXADA", "Puxada", "", null, antes, 2500));

        var g = _amb.CriarGerenciador(new OrquestradorFalso());
        await _amb.RodarAteOciosoAsync(g);

        var m = Assert.IsType<DadosPacote>(spy.Dados).Medicao!;
        Assert.Null(m.EsperaJanelaMs);
        Assert.Null(m.LeituraMs);
        Assert.Equal(4000, m.IntegracaoFaseMs); // sai do diário
        Assert.Equal(2500, m.ChamadasWmsMs);
        var bloco = BlocoTempo.Montar(ModeloRelatorio.Montar(Array.Empty<LinhaRelatorioErro>()).Resumo, m)!;
        Assert.DoesNotContain(bloco.Etapas, e => e.Etapa.StartsWith("Espera", StringComparison.Ordinal));
    }
}
