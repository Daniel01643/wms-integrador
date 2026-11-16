using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Worker;

/// <summary>
/// CLI <c>--testar-token</c>: faz UM GetToken real (sem cache nem cooldown) e imprime SÓ o status.
/// Exit code 0 = token válido; 1 = qualquer outra coisa (inclui credenciais ausentes). Nunca imprime
/// token, usuário nem senha. Não toca em notas nem em pastas.
/// </summary>
public static class TestarTokenWms
{
    /// <summary>Argumento que aciona o teste.</summary>
    public const string Argumento = "--testar-token";

    /// <summary>Executa o teste usando a configuração do serviço (appsettings, config\ e variáveis de ambiente).</summary>
    public static Task<int> ExecutarAsync(string[] args) =>
        ExecutarAsync(args, Console.Out, null, null);

    /// <summary>Versão testável: configuração e handler HTTP injetáveis.</summary>
    public static async Task<int> ExecutarAsync(
        string[] args, TextWriter saida, IConfiguration? configuracao, HttpMessageHandler? handler)
    {
        var config = configuracao ?? CarregarConfiguracao(args);
        var opcoes = new OpcoesWms();
        config.GetSection(OpcoesWms.Secao).Bind(opcoes);

        if (string.IsNullOrWhiteSpace(opcoes.Usuario) || string.IsNullOrWhiteSpace(opcoes.Senha))
        {
            saida.WriteLine("GetToken: FALHA (credenciais ausentes; defina WMS__Usuario e WMS__Senha)");
            return 1;
        }

        if (!Uri.TryCreate(opcoes.BaseUrl.EndsWith('/') ? opcoes.BaseUrl : opcoes.BaseUrl + "/", UriKind.Absolute, out var baseUri))
        {
            saida.WriteLine("GetToken: FALHA (Wms:BaseUrl ausente ou inválida)");
            return 1;
        }

        using var http = new HttpClient(handler ?? RegistroClienteWms.CriarHandler(), disposeHandler: handler is null)
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(Math.Max(1, opcoes.TimeoutSegundos)),
        };
        var cliente = new ClienteWmsHttp(
            http, Options.Create(opcoes), NullLogger<ClienteWmsHttp>.Instance, new SystemClock());

        try
        {
            var (status, ok) = await cliente.TestarTokenAsync().ConfigureAwait(false);
            var texto = status is { } s ? "HTTP " + s : "sem resposta (timeout/rede)";
            saida.WriteLine(ok ? $"GetToken: {texto} OK" : $"GetToken: {texto} FALHA");
            return ok ? 0 : 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            saida.WriteLine("GetToken: FALHA (" + ex.GetType().Name + ")");
            return 1;
        }
    }

    // Mesma ordem do serviço: appsettings.json, appsettings.Production.json, config\ ao lado do exe, ambiente.
    private static IConfiguration CarregarConfiguracao(string[] args)
    {
        var baseDir = AppContext.BaseDirectory;
        return new ConfigurationBuilder()
            .SetBasePath(baseDir)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Production.json", optional: true)
            .AddJsonFile(Path.Combine("config", "appsettings.Production.json"), optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
    }
}
