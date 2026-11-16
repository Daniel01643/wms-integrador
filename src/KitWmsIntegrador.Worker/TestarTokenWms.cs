using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Worker;

/// <summary>
/// CLI <c>--testar-token [--vezes N]</c>: faz GetToken real (sem cache nem cooldown; N de 1 a 20, 2 s entre eles) e imprime SÓ o status,
/// host/porta, o identificador do servidor (hash do cookie de afinidade) e a latência.
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

        var vezes = LerVezes(args);
        using var http = new HttpClient(handler ?? RegistroClienteWms.CriarHandler(), disposeHandler: handler is null)
        {
            BaseAddress = baseUri!,
            Timeout = TimeSpan.FromSeconds(Math.Max(1, opcoes.TimeoutSegundos)),
        };
        // Em produção, cada tentativa de --vezes usa conexão nova (handler próprio) para amostrar servidores diferentes.
        var cliente = new ClienteWmsHttp(
            http, Options.Create(opcoes), NullLogger<ClienteWmsHttp>.Instance, new SystemClock(),
            fabricaConexaoNova: handler is null ? RegistroClienteWms.CriarFabricaConexaoNova(opcoes) : null);

        var ultimoOk = false;
        try
        {
            for (var i = 1; i <= vezes; i++)
            {
                var r = await cliente.TestarTokenDetalhadoAsync(vezes > 1, CancellationToken.None).ConfigureAwait(false);
                var texto = r.Status is { } s ? "HTTP " + s : "sem resposta (timeout/rede)";
                var servidor = r.Servidor == BackendWms.Desconhecido.Chave ? string.Empty : " servidor=" + r.Servidor;
                var prefixo = vezes > 1 ? $"[{i}/{vezes}] " : string.Empty;
                var extra = vezes > 1 ? $" latenciaMs={r.LatenciaMs}" : string.Empty;
                saida.WriteLine(prefixo + (r.Ok ? $"GetToken: {texto} OK" : $"GetToken: {texto} FALHA") + destino + servidor + extra);
                ultimoOk = r.Ok;
                if (i < vezes)
                {
                    await Task.Delay(IntervaloEntreTentativas).ConfigureAwait(false);
                }
            }

            return ultimoOk ? 0 : 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            saida.WriteLine("GetToken: FALHA (" + ex.GetType().Name + ")" + destino);
            return 1;
        }
    }

    /// <summary>Lê <c>--vezes N</c> (ou <c>--vezes=N</c>): padrão 1, máximo 20.</summary>
    public static int LerVezes(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            string? valor = null;
            if (string.Equals(args[i], "--vezes", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                valor = args[i + 1];
            }
            else if (args[i].StartsWith("--vezes=", StringComparison.OrdinalIgnoreCase))
            {
                valor = args[i]["--vezes=".Length..];
            }

            if (valor is not null)
            {
                return int.TryParse(valor, out var n) ? Math.Clamp(n, 1, 20) : 1;
            }
        }

        return 1;
    }

    /// <summary>Intervalo entre as tentativas de <c>--vezes</c> (ajustável nos testes).</summary>
    public static TimeSpan IntervaloEntreTentativas { get; set; } = TimeSpan.FromSeconds(2);
    // Mesma configuração do serviço (appsettings.json, appsettings.Production.json, ambiente, linha de comando, email.settings.json).
    private static IConfiguration CarregarConfiguracao(string[] args) => ConfiguracaoAplicacao.Criar(args);
}
