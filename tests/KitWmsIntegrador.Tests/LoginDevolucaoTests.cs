using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Devolucao;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Tests.Infra;
using KitWmsIntegrador.Worker.TelaDevolucao;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// "Login" simples da tela de devolução: o usuário do WMS é só declarado (para a auditoria) e a senha
/// nunca é guardada, logada, auditada, devolvida nem enviada ao WMS.
/// </summary>
public sealed class LoginDevolucaoTests : IAsyncLifetime
{
    private const string SenhaSecreta = "S3nh@-Ultra-Secreta-9981";

    private readonly AmbienteDevolucao _amb = new();
    private readonly LoggerFactoryColetora _logs = new();
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        // Mesmo serviço do ambiente, mas com logger que captura tudo (serviço e endpoint).
        var servico = new ServicoDevolucao(
            _amb.Cliente,
            new ConstrutorI40(Options.Create(_amb.Operacional), Options.Create(_amb.Devolucao)),
            new LeitorNotaFiscalXml(),
            new AuditoriaDevolucao(
                new FakeClock(DateTimeOffset.UtcNow),
                Options.Create(_amb.Pastas),
                Options.Create(_amb.Devolucao),
                _logs.CreateLogger<AuditoriaDevolucao>()),
            Options.Create(_amb.Pastas),
            Options.Create(_amb.Operacional),
            Options.Create(_amb.Devolucao),
            _logs.CreateLogger<ServicoDevolucao>());

