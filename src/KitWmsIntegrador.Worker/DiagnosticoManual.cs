using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Diagnostico;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KitWmsIntegrador.Worker;

/// <summary>
/// CLI <c>--diagnostico [--enviar]</c>: gera o "Diagnóstico agente integrador" das últimas 24 h (HTML e PDF em
/// <c>Relatorio:PastaSaida\diagnostico</c>) sem esperar as 14:00 e sem tocar a marca do slot. Com <c>--enviar</c> tenta
/// também o e-mail (só aos destinatários de diagnóstico). Saída: 0 ok, 1 erro de geração/gravação.
/// </summary>
public static class DiagnosticoManual
{
    /// <summary>Argumento que aciona a CLI.</summary>
    public const string Argumento = "--diagnostico";

    /// <summary>Executa a CLI.</summary>
    public static async Task<int> ExecutarAsync(string[] args)
    {
        var config = ConfiguracaoManual.Criar(args);
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSimpleConsole());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IConfiguration>(config);
        services.AddOptions<OpcoesPasta>().Configure(p =>
        {
            var lido = ConfiguracaoManual.Pastas(config);
            p.Entrada = lido.Entrada;
            p.Processados = lido.Processados;
            p.Erro = lido.Erro;
            p.Estado = lido.Estado;
        });
        Bind<OpcoesI51>(services, config, OpcoesI51.Secao);
        Bind<OpcoesI60>(services, config, OpcoesI60.Secao);
        Bind<OpcoesRelatorioI60>(services, config, OpcoesRelatorioI60.Secao);
        Bind<OpcoesRelatorio>(services, config, OpcoesRelatorio.Secao);
        Bind<OpcoesEmail>(services, config, OpcoesEmail.Secao);
        Bind<OpcoesMonitoramento>(services, config, OpcoesMonitoramento.Secao);
        Bind<OpcoesDevolucao>(services, config, OpcoesDevolucao.Secao);
        Bind<OpcoesLimpeza>(services, config, OpcoesLimpeza.Secao);
        Bind<OpcoesDiagnostico>(services, config, OpcoesDiagnostico.Secao);
        services.AddSingleton<RegistradorTelemetria>();
        services.AddSingleton<RegistroDiagnosticoArquivo>();
        services.AddSingleton<FonteDestinatariosResumo>();
        services.AddSingleton<FonteDestinatariosDiagnostico>();
        services.AddSingleton<IEnviadorEmail, EnviadorEmailSmtp>();
        services.AddSingleton<Core.Orquestrador.ClassificadorRetorno>();
        services.AddSingleton<Core.Leitura.LeitorNotaFiscalXml>();
        services.AddSingleton<Core.Construtores.IConstrutorI60, Core.Construtores.ConstrutorI60>();
        services.AddOptions<OpcoesOperacionais>().Bind(config.GetSection(OpcoesOperacionais.Secao));
        services.AddSingleton<IClienteWms>(_ => throw new InvalidOperationException("Cliente WMS não é usado pelo diagnóstico."));
        services.AddSingleton<IClienteWmsBruto>(_ => throw new InvalidOperationException("Cliente WMS não é usado pelo diagnóstico."));
        services.AdicionarSeparacaoI51();
        services.AdicionarDiagnostico();

        await using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("DiagnosticoManual");
        try
        {
            var servico = provider.GetRequiredService<ServicoDiagnostico>();
            var (html, enviado) = await servico.GerarAgoraAsync(args.Contains("--enviar"), CancellationToken.None);
            logger.LogInformation("Diagnóstico gerado: {Html} (e-mail enviado: {Enviado}).", html, enviado);
            Console.WriteLine(html);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao gerar o Diagnóstico agente integrador.");
            return 1;
        }
    }

    private static void Bind<T>(IServiceCollection services, IConfiguration config, string secao)
        where T : class =>
        services.AddOptions<T>().Bind(config.GetSection(secao));
}
