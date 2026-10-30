using KitWmsIntegrador.Core.Modelos;

namespace KitWmsIntegrador.Core.Agrupamento;

/// <summary>
/// Uma onda de separação = conjunto de notas que compartilham o mesmo número de
/// transporte.
///
/// Regra de negócio modelada (NÃO implementa envio): cada nota vira 1 DocCarga
/// (I50); o <see cref="NumeroTransporte"/> é o <c>nroagrupseparacao</c> que
/// agrupa os DocCargas numa onda.
/// </summary>
public record Onda
{
    /// <summary>Número do transporte (agrupador). Mantido como string (zeros à esquerda).</summary>
    public string NumeroTransporte { get; init; } = string.Empty;

    /// <summary>Notas que compõem a onda.</summary>
    public IReadOnlyList<NotaFiscal> Notas { get; init; } = Array.Empty<NotaFiscal>();
}
