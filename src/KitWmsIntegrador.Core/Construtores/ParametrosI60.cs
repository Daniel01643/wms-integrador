namespace KitWmsIntegrador.Core.Construtores;

/// <summary>Quantidade de um item (por sequência i+1 da nota) para a I60 ajustada.</summary>
public sealed record QuantidadeItemI60(int Sequencia, decimal Quantidade);

/// <summary>
/// Parâmetros do overload <c>Construir(nota, parametros)</c> da I60 (fluxo I51 → I60). Todos
/// opcionais: nulo mantém o comportamento de <c>Construir(nota)</c>.
/// </summary>
public sealed record ParametrosI60
{
    /// <summary>
    /// Quantidades separadas por sequência. Item sem entrada é omitido da I60. Nulo = quantidades da nota.
    /// </summary>
    public IReadOnlyList<QuantidadeItemI60>? Quantidades { get; init; }

    /// <summary><c>tipoOperacaoNota</c> e <c>Tpsit</c> (INE/IMP); nulo usa <c>I60:TipoOperacaoNota</c>.</summary>
    public string? TipoOperacaoNota { get; init; }

    /// <summary><c>tpacao</c> (I/UPD); nulo usa <c>I</c>.</summary>
    public string? Tpacao { get; init; }

    /// <summary>
    /// <c>qtdeVolume</c> escolhido; nulo = soma das quantidades ajustadas arredondada.
    /// <c>I60:QtdeVolumeZero=true</c> tem precedência.
    /// </summary>
    public decimal? QtdeVolume { get; init; }
}
