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

        Assert.False(cfg.TryGetProperty("enviarPlacaPadrao", out _));
        Assert.Equal(30, cfg.GetProperty("maxChaves").GetInt32());
    }

    [Fact]
    public async Task GetSaude_Ok()
    {
        var saude = await _http.GetFromJsonAsync<JsonElement>("/api/saude");

        Assert.Equal("ok", saude.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PostEnviar_EnviaIMPEDevolveResultadoPorChave()
    {
        _amb.CopiarXml(AmbienteDevolucao.Chave1);
        _amb.Cliente.Responder(InterfaceWms.I40,
            AmbienteDevolucao.Resposta("4042207", (AmbienteDevolucao.Nota1, true, "Registrado com sucesso")));

        var resp = await _http.PostAsync("/api/devolucao/enviar",
            Json(new { usuario = "teste.web", chaves = new[] { AmbienteDevolucao.Chave1 } }));
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(1, corpo.GetProperty("total").GetInt32());
        Assert.Equal(1, corpo.GetProperty("enviadas").GetInt32());
        Assert.Equal("4042207", corpo.GetProperty("transacao").GetString());
        var r = corpo.GetProperty("resultados")[0];
        Assert.Equal(AmbienteDevolucao.Chave1, r.GetProperty("chave").GetString());
        Assert.Equal(AmbienteDevolucao.Nota1, r.GetProperty("nota").GetString());
        Assert.Equal("6", r.GetProperty("serie").GetString());
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
            Json(new { usuario = "teste.web", chaves = new[] { "123", semXml } }));
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("ChaveInvalida", corpo.GetProperty("resultados")[0].GetProperty("status").GetString());
        Assert.Equal("XmlNaoEncontrado", corpo.GetProperty("resultados")[1].GetProperty("status").GetString());
        Assert.Empty(_amb.Cliente.Envios);
    }

    [Fact]
    public async Task PostEnviar_PlacaNoCorpo_EIgnorada_SempreIMPSemPlaca()
    {
        _amb.CopiarXml(AmbienteDevolucao.Chave1);
        _amb.Cliente.Responder(InterfaceWms.I40,
            AmbienteDevolucao.Resposta("1", (AmbienteDevolucao.Nota1, true, "ok")));

        var resp = await _http.PostAsync("/api/devolucao/enviar",
            Json(new { usuario = "teste.web", chaves = new[] { AmbienteDevolucao.Chave1 }, enviarPlaca = true, placa = "EPR7871" }));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var nf = ((KitWmsIntegrador.Core.Construtores.Dtos.PayloadI40)_amb.Cliente.Envios[0].Payload).NotaFiscal[0];
        Assert.Equal("IMP", nf.TipoOperacaoNota);
        Assert.Equal(string.Empty, nf.PlacaVeiculoPrevistoNota);
        Assert.Equal(string.Empty, nf.NomeMotorista);
    }

    [Fact]
    public async Task GetRaiz_NaoTemMaisPlaca_ETemCamposDeNumeroESerie()
    {
        var html = await _http.GetStringAsync("/");

        Assert.DoesNotContain("laca", html);
        Assert.Contains("for=\"numero\"", html);
        Assert.Contains("for=\"serie\"", html);
        Assert.Contains("/api/devolucao/buscar", html);
        Assert.Contains("aria-live", html);
    }

    [Fact]
    public async Task GetBuscar_UmaNota_Devolve200ComUmaOpcao()
    {
        var chave = AmbienteDevolucao.ChaveDe(AmbienteDevolucao.CnpjDepositanteDigitos, 6, 15671289);
        _amb.CriarArquivoDaChave(chave, "pacote_20261101_100000");

        var resp = await _http.GetAsync("/api/devolucao/buscar?numero=15671289&serie=6");
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("15671289", corpo.GetProperty("numero").GetString());
        Assert.Equal("6", corpo.GetProperty("serie").GetString());
        var o = Assert.Single(corpo.GetProperty("opcoes").EnumerateArray());
        Assert.Equal(chave, o.GetProperty("chave").GetString());
        Assert.Equal("pacote_20261101_100000", o.GetProperty("pacote").GetString());
        Assert.False(string.IsNullOrEmpty(o.GetProperty("dataArquivo").GetString()));
        Assert.Empty(_amb.Cliente.Envios);
    }

    [Fact]
    public async Task GetBuscar_VariasChaves_Devolve200ComTodasAsOpcoes_SemEnviarNada()
    {
        var a = AmbienteDevolucao.ChaveDe(AmbienteDevolucao.CnpjDepositanteDigitos, 6, 500, variacao: 1);
        var b = AmbienteDevolucao.ChaveDe(AmbienteDevolucao.CnpjDepositanteDigitos, 6, 500, variacao: 2);
        _amb.CriarArquivoDaChave(a, "pacote_a", new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        _amb.CriarArquivoDaChave(b, "pacote_b", new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc));

        var corpo = await _http.GetFromJsonAsync<JsonElement>("/api/devolucao/buscar?numero=500&serie=006");

        var chaves = corpo.GetProperty("opcoes").EnumerateArray().Select(o => o.GetProperty("chave").GetString()).ToArray();
        Assert.Equal(new[] { b, a }, chaves);
        Assert.Empty(_amb.Cliente.Envios);
    }

    [Fact]
    public async Task GetBuscar_NaoEncontrada_Devolve404ComMensagem()
    {
        var resp = await _http.GetAsync("/api/devolucao/buscar?numero=999&serie=6");
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal("Nota não encontrada na retenção de 30 dias.", corpo.GetProperty("erro").GetString());
    }

    [Fact]
    public async Task GetBuscar_OutroCnpj_Devolve404()
    {
        _amb.CriarArquivoDaChave(AmbienteDevolucao.ChaveDe(AmbienteDevolucao.CnpjOutroEmitente, 6, 500), "pacote_a");

        var resp = await _http.GetAsync("/api/devolucao/buscar?numero=500&serie=6");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?numero=500")]
    [InlineData("?serie=6")]
    [InlineData("?numero=12a&serie=6")]
    [InlineData("?numero=1234567890&serie=6")]
    [InlineData("?numero=500&serie=1234")]
    [InlineData("?numero=500&serie=-1")]
    [InlineData("?numero=%27%3Bdrop&serie=6")]
    public async Task GetBuscar_EntradaInvalida_Devolve400(string consulta)
    {
        var resp = await _http.GetAsync("/api/devolucao/buscar" + consulta);
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(corpo.GetProperty("erro").GetString()));
    }

    [Fact]
    public async Task GetBuscar_SemCnpjDoDepositante_Devolve503()
    {
        _amb.Operacional.CnpjDepositante = string.Empty;

        var resp = await _http.GetAsync("/api/devolucao/buscar?numero=500&serie=6");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task GetBuscar_ErroInesperado_Devolve500GenericoSemDetalhes()
    {
        await using var app = TelaDevolucaoApp.Criar(
            new ServicoQueExplode(), _amb.Devolucao, NullLoggerFactory.Instance, "http://127.0.0.1:0");
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

        var resp = await http.GetAsync("/api/devolucao/buscar?numero=500&serie=6");
        var texto = await resp.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
        Assert.DoesNotContain("segredo-interno", texto);
        await app.StopAsync();
    }
    [Fact]
    public async Task PostEnviar_31Chaves_Devolve400()
    {
        var chaves = Enumerable.Range(0, 31)
            .Select(i => AmbienteDevolucao.ChaveComDv(AmbienteDevolucao.Chave1[..30] + i.ToString("D13"))).ToArray();

        var resp = await _http.PostAsync("/api/devolucao/enviar", Json(new { usuario = "teste.web", chaves }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty(_amb.Cliente.Envios);
    }

    [Fact]
    public async Task PostEnviar_ListaVazia_Devolve400()
    {
        var resp = await _http.PostAsync("/api/devolucao/enviar", Json(new { usuario = "teste.web", chaves = Array.Empty<string>() }));

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
            Json(new { usuario = "teste.web", chaves = new[] { AmbienteDevolucao.Chave1 } }));
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

        var resp = await http.PostAsync("/api/devolucao/enviar", Json(new { usuario = "teste.web", chaves = new[] { AmbienteDevolucao.Chave1 } }));
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

        public ResultadoBuscaDevolucao Buscar(string? numero, string? serie) =>
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
