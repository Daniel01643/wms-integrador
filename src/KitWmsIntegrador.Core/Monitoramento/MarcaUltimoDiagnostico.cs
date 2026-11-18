using System.Globalization;
using Microsoft.Extensions.Logging;

namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Persiste o último slot do Diagnóstico agente integrador em <c>&lt;telemetria&gt;/ultimo-diagnostico.txt</c>
/// (ISO 8601). Falhas de I/O só são logadas.
/// </summary>
public sealed class MarcaUltimoDiagnostico
{
    /// <summary>Nome do arquivo da marca.</summary>
    public const string NomeArquivo = "ultimo-diagnostico.txt";

    private readonly RegistradorTelemetria _telemetria;
    private readonly ILogger<MarcaUltimoDiagnostico> _logger;
    private readonly object _lock = new();

    /// <summary>Cria a marca.</summary>
    public MarcaUltimoDiagnostico(RegistradorTelemetria telemetria, ILogger<MarcaUltimoDiagnostico> logger)
    {
        _telemetria = telemetria ?? throw new ArgumentNullException(nameof(telemetria));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Caminho do arquivo da marca.</summary>
    public string Caminho => Path.Combine(_telemetria.PastaBase, NomeArquivo);

    /// <summary>Lê o último slot (ausente/ilegível = null).</summary>
    public DateTimeOffset? Ler()
    {
        try
        {
            lock (_lock)
            {
                if (!File.Exists(Caminho))
                {
                    return null;
                }

                return DateTimeOffset.TryParse(File.ReadAllText(Caminho).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var slot)
                    ? slot
                    : null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Falha ao ler a marca do Diagnóstico agente integrador.");
            return null;
        }
    }

    /// <summary>Grava o slot enviado.</summary>
    public void Marcar(DateTimeOffset slot)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(_telemetria.PastaBase);
                var tmp = Caminho + ".tmp";
                File.WriteAllText(tmp, slot.ToString("o", CultureInfo.InvariantCulture));
                File.Move(tmp, Caminho, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Falha ao gravar a marca do Diagnóstico agente integrador.");
        }
    }
}
