using System.Net;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>Alerta falso que só conta as chamadas.</summary>
public sealed class AlertaAutenticacaoFalso : IAlertaAutenticacaoWms
{
    public int Indisponiveis { get; private set; }

    public int Recuperacoes { get; private set; }

    public Task IndisponivelAsync(int? status, DateTimeOffset proximaTentativaUtc, CancellationToken ct)
    {
        Indisponiveis++;
        return Task.CompletedTask;
    }

    public Task RecuperadoAsync(CancellationToken ct)
    {
        Recuperacoes++;
        return Task.CompletedTask;
    }
}

/// <summary>Logger que guarda as mensagens formatadas (para provar que segredos não vazam).</summary>
public sealed class LoggerCaptura<T> : ILogger<T>
{
    public List<string> Mensagens { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (Mensagens)
        {
            Mensagens.Add(formatter(state, exception) + (exception?.ToString() ?? string.Empty));
        }
    }
}

public sealed class AutenticacaoWmsTests
{
    private const string JsonSucesso =
        "{\"transacao\":\"1\",\"tipoEDI\":\"I50\",\"sucesso\":\"S\"," +
        "\"retornos\":[{\"documento\":\"D\",\"idDocumento\":\"1\",\"sucesso\":\"S\",\"mensagem\":\"Registrado com sucesso.\"}]}";

    private static readonly DateTimeOffset Inicio = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static OpcoesWms Opcoes(int tentativasImediatas = 1) => new()
    {
        BaseUrl = "https://wms.exemplo.test/wmsapi/api",
        Requisitante = "01.838.723/0098-50",
        Destino = "14.605.065/0001-75",
        Usuario = "USUARIO-TESTE",
        Senha = "senha-secreta-de-teste",
        TimeoutSegundos = 30,
        MaxTentativas = 3,
        MargemRenovacaoTokenSegundos = 60,
        TokenPorEnvio = false, // estes cenários exercitam o cache de token (TokenPorEnvio=false)
        TokenTentativasImediatas = tentativasImediatas,
        CooldownTokenSegundos = "60;120;300;600;900",
    };

    private sealed class Cenario
    {
        public required ClienteWmsHttp Cliente { get; init; }

        public required FakeHttpMessageHandler Handler { get; init; }

        public required FakeClock Clock { get; init; }

        public required AlertaAutenticacaoFalso Alerta { get; init; }

        public required GuardaAutenticacaoWms Guarda { get; init; }

        public int GetTokens => Handler.Requisicoes.Count(r => r.Metodo == HttpMethod.Get);

        public int Posts => Handler.Requisicoes.Count(r => r.Metodo == HttpMethod.Post);
    }

    private static HttpResponseMessage TokenOk(string jwt) =>
        FakeHttpMessageHandler.Json(HttpStatusCode.OK, $"{{\"token\":\"{jwt}\"}}");

    private static bool EhGetToken(HttpRequestMessage req) =>
        req.RequestUri!.AbsolutePath.EndsWith("Integracao/GetToken", StringComparison.OrdinalIgnoreCase);

    private static Cenario Criar(
        Func<HttpRequestMessage, int, HttpResponseMessage> responder,
        OpcoesWms? opcoes = null,
        ILogger<ClienteWmsHttp>? logger = null)
    {
        opcoes ??= Opcoes();
        var clock = new FakeClock(Inicio);
        var handler = new FakeHttpMessageHandler(responder);
        var http = new HttpClient(handler) { BaseAddress = new Uri(opcoes.BaseUrl + "/") };
        var guarda = new GuardaAutenticacaoWms(clock, Options.Create(opcoes));
        var alerta = new AlertaAutenticacaoFalso();
        var cliente = new ClienteWmsHttp(
            http, Options.Create(opcoes), logger ?? NullLogger<ClienteWmsHttp>.Instance, clock, guarda, alerta);
        return new Cenario { Cliente = cliente, Handler = handler, Clock = clock, Alerta = alerta, Guarda = guarda };
    }

    private static string Jwt(TimeSpan validade) => JwtFake.ComExpiracao(Inicio + validade);

