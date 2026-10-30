using System.Text.Json.Serialization;

namespace KitWmsIntegrador.Core.Construtores.Dtos;

/// <summary>
/// Payload da I60 (NOTAS_VENDA). Envelope com a lista <c>NOTAS_VENDA</c>, conforme o
/// template em <c>docs/TEMPLATES-INTERFACES.md</c>. 1 I60 por nota, referenciando o
/// DocCarga da I50 via <c>pedidos[]</c>. Nomes JSON exatos do template.
/// </summary>
public sealed record PayloadI60
{
    [JsonPropertyName("NOTAS_VENDA")]
    public IReadOnlyList<NotaVendaI60> NotasVenda { get; init; } = Array.Empty<NotaVendaI60>();
}

/// <summary>Uma nota de venda (cabeçalho de NOTAS_VENDA) + seus itens.</summary>
public sealed record NotaVendaI60
{
    /// <summary>CNPJ do emitente — <c>emit/CNPJ</c>.</summary>
    [JsonPropertyName("cnpjEmitente")]
    public string CnpjEmitente { get; init; } = string.Empty;

    /// <summary>CNPJ do remetente — <c>emit/CNPJ</c>.</summary>
    [JsonPropertyName("cnpjRemetente")]
    public string CnpjRemetente { get; init; } = string.Empty;

    /// <summary>CNPJ do destinatário — <c>dest/CNPJ</c>.</summary>
    [JsonPropertyName("cnpjDestinatario")]
    public string CnpjDestinatario { get; init; } = string.Empty;

    /// <summary>CNPJ do local de entrega — <c>dest/CNPJ</c>.</summary>
    [JsonPropertyName("cnpjLocalEntrega")]
    public string CnpjLocalEntrega { get; init; } = string.Empty;

    /// <summary>CNPJ da transportadora; se ausente, CNPJ do armazém (config).</summary>
    [JsonPropertyName("cnpjTransportadora")]
    public string CnpjTransportadora { get; init; } = string.Empty;

    /// <summary>CFOP do primeiro item — <c>det/prod/CFOP</c>.</summary>
    [JsonPropertyName("codNatOperacaoFiscal")]
    public string CodNatOperacaoFiscal { get; init; } = string.Empty;

    /// <summary>Nº da nota — <c>nNF</c>.</summary>
    [JsonPropertyName("numeroNota")]
    public string NumeroNota { get; init; } = string.Empty;

    /// <summary>Série da nota — <c>serie</c>.</summary>
    [JsonPropertyName("serieNota")]
    public string SerieNota { get; init; } = string.Empty;

    /// <summary>Chave da NFe (44 dígitos).</summary>
    [JsonPropertyName("chavenfe")]
    public string Chavenfe { get; init; } = string.Empty;

    /// <summary>
    /// Protocolo de autorização — <c>protNFe/infProt/nProt</c>. Não extraído do XML
    /// nesta etapa (fora de escopo estender o parser); branco. A CONFIRMAR.
    /// </summary>
    [JsonPropertyName("protocolo")]
    public string Protocolo { get; init; } = string.Empty;

    /// <summary>CNPJ da nota — <c>emit/CNPJ</c>.</summary>
    [JsonPropertyName("cnpjNota")]
    public string CnpjNota { get; init; } = string.Empty;

    /// <summary>Tipo de nota — fixo "AR" (template).</summary>
    [JsonPropertyName("tipoNota")]
    public string TipoNota { get; init; } = "AR";

    /// <summary>Data de emissão — <c>dhEmi</c> (DD/MM/YYYY HH:MM:SS).</summary>
    [JsonPropertyName("dataEmissao")]
    public string? DataEmissao { get; init; }

    /// <summary>Qtd total de caixas da nota — soma das qtds dos itens.</summary>
    [JsonPropertyName("qtdeTotalNota")]
    public decimal QtdeTotalNota { get; init; }

    /// <summary>Peso bruto (kg) — <c>transp/vol/pesoB</c>.</summary>
    [JsonPropertyName("pesoBrutoKg")]
    public decimal PesoBrutoKg { get; init; }

    /// <summary>Peso líquido (kg) — <c>transp/vol/pesoL</c>.</summary>
    [JsonPropertyName("pesoLiquidoKg")]
    public decimal PesoLiquidoKg { get; init; }

    /// <summary>Qtd de volumes — <c>transp/vol/qVol</c>.</summary>
    [JsonPropertyName("qtdeVolume")]
    public decimal QtdeVolume { get; init; }

    [JsonPropertyName("valorFrete")]
    public decimal ValorFrete { get; init; }

    [JsonPropertyName("valorSeguro")]
    public decimal ValorSeguro { get; init; }

    [JsonPropertyName("valorDespesaAcessoria")]
    public decimal ValorDespesaAcessoria { get; init; }

