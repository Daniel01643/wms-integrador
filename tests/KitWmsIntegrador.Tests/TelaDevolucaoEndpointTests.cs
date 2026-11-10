using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Devolucao;
using KitWmsIntegrador.Tests.Infra;
using KitWmsIntegrador.Worker.TelaDevolucao;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Integração do endpoint da tela de devolução: sobe o MESMO WebApplication de produção
/// (<see cref="TelaDevolucaoApp.Criar"/>) em porta efêmera de 127.0.0.1 e chama com HttpClient real.
/// Usa FakeClienteWms: nada é enviado ao WMS.
/// </summary>
public sealed class TelaDevolucaoEndpointTests : IAsyncLifetime
{
    private readonly AmbienteDevolucao _amb = new();
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        _app = TelaDevolucaoApp.Criar(_amb.Servico, _amb.Devolucao, NullLoggerFactory.Instance, "http://127.0.0.1:0");
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

    [Fact]
    public async Task GetRaiz_DevolvePaginaHtmlAutocontida_ComLabelsELogo()
    {
        var resp = await _http.GetAsync("/");
        var html = await resp.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.StartsWith("text/html", resp.Content.Headers.ContentType!.ToString());
        Assert.Contains("<label", html);
        Assert.Contains("lang=\"pt-BR\"", html);
        Assert.Contains("data:image/png;base64,iVBOR", html);
        Assert.DoesNotContain("{{LOGO_BASE64}}", html);
        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
        Assert.Contains("no-store", resp.Headers.CacheControl!.ToString());
        Assert.Contains("default-src 'self'", resp.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task GetConfig_RefleteAsOpcoes()
    {
        var cfg = await _http.GetFromJsonAsync<JsonElement>("/api/config");

        Assert.False(cfg.GetProperty("enviarPlacaPadrao").GetBoolean());
        Assert.Equal(30, cfg.GetProperty("maxChaves").GetInt32());
    }

    [Fact]
    public async Task GetSaude_Ok()
    {
        var saude = await _http.GetFromJsonAsync<JsonElement>("/api/saude");

        Assert.Equal("ok", saude.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PostEnviar_SemPlaca_EnviaIMPEDevolveResultadoPorChave()
    {
        _amb.CopiarXml(AmbienteDevolucao.Chave1);
        _amb.Cliente.Responder(InterfaceWms.I40,
            AmbienteDevolucao.Resposta("4042207", (AmbienteDevolucao.Nota1, true, "Registrado com sucesso")));

        var resp = await _http.PostAsync("/api/devolucao/enviar",
            Json(new { chaves = new[] { AmbienteDevolucao.Chave1 }, enviarPlaca = false, placa = "" }));
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(1, corpo.GetProperty("total").GetInt32());
        Assert.Equal(1, corpo.GetProperty("enviadas").GetInt32());
        Assert.Equal("4042207", corpo.GetProperty("transacao").GetString());
        var r = corpo.GetProperty("resultados")[0];
        Assert.Equal(AmbienteDevolucao.Chave1, r.GetProperty("chave").GetString());
        Assert.Equal(AmbienteDevolucao.Nota1, r.GetProperty("nota").GetString());
        Assert.Equal("Enviada", r.GetProperty("status").GetString());
        Assert.True(r.GetProperty("sucesso").GetBoolean());
        Assert.Equal("Registrado com sucesso", r.GetProperty("mensagem").GetString());
        Assert.Equal(1, _amb.Cliente.Contar(InterfaceWms.I40));
    }

    [Fact]
    public async Task PostEnviar_ChaveInvalidaESemXml_VoltamNoResultado200_SemEnviarAoWms()
    {
        var semXml = AmbienteDevolucao.ChaveComDv(AmbienteDevolucao.Chave1[..30] + "0000000000001");

        var resp = await _http.PostAsync("/api/devolucao/enviar",
            Json(new { chaves = new[] { "123", semXml }, enviarPlaca = false }));
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("ChaveInvalida", corpo.GetProperty("resultados")[0].GetProperty("status").GetString());
        Assert.Equal("XmlNaoEncontrado", corpo.GetProperty("resultados")[1].GetProperty("status").GetString());
        Assert.Empty(_amb.Cliente.Envios);
    }

    [Fact]
    public async Task PostEnviar_PlacaLigadaSemPlaca_Devolve400()
    {
        _amb.CopiarXml(AmbienteDevolucao.Chave1);

        var resp = await _http.PostAsync("/api/devolucao/enviar",
            Json(new { chaves = new[] { AmbienteDevolucao.Chave1 }, enviarPlaca = true, placa = "" }));
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("placa", corpo.GetProperty("erro").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_amb.Cliente.Envios);
    }

    [Fact]
    public async Task PostEnviar_31Chaves_Devolve400()
    {
        var chaves = Enumerable.Range(0, 31)
            .Select(i => AmbienteDevolucao.ChaveComDv(AmbienteDevolucao.Chave1[..30] + i.ToString("D13"))).ToArray();

        var resp = await _http.PostAsync("/api/devolucao/enviar", Json(new { chaves, enviarPlaca = false }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty(_amb.Cliente.Envios);
    }

    [Fact]
    public async Task PostEnviar_ListaVazia_Devolve400()
    {
        var resp = await _http.PostAsync("/api/devolucao/enviar", Json(new { chaves = Array.Empty<string>() }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PostEnviar_JsonMalformado_ECorpoNaoJson_Devolvem400()
    {
        var ruim = await _http.PostAsync("/api/devolucao/enviar",
            new StringContent("{ chaves: [", Encoding.UTF8, "application/json"));
        var texto = await _http.PostAsync("/api/devolucao/enviar",
            new StringContent("chaves=1", Encoding.UTF8, "application/x-www-form-urlencoded"));

        Assert.Equal(HttpStatusCode.BadRequest, ruim.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, texto.StatusCode);
    }

    [Fact]
    public async Task PostEnviar_FalhaDoClienteWms_Devolve200ComErroWms_ENaoDerrubaOServidor()
    {
        _amb.CopiarXml(AmbienteDevolucao.Chave1);
        // Sem resposta programada, o FakeClienteWms lança InvalidOperationException.
        var resp = await _http.PostAsync("/api/devolucao/enviar",
            Json(new { chaves = new[] { AmbienteDevolucao.Chave1 }, enviarPlaca = false }));
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("ErroWms", corpo.GetProperty("resultados")[0].GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/api/saude")).StatusCode);
    }

    [Fact]
    public async Task PostEnviar_ErroInesperadoNoServico_Devolve500GenericoSemDetalhes()
    {
        await using var app = TelaDevolucaoApp.Criar(
            new ServicoQueExplode(), _amb.Devolucao, NullLoggerFactory.Instance, "http://127.0.0.1:0");
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

        var resp = await http.PostAsync("/api/devolucao/enviar", Json(new { chaves = new[] { AmbienteDevolucao.Chave1 } }));
        var texto = await resp.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
        Assert.DoesNotContain("segredo-interno", texto);
        Assert.DoesNotContain("at KitWms", texto);
        await app.StopAsync();
    }

    private sealed class ServicoQueExplode : IServicoDevolucao
    {
        public Task<ResultadoLoteDevolucao> EnviarAsync(SolicitacaoDevolucao solicitacao, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("segredo-interno");
    }
}

/// <summary>Flag e isolamento do <see cref="ServidorTelaDevolucao"/> (hospedagem no Worker).</summary>
public sealed class ServidorTelaDevolucaoTests : IDisposable
{
    private readonly AmbienteDevolucao _amb = new();

    public void Dispose() => _amb.Dispose();

    private static int PortaLivre()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var porta = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return porta;
    }

    private static bool PortaAceitaConexao(int porta)
    {
        try
        {
            using var c = new TcpClient();
            return c.ConnectAsync(IPAddress.Loopback, porta).Wait(TimeSpan.FromSeconds(2)) && c.Connected;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private ServidorTelaDevolucao Criar(OpcoesDevolucao opcoes, bool registrarServico = true)
    {
        var s = new ServiceCollection();
        s.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        s.AddSingleton(Options.Create(opcoes));
        if (registrarServico)
        {
            s.AddSingleton<IServicoDevolucao>(_amb.Servico);
        }

        return new ServidorTelaDevolucao(s.BuildServiceProvider(), NullLogger<ServidorTelaDevolucao>.Instance);
    }

    [Fact]
    public async Task Desabilitada_NaoAbrePorta_ETerminaSemErro()
    {
        var porta = PortaLivre();
        var srv = Criar(new OpcoesDevolucao { Habilitada = false, Porta = porta });

        await srv.StartAsync(CancellationToken.None);
        await srv.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        await srv.StopAsync(CancellationToken.None);

        Assert.False(PortaAceitaConexao(porta));
    }

    [Fact]
    public async Task Habilitada_SobeNaPorta_ERespondeSaude()
    {
        var porta = PortaLivre();
        var srv = Criar(new OpcoesDevolucao { Habilitada = true, Porta = porta });

        await srv.StartAsync(CancellationToken.None);
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{porta}") };
            HttpResponseMessage? resp = null;
            for (var i = 0; i < 50 && resp is null; i++)
            {
                try
                {
                    resp = await http.GetAsync("/api/saude");
                }
                catch (HttpRequestException)
                {
                    await Task.Delay(100);
                }
            }

            Assert.Equal(HttpStatusCode.OK, resp!.StatusCode);
        }
        finally
        {
            await srv.StopAsync(CancellationToken.None);
        }

        Assert.False(PortaAceitaConexao(porta));
    }

    [Fact]
    public async Task PortaInvalida_ServicoAusente_ePortaOcupada_NaoPropagamExcecao()
    {
        var invalida = Criar(new OpcoesDevolucao { Habilitada = true, Porta = 0 });
        var semServico = Criar(new OpcoesDevolucao { Habilitada = true, Porta = PortaLivre() }, registrarServico: false);

        var ocupada = new TcpListener(IPAddress.Any, 0);
        ocupada.Start();
        try
        {
            var porta = ((IPEndPoint)ocupada.LocalEndpoint).Port;
            var conflito = Criar(new OpcoesDevolucao { Habilitada = true, Porta = porta });

            foreach (var srv in new[] { invalida, semServico, conflito })
            {
                await srv.StartAsync(CancellationToken.None);
                await srv.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(srv.ExecuteTask.IsFaulted);
                await srv.StopAsync(CancellationToken.None);
            }
        }
        finally
        {
            ocupada.Stop();
        }
    }
}
