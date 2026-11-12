using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Worker.Configuracao;
using Microsoft.Extensions.Configuration;

namespace KitWmsIntegrador.Worker;

/// <summary>Configuração das CLIs de apoio (fora do host): appsettings ao lado do executável + variáveis de ambiente.</summary>
internal static class ConfiguracaoManual
{
    public static IConfiguration Criar() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("email.settings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

    /// <summary>Opções de pasta com o mesmo fallback do host (Pastas:Entrada vazia herda Entrada:PastaEntrada).</summary>
    public static OpcoesPasta Pastas(IConfiguration config)
    {
        var pastas = new OpcoesPasta();
        config.GetSection(OpcoesPasta.Secao).Bind(pastas);
        if (string.IsNullOrWhiteSpace(pastas.Entrada))
        {
            var legado = new OpcoesEntrada();
            config.GetSection(OpcoesEntrada.Secao).Bind(legado);
            pastas.Entrada = legado.PastaEntrada;
        }

        return pastas;
    }

    /// <summary>Valor do argumento <c>--nome valor</c> (ou vazio).</summary>
    public static string Argumento(string[] args, string nome)
    {
        var i = Array.IndexOf(args, nome);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : string.Empty;
    }
}
