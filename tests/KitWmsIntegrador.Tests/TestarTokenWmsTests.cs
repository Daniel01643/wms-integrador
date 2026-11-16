using System.Net;
using KitWmsIntegrador.Tests.Infra;
using KitWmsIntegrador.Worker;
using Microsoft.Extensions.Configuration;

namespace KitWmsIntegrador.Tests;

/// <summary>CLI --testar-token: um GetToken, imprime só o status, exit 0/1, nunca token nem credenciais.</summary>
public sealed class TestarTokenWmsTests
{
    private const string Senha = "senha-secreta-de-teste";
    private const string Usuario = "USUARIO-TESTE";
    private const string Jwt = "eyJ.token-que-nao-pode-aparecer.assinatura";

    private static IConfiguration Config(bool credenciais = true) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Wms:BaseUrl"] = "https://wms.exemplo.test/wmsapi/api",
            ["Wms:Usuario"] = credenciais ? Usuario : string.Empty,
            ["Wms:Senha"] = credenciais ? Senha : string.Empty,
            ["Wms:TimeoutSegundos"] = "5",
        }).Build();

    private static async Task<(int Codigo, string Saida, FakeHttpMessageHandler Handler)> Rodar(
        Func<HttpRequestMessage, int, HttpResponseMessage> responder, bool credenciais = true)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var saida = new StringWriter();
        var codigo = await TestarTokenWms.ExecutarAsync(new[] { TestarTokenWms.Argumento }, saida, Config(credenciais), handler);
        return (codigo, saida.ToString(), handler);
    }

    private static void SemSegredos(string saida)
    {
        Assert.DoesNotContain(Jwt, saida);
        Assert.DoesNotContain(Senha, saida);
        Assert.DoesNotContain(Usuario, saida);
    }

    [Fact]
    public async Task Status200ComToken_Exit0_ImprimeSoOStatus()
    {
        var (codigo, saida, handler) = await Rodar((_, _) => FakeHttpMessageHandler.Json(HttpStatusCode.OK, $"{{\"token\":\"{Jwt}\"}}"));

        Assert.Equal(0, codigo);
        Assert.Equal("GetToken: HTTP 200 OK", saida.Trim());
        Assert.Single(handler.Requisicoes);
        SemSegredos(saida);
    }

    [Fact]
    public async Task Status401_Exit1()
    {
        var (codigo, saida, handler) = await Rodar((_, _) => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}"));

        Assert.Equal(1, codigo);
        Assert.Equal("GetToken: HTTP 401 FALHA", saida.Trim());
        Assert.Single(handler.Requisicoes); // um único GetToken, sem retry
        SemSegredos(saida);
    }

    [Fact]
    public async Task Timeout_Exit1()
    {
        var (codigo, saida, _) = await Rodar((_, _) => throw new TaskCanceledException("timeout simulado"));

        Assert.Equal(1, codigo);
        Assert.Contains("FALHA", saida);
        SemSegredos(saida);
    }

    [Fact]
    public async Task CorpoSemToken_Exit1()
    {
        var (codigo, _, _) = await Rodar((_, _) => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));

        Assert.Equal(1, codigo);
    }

    [Fact]
    public async Task SemCredenciais_Exit1_SemChamarARede()
    {
        var (codigo, saida, handler) = await Rodar((_, _) => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"), credenciais: false);

        Assert.Equal(1, codigo);
        Assert.Contains("credenciais ausentes", saida);
        Assert.Empty(handler.Requisicoes);
    }
}