    [Fact]
    public async Task TempestadeDe401_UmUnicoGetToken_EDemaisChamadasEmCooldownSemRede()
    {
        var c = Criar((req, _) => EhGetToken(req)
            ? FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}")
            : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso));

        for (var i = 0; i < 50; i++)
        {
            await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(
                () => c.Cliente.EnviarAsync(InterfaceWms.I50, "{}"));
        }

        Assert.Equal(1, c.GetTokens);   // um episódio; as outras 49 nem chamaram o WMS
        Assert.Equal(0, c.Posts);
        Assert.Equal(1, c.Alerta.Indisponiveis);
        Assert.Equal(0, c.Alerta.Recuperacoes);
        Assert.True(c.Guarda.Bloqueada);
    }

    [Fact]
    public async Task TentativasImediatas_SaoAsConfiguradasPorEpisodio()
    {
        var c = Criar((req, _) => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}"), Opcoes(tentativasImediatas: 2));

        await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(() => c.Cliente.EnviarAsync(InterfaceWms.I50, "{}"));

        Assert.Equal(2, c.GetTokens);
    }

    [Fact]
    public async Task EscadaDoCooldown_60_120_300_600_900_ComTeto_SemNovoAlerta()
    {
        var c = Criar((req, _) => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}"));
        var esperado = new[] { 60, 120, 300, 600, 900, 900, 900 };

        for (var i = 0; i < esperado.Length; i++)
        {
            await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(() => c.Cliente.EnviarAsync(InterfaceWms.I50, "{}"));
            var espera = c.Guarda.ProximaTentativaUtc!.Value - c.Clock.UtcNow;
            Assert.Equal(TimeSpan.FromSeconds(esperado[i]), espera);

            // um segundo antes do fim: continua bloqueado e sem chamada
            c.Clock.Avancar(espera - TimeSpan.FromSeconds(1));
            var antes = c.GetTokens;
            await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(() => c.Cliente.EnviarAsync(InterfaceWms.I50, "{}"));
            Assert.Equal(antes, c.GetTokens);

            c.Clock.Avancar(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(esperado.Length, c.GetTokens);
        Assert.Equal(1, c.Alerta.Indisponiveis);
    }

    [Fact]
    public async Task Recuperacao_ZeraCooldown_EAlertaDeRecuperacaoUmaVez()
    {
        var falhar = true;
        var jwt = Jwt(TimeSpan.FromHours(1));
        var c = Criar((req, _) => EhGetToken(req)
            ? (falhar ? FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}") : TokenOk(jwt))
            : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso));

        await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(() => c.Cliente.EnviarAsync(InterfaceWms.I50, "{}"));
        falhar = false;
        c.Clock.Avancar(TimeSpan.FromSeconds(61));

        var resposta = await c.Cliente.EnviarAsync(InterfaceWms.I50, "{}");
        await c.Cliente.EnviarAsync(InterfaceWms.I50, "{}");

        Assert.Equal("S", resposta.Sucesso);
        Assert.Equal(1, c.Alerta.Indisponiveis);
        Assert.Equal(1, c.Alerta.Recuperacoes);
        Assert.False(c.Guarda.Bloqueada);
        Assert.Equal(0, c.Guarda.FalhasConsecutivas);
        Assert.Equal(2, c.GetTokens);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task OutrasFalhasDoGetToken_TambemLancamAExcecaoDeAutenticacao(HttpStatusCode status)
    {
        var c = Criar((req, _) => FakeHttpMessageHandler.Json(status, "{}"));

        var ex = await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(
            () => c.Cliente.EnviarAsync(InterfaceWms.I50, "{}"));

        Assert.Equal((int)status, ex.Status);
        Assert.IsAssignableFrom<HttpRequestException>(ex);
    }

    [Fact]
    public async Task CorpoSemToken_ETimeout_ETambemFalhaDeAutenticacao()
    {
        var c = Criar((req, _) => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"outro\":1}"));
        await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(() => c.Cliente.EnviarAsync(InterfaceWms.I50, "{}"));

        var t = Criar((req, _) => throw new TaskCanceledException("timeout simulado"));
        var ex = await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(() => t.Cliente.EnviarAsync(InterfaceWms.I50, "{}"));
        Assert.Null(ex.Status);

        var r = Criar((req, _) => throw new HttpRequestException("rede simulada"));
        await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(() => r.Cliente.EnviarAsync(InterfaceWms.I50, "{}"));
    }

    [Fact]
    public async Task LogDoDiagnostico_TemStatusWwwAuthenticateCorpoERequestId_SemSegredos()
    {
        var jwtVazado = "token.vazado.exemplo";
        var logger = new LoggerCaptura<ClienteWmsHttp>();
        var c = Criar((req, _) =>
        {
            var resp = FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{\"erro\":\"" + new string('x', 500) + "\"}");
            resp.Headers.TryAddWithoutValidation("WWW-Authenticate", "Basic realm=\"wms\"");
            resp.Headers.TryAddWithoutValidation("X-Request-Id", "req-123");
            return resp;
        }, logger: logger);

        await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(() => c.Cliente.EnviarAsync(InterfaceWms.I50, "{}"));

        var todo = string.Join("\n", logger.Mensagens);
        Assert.Contains("status=401", todo);
        Assert.Contains("Basic realm=\"wms\"", todo);
        Assert.Contains("requestId=req-123", todo);
        Assert.DoesNotContain(new string('x', 300), todo); // corpo truncado
        Assert.DoesNotContain("senha-secreta-de-teste", todo);
        Assert.DoesNotContain("USUARIO-TESTE", todo);
        Assert.DoesNotContain(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("USUARIO-TESTE:senha-secreta-de-teste")), todo);
        Assert.DoesNotContain(jwtVazado, todo);
    }

    [Fact]
    public async Task LogDeSucesso_NaoContemOToken()
    {
        var jwt = Jwt(TimeSpan.FromHours(1));
        var logger = new LoggerCaptura<ClienteWmsHttp>();
        var c = Criar((req, _) => EhGetToken(req) ? TokenOk(jwt) : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso), logger: logger);

        await c.Cliente.EnviarAsync(InterfaceWms.I50, "{}");

        Assert.DoesNotContain(jwt, string.Join("\n", logger.Mensagens));
    }

    [Fact]
    public async Task Post401_ContinuaRenovandoOTokenUmaVez_SemAcionarCooldownNemAlerta()
    {
        var gets = 0;
        var posts = 0;
        var c = Criar((req, _) =>
        {
            if (EhGetToken(req))
            {
                gets++;
                return TokenOk(Jwt(TimeSpan.FromHours(gets)));
            }

            posts++;
            return posts == 1
                ? FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}")
                : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso);
        });

        var resposta = await c.Cliente.EnviarAsync(InterfaceWms.I50, "{}");

        Assert.Equal("S", resposta.Sucesso);
        Assert.Equal(2, gets);
        Assert.Equal(2, posts);
        Assert.Equal(0, c.Alerta.Indisponiveis);
        Assert.False(c.Guarda.Bloqueada);
    }

    [Fact]
    public async Task DezChamadasParalelasComTokenExpirado_FazemUmUnicoGetToken()
    {
        var c = Criar((req, _) => EhGetToken(req)
            ? TokenOk(Jwt(TimeSpan.FromHours(1)))
            : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso));

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => c.Cliente.EnviarAsync(InterfaceWms.I50, "{}")));

        Assert.Equal(1, c.GetTokens);
        Assert.Equal(10, c.Posts);
    }

    [Fact]
    public async Task RenovacaoProativaQueFalha_ComTokenAindaDentroDoExp_UsaOTokenAtualSemLancar()
    {
        var gets = 0;
        // 1º GetToken: exp em 90 s. Depois o WMS passa a recusar.
        var c = Criar((req, _) =>
        {
            if (EhGetToken(req))
            {
                gets++;
                return gets == 1 ? TokenOk(Jwt(TimeSpan.FromSeconds(90))) : FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}");
            }

            return FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso);
        });

        await c.Cliente.EnviarAsync(InterfaceWms.I50, "{}");
        c.Clock.Avancar(TimeSpan.FromSeconds(40)); // dentro da margem de 60 s, mas antes do exp

        var resposta = await c.Cliente.EnviarAsync(InterfaceWms.I50, "{}");

        Assert.Equal("S", resposta.Sucesso);
        Assert.Equal(2, gets);
        Assert.Equal(1, c.Alerta.Indisponiveis);
        Assert.True(c.Guarda.Bloqueada);

        // Em cooldown, ainda sem chamar o WMS, o token atual segue valendo até o exp.
        await c.Cliente.EnviarAsync(InterfaceWms.I50, "{}");
        Assert.Equal(2, gets);

        // Depois do exp não há mais token utilizável: falha de autenticação.
        c.Clock.Avancar(TimeSpan.FromSeconds(60));
        await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(() => c.Cliente.EnviarAsync(InterfaceWms.I50, "{}"));
    }

    [Fact]
    public async Task ValidadeMaximaDoToken_RenovaAosTresMinutos_EZeroDesligaOLimite()
    {
        var gets = 0;
        Func<HttpRequestMessage, int, HttpResponseMessage> responder = (req, _) =>
        {
            if (EhGetToken(req))
            {
                gets++;
                return TokenOk(Jwt(TimeSpan.FromHours(10)));
            }

            return FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso);
        };

        var c = Criar(responder);
        await c.Cliente.EnviarAsync(InterfaceWms.I50, "{}");
        c.Clock.Avancar(TimeSpan.FromSeconds(179));
        await c.Cliente.EnviarAsync(InterfaceWms.I50, "{}");
        Assert.Equal(1, gets);
        c.Clock.Avancar(TimeSpan.FromSeconds(1));
        await c.Cliente.EnviarAsync(InterfaceWms.I50, "{}");
        Assert.Equal(2, gets);

        gets = 0;
        var op = Opcoes();
        op.ValidadeMaximaTokenMinutos = 0;
        var z = Criar(responder, op);
        await z.Cliente.EnviarAsync(InterfaceWms.I50, "{}");
        z.Clock.Avancar(TimeSpan.FromMinutes(30));
        await z.Cliente.EnviarAsync(InterfaceWms.I50, "{}");
        Assert.Equal(1, gets);
    }

    [Fact]
    public async Task TestarToken_FazUmaChamadaSemCacheNemCooldown_EDevolveStatus()
    {
        var c = Criar((req, _) => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}"));

        var (status, ok) = await c.Cliente.TestarTokenAsync();
        var (status2, _) = await c.Cliente.TestarTokenAsync();

        Assert.Equal(401, status);
        Assert.Equal(401, status2);
        Assert.False(ok);
        Assert.Equal(2, c.GetTokens);
        Assert.False(c.Guarda.Bloqueada);
        Assert.Equal(0, c.Alerta.Indisponiveis);
    }

    [Fact]
    public void Cooldown_ConfigInvalidaCaiNaEscadaPadrao()
    {
        var op = new OpcoesWms { CooldownTokenSegundos = "abc;;-5" };

        Assert.Equal(new[] { 60, 120, 300, 600, 900 }, op.ListaCooldown.Select(t => (int)t.TotalSeconds).ToArray());
        Assert.Equal(new[] { 30, 45 },
            new OpcoesWms { CooldownTokenSegundos = " 30 ; 45 " }.ListaCooldown.Select(t => (int)t.TotalSeconds).ToArray());
    }

    // ---- Alerta por e-mail ----

    private static AlertaAutenticacaoWmsEmail CriarAlertaEmail(
        GravadorEmail email, bool emailHabilitado, string destinatarios, bool alertaLigado = true)
    {
        var opEmail = new OpcoesEmail { Habilitado = emailHabilitado, Destinatarios = destinatarios };
        var tmp = new OpcoesPasta { Entrada = Path.Combine(Path.GetTempPath(), "kitwms-alerta-" + Guid.NewGuid().ToString("N")) };
        var telemetria = new RegistradorTelemetria(
            Options.Create(tmp), Options.Create(new OpcoesMonitoramento()), NullLogger<RegistradorTelemetria>.Instance);
        var fonte = new FonteDestinatariosFechamento(Options.Create(opEmail), telemetria, NullLogger<FonteDestinatariosFechamento>.Instance);
        return new AlertaAutenticacaoWmsEmail(
            email, fonte, Options.Create(opEmail), Options.Create(new OpcoesWms { AlertaAutenticacao = alertaLigado }),
            NullLogger<AlertaAutenticacaoWmsEmail>.Instance);
    }

    [Fact]
    public async Task AlertaEmail_EnviaQuandoHabilitado_ERespeitaEmailHabilitadoEAlertaAutenticacao()
    {
        var email = new GravadorEmail();
        var alerta = CriarAlertaEmail(email, true, "ops@exemplo.com");
        await alerta.IndisponivelAsync(401, Inicio.AddMinutes(1), CancellationToken.None);
        await alerta.RecuperadoAsync(CancellationToken.None);

        Assert.Equal(2, email.Enviadas.Count);
        Assert.Contains("indisponível", email.Enviadas[0].Assunto);
        Assert.Contains("RECUPERADO", email.Enviadas[1].Assunto);
        Assert.Equal(new[] { "ops@exemplo.com" }, email.Enviadas[0].DestinatariosOverride);

        var desligadoEmail = new GravadorEmail();
        await CriarAlertaEmail(desligadoEmail, false, "ops@exemplo.com").IndisponivelAsync(401, Inicio, CancellationToken.None);
        Assert.Empty(desligadoEmail.Enviadas);

        var semAlerta = new GravadorEmail();
        await CriarAlertaEmail(semAlerta, true, "ops@exemplo.com", alertaLigado: false).IndisponivelAsync(401, Inicio, CancellationToken.None);
        Assert.Empty(semAlerta.Enviadas);

        var semDestino = new GravadorEmail();
        await CriarAlertaEmail(semDestino, true, string.Empty).IndisponivelAsync(401, Inicio, CancellationToken.None);
        Assert.Empty(semDestino.Enviadas);
    }
}
