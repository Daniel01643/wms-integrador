namespace KitWmsIntegrador.Core.Modelos;

/// <summary>
/// Volumes/pesos da nota extraídos de <c>transp/vol</c>.
/// </summary>
public record VolumeTransporte
{
    /// <summary>Quantidade de volumes — <c>vol/qVol</c>. Mantido como string (pode vir vazio).</summary>
    public string? QuantidadeVolumes { get; init; }

    /// <summary>Peso líquido — <c>vol/pesoL</c>.</summary>
    public decimal? PesoLiquido { get; init; }

    /// <summary>Peso bruto — <c>vol/pesoB</c>.</summary>
    public decimal? PesoBruto { get; init; }
}
