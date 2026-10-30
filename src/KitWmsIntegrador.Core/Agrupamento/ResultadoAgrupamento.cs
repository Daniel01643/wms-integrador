using KitWmsIntegrador.Core.Modelos;

namespace KitWmsIntegrador.Core.Agrupamento;

/// <summary>
/// Resultado do agrupamento de notas por transporte.
/// </summary>
public record ResultadoAgrupamento
{
    /// <summary>Ondas (uma por número de transporte distinto).</summary>
    public IReadOnlyList<Onda> Ondas { get; init; } = Array.Empty<Onda>();

    /// <summary>
    /// Notas sem número de transporte (pendências) — ex.: emitentes não-BRF cujo
    /// <c>infCpl</c> não traz o padrão "Transporte:".
    /// </summary>
    public IReadOnlyList<NotaFiscal> SemTransporte { get; init; } = Array.Empty<NotaFiscal>();

    /// <summary>Total de notas consideradas (ondas + pendências).</summary>
    public int TotalNotas { get; init; }
}