    [JsonPropertyName("valorDesconto")]
    public decimal ValorDesconto { get; init; }

    /// <summary>Valor total da nota — <c>total/ICMSTot/vNF</c>.</summary>
    [JsonPropertyName("valorTotal")]
    public decimal ValorTotal { get; init; }

    /// <summary>Forma de transporte — fixo "PR" (template).</summary>
    [JsonPropertyName("formaTransporte")]
    public string FormaTransporte { get; init; } = "PR";

    [JsonPropertyName("baseICMS")]
    public decimal BaseICMS { get; init; }

    [JsonPropertyName("valorIPI")]
    public decimal ValorIPI { get; init; }

    /// <summary>CNPJ da proprietária do produto — CONFIG.</summary>
    [JsonPropertyName("cnpjProprietariaProduto")]
    public string CnpjProprietariaProduto { get; init; } = string.Empty;

    /// <summary>Depósito — CONFIG (BRF: JD).</summary>
    [JsonPropertyName("deposito")]
    public string Deposito { get; init; } = string.Empty;

    /// <summary>CNPJ do armazém — CONFIG.</summary>
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

    /// <summary>Tipo de operação da nota — fixo "INE" (template). A CONFIRMAR p/ BRF.</summary>
    [JsonPropertyName("tipoOperacaoNota")]
    public string TipoOperacaoNota { get; init; } = "INE";

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

    /// <summary>Ação — sempre "I" (template). Caixa exata: <c>tpacao</c>.</summary>
    [JsonPropertyName("tpacao")]
    public string Tpacao { get; init; } = "I";

    /// <summary>Nº do embarque — branco (template).</summary>
    [JsonPropertyName("NroEmb")]
    public string NroEmb { get; init; } = string.Empty;

    /// <summary>Tipo de separação — branco (template).</summary>
    [JsonPropertyName("Tpsep")]
    public string Tpsep { get; init; } = string.Empty;

    /// <summary>
    /// Tipo de situação — fixo "INE" (template): IMB gera embarque / INE vincula sem
    /// gerar embarque. A CONFIRMAR (IMB vs INE p/ BRF).
    /// </summary>
    [JsonPropertyName("Tpsit")]
    public string Tpsit { get; init; } = "INE";

    /// <summary>Tipo de nota — fixo "NV" (nota venda) (template).</summary>
    [JsonPropertyName("Tpnota")]
    public string Tpnota { get; init; } = "NV";

    /// <summary>
    /// Remessas/pedidos que geraram a nota — associa ao DocCarga da I50.
    /// A CONFIRMAR (nNF vs xPed). Default: [numeroPedido derivado do nNF].
    /// </summary>
    [JsonPropertyName("pedidos")]
    public IReadOnlyList<string> Pedidos { get; init; } = Array.Empty<string>();

    /// <summary>Itens da nota.</summary>
    [JsonPropertyName("ITEM_NOTA_VENDA")]
    public IReadOnlyList<ItemNotaVendaI60> ItemNotaVenda { get; init; } = Array.Empty<ItemNotaVendaI60>();
}

/// <summary>Item de uma nota de venda (ITEM_NOTA_VENDA).</summary>
public sealed record ItemNotaVendaI60
{
    /// <summary>Sequência do item na nota.</summary>
    [JsonPropertyName("numeroSequencialItem")]
    public int NumeroSequencialItem { get; init; }

    /// <summary>Código do produto — <c>cProd</c>.</summary>
    [JsonPropertyName("codProduto")]
    public string CodProduto { get; init; } = string.Empty;

    /// <summary>Qtd do produto (unidade menor) — <c>qCom</c>.</summary>
    [JsonPropertyName("qtdeProduto")]
    public decimal QtdeProduto { get; init; }

    /// <summary>Qtd confirmada — <c>qCom</c>.</summary>
    [JsonPropertyName("qtdeProdutoConfirmada")]
    public decimal QtdeProdutoConfirmada { get; init; }

    /// <summary>Qtd de caixas/embalagem. A CONFIRMAR; replicamos <c>qCom</c>.</summary>
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

    /// <summary>Código de situação tributária — fixo "040" (template). A CONFIRMAR.</summary>
    [JsonPropertyName("codSituacaoTributaria")]
    public string CodSituacaoTributaria { get; init; } = "040";

    /// <summary>Nº do lote — <c>rastro/nLote</c> se houver.</summary>
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

    /// <summary>Data de fabricação — <c>rastro/dFab</c> (não extraído; branco). A CONFIRMAR.</summary>
    [JsonPropertyName("dataFabricacao")]
    public string DataFabricacao { get; init; } = string.Empty;

    /// <summary>Data de validade — <c>rastro/dVal</c> (DD/MM/YYYY HH:MM:SS) se houver.</summary>
    [JsonPropertyName("dataValidade")]
    public string DataValidade { get; init; } = string.Empty;
}
