using System.Net;
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
/// Falha do GetToken (401) nunca manda nota para Erro: o pacote é interrompido, os arquivos ficam na entrada,
/// o cooldown global impede novas chamadas e, na recuperação, o pacote é processado inteiro. Orquestrador e
/// cliente HTTP reais sobre um handler falso (sem rede).
/// </summary>
public sealed class Autenticacao401PacoteTests : IDisposable
{
    private const string JsonSucesso =
        "{\"transacao\":\"1\",\"tipoEDI\":\"I50\",\"sucesso\":\"S\"," +
        "\"retornos\":[{\"documento\":\"D\",\"idDocumento\":\"1\",\"sucesso\":\"S\",\"mensagem\":\"Registrado com sucesso.\"}]}";

    private readonly AmbientePacote _amb = new();
    private readonly AlertaAutenticacaoFalso _alerta = new();
    private readonly OpcoesWms _opcoesWms = new()
    {
        BaseUrl = "https://wms.exemplo.test/wmsapi/api",
        Requisitante = "01.838.723/0098-50",
        Destino = "14.605.065/0001-75",
        Usuario = "USUARIO-TESTE",
        Senha = "senha-secreta-de-teste",
        TokenTentativasImediatas = 1,
        CooldownTokenSegundos = "60;120;300;600;900",
    };

    private FakeHttpMessageHandler _handler = null!;
    private GuardaAutenticacaoWms _guarda = null!;

    // Comportamento programável do WMS falso.
    private Func<int, bool> _getTokenFalha = _ => true;       // índice (1..n) do GetToken -> falha?
    private Func<int, bool> _post401 = _ => false;            // índice (1..n) do POST -> 401?
    private int _getTokens;
    private int _posts;

    public void Dispose() => _amb.Dispose();