        _app = TelaDevolucaoApp.Criar(servico, _amb.Devolucao, _logs, "http://127.0.0.1:0");
        await _app.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        _amb.Dispose();
    }

    private static StringContent Json(object corpo) =>
        new(JsonSerializer.Serialize(corpo), Encoding.UTF8, "application/json");

    private string LerAuditoria() =>
        string.Concat(Directory.GetFiles(_amb.Auditoria, "devolucao_*.log").Select(File.ReadAllText));

    private void PrepararEnvioOk()
    {
        _amb.CopiarXml(AmbienteDevolucao.Chave1);
        _amb.Cliente.Responder(InterfaceWms.I40,
            AmbienteDevolucao.Resposta("4042207", (AmbienteDevolucao.Nota1, true, "Registrado com sucesso")));
    }

    [Fact]
    public async Task PostEnviar_SemUsuario_Devolve400_SemEnviarAoWms()
    {
        PrepararEnvioOk();

        var resp = await _http.PostAsync("/api/devolucao/enviar",
            Json(new { chaves = new[] { AmbienteDevolucao.Chave1 } }));
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("Informe o usuário do WMS.", corpo.GetProperty("erro").GetString());
        Assert.Empty(_amb.Cliente.Envios);
        Assert.Empty(Directory.Exists(_amb.Auditoria) ? Directory.GetFiles(_amb.Auditoria) : Array.Empty<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ana;bia")]
    [InlineData("ana\nbia")]
    [InlineData("ana\r\nbia")]
    [InlineData("ana/bia")]
    [InlineData("ana'--")]
    [InlineData("<script>")]
    [InlineData("ana,bia")]
    [InlineData("ana|bia")]
    public async Task PostEnviar_UsuarioInvalido_Devolve400(string usuario)
    {
        PrepararEnvioOk();

        var resp = await _http.PostAsync("/api/devolucao/enviar",
            Json(new { usuario, chaves = new[] { AmbienteDevolucao.Chave1 } }));
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(corpo.GetProperty("erro").GetString()));
        Assert.Empty(_amb.Cliente.Envios);
    }

    [Fact]
    public async Task PostEnviar_UsuarioCom61Caracteres_Devolve400_ECom60E_Aceito()
    {
        PrepararEnvioOk();

        var longo = await _http.PostAsync("/api/devolucao/enviar",
            Json(new { usuario = new string('a', 61), chaves = new[] { AmbienteDevolucao.Chave1 } }));
        var limite = await _http.PostAsync("/api/devolucao/enviar",
            Json(new { usuario = new string('a', 60), chaves = new[] { AmbienteDevolucao.Chave1 } }));

        Assert.Equal(HttpStatusCode.BadRequest, longo.StatusCode);
        Assert.Equal(HttpStatusCode.OK, limite.StatusCode);
    }

    [Fact]
    public async Task PostEnviar_UsuarioValido_GravaUsuarioNaLinhaDeAuditoria_ENoLog()
    {
        PrepararEnvioOk();

        var resp = await _http.PostAsync("/api/devolucao/enviar",
            Json(new { usuario = "  Maria.Silva@friozem  ", chaves = new[] { AmbienteDevolucao.Chave1 } }));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var linha = Assert.Single(LerAuditoria().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        var campos = linha.Split(';');
        Assert.Equal(8, campos.Length);
        Assert.Equal("Maria.Silva@friozem", campos[2]);
        Assert.Equal(AmbienteDevolucao.Chave1, campos[3]);
        Assert.Contains(_logs.Mensagens, m => m.Contains("usuario=Maria.Silva@friozem") && m.Contains(AmbienteDevolucao.Chave1));
        Assert.Equal(1, _amb.Cliente.Contar(InterfaceWms.I40));
    }

    [Fact]
    public async Task PostEnviar_SenhaNaoApareceEmAuditoria_Log_Resposta_NemNoPayloadDoWms()
    {
        PrepararEnvioOk();

        var resp = await _http.PostAsync("/api/devolucao/enviar",
            Json(new { usuario = "joao", senha = SenhaSecreta, chaves = new[] { AmbienteDevolucao.Chave1 } }));
        var texto = await resp.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.DoesNotContain(SenhaSecreta, texto);
        Assert.DoesNotContain(SenhaSecreta, LerAuditoria());
        Assert.All(_logs.Mensagens, m => Assert.DoesNotContain(SenhaSecreta, m));
        Assert.DoesNotContain(SenhaSecreta, JsonSerializer.Serialize(_amb.Cliente.Envios[0].Payload));
        Assert.DoesNotContain(SenhaSecreta, string.Concat(Directory.GetFiles(_amb.Raiz, "*", SearchOption.AllDirectories).Select(File.ReadAllText)));
    }

    [Fact]
    public async Task PostEnviar_SenhaComUsuarioInvalido_NaoVazaNaMensagemDeErro()
    {
        var resp = await _http.PostAsync("/api/devolucao/enviar",
            Json(new { usuario = "a;b", senha = SenhaSecreta, chaves = new[] { AmbienteDevolucao.Chave1 } }));
        var texto = await resp.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.DoesNotContain(SenhaSecreta, texto);
        Assert.All(_logs.Mensagens, m => Assert.DoesNotContain(SenhaSecreta, m));
    }

    [Fact]
    public async Task PostEnviar_ValidacaoDeChavesContinuaIgual_ComUsuarioValido()
    {
        var resp = await _http.PostAsync("/api/devolucao/enviar",
            Json(new { usuario = "joao", chaves = Array.Empty<string>() }));
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("Informe ao menos uma chave de acesso.", corpo.GetProperty("erro").GetString());
    }

    [Fact]
    public async Task GetRaiz_TemSecaoDeLogin_ComRotulosNotaEBotaoEnviarDevolucao()
    {
        var html = await _http.GetStringAsync("/");

        Assert.Contains("for=\"usuario\"", html);
        Assert.Contains("Usuário do WMS", html);
        Assert.Contains("for=\"senha\"", html);
        Assert.Contains("type=\"password\"", html);
        Assert.Contains("O login é usado só para registrar quem enviou. A senha não é verificada nem guardada.", html);
        Assert.Contains("Enviar devolução", html);
        Assert.Contains("Trocar usuário", html);
        Assert.Contains("Enviando como: ", html);
        Assert.Contains("sessionStorage", html);
        // A senha nunca é guardada nem enviada pela página.
        Assert.DoesNotContain("senha:", html);
        Assert.DoesNotContain("setItem('senha", html);
    }

    [Theory]
    [InlineData("joao", true)]
    [InlineData("Maria Silva", true)]
    [InlineData("a.b_c-d@x", true)]
    [InlineData("José", true)]
    [InlineData("", false)]
    [InlineData("a;b", false)]
    [InlineData("a\nb", false)]
    public void Validador_AplicaAsRegras(string entrada, bool valido)
    {
        Assert.Equal(valido, ValidadorUsuarioDevolucao.Validar(entrada, out _) is null);
    }

    [Fact]
    public void Sanitizar_RemovePontoEVirgulaEQuebras_EUsaTracoQuandoVazio()
    {
        Assert.Equal("a,b c d", ValidadorUsuarioDevolucao.Sanitizar("a;b\rc\nd"));
        Assert.Equal("-", ValidadorUsuarioDevolucao.Sanitizar(null));
        Assert.Equal("-", ValidadorUsuarioDevolucao.Sanitizar("  "));
    }

    [Fact]
    public async Task Servico_ComUsuarioHostil_AuditoriaContinuaComOitoCampos()
    {
        PrepararEnvioOk();

        await _amb.Servico.EnviarAsync(new SolicitacaoDevolucao
        {
            Chaves = new[] { AmbienteDevolucao.Chave1 },
            Origem = "10.0.0.5",
            Usuario = "x;y\r\nz",
        });

        var linha = Assert.Single(LerAuditoria().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(8, linha.Split(';').Length);
        Assert.Equal("x,y  z", linha.Split(';')[2]);
    }

    /// <summary>Fábrica de loggers que guarda todas as mensagens formatadas (inclui exceções).</summary>
    private sealed class LoggerFactoryColetora : ILoggerFactory
    {
        private readonly List<string> _mensagens = new();

        public IReadOnlyList<string> Mensagens
        {
            get
            {
                lock (_mensagens)
                {
                    return _mensagens.ToList();
                }
            }
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new Coletor(this);

        public ILogger<T> CreateLogger<T>() => new Coletor<T>(this);

        public void Dispose()
        {
        }

        private void Adicionar(string mensagem)
        {
            lock (_mensagens)
            {
                _mensagens.Add(mensagem);
            }
        }

        private class Coletor : ILogger
        {
            private readonly LoggerFactoryColetora _pai;

            public Coletor(LoggerFactoryColetora pai) => _pai = pai;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                _pai.Adicionar(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
        }

        private sealed class Coletor<T> : Coletor, ILogger<T>
        {
            public Coletor(LoggerFactoryColetora pai) : base(pai)
            {
            }
        }
    }
}
