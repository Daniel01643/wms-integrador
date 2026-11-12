using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KitWmsIntegrador.Worker;

/// <summary>
/// CLI <c>--relatorio-i60 [--enviar]</c>: gera o relatório diário da I51/I60 do estado atual (HTML + PDF em
/// <c>Relatorio:PastaSaida\i60</c>) sem esperar as 12:00. Com <c>--enviar</c> tenta também o e-mail.
/// Saída: 0 ok, 1 erro de geração/gravação.
/// </summary>
public static class RelatorioManualI60
{
    /// <summary>Argumento que aciona a CLI.</summary>
    public const string Argumento = "--relatorio-i60";

    /// <summary>Executa a CLI.</summary>
    public static async Task<int> ExecutarAsync(string[] args)
    {
        var config = ConfiguracaoManual.Criar();
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
        services.AddSingleton<RegistradorTelemetria>();
        services.AddSingleton<FonteDestinatariosResumo>();
        services.AddSingleton<IEnviadorEmail, EnviadorEmailSmtp>();
        services.AddSingleton<Core.Orquestrador.ClassificadorRetorno>();
        services.AddSingleton<Core.Leitura.LeitorNotaFiscalXml>();
        services.AddSingleton<Core.Construtores.IConstrutorI60, Core.Construtores.ConstrutorI60>();
        services.AddOptions<OpcoesOperacionais>().Bind(config.GetSection(OpcoesOperacionais.Secao));
        services.AddSingleton<IClienteWms>(_ => throw new InvalidOperationException("Cliente WMS não é usado pelo relatório."));
        services.AddSingleton<IClienteWmsBruto>(_ => throw new InvalidOperationException("Cliente WMS não é usado pelo relatório."));
        services.AdicionarSeparacaoI51();

        await using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("RelatorioManualI60");
        try
        {
            var servico = provider.GetRequiredService<ServicoRelatorioDiarioI60>();
            var (html, pdf, enviado) = await servico.GerarAgoraAsync(args.Contains("--enviar"), CancellationToken.None);
            logger.LogInformation("Relatório gerado: {Html} e {Pdf} (e-mail enviado: {Enviado}).", html, pdf, enviado);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao gerar o relatório diário da I51/I60.");
            return 1;
        }
    }

    private static void Bind<T>(IServiceCollection services, IConfiguration config, string secao)
        where T : class =>
        services.AddOptions<T>().Bind(config.GetSection(secao));
}
