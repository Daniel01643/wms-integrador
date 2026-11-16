using System.Net;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Tests.Infra;
using KitWmsIntegrador.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Token por envio (orientação da Assist, 16/11/2026): um GetToken para cada POST, nova tentativa em conexão nova sem o
/// cookie de afinidade, teto de GetToken por minuto, diagnóstico por servidor e CLI --testar-token --vezes.
/// </summary>
public sealed class TokenPorEnvioTests
{
    private const string Usuario = "USUARIO-TESTE";
    private const string Senha = "senha-secreta-de-teste";

    private const string JsonSucesso =
        "{\"transacao\":\"1\",\"tipoEDI\":\"I50\",\"sucesso\":\"S\"," +
        "\"retornos\":[{\"documento\":\"D\",\"idDocumento\":\"1\",\"sucesso\":\"S\",\"mensagem\":\"Registrado com sucesso.\"}]}";

    private static readonly DateTimeOffset Inicio = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly ITestOutputHelper _saida;

    public TokenPorEnvioTests(ITestOutputHelper saida) => _saida = saida;

    private static OpcoesWms Opcoes(bool porEnvio = true, int tentativas = 3, int maxPorMinuto = 0) => new()
    {
        BaseUrl = "https://wms.exemplo.test/wmsapi/api",
        Requisitante = "01.838.723/0098-50",
        Destino = "14.605.065/0001-75",
        Usuario = Usuario,
        Senha = Senha,
        TimeoutSegundos = 30,
        MaxTentativas = 3,
        TokenPorEnvio = porEnvio,
        TokenTentativasImediatas = tentativas,
        MaxGetTokenPorMinuto = maxPorMinuto,
        CooldownTokenSegundos = "60;120;300;600;900",
    };

    private sealed class Cenario
    {
        public required ClienteWmsHttp Cliente { get; init; }

        public required FakeHttpMessageHandler Handler { get; init; }

        public required FakeClock Clock { get; init; }

        public required AlertaAutenticacaoFalso Alerta { get; init; }

        public required GuardaAutenticacaoWms Guarda { get; init; }

        public int[] Fabrica { get; init; } = new int[1];

        public int FabricaChamadas => Fabrica[0];

        public int GetTokens => Handler.Requisicoes.Count(r => r.Metodo == HttpMethod.Get);

        public int Posts => Handler.Requisicoes.Count(r => r.Metodo == HttpMethod.Post);
    }

    private static bool EhGetToken(HttpRequestMessage req) =>
        req.RequestUri!.AbsolutePath.EndsWith("Integracao/GetToken", StringComparison.OrdinalIgnoreCase);

    private static string JwtN(int n) => JwtFake.ComExpiracao(Inicio.AddHours(1).AddSeconds(n));

