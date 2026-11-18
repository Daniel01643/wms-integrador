using KitWmsIntegrador.Core.Diagnostico;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.DependencyInjection;

namespace KitWmsIntegrador.Worker;

/// <summary>
/// Registro dos serviços do relatório "Diagnóstico agente integrador". Compartilhado pelo host e pela CLI
/// <c>--diagnostico</c>. Pressupõe os registros-base (opções, relógio, logging, telemetria, e-mail, diário e fonte de
/// destinatários do diagnóstico) e <c>AdicionarSeparacaoI51()</c> feitos pelo chamador.
/// </summary>
public static class RegistroDiagnostico
{
    /// <summary>Registra marca, coletor, saúde, gerador, enviador, serviço do diagnóstico e a amostragem de memória.</summary>
    public static IServiceCollection AdicionarDiagnostico(this IServiceCollection services)
    {
        services.AddSingleton<MarcaUltimoDiagnostico>();
        services.AddSingleton<LeitorAuditoriaDevolucao>();
        services.AddSingleton<IProvedorSaudeSistema, ProvedorSaudeSistema>();
        services.AddSingleton<ColetorDiagnostico>();
        services.AddSingleton<IColetorDiagnostico>(sp => sp.GetRequiredService<ColetorDiagnostico>());
        services.AddSingleton<IGeradorDiagnostico, GeradorDiagnostico>();
        services.AddSingleton<EnviadorDiagnostico>();
        services.AddSingleton<ServicoDiagnostico>();
        services.AddSingleton<IColetorAmostraSistema, ColetorAmostraSistema>();
        services.AddSingleton<ServicoAmostraSaude>();
        return services;
    }
}
