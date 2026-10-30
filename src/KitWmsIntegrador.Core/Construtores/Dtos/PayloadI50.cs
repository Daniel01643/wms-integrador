using System.Text.Json.Serialization;

namespace KitWmsIntegrador.Core.Construtores.Dtos;

/// <summary>
/// Payload da I50 (PEDIDO/DocCarga). Envelope com a lista <c>PEDIDO</c>, conforme o
/// template em <c>docs/TEMPLATES-INTERFACES.md</c>. 1 nota = 1 DocCarga = 1 I50; o
/// agrupador da onda (<c>nroagrupseparacao</c>) é o número do transporte.
///
/// Os nomes JSON casam EXATAMENTE (incluindo caixa) os do template — fonte de verdade
/// do contrato. Campos opcionais são <c>string?</c> para herdar o ignore-null.
/// </summary>
public sealed record PayloadI50
{
    [JsonPropertyName("PEDIDO")]
    public IReadOnlyList<PedidoI50> Pedido { get; init; } = Array.Empty<PedidoI50>();
}

/// <summary>Um DocCarga (cabeçalho do PEDIDO) + seus itens.</summary>
public sealed record PedidoI50
{
    /// <summary>Doca — mandar em branco (template).</summary>
    [JsonPropertyName("doca")]
    public string Doca { get; init; } = string.Empty;

    /// <summary>CNPJ do proprietário/depositante — vem da config operacional.</summary>
    [JsonPropertyName("cnpjProprietario")]
    public string CnpjProprietario { get; init; } = string.Empty;

    /// <summary>CNPJ do destinatário da entrega — <c>dest/CNPJ</c> do XML.</summary>
    [JsonPropertyName("cnpjDestinatario")]
    public string CnpjDestinatario { get; init; } = string.Empty;

    /// <summary>CNPJ da transportadora — mandar em branco (template).</summary>
    [JsonPropertyName("cnpjTransportadora")]
    public string CnpjTransportadora { get; init; } = string.Empty;

    /// <summary>Nº do pedido/remessa; amarra I50↔I60. Derivado do <c>nNF</c>.</summary>
    [JsonPropertyName("numeroPedido")]
    public string NumeroPedido { get; init; } = string.Empty;

    /// <summary>Tipo de carga — CONFIG (<c>OpcoesI50.TipoCarga</c>).</summary>
    [JsonPropertyName("tipoCarga")]
    public string TipoCarga { get; init; } = string.Empty;

    /// <summary>Tipo de acondicionamento — CONFIG. A CONFIRMAR (PC vs PP).</summary>
    [JsonPropertyName("tipoAcondicionamento")]
    public string TipoAcondicionamento { get; init; } = string.Empty;

    /// <summary>Ação — sempre "I" (template).</summary>
    [JsonPropertyName("tpAcao")]
    public string TpAcao { get; init; } = "I";

    /// <summary>Tipo de separação 1 — CONFIG (<c>OpcoesI50.TipoSeparacao</c>).</summary>
    [JsonPropertyName("tipoSeparacao")]
    public string TipoSeparacao { get; init; } = string.Empty;

    /// <summary>Tipo de separação 2 — CONFIG (<c>OpcoesI50.TipoSeparacao2</c>).</summary>
    [JsonPropertyName("tipoSeparacao2")]
    public string TipoSeparacao2 { get; init; } = string.Empty;

    /// <summary>Data de geração da remessa/pedido — emissão do XML (DD/MM/YYYY HH:MM:SS).</summary>
    [JsonPropertyName("dataEmissao")]
    public string? DataEmissao { get; init; }

    /// <summary>
    /// Previsão de entrega. A CONFIRMAR (emissão + N dias?). Default: emissão do XML.
    /// </summary>
    [JsonPropertyName("dataprev")]
    public string? Dataprev { get; init; }

    /// <summary>Carga/embarque/carro — a definir; em branco por ora.</summary>
    [JsonPropertyName("numeroPlanejamento")]
    public string NumeroPlanejamento { get; init; } = string.Empty;

    /// <summary>Nº do embarque — opcional; omitido (null) nesta etapa.</summary>
    [JsonPropertyName("numeroEmbarque")]
    public string? NumeroEmbarque { get; init; }

    /// <summary>AGRUPADOR DA ONDA = nº do transporte (string, zeros à esquerda).</summary>
    [JsonPropertyName("nroagrupseparacao")]
    public string Nroagrupseparacao { get; init; } = string.Empty;

    /// <summary>Placa do veículo — branco (template).</summary>
    [JsonPropertyName("placaVeiculo")]
    public string PlacaVeiculo { get; init; } = string.Empty;

    /// <summary>Depósito — CONFIG operacional (BRF: JD).</summary>
    [JsonPropertyName("deposito")]
    public string Deposito { get; init; } = string.Empty;

    /// <summary>CNPJ do armazém — CONFIG operacional.</summary>
    [JsonPropertyName("cnpjArmazem")]
    public string CnpjArmazem { get; init; } = string.Empty;

    /// <summary>Nº do lacre — branco (template).</summary>
    [JsonPropertyName("numeroLacre")]
    public string NumeroLacre { get; init; } = string.Empty;

    /// <summary>Local de entrega: logradouro + número — do destinatário do XML.</summary>
    [JsonPropertyName("indicaLocalEntregaNome")]
    public string IndicaLocalEntregaNome { get; init; } = string.Empty;

    /// <summary>Local de entrega: bairro — do destinatário do XML.</summary>
    [JsonPropertyName("indicaLocalEntregaBairro")]
    public string IndicaLocalEntregaBairro { get; init; } = string.Empty;

