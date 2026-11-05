using KitWmsIntegrador.Core.Configuracao;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Lê os destinatários do resumo semanal de um arquivo txt dedicado (fora do git), um e-mail por
/// linha, ignorando linhas em branco e comentários iniciados por '#'. Recarrega SEMPRE que
/// chamado (sem cache), para refletir edições a quente sem reiniciar o serviço. Arquivo ausente,
/// vazio ou ilegível cai no padrão mínimo <c>ti@friozem.com.br</c> com aviso em log.
/// </summary>
public sealed class FonteDestinatariosResumo
{
    /// <summary>Destinatário padrão quando o arquivo está ausente/vazio/ilegível.</summary>
    public const string DestinatarioPadrao = "ti@friozem.com.br";

    /// <summary>Nome padrão do arquivo quando <see cref="OpcoesMonitoramento.CaminhoDestinatarios"/> está vazio.</summary>
    public const string NomeArquivoPadrao = "destinatarios.txt";

    private readonly RegistradorTelemetria _telemetria;
    private readonly OpcoesMonitoramento _monitoramento;
    private readonly ILogger<FonteDestinatariosResumo> _logger;

    /// <summary>Cria a fonte de destinatários.</summary>
    public FonteDestinatariosResumo(
        RegistradorTelemetria telemetria,
        IOptions<OpcoesMonitoramento> monitoramento,
        ILogger<FonteDestinatariosResumo> logger)
    {
        _telemetria = telemetria ?? throw new ArgumentNullException(nameof(telemetria));
        _monitoramento = monitoramento?.Value ?? throw new ArgumentNullException(nameof(monitoramento));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Caminho do arquivo de destinatários (configurado ou <c>&lt;telemetria&gt;/destinatarios.txt</c>).</summary>
    public string Caminho =>
        string.IsNullOrWhiteSpace(_monitoramento.CaminhoDestinatarios)
            ? Path.Combine(_telemetria.PastaBase, NomeArquivoPadrao)
            : _monitoramento.CaminhoDestinatarios;

    /// <summary>
    /// Lê e devolve a lista de destinatários, relendo o arquivo a cada chamada. Devolve o padrão
    /// mínimo (<see cref="DestinatarioPadrao"/>) com aviso quando não há nenhum e-mail utilizável.
    /// </summary>
    public IReadOnlyList<string> Ler()
    {
        var caminho = Caminho;
        var emails = LeitorListaEmails.Ler(caminho);
        if (emails.Count > 0)
        {
            return emails;
        }

        _logger.LogWarning(
            "Arquivo de destinatários do resumo ausente/vazio ({Caminho}); usando o padrão {Padrao}.",
            caminho, DestinatarioPadrao);
        return new[] { DestinatarioPadrao };
    }
}
