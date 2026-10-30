namespace KitWmsIntegrador.Core.Modelos;

/// <summary>
/// Item de uma NF-e, extraído de cada <c>det/prod</c>.
/// </summary>
public record ItemNota
{
    /// <summary>Código do produto — <c>prod/cProd</c>.</summary>
    public string CodigoProduto { get; init; } = string.Empty;

    /// <summary>Descrição do produto — <c>prod/xProd</c>.</summary>
    public string Descricao { get; init; } = string.Empty;

    /// <summary>Quantidade comercial — <c>prod/qCom</c>.</summary>
    public decimal Quantidade { get; init; }

    /// <summary>Unidade comercial — <c>prod/uCom</c>.</summary>
    public string Unidade { get; init; } = string.Empty;

    /// <summary>Valor do produto — <c>prod/vProd</c>.</summary>
    public decimal ValorProduto { get; init; }

    /// <summary>CFOP — <c>prod/CFOP</c>.</summary>
    public string Cfop { get; init; } = string.Empty;

    /// <summary>Lote — <c>prod/rastro/nLote</c> (quando existir).</summary>
    public string? Lote { get; init; }

    /// <summary>Validade — <c>prod/rastro/dVal</c> (quando existir).</summary>
    public DateOnly? Validade { get; init; }
}
