using KitWmsIntegrador.Core.Configuracao;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Destinatários técnicos (TI) do Diagnóstico agente integrador e do alerta de autenticação do WMS. Precedência:
/// txt (<c>Diagnostico:CaminhoDestinatarios</c>; vazio = <c>&lt;telemetria&gt;/destinatarios-diagnostico.txt</c>) →
/// <c>Diagnostico:Destinatarios</c> → constante <see cref="DestinatarioPadrao"/> com aviso em log. Relê a cada chamada
/// (sem cache), para refletir edições a quente.
/// </summary>
public sealed class FonteDestinatariosDiagnostico
{
    /// <summary>Destinatário de último recurso.</summary>
    public const string DestinatarioPadrao = "ti@friozem.com.br";

    /// <summary>Nome padrão do arquivo quando <see cref="OpcoesDiagnostico.CaminhoDestinatarios"/> está vazio.</summary>
    public const string NomeArquivoPadrao = "destinatarios-diagnostico.txt";

    private readonly RegistradorTelemetria _telemetria;
    private readonly OpcoesDiagnostico _opcoes;
    private readonly ILogger<FonteDestinatariosDiagnostico> _logger;

    /// <summary>Cria a fonte de destinatários do diagnóstico.</summary>
    public FonteDestinatariosDiagnostico(
        RegistradorTelemetria telemetria,
        IOptions<OpcoesDiagnostico> opcoes,
        ILogger<FonteDestinatariosDiagnostico> logger)
    {
        _telemetria = telemetria ?? throw new ArgumentNullException(nameof(telemetria));
        _opcoes = opcoes?.Value ?? throw new ArgumentNullException(nameof(opcoes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Caminho do txt (configurado ou <c>&lt;telemetria&gt;/destinatarios-diagnostico.txt</c>).</summary>
    public string Caminho =>
        string.IsNullOrWhiteSpace(_opcoes.CaminhoDestinatarios)
            ? Path.Combine(_telemetria.PastaBase, NomeArquivoPadrao)
            : _opcoes.CaminhoDestinatarios;

    /// <summary>Lê e devolve os destinatários (txt → opção → constante), relendo o txt a cada chamada.</summary>
    public IReadOnlyList<string> Ler()
    {
        var caminho = Caminho;
        var doArquivo = LeitorListaEmails.Ler(caminho);
        if (doArquivo.Count > 0)
        {
            return doArquivo;
        }

        var daOpcao = _opcoes.ListaDestinatarios;
        if (daOpcao.Count > 0)
        {
            return daOpcao;
        }

        _logger.LogWarning(
            "Sem destinatários de diagnóstico: txt ausente/vazio ({Caminho}) e Diagnostico:Destinatarios vazio; usando {Padrao}.",
            caminho, DestinatarioPadrao);
        return new[] { DestinatarioPadrao };
    }
}