    /// <summary>Local de entrega: cidade — do destinatário do XML.</summary>
    [JsonPropertyName("indicaLocalEntregaCidade")]
    public string IndicaLocalEntregaCidade { get; init; } = string.Empty;

    /// <summary>Local de entrega: CEP — do destinatário do XML.</summary>
    [JsonPropertyName("indicaLocalEntregaCep")]
    public string IndicaLocalEntregaCep { get; init; } = string.Empty;

    /// <summary>Local de entrega: UF — do destinatário do XML.</summary>
    [JsonPropertyName("indicaLocalEntregaUf")]
    public string IndicaLocalEntregaUf { get; init; } = string.Empty;

    /// <summary>Local de entrega: telefone — não disponível no XML; branco.</summary>
    [JsonPropertyName("indicaLocalEntregaTel")]
    public string IndicaLocalEntregaTel { get; init; } = string.Empty;

    [JsonPropertyName("atributo1")]
    public string Atributo1 { get; init; } = string.Empty;

    [JsonPropertyName("atributo2")]
    public string Atributo2 { get; init; } = string.Empty;

    [JsonPropertyName("atributo3")]
    public string Atributo3 { get; init; } = string.Empty;

    /// <summary>Sequência de embarque (carga montada). Default 1 (template).</summary>
    [JsonPropertyName("seqEmbarque")]
    public int SeqEmbarque { get; init; } = 1;

    /// <summary>Previsão de entrega (DhprevEntrega) — branco (template).</summary>
    [JsonPropertyName("DhprevEntrega")]
    public string DhprevEntrega { get; init; } = string.Empty;

    /// <summary>
    /// Previsão de embarque. A CONFIRMAR (template Unidasul: emissão + 2 dias).
    /// Default: emissão do XML. Omitido quando nulo.
    /// </summary>
    [JsonPropertyName("DhprevEmbarque")]
    public string? DhprevEmbarque { get; init; }

    /// <summary>Previsão de serviço (DhprevServico) — branco (template).</summary>
    [JsonPropertyName("DhprevServico")]
    public string DhprevServico { get; init; } = string.Empty;

    /// <summary>Nº de referência do pedido do cliente — branco (template).</summary>
    [JsonPropertyName("numeroRefPedCliente")]
    public string NumeroRefPedCliente { get; init; } = string.Empty;

    /// <summary>Observação livre.</summary>
    [JsonPropertyName("Observacao")]
    public string Observacao { get; init; } = string.Empty;

    /// <summary>Itens do pedido.</summary>
    [JsonPropertyName("ITEM_PEDIDO")]
    public IReadOnlyList<ItemPedidoI50> ItemPedido { get; init; } = Array.Empty<ItemPedidoI50>();
}

/// <summary>Item de um PEDIDO (ITEM_PEDIDO).</summary>
public sealed record ItemPedidoI50
{
    /// <summary>Sequência do item no pedido/remessa.</summary>
    [JsonPropertyName("numeroSequencialItem")]
    public int NumeroSequencialItem { get; init; }

    /// <summary>Ação — sempre "I" (template).</summary>
    [JsonPropertyName("tpAcao")]
    public string TpAcao { get; init; } = "I";

    /// <summary>Código do produto — <c>cProd</c>.</summary>
    [JsonPropertyName("codigoProduto")]
    public string CodigoProduto { get; init; } = string.Empty;

    /// <summary>
    /// Qtd de embalagens/caixas a separar. A CONFIRMAR: sem fator de conversão
    /// no XML, replicamos a qtd comercial (<c>qCom</c>).
    /// </summary>
    [JsonPropertyName("qtdeEmbalagem")]
    public decimal QtdeEmbalagem { get; init; }

    /// <summary>Qtd na unidade menor (UND/KG) — <c>qCom</c>.</summary>
    [JsonPropertyName("quantidadeProduto")]
    public decimal QuantidadeProduto { get; init; }

    /// <summary>Base da quantidade — CONFIG (<c>OpcoesI50.QtdMandante</c>). A CONFIRMAR (U vs E).</summary>
    [JsonPropertyName("qtdMandante")]
    public string QtdMandante { get; init; } = string.Empty;

    /// <summary>Tipo de separação 1 — CONFIG (mesmo da sep1 do cabeçalho).</summary>
    [JsonPropertyName("tipoSeparacao")]
    public string TipoSeparacao { get; init; } = string.Empty;

    /// <summary>Tipo de separação 2 — CONFIG (mesmo da sep2 do cabeçalho).</summary>
    [JsonPropertyName("tipoSeparacao2")]
    public string TipoSeparacao2 { get; init; } = string.Empty;

    [JsonPropertyName("valorDetalhe1")]
    public string ValorDetalhe1 { get; init; } = string.Empty;

    [JsonPropertyName("valorDetalhe2")]
    public string ValorDetalhe2 { get; init; } = string.Empty;

    [JsonPropertyName("valorDetalhe3")]
    public string ValorDetalhe3 { get; init; } = string.Empty;

    [JsonPropertyName("valorDetalhe4")]
    public string ValorDetalhe4 { get; init; } = string.Empty;

    /// <summary>Tipo de especificação do item — LOTE/DATA/ShelfLife quando aplicável.</summary>
    [JsonPropertyName("tipoEspecificacaoItem")]
    public string TipoEspecificacaoItem { get; init; } = string.Empty;

    /// <summary>Valor da especificação do item.</summary>
    [JsonPropertyName("valorEspecificacaoItem")]
    public string ValorEspecificacaoItem { get; init; } = string.Empty;
}
