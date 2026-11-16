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

    /// <summary>Executa o teste usando a mesma configuração do serviço (<see cref="ConfiguracaoAplicacao"/>).</summary>
    public static Task<int> ExecutarAsync(string[] args) =>
        ExecutarAsync(args, Console.Out, null, null);

    /// <summary>Versão testável: configuração e handler HTTP injetáveis.</summary>
    public static async Task<int> ExecutarAsync(
        string[] args, TextWriter saida, IConfiguration? configuracao, HttpMessageHandler? handler)
    {
        var config = configuracao ?? CarregarConfiguracao(args);
        var opcoes = new OpcoesWms();
        config.GetSection(OpcoesWms.Secao).Bind(opcoes);

        var temUri = Uri.TryCreate(opcoes.BaseUrl.EndsWith('/') ? opcoes.BaseUrl : opcoes.BaseUrl + "/", UriKind.Absolute, out var baseUri);
        // Só host e porta efetivos (nunca caminho, usuário, senha nem token): mostra para onde o teste realmente chama.
        var destino = temUri ? $" ({baseUri!.Host}, porta {baseUri.Port})" : string.Empty;

        if (string.IsNullOrWhiteSpace(opcoes.Usuario) || string.IsNullOrWhiteSpace(opcoes.Senha))
        {
            saida.WriteLine("GetToken: FALHA (credenciais ausentes; defina WMS__Usuario e WMS__Senha)" + destino);
            return 1;
        }

        if (!temUri)
        {
            saida.WriteLine("GetToken: FALHA (Wms:BaseUrl ausente ou inválida)");
            return 1;
        }

        using var http = new HttpClient(handler ?? RegistroClienteWms.CriarHandler(), disposeHandler: handler is null)
        {
            BaseAddress = baseUri!,
            Timeout = TimeSpan.FromSeconds(Math.Max(1, opcoes.TimeoutSegundos)),
        };
        var cliente = new ClienteWmsHttp(
            http, Options.Create(opcoes), NullLogger<ClienteWmsHttp>.Instance, new SystemClock());

        try
        {
            var (status, ok) = await cliente.TestarTokenAsync().ConfigureAwait(false);
            var texto = status is { } s ? "HTTP " + s : "sem resposta (timeout/rede)";
            saida.WriteLine((ok ? $"GetToken: {texto} OK" : $"GetToken: {texto} FALHA") + destino);
            return ok ? 0 : 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            saida.WriteLine("GetToken: FALHA (" + ex.GetType().Name + ")" + destino);
            return 1;
        }
    }

    // Mesma configuração do serviço (appsettings.json, appsettings.Production.json, ambiente, linha de comando, email.settings.json).
    private static IConfiguration CarregarConfiguracao(string[] args) => ConfiguracaoAplicacao.Criar(args);
}
