using System.Globalization;
using Microsoft.Extensions.Logging;

namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Persiste e lê o último slot de resumo enviado em <c>&lt;telemetria&gt;/ultimo-resumo.txt</c>
/// (ISO 8601), para a idempotência e a recuperação de slot perdido do
/// <see cref="AgendadorResumoSemanal"/>. Falhas de I/O são apenas logadas, nunca lançadas.
/// </summary>
public sealed class MarcaUltimoResumo
{
    /// <summary>Nome do arquivo da marca.</summary>
    public const string NomeArquivo = "ultimo-resumo.txt";

    private readonly RegistradorTelemetria _telemetria;
    private readonly ILogger<MarcaUltimoResumo> _logger;
    private readonly object _lock = new();

    /// <summary>Cria a marca, reaproveitando a pasta base do <see cref="RegistradorTelemetria"/>.</summary>
    public MarcaUltimoResumo(RegistradorTelemetria telemetria, ILogger<MarcaUltimoResumo> logger)
    {
        _telemetria = telemetria ?? throw new ArgumentNullException(nameof(telemetria));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Caminho completo do arquivo da marca.</summary>
    public string Caminho => Path.Combine(_telemetria.PastaBase, NomeArquivo);

    /// <summary>Lê o último slot gravado; ausente/corrompido devolve <c>null</c> (apenas loga).</summary>
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

                var conteudo = File.ReadAllText(Caminho).Trim();
                if (DateTimeOffset.TryParse(
                        conteudo, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var slot))
                {
                    return slot;
                }

                _logger.LogWarning("Marca de último resumo ilegível ({Caminho}); ignorada.", Caminho);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao ler a marca de último resumo ({Caminho}).", Caminho);
            return null;
        }
    }

    /// <summary>Grava o slot enviado de forma atômica (escreve e faz flush). Falhas são logadas.</summary>
    public void Marcar(DateTimeOffset slot)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(_telemetria.PastaBase);
                var texto = slot.ToString("o", CultureInfo.InvariantCulture);
                using var fs = new FileStream(Caminho, FileMode.Create, FileAccess.Write, FileShare.Read);
                using var sw = new StreamWriter(fs);
                sw.Write(texto);
                sw.Flush();
                fs.Flush(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao gravar a marca de último resumo ({Caminho}).", Caminho);
        }
    }
}