    private static Cenario Criar(
        Func<HttpRequestMessage, int, HttpResponseMessage> responder,
        OpcoesWms? opcoes = null,
        ILogger<ClienteWmsHttp>? logger = null,
        bool comFabrica = false)
    {
        opcoes ??= Opcoes();
        var clock = new FakeClock(Inicio);
        var handler = new FakeHttpMessageHandler(responder);
        var http = new HttpClient(handler) { BaseAddress = new Uri(opcoes.BaseUrl + "/") };
        var guarda = new GuardaAutenticacaoWms(clock, Options.Create(opcoes));
        var alerta = new AlertaAutenticacaoFalso();
        var contadorFabrica = new int[1];
        Func<HttpClient>? fabrica = comFabrica
            ? () =>
            {
                Interlocked.Increment(ref contadorFabrica[0]);
                return new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri(opcoes.BaseUrl + "/") };
            }
            : null;
        var cliente = new ClienteWmsHttp(
            http, Options.Create(opcoes), logger ?? NullLogger<ClienteWmsHttp>.Instance, clock, guarda, alerta, fabrica);
        return new Cenario { Cliente = cliente, Handler = handler, Clock = clock, Alerta = alerta, Guarda = guarda, Fabrica = contadorFabrica };
    }

    private static HttpResponseMessage Token(string jwt, string? setCookie = null)
    {
        var r = FakeHttpMessageHandler.Json(HttpStatusCode.OK, $"{{\"token\":\"{jwt}\"}}");
        if (setCookie is not null)
        {
            r.Headers.TryAddWithoutValidation("Set-Cookie", setCookie);
        }

        return r;
    }

    private static HttpResponseMessage NaoAutorizado(string? setCookie = null)
    {
        var r = FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}");
        if (setCookie is not null)
        {
            r.Headers.TryAddWithoutValidation("Set-Cookie", setCookie);
        }

        return r;
    }

    [Fact]
    public async Task TokenPorEnvioTrue_GetTokenAntesDeCadaPost_ENuncaReaproveitaToken()
    {
        var n = 0;
        var c = Criar((req, _) => EhGetToken(req)
            ? Token(JwtN(++n))
            : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso));

        for (var i = 0; i < 4; i++)
        {
            await c.Cliente.EnviarAsync(InterfaceWms.I50, "{}");
        }

        var sequencia = c.Handler.Requisicoes.Select(r => r.Metodo.Method).ToArray();
        Assert.Equal(new[] { "GET", "POST", "GET", "POST", "GET", "POST", "GET", "POST" }, sequencia);
        var tokensDosPosts = c.Handler.Requisicoes.Where(r => r.Metodo == HttpMethod.Post).Select(r => r.AuthParametro).ToArray();
        Assert.Equal(4, tokensDosPosts.Distinct().Count());
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(JwtN(i + 1), tokensDosPosts[i]); // cada POST usa o token do GetToken imediatamente anterior
        }
    }

    [Fact]
    public async Task TokenPorEnvioFalse_MantemOCacheAntigo()
    {
        var c = Criar((req, _) => EhGetToken(req)
            ? Token(JwtN(1))
            : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso), Opcoes(porEnvio: false));

        for (var i = 0; i < 4; i++)
        {
            await c.Cliente.EnviarAsync(InterfaceWms.I50, "{}");
        }

        Assert.Equal(1, c.GetTokens);
        Assert.Equal(4, c.Posts);
    }

    [Fact]
    public async Task PostBrutoI51_EI40_TambemUsamTokenPorEnvio()
    {
        var n = 0;
        var c = Criar((req, _) => EhGetToken(req)
            ? Token(JwtN(++n))
            : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso));

        await c.Cliente.PostBrutoAsync("I51", "{}");
        await c.Cliente.PostBrutoAsync("I51", "{}");
        await c.Cliente.EnviarAsync(InterfaceWms.I40, "{}");
        await c.Cliente.EnviarAsync(InterfaceWms.I60, "{}");

        Assert.Equal(4, c.GetTokens);
        Assert.Equal(4, c.Posts);
        Assert.Equal(
            Enumerable.Range(1, 4).Select(JwtN).ToArray(),
            c.Handler.Requisicoes.Where(r => r.Metodo == HttpMethod.Post).Select(r => r.AuthParametro!).ToArray());
    }

    [Fact]
    public async Task GetToken401Depois200_ComConexaoNovaESemCookie_PostSucede_SemErro()
    {
        var gets = 0;
        var cookiesDosGets = new List<string?>();
        var fechamentos = new List<bool>();
        string? cookieDoPost = null;
        var c = Criar((req, _) =>
        {
            if (EhGetToken(req))
            {
                gets++;
                cookiesDosGets.Add(req.Headers.TryGetValues("Cookie", out var v) ? string.Join(";", v) : null);
                fechamentos.Add(req.Headers.ConnectionClose == true);
                return gets == 1
                    ? NaoAutorizado("SERVERID=no-ruim; path=/")
                    : Token(JwtN(gets), "SERVERID=no-bom; path=/");
            }

            cookieDoPost = req.Headers.TryGetValues("Cookie", out var cp) ? string.Join(";", cp) : null;
            return FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso);
        }, comFabrica: true);

        var resposta = await c.Cliente.EnviarAsync(InterfaceWms.I50, "{}");

        Assert.Equal("S", resposta.Sucesso);
        Assert.Equal(2, gets);
        Assert.All(cookiesDosGets, Assert.Null); // nunca envia cookie no GetToken
        Assert.Equal(new[] { false, true }, fechamentos);   // 1ª no pool; nova tentativa com Connection: close
        Assert.Equal(1, c.FabricaChamadas);                  // e em HttpClient/handler novo
        Assert.Equal("SERVERID=no-bom", cookieDoPost);       // o POST volta ao servidor que emitiu o token bom
        Assert.Equal(1, c.Posts);
        Assert.False(c.Guarda.Bloqueada);
        Assert.Equal(0, c.Alerta.Indisponiveis);
        Assert.Equal(1, c.Guarda.FalhasPorBackend.Values.Sum());
    }

    [Fact]
    public async Task CookieDeAfinidade_NaoVazaEntreEnvios()
    {
        var cookiesDosPosts = new List<string?>();
        var n = 0;
        var c = Criar((req, _) =>
        {
            if (EhGetToken(req))
            {
                n++;
                Assert.False(req.Headers.Contains("Cookie"));
                return Token(JwtN(n), n == 1 ? "SERVERID=A; path=/" : "SERVERID=B; path=/");
            }

            cookiesDosPosts.Add(req.Headers.TryGetValues("Cookie", out var v) ? string.Join(";", v) : null);
            return FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso);
        });

        await c.Cliente.EnviarAsync(InterfaceWms.I50, "{}");
        await c.Cliente.EnviarAsync(InterfaceWms.I50, "{}");

        Assert.Equal(new string?[] { "SERVERID=A", "SERVERID=B" }, cookiesDosPosts);
    }

    [Fact]
    public async Task TresGetToken401Seguidos_CooldownEExcecao_NotasFicam()
    {
        var c = Criar((req, _) => EhGetToken(req)
            ? NaoAutorizado("SERVERID=ruim; path=/")
            : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso));

        var ex = await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(
            () => c.Cliente.EnviarAsync(InterfaceWms.I50, "{}"));

        Assert.Equal(401, ex.Status);
        Assert.False(ex.EmCooldown);
        Assert.Equal(3, c.GetTokens);
        Assert.Equal(0, c.Posts);
        Assert.True(c.Guarda.Bloqueada);
        Assert.Equal(1, c.Alerta.Indisponiveis);
        Assert.Equal(3, c.Guarda.FalhasPorBackend.Values.Sum());

        // Em cooldown, nenhuma chamada ao WMS.
        await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(() => c.Cliente.EnviarAsync(InterfaceWms.I50, "{}"));
        Assert.Equal(3, c.GetTokens);
    }

    [Fact]
    public async Task EnviosParalelosComFalha_NaoEscalamAEscadaDoCooldownVariasVezes()
    {
        var c = Criar((req, _) => NaoAutorizado(), Opcoes(tentativas: 1));

        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(() => c.Cliente.EnviarAsync(InterfaceWms.I50, "{}"))));

        Assert.True(c.Guarda.FalhasConsecutivas >= 1);
        Assert.Equal(1, c.Alerta.Indisponiveis);
        Assert.True(c.Guarda.FalhasConsecutivas <= 2);
    }

    [Fact]
    public async Task Post401_ComTokenPorEnvio_PedeOutroTokenERepeteUmaVez()
    {
        var gets = 0;
        var posts = 0;
        var c = Criar((req, _) =>
        {
            if (EhGetToken(req))
            {
                return Token(JwtN(++gets));
            }

            return ++posts == 1 ? NaoAutorizado() : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso);
        });

        var r = await c.Cliente.EnviarAsync(InterfaceWms.I50, "{}");

        Assert.Equal("S", r.Sucesso);
        Assert.Equal(2, gets);
        Assert.Equal(2, posts);
        Assert.False(c.Guarda.Bloqueada);
    }

    [Fact]
    public void TetoDeGetTokenPorMinuto_ReservaAteOLimite_EDepoisPedeEspera_LiberaAposAJanela()
    {
        var clock = new FakeClock(Inicio);
        var guarda = new GuardaAutenticacaoWms(clock, Options.Create(Opcoes(maxPorMinuto: 2)));

        Assert.Equal(TimeSpan.Zero, guarda.ReservarGetToken());
        clock.Avancar(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.Zero, guarda.ReservarGetToken());
        Assert.Equal(TimeSpan.FromSeconds(50), guarda.ReservarGetToken()); // a 1ª sai da janela em 50 s

        clock.Avancar(TimeSpan.FromSeconds(50));
        Assert.Equal(TimeSpan.Zero, guarda.ReservarGetToken());

        var semTeto = new GuardaAutenticacaoWms(clock, Options.Create(Opcoes(maxPorMinuto: 0)));
        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal(TimeSpan.Zero, semTeto.ReservarGetToken());
        }
    }

    [Fact]
    public async Task TetoAtingido_ClienteEspera_LogaOLimite_ENaoChamaOWms()
    {
        var logger = new LoggerCaptura<ClienteWmsHttp>();
        var c = Criar((req, _) => Token(JwtN(1)), Opcoes(maxPorMinuto: 1), logger);
        Assert.Equal(TimeSpan.Zero, c.Guarda.ReservarGetToken()); // esgota o teto

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => c.Cliente.EnviarAsync(InterfaceWms.I50, "{}", cts.Token));

        Assert.Equal(0, c.GetTokens);
        Assert.Contains(logger.Mensagens, m => m.Contains("limitado") && m.Contains("MaxGetTokenPorMinuto"));
    }

    [Fact]
    public async Task Diagnostico_TemServidorStatusELatencia_SemSegredos()
    {
        var jwt = JwtN(7);
        var logger = new LoggerCaptura<ClienteWmsHttp>();
        var gets = 0;
        var c = Criar((req, _) =>
        {
            if (!EhGetToken(req))
            {
                return FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso);
            }

            var r = ++gets == 1 ? NaoAutorizado("SERVERID=valor-do-cookie-secreto; path=/") : Token(jwt, "SERVERID=outro-valor-secreto; path=/");
            r.Headers.TryAddWithoutValidation("Server", "Microsoft-IIS/10.0");
            r.Headers.TryAddWithoutValidation("X-Powered-By", "ASP.NET");
            return r;
        }, logger: logger);

        await c.Cliente.EnviarAsync(InterfaceWms.I50, "{}");

        var todo = string.Join("\n", logger.Mensagens);
        var hash = IdentificadorBackendWms.HashCurto("valor-do-cookie-secreto");
        Assert.Contains("status=401", todo);
        Assert.Contains("SERVERID#" + hash, todo);
        Assert.Contains("server=Microsoft-IIS/10.0", todo);
        Assert.Contains("X-Powered-By=ASP.NET", todo);
        Assert.Contains("latenciaMs=", todo);
        Assert.DoesNotContain("valor-do-cookie-secreto", todo);
        Assert.DoesNotContain("outro-valor-secreto", todo);
        Assert.DoesNotContain(Senha, todo);
        Assert.DoesNotContain(Usuario, todo);
        Assert.DoesNotContain(jwt, todo);
        Assert.DoesNotContain(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Usuario + ":" + Senha)), todo);
        Assert.Equal(1, c.Guarda.FalhasPorBackend["SERVERID#" + hash]);
    }

    [Fact]
    public async Task RelatorioDoPacote_ContaFalhasPorServidor()
    {
        var c = Criar((req, _) => NaoAutorizado("SERVERID=no-ruim; path=/"), Opcoes(tentativas: 2));
        await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(() => c.Cliente.EnviarAsync(InterfaceWms.I50, "{}"));

        var dados = new KitWmsIntegrador.Core.Relatorio.DadosPacote("pacote_x", null, 1, true, c.Guarda.FalhasPorBackend);
        var texto = KitWmsIntegrador.Core.Relatorio.IdentidadeVisual.TextoFalhasPorBackend(dados);

        Assert.Contains("SERVERID#" + IdentificadorBackendWms.HashCurto("no-ruim") + "=2", texto);
        Assert.DoesNotContain("no-ruim;", texto);
    }

    [Fact]
    public async Task Desempenho_TokenPorEnvioFazUmGetTokenPorPost_CustoEmRequisicoes()
    {
        const int notas = 200;
        var porEnvio = Criar((req, _) => EhGetToken(req) ? Token(JwtN(1)) : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso));
        var cache = Criar((req, _) => EhGetToken(req) ? Token(JwtN(1)) : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso),
            Opcoes(porEnvio: false));

        var t1 = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < notas; i++)
        {
            await porEnvio.Cliente.EnviarAsync(InterfaceWms.I50, "{}");
        }

        t1.Stop();
        var t2 = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < notas; i++)
        {
            await cache.Cliente.EnviarAsync(InterfaceWms.I50, "{}");
        }

        t2.Stop();

        _saida.WriteLine($"{notas} notas, fake handler: token por envio {t1.ElapsedMilliseconds} ms ({porEnvio.Handler.Requisicoes.Count} req), " +
                         $"cache {t2.ElapsedMilliseconds} ms ({cache.Handler.Requisicoes.Count} req)");
        Assert.Equal(notas, porEnvio.GetTokens);
        Assert.Equal(1, cache.GetTokens);
        Assert.Equal(notas * 2, porEnvio.Handler.Requisicoes.Count);
    }

    // ---- CLI --testar-token --vezes ----

    [Theory]
    [InlineData("", 1)]
    [InlineData("--vezes 5", 5)]
    [InlineData("--vezes=7", 7)]
    [InlineData("--vezes 0", 1)]
    [InlineData("--vezes 99", 20)]
    [InlineData("--vezes abc", 1)]
    public void Cli_Vezes_PadraoUm_Maximo20(string args, int esperado)
    {
        var lista = new[] { TestarTokenWms.Argumento }.Concat(args.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToArray();
        Assert.Equal(esperado, TestarTokenWms.LerVezes(lista));
    }

    [Fact]
    public async Task Cli_Vezes_ImprimeUmaLinhaPorTentativa_ComServidorEHostPorta_SemSegredos()
    {
        var anterior = TestarTokenWms.IntervaloEntreTentativas;
        TestarTokenWms.IntervaloEntreTentativas = TimeSpan.Zero;
        try
        {
            var n = 0;
            var handler = new FakeHttpMessageHandler((_, _) => ++n == 2
                ? NaoAutorizado("SERVERID=no-ruim; path=/")
                : Token(JwtN(n), "SERVERID=no-bom; path=/"));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Wms:BaseUrl"] = "https://wms.exemplo.test/wmsapi/api",
                ["Wms:Usuario"] = Usuario,
                ["Wms:Senha"] = Senha,
            }).Build();
            var saida = new StringWriter();

            var codigo = await TestarTokenWms.ExecutarAsync(new[] { TestarTokenWms.Argumento, "--vezes", "3" }, saida, config, handler);

            var linhas = saida.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Equal(3, linhas.Length);
            Assert.StartsWith("[1/3] GetToken: HTTP 200 OK (wms.exemplo.test, porta 443) servidor=SERVERID#", linhas[0]);
            Assert.StartsWith("[2/3] GetToken: HTTP 401 FALHA", linhas[1]);
            Assert.Contains("SERVERID#" + IdentificadorBackendWms.HashCurto("no-ruim"), linhas[1]);
            Assert.Contains("latenciaMs=", linhas[2]);
            Assert.Equal(0, codigo); // exit code pelo último resultado
            Assert.DoesNotContain(Senha, saida.ToString());
            Assert.DoesNotContain(Usuario, saida.ToString());
            Assert.DoesNotContain("no-ruim;", saida.ToString());
        }
        finally
        {
            TestarTokenWms.IntervaloEntreTentativas = anterior;
        }
    }
}
