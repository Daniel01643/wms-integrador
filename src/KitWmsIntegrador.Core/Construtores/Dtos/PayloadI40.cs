using System.Text.Json.Serialization;

namespace KitWmsIntegrador.Core.Construtores.Dtos;

/// <summary>
/// Payload da I40 (WEBAPI-I40, função EDI_JS_IMP_I40): raiz <c>NOTA_FISCAL</c> com 1 a 30
/// notas. Usada na devolução (Plano B). Nomes JSON exatos do layout.
/// </summary>
public sealed record PayloadI40
{
    [JsonPropertyName("NOTA_FISCAL")]
    public IReadOnlyList<NotaFiscalI40> NotaFiscal { get; init; } = Array.Empty<NotaFiscalI40>();
}

/// <summary>Uma nota fiscal da I40 (cabeçalho) + seus itens (1 a 990).</summary>
public sealed record NotaFiscalI40
{
    [JsonPropertyName("cnpjEmitente")]
    public string CnpjEmitente { get; init; } = string.Empty;

    [JsonPropertyName("cnpjRemetente")]
    public string CnpjRemetente { get; init; } = string.Empty;

    [JsonPropertyName("cnpjDestinatario")]
    public string CnpjDestinatario { get; init; } = string.Empty;

    [JsonPropertyName("cnpjLocalEntrega")]
    public string CnpjLocalEntrega { get; init; } = string.Empty;

    [JsonPropertyName("cnpjTransportadora")]
    public string CnpjTransportadora { get; init; } = string.Empty;

    [JsonPropertyName("codNatOperacaoFiscal")]
    public string CodNatOperacaoFiscal { get; init; } = string.Empty;

    [JsonPropertyName("numeroNota")]
    public string NumeroNota { get; init; } = string.Empty;

    [JsonPropertyName("serieNota")]
    public string SerieNota { get; init; } = string.Empty;

    [JsonPropertyName("chavenfe")]
    public string Chavenfe { get; init; } = string.Empty;

    [JsonPropertyName("protocolo")]
    public string Protocolo { get; init; } = string.Empty;

    [JsonPropertyName("cnpjNota")]
    public string CnpjNota { get; init; } = string.Empty;

    /// <summary>OR/AR/NO/OI/OE/DE/CD/OC/SU/SD. Devolução = "DE".</summary>
    [JsonPropertyName("tipoNota")]
    public string TipoNota { get; init; } = "DE";

    /// <summary>DD/MM/YYYY HH:MM:SS.</summary>
    [JsonPropertyName("dataEmissao")]
    public string? DataEmissao { get; init; }

    [JsonPropertyName("qtdeTotalNota")]
    public decimal QtdeTotalNota { get; init; }

    [JsonPropertyName("pesoBrutoKg")]
    public decimal PesoBrutoKg { get; init; }

    [JsonPropertyName("pesoLiquidoKg")]
    public decimal PesoLiquidoKg { get; init; }

    /// <summary>Inteiro no WMS: serializado sem casas decimais.</summary>
    [JsonPropertyName("qtdeVolume")]
    public long QtdeVolume { get; init; }

    [JsonPropertyName("valorFrete")]
    public decimal ValorFrete { get; init; }

    [JsonPropertyName("valorSeguro")]
    public decimal ValorSeguro { get; init; }

    [JsonPropertyName("valorDespesaAcessoria")]
    public decimal ValorDespesaAcessoria { get; init; }

    [JsonPropertyName("valorDesconto")]
    public decimal ValorDesconto { get; init; }

    [JsonPropertyName("valorTotal")]
    public decimal ValorTotal { get; init; }

    /// <summary>PR/CP/CD.</summary>
    [JsonPropertyName("formaTransporte")]
    public string FormaTransporte { get; init; } = "PR";

    [JsonPropertyName("baseICMS")]
    public decimal BaseICMS { get; init; }

    [JsonPropertyName("valorIPI")]
    public decimal ValorIPI { get; init; }

    [JsonPropertyName("cnpjProprietariaProduto")]
    public string CnpjProprietariaProduto { get; init; } = string.Empty;

    /// <summary>Informar ao menos um entre <c>deposito</c> e <c>cnpjArmazem</c> (enviamos os dois).</summary>
    [JsonPropertyName("deposito")]
    public string Deposito { get; init; } = string.Empty;

    [JsonPropertyName("cnpjArmazem")]
    public string CnpjArmazem { get; init; } = string.Empty;

