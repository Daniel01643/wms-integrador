using KitWmsIntegrador.Worker;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// A configuração das CLIs é a do serviço: appsettings.json (homolog) + appsettings.Production.json (produção) na
/// pasta do executável, ambiente e linha de comando por cima. Roda em pasta temporária de teste (nunca em %TEMP% de DLLs).
/// </summary>
[Collection("AmbienteProcesso")]
public sealed class ConfiguracaoAplicacaoTests : IDisposable
{
    private const string UrlHomolog = "https://wms.exemplo.test:8888/wmsapi/api";
    private const string UrlProducao = "https://wms.exemplo.test/wmsapi/api";

    private readonly string _pasta = Path.Combine(AppContext.BaseDirectory, "cfg-" + Guid.NewGuid().ToString("N"));
    private readonly string? _envBaseUrlAnterior = Environment.GetEnvironmentVariable("Wms__BaseUrl");

    public ConfiguracaoAplicacaoTests()
    {
        Environment.SetEnvironmentVariable("Wms__BaseUrl", null);
        Directory.CreateDirectory(_pasta);
        File.WriteAllText(Path.Combine(_pasta, "appsettings.json"),
            $"{{\"Wms\":{{\"BaseUrl\":\"{UrlHomolog}\",\"TimeoutSegundos\":30}},\"Pastas\":{{\"Entrada\":\"C:\\\\padrao\"}}}}");
        File.WriteAllText(Path.Combine(_pasta, "appsettings.Production.json"),
            $"{{\"Wms\":{{\"BaseUrl\":\"{UrlProducao}\"}},\"Pastas\":{{\"Entrada\":\"C:\\\\producao\"}}}}");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("Wms__BaseUrl", _envBaseUrlAnterior);
        Directory.Delete(_pasta, recursive: true);
    }

    [Fact]
    public void Producao_VenceAppsettingsBase_AsDemaisChavesVemDoBase()
    {
        var config = ConfiguracaoAplicacao.Criar(Array.Empty<string>(), _pasta, "Production");

        Assert.Equal(UrlProducao, config["Wms:BaseUrl"]);
        Assert.Equal("C:\\producao", config["Pastas:Entrada"]);
        Assert.Equal("30", config["Wms:TimeoutSegundos"]);
    }

    [Fact]
    public void VariavelDeAmbiente_VenceOArquivoDeProducao()
    {
        Environment.SetEnvironmentVariable("Wms__BaseUrl", "https://env.exemplo.test/api");

        var config = ConfiguracaoAplicacao.Criar(Array.Empty<string>(), _pasta, "Production");

        Assert.Equal("https://env.exemplo.test/api", config["Wms:BaseUrl"]);
    }

    [Fact]
    public void LinhaDeComando_VenceOAmbiente()
    {
        Environment.SetEnvironmentVariable("Wms__BaseUrl", "https://env.exemplo.test/api");

        var config = ConfiguracaoAplicacao.Criar(new[] { "--Wms:BaseUrl=https://cli.exemplo.test/api" }, _pasta, "Production");

        Assert.Equal("https://cli.exemplo.test/api", config["Wms:BaseUrl"]);
    }

    [Fact]
    public void EmailSettings_EhLidoPorUltimo()
    {
        File.WriteAllText(Path.Combine(_pasta, "email.settings.json"), "{\"Email\":{\"Host\":\"smtp.exemplo.test\"}}");

        var config = ConfiguracaoAplicacao.Criar(Array.Empty<string>(), _pasta, "Production");

        Assert.Equal("smtp.exemplo.test", config["Email:Host"]);
    }

    [Fact]
    public void SemArquivoDeProducao_UsaOAppsettingsBase()
    {
        File.Delete(Path.Combine(_pasta, "appsettings.Production.json"));

        var config = ConfiguracaoAplicacao.Criar(Array.Empty<string>(), _pasta, "Production");

        Assert.Equal(UrlHomolog, config["Wms:BaseUrl"]);
    }
}
