using System.Globalization;
using Microsoft.Extensions.Logging;

namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Persiste o último slot do relatório diário da I51/I60 em <c>&lt;telemetria&gt;/ultimo-relatorio-i60.txt</c>
/// (ISO 8601). Falhas de I/O só são logadas.
/// </summary>
public sealed class MarcaUltimoRelatorioI60
{
    /// <summary>Nome do arquivo da marca.</summary>
    public const string NomeArquivo = "ultimo-relatorio-i60.txt";

    private readonly RegistradorTelemetria _telemetria;
    private readonly ILogger<MarcaUltimoRelatorioI60> _logger;
    private readonly object _lock = new();

    /// <summary>Cria a marca.</summary>
    public MarcaUltimoRelatorioI60(RegistradorTelemetria telemetria, ILogger<MarcaUltimoRelatorioI60> logger)
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
            _logger.LogError(ex, "Falha ao ler a marca do relatório diário I60.");
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
            _logger.LogError(ex, "Falha ao gravar a marca do relatório diário I60.");
        }
    }
}