    private GerenciadorPacote Montar()
    {
        _handler = new FakeHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("Integracao/GetToken", StringComparison.OrdinalIgnoreCase))
            {
                _getTokens++;
                return _getTokenFalha(_getTokens)
                    ? FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}")
                    : FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                        $"{{\"token\":\"{JwtFake.ComExpiracao(_amb.Clock.UtcNow.AddHours(1))}\"}}");
            }

            _posts++;
            return _post401(_posts)
                ? FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}")
                : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso);
        });

        var http = new HttpClient(_handler) { BaseAddress = new Uri(_opcoesWms.BaseUrl + "/") };
        _guarda = new GuardaAutenticacaoWms(_amb.Clock, Options.Create(_opcoesWms));
        var cliente = new ClienteWmsHttp(
            http, Options.Create(_opcoesWms), NullLogger<ClienteWmsHttp>.Instance, _amb.Clock, _guarda, _alerta);

        var op = Options.Create(new OpcoesOperacionais
        {
            Deposito = "JD",
            CnpjArmazem = "14.605.065/0001-75",
            CnpjDepositante = "01.838.723/0098-50",
            CodigoDepositante = "3509308",
        });
        var orquestrador = new Orquestrador(
            cliente,
            new ConstrutorI50(op, Options.Create(new OpcoesI50())),
            new ConstrutorI10(op),
            new ConstrutorI30(op),
            new ConstrutorI60(op),
            new ClassificadorRetorno(),
            Options.Create(new OpcoesI60 { Habilitada = false }),
            _amb.Clock,
            NullLogger<Orquestrador>.Instance);

        _amb.Autenticacao = _guarda;
        return _amb.CriarGerenciador(orquestrador);
    }

    private string[] EscreverNotas(int quantidade)
    {
        var baseTempo = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var nomes = new List<string>();
        for (var i = 0; i < quantidade; i++)
        {
            var nome = $"n{i + 1}.xml";
            var caminho = _amb.EscreverXml(nome, (1000 + i).ToString(), i % 2 == 0 ? "70001" : "80002");
            File.SetCreationTimeUtc(caminho, baseTempo.AddMinutes(i));
            nomes.Add(nome);
        }

        return nomes.ToArray();
    }

    private string[] NaEntrada() =>
        Directory.GetFiles(_amb.Entrada, "*.xml", SearchOption.TopDirectoryOnly).Select(Path.GetFileName).OrderBy(n => n).ToArray()!;

    [Fact]
    public async Task Tempestade401_NenhumaNotaVaiParaErro_ArquivosFicamNaEntrada_UmUnicoGetTokenNoCooldown()
    {
        var nomes = EscreverNotas(6);
        var g = Montar();

        await _amb.RodarAteOciosoAsync(g);

        // Nada foi tratado: nem Erro nem Processados; todos os XMLs continuam na entrada.
        Assert.Empty(_amb.Arquivos("Erro"));
        Assert.Empty(_amb.Arquivos("Processados"));
        Assert.Equal(nomes.OrderBy(n => n).ToArray(), NaEntrada());
        Assert.Equal(1, _getTokens);
        Assert.Equal(0, _posts);
        Assert.Equal(1, _alerta.Indisponiveis);
        Assert.Empty(_amb.RelatoriosGerados(".html"));

        // Ciclos seguintes dentro do cooldown (60 s): não abrem pacote e não chamam o WMS.
        for (var i = 0; i < 4; i++)
        {
            _amb.Clock.Avancar(TimeSpan.FromSeconds(10));
            await g.CicloAsync(CancellationToken.None);
            Assert.Equal(EstadoPacote.Ocioso, g.Estado);
        }

        Assert.True(g.AguardandoAutenticacao);
        Assert.NotNull(g.ProximaTentativaAutenticacaoUtc);
        Assert.Equal(1, _getTokens);
        Assert.Equal(0, _posts);
        Assert.Empty(_amb.Arquivos("Erro"));
        Assert.Equal(nomes.OrderBy(n => n).ToArray(), NaEntrada());
    }

    [Fact]
    public async Task Recuperacao_ProcessaOPacoteInteiro_ComUmAlertaDeIndisponibilidadeEUmDeRecuperacao()
    {
        var nomes = EscreverNotas(6);
        _getTokenFalha = n => n == 1; // só o 1º GetToken falha
        var g = Montar();

        await _amb.RodarAteOciosoAsync(g);
        Assert.Equal(nomes.OrderBy(n => n).ToArray(), NaEntrada());

        _amb.Clock.Avancar(TimeSpan.FromSeconds(70)); // passa do cooldown de 60 s
        await _amb.RodarAteOciosoAsync(g);

        Assert.Empty(NaEntrada());
        Assert.Equal(nomes.OrderBy(n => n).ToArray(),
            _amb.Arquivos("Processados").Select(Path.GetFileName).OrderBy(n => n).ToArray());
        Assert.Empty(_amb.Arquivos("Erro"));
        Assert.Equal(2, _getTokens);
        Assert.Equal(6, _posts);
        Assert.Equal(1, _alerta.Indisponiveis);
        Assert.Equal(1, _alerta.Recuperacoes);
        Assert.False(g.AguardandoAutenticacao);
        Assert.Single(_amb.RelatoriosGerados(".html"));
    }

    [Fact]
    public async Task Falha401NoMeioDoPacote_NotasIntegradasFicamEmProcessados_RestantesNaEntrada_ERelatorioMostraOContador()
    {
        var nomes = EscreverNotas(5);
        // 1º GetToken ok; o 3º POST volta 401 e a renovação (2º GetToken) também falha.
        _getTokenFalha = n => n >= 2;
        _post401 = n => n >= 3;
        var g = Montar();

        await _amb.RodarAteOciosoAsync(g);

        var processados = _amb.Arquivos("Processados").Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.Equal(2, processados.Length);
        Assert.Empty(_amb.Arquivos("Erro"));
        Assert.Equal(nomes.Except(processados!).OrderBy(n => n).ToArray(), NaEntrada());
        Assert.Equal(2, _getTokens);
        Assert.Equal(1, _alerta.Indisponiveis);

        // Pacote fechado com as 2 notas tratadas: relatório e e-mail trazem o contador e o aviso de interrupção.
        var html = File.ReadAllText(Assert.Single(_amb.RelatoriosGerados(".html")));
        Assert.Contains("Falhas de autenticação (GetToken):", html);
        Assert.Contains("pacote interrompido", html);
        var corpo = Assert.Single(_amb.Email.Enviadas).CorpoHtml;
        Assert.Contains("Falhas de autenticação (GetToken):", corpo);
        Assert.Contains("Pacote interrompido", corpo);
    }

    [Fact]
    public async Task Post401ComGetTokenOk_RenovaOTokenUmaVez_ENotaIntegra()
    {
        EscreverNotas(1);
        _getTokenFalha = _ => false;
        _post401 = n => n == 1;
        var g = Montar();

        await _amb.RodarAteOciosoAsync(g);

        Assert.Single(_amb.Arquivos("Processados"));
        Assert.Empty(_amb.Arquivos("Erro"));
        Assert.Equal(2, _getTokens);
        Assert.Equal(2, _posts);
        Assert.Equal(0, _alerta.Indisponiveis);
        // Sem falha de autenticação o relatório não ganha a linha nova.
        Assert.DoesNotContain("GetToken", File.ReadAllText(Assert.Single(_amb.RelatoriosGerados(".html"))));
        Assert.DoesNotContain("GetToken", Assert.Single(_amb.Email.Enviadas).CorpoHtml);
    }

    [Fact]
    public async Task ZipComFalhaDeAutenticacao_NaoEMovidoDaEntrada_ENadaVaiParaErro()
    {
        var zip = _amb.EscreverZipBrf("Puxada", "lote.zip", 5000, "1000000001", "1000000002");
        var g = Montar();

        await _amb.RodarAteOciosoAsync(g);

        Assert.True(File.Exists(zip));
        Assert.Empty(_amb.Arquivos("Erro"));
        Assert.Empty(_amb.Arquivos("Processados"));
        Assert.Equal(0, _posts);

        // Recuperado: o ZIP é processado e sai da entrada.
        _getTokenFalha = _ => false;
        _amb.Clock.Avancar(TimeSpan.FromSeconds(70));
        await _amb.RodarAteOciosoAsync(g);

        Assert.False(File.Exists(zip));
        Assert.Equal(2, _amb.Arquivos("Processados").Length);
        Assert.Empty(_amb.Arquivos("Erro"));
    }

    [Fact]
    public async Task SidecarDeErro_RegistraAOrigemDoArquivo()
    {
        // Nota que vai para Erro por motivo da própria nota (XML inválido) grava a origem para o reprocesso.
        File.WriteAllText(_amb.Caminho("quebrado.xml"), "<nfeProc><NFe>");
        _getTokenFalha = _ => false;
        var g = Montar();

        await _amb.RodarAteOciosoAsync(g);

        var sidecar = Directory.GetFiles(Path.Combine(_amb.Entrada, "Erro"), "*.motivo.txt", SearchOption.AllDirectories).Single();
        Assert.Contains("Origem: raiz", File.ReadAllText(sidecar));
    }
}
