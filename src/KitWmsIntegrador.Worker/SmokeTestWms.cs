using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Worker;

/// <summary>
/// Smoke test MANUAL do cliente WMS: faz um GetToken real + um POST contra o
/// homolog e imprime transacao/sucesso/classificação. Acionado por
/// <c>--smoke-wms</c> na linha de comando.
///
/// NÃO roda em testes/CI e NÃO faz parte do fluxo padrão do Worker. Exige as
/// credenciais em <c>WMS__Usuario</c> / <c>WMS__Senha</c>.
/// </summary>
public static class SmokeTestWms
{
    /// <summary>Argumento que aciona o smoke test.</summary>
    public const string Argumento = "--smoke-wms";

    public static async Task<int> ExecutarAsync(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        var logger = loggerFactory.CreateLogger("SmokeTestWms");

        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        var opcoes = new OpcoesWms();
        config.GetSection(OpcoesWms.Secao).Bind(opcoes);

        if (string.IsNullOrWhiteSpace(opcoes.Usuario) || string.IsNullOrWhiteSpace(opcoes.Senha))
        {
            logger.LogError("Credenciais ausentes. Defina WMS__Usuario e WMS__Senha no ambiente.");
            return 2;
        }

        var baseUrl = opcoes.BaseUrl.EndsWith('/') ? opcoes.BaseUrl : opcoes.BaseUrl + "/";
        using var http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(opcoes.TimeoutSegundos),
        };

        var cliente = new ClienteWmsHttp(
            http,
            Options.Create(opcoes),
            loggerFactory.CreateLogger<ClienteWmsHttp>(),
            new SystemClock());

        try
        {
            // Payload mínimo/vazio só para exercitar o contrato (deve voltar como
            // rejeitado/pendente no homolog, é esperado).
            var resposta = await cliente.EnviarAsync(InterfaceWms.I50, "{}");
            var classificacao = resposta.Retornos.Count > 0
                ? cliente.Classificar(resposta.Retornos[0]).ToString()
                : "(sem retornos)";

            logger.LogInformation(
                "Smoke WMS OK: transacao={Transacao} sucesso={Sucesso} classificacao={Classificacao}",
                resposta.Transacao, resposta.Sucesso, classificacao);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Smoke WMS falhou.");
            return 1;
        }
    }
}
