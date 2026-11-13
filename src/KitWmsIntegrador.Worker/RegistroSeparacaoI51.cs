using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Relatorio;
using KitWmsIntegrador.Core.Separacao;
using Microsoft.Extensions.DependencyInjection;

namespace KitWmsIntegrador.Worker;

/// <summary>
/// Registro dos serviços do fluxo I51 → I60 e do relatório diário. Compartilhado pelo host e pela CLI
/// <c>--relatorio-i60</c>. Pressupõe os registros-base (opções, relógio, logging, cliente WMS, construtor da I60,
/// leitor de XML, classificador, telemetria e e-mail) feitos pelo chamador.
/// </summary>
public static class RegistroSeparacaoI51
{
    /// <summary>Registra repositório, consulta, casamento, I60 pós-separação e relatório diário.</summary>
    public static IServiceCollection AdicionarSeparacaoI51(this IServiceCollection services)
    {
        services.AddSingleton<CaminhosI51>();
        services.AddSingleton<IRepositorioSeparacao, RepositorioSeparacaoArquivo>();
        services.AddSingleton<RepositorioOrfaosI51>();
        services.AddSingleton<ArmazenamentoBrutoI51>();
        services.AddSingleton<ClienteI51>();
        services.AddSingleton<CasadorDocumentoNota>();
        services.AddSingleton<IRegistradorSeparacao, RegistradorSeparacao>();
        services.AddSingleton<ProcessadorI60Separacao>();
        services.AddSingleton<ComandosI51>();
        services.AddSingleton<AlertaI60>();
        services.AddSingleton<ServicoSeparacaoI51>();

        services.AddSingleton<MarcaUltimoRelatorioI60>();
        services.AddSingleton<FonteDestinatariosRelatorioI60>();
        services.AddSingleton<IDestinatariosAlertaI60>(sp => sp.GetRequiredService<FonteDestinatariosRelatorioI60>());
        services.AddSingleton<IGeradorRelatorioI60, GeradorRelatorioI60>();
        services.AddSingleton<EnviadorRelatorioDiarioI60>();
        services.AddSingleton<ServicoRelatorioDiarioI60>();
        return services;
    }
}