    [JsonPropertyName("numeroCertificadoSif")]
    public string NumeroCertificadoSif { get; init; } = string.Empty;

    [JsonPropertyName("numeroPedidoCliente")]
    public string NumeroPedidoCliente { get; init; } = string.Empty;

    [JsonPropertyName("nomeMotorista")]
    public string NomeMotorista { get; init; } = string.Empty;

    [JsonPropertyName("valorICMS")]
    public decimal ValorICMS { get; init; }

    [JsonPropertyName("numeroDoca")]
    public string NumeroDoca { get; init; } = string.Empty;

    /// <summary>IMC/ICD/IMP. Devolução = "ICD" (gera chegada de veículo e descarga).</summary>
    [JsonPropertyName("tipoOperacaoNota")]
    public string TipoOperacaoNota { get; init; } = "ICD";

    [JsonPropertyName("numeroRefPedCliente")]
    public string NumeroRefPedCliente { get; init; } = string.Empty;

    [JsonPropertyName("placaVeiculoPrevistoNota")]
    public string PlacaVeiculoPrevistoNota { get; init; } = string.Empty;

    [JsonPropertyName("atributo1")]
    public string Atributo1 { get; init; } = string.Empty;

    [JsonPropertyName("atributo2")]
    public string Atributo2 { get; init; } = string.Empty;

    [JsonPropertyName("atributo3")]
    public string Atributo3 { get; init; } = string.Empty;

    /// <summary>
    /// Itens (looping 1-990). Serializado como ARRAY. O exemplo do manual mostra um objeto;
    /// se o WMS recusar o array, o teste ao vivo dirá.
    /// </summary>
    [JsonPropertyName("ITEM_NOTA_FISCAL")]
    public IReadOnlyList<ItemNotaFiscalI40> ItemNotaFiscal { get; init; } = Array.Empty<ItemNotaFiscalI40>();
}

/// <summary>Item de uma nota da I40 (ITEM_NOTA_FISCAL).</summary>
public sealed record ItemNotaFiscalI40
{
    [JsonPropertyName("numeroSequencialItem")]
    public int NumeroSequencialItem { get; init; }

    [JsonPropertyName("codProduto")]
    public string CodProduto { get; init; } = string.Empty;

    [JsonPropertyName("qtdeProduto")]
    public decimal QtdeProduto { get; init; }

    [JsonPropertyName("qtdeProdutoConfirmada")]
    public decimal QtdeProdutoConfirmada { get; init; }

    [JsonPropertyName("qtdeEmbalagem")]
    public decimal QtdeEmbalagem { get; init; }

    [JsonPropertyName("valorOriginalItem")]
    public decimal ValorOriginalItem { get; init; }

    [JsonPropertyName("valorAtualItem")]
    public decimal ValorAtualItem { get; init; }

    [JsonPropertyName("aliquotaICMS")]
    public decimal AliquotaICMS { get; init; }

    [JsonPropertyName("aliquotaIPI")]
    public decimal AliquotaIPI { get; init; }

    [JsonPropertyName("codSituacaoTributaria")]
    public string CodSituacaoTributaria { get; init; } = "040";

    [JsonPropertyName("numeroLote")]
    public string NumeroLote { get; init; } = string.Empty;

    [JsonPropertyName("valorDetalhe1")]
    public string ValorDetalhe1 { get; init; } = string.Empty;

    [JsonPropertyName("valorDetalhe2")]
    public string ValorDetalhe2 { get; init; } = string.Empty;

    [JsonPropertyName("valorDetalhe3")]
    public string ValorDetalhe3 { get; init; } = string.Empty;

    [JsonPropertyName("valorDetalhe4")]
    public string ValorDetalhe4 { get; init; } = string.Empty;

    [JsonPropertyName("dataFabricacao")]
    public string DataFabricacao { get; init; } = string.Empty;

    [JsonPropertyName("dataValidade")]
    public string DataValidade { get; init; } = string.Empty;

    [JsonPropertyName("ean")]
    public string Ean { get; init; } = string.Empty;

    [JsonPropertyName("descricao")]
    public string Descricao { get; init; } = string.Empty;

    [JsonPropertyName("ncm")]
    public string Ncm { get; init; } = string.Empty;

    [JsonPropertyName("codunidade")]
    public string CodUnidade { get; init; } = string.Empty;
}
