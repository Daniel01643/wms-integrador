using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Monitoramento;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Pacote;

/// <summary>
/// Lê os destinatários do e-mail de FECHAMENTO do pacote de um arquivo txt dedicado (fora do git),
/// um e-mail por linha, ignorando linhas em branco e comentários iniciados por '#'. Recarrega
/// SEMPRE que chamado (sem cache), para refletir edições a quente sem reiniciar o serviço. Espelha
/// a <see cref="FonteDestinatariosResumo"/> no parsing (via <see cref="LeitorListaEmails"/>), mas
/// com fallback PRÓPRIO: txt ausente/vazio → <c>Email:Destinatarios</c> (comportamento atual);
/// se também vazio → lista vazia com aviso (o e-mail não é enviado).
/// </summary>
public sealed class FonteDestinatariosFechamento
{
    /// <summary>Nome padrão do arquivo quando <see cref="OpcoesEmail.CaminhoDestinatarios"/> está vazio.</summary>
    public const string NomeArquivoPadrao = "destinatarios-fechamento.txt";

    private readonly OpcoesEmail _opcoesEmail;
    private readonly RegistradorTelemetria _telemetria;
    private readonly ILogger<FonteDestinatariosFechamento> _logger;

    /// <summary>Cria a fonte de destinatários do fechamento.</summary>
    public FonteDestinatariosFechamento(
        IOptions<OpcoesEmail> opcoesEmail,
        RegistradorTelemetria telemetria,
        ILogger<FonteDestinatariosFechamento> logger)
    {
        _opcoesEmail = opcoesEmail?.Value ?? throw new ArgumentNullException(nameof(opcoesEmail));
        _telemetria = telemetria ?? throw new ArgumentNullException(nameof(telemetria));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Caminho do txt (configurado ou <c>&lt;telemetria&gt;/destinatarios-fechamento.txt</c>).</summary>
    public string Caminho =>
        string.IsNullOrWhiteSpace(_opcoesEmail.CaminhoDestinatarios)
            ? Path.Combine(_telemetria.PastaBase, NomeArquivoPadrao)
            : _opcoesEmail.CaminhoDestinatarios;

    /// <summary>
    /// Lê e devolve a lista de destinatários, relendo o arquivo a cada chamada. Precedência: txt →
    /// <c>Email:Destinatarios</c> → lista VAZIA (com aviso; o e-mail não deve ser enviado).
    /// </summary>
    public IReadOnlyList<string> Ler()
    {
        var caminho = Caminho;
        var doArquivo = LeitorListaEmails.Ler(caminho);
        if (doArquivo.Count > 0)
        {
            return doArquivo;
        }

        var fallback = _opcoesEmail.ListaDestinatarios;
        if (fallback.Count > 0)
        {
            return fallback;
        }

        _logger.LogWarning(
            "Sem destinatários de fechamento: txt ausente/vazio ({Caminho}) e Email:Destinatarios vazio; " +
            "o e-mail do pacote não será enviado.",
            caminho);
        return Array.Empty<string>();
    }
}
