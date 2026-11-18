using System.Text.Json;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Log HTTP menor (18/11/2026): <c>System.Net.Http.HttpClient</c> e <c>Microsoft.AspNetCore</c> em Warning no
/// appsettings.json e no exemplo de produção, mantendo o nível Information dos logs próprios do integrador.
/// </summary>
public sealed class ConfiguracaoLogHttpTests
{
    private static string Raiz()
    {
        var raiz = AppContext.BaseDirectory;
        while (raiz is not null && !File.Exists(Path.Combine(raiz, "KitWmsIntegrador.sln")))
        {
            raiz = Path.GetDirectoryName(raiz);
        }

        Assert.NotNull(raiz);
        return raiz!;
    }

    private static JsonElement LogLevel(string a, string b, string c)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Raiz(), a, b, c)));
        return doc.RootElement.GetProperty("Logging").GetProperty("LogLevel").Clone();
    }

    [Theory]
    [InlineData("src", "KitWmsIntegrador.Worker", "appsettings.json")]
    [InlineData("deploy", "config-exemplo", "appsettings.Production.json")]
    public void HttpClientEAspNetCore_EmWarning_ELogsProprios_EmInformation(string a, string b, string c)
    {
        var nivel = LogLevel(a, b, c);

        Assert.Equal("Warning", nivel.GetProperty("System.Net.Http.HttpClient").GetString());
        Assert.Equal("Warning", nivel.GetProperty("Microsoft.AspNetCore").GetString());
        Assert.Equal("Information", nivel.GetProperty("Default").GetString());
        Assert.Equal("Information", nivel.GetProperty("Microsoft.Hosting.Lifetime").GetString());
    }

    [Theory]
    [InlineData("src", "KitWmsIntegrador.Worker", "appsettings.json")]
    [InlineData("deploy", "config-exemplo", "appsettings.Production.json")]
    public void ChavesNovasDeLimpezaEDiagnostico_ExistemNosArquivosDeConfiguracao(string a, string b, string c)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Raiz(), a, b, c)));
        var limpeza = doc.RootElement.GetProperty("Limpeza");
        var diag = doc.RootElement.GetProperty("Diagnostico");

        Assert.Equal(30, limpeza.GetProperty("DiasRetencaoLogs").GetInt32());
        Assert.True(limpeza.TryGetProperty("PastaLogs", out _));
        Assert.Equal(15, diag.GetProperty("IntervaloAmostraMinutos").GetInt32());
        Assert.Equal(500, diag.GetProperty("AlertaMemoriaLivreMb").GetInt32());
    }
}
