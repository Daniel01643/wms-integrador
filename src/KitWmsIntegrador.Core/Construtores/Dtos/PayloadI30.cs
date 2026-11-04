using System.Text.Json.Serialization;

namespace KitWmsIntegrador.Core.Construtores.Dtos;

/// <summary>
/// Payload da I30 (PRODUTO). Envelope com a lista <c>PRODUTO</c>, conforme o template
/// em <c>docs/TEMPLATES-INTERFACES.md</c>. Enviado como pré-condição da I50 quando o WMS
/// reporta "produto não cadastrado"; cada item da nota vira um <see cref="ProdutoI30"/>.
///
/// IMPORTANTE: o produto criado por contenção nasce "genérico" (defaults do template) e
/// ENTRA EM REVISÃO do colaborador — o objetivo do I30 é só desbloquear a nota, não
/// substituir o cadastro mestre. Nomes JSON exatos do template.
/// </summary>
public sealed record PayloadI30
{
    [JsonPropertyName("PRODUTO")]
    public IReadOnlyList<ProdutoI30> Produto { get; init; } = Array.Empty<ProdutoI30>();
}

/// <summary>
/// Um produto (elemento de PRODUTO). Valores fixos/defaults seguem o template e os
/// achados ao vivo (ver <c>docs/TESTE-COMUNICACAO.md</c>): <c>codclassificacao="2"</c> e
/// <c>codempresa</c> da config fazem o produto cadastrar na hora.
/// </summary>
public sealed record ProdutoI30
{
    /// <summary>Tipo de produto 1 — fixo "2" (picking).</summary>
    [JsonPropertyName("codtpprod1")]
    public string Codtpprod1 { get; init; } = "2";

    /// <summary>Código do produto — <c>cProd</c> SEM zeros à esquerda.</summary>
    [JsonPropertyName("codigo")]
    public string Codigo { get; init; } = string.Empty;

    /// <summary>Descrição — <c>xProd</c>.</summary>
    [JsonPropertyName("descricao")]
    public string Descricao { get; init; } = string.Empty;

    /// <summary>Código da empresa proprietária — CONFIG (<c>CodigoDepositante</c>, confirmado "3509308").</summary>
    [JsonPropertyName("codempresa")]
    public string Codempresa { get; init; } = string.Empty;

    /// <summary>Unidade — <c>uCom</c> (precisa existir no WMS: CX, UN...).</summary>
    [JsonPropertyName("codunidade")]
    public string Codunidade { get; init; } = string.Empty;

    /// <summary>Controle unidade fiscal — "E" embalagem / "U" unidade (template).</summary>
    [JsonPropertyName("ctrunifiscal")]
    public string Ctrunifiscal { get; init; } = "E";

    /// <summary>Controle unidade física — "E" peso padrão / "U" peso variável (template).</summary>
    [JsonPropertyName("ctrunifisica")]
    public string Ctrunifisica { get; init; } = "E";

    /// <summary>Descrição abreviada — <c>xProd</c>.</summary>
    [JsonPropertyName("aliasproduto")]
    public string Aliasproduto { get; init; } = string.Empty;

    /// <summary>Código do cliente — <c>cProd</c> COM zeros à esquerda (como no XML).</summary>
    [JsonPropertyName("codcliente")]
    public string Codcliente { get; init; } = string.Empty;

    /// <summary>EAN do cliente — <c>cEAN</c> (EAN13) ou branco.</summary>
    [JsonPropertyName("eancliente")]
    public string Eancliente { get; init; } = string.Empty;

    [JsonPropertyName("validaeancliente")]
    public string Validaeancliente { get; init; } = "N";

    /// <summary>Tipologia — "SEC"/"RES"/"CONG". Default "SEC" (produto entra em revisão).</summary>
    [JsonPropertyName("codtipologia")]
    public string Codtipologia { get; init; } = "SEC";

    [JsonPropertyName("tipoqtd")]
    public string Tipoqtd { get; init; } = "C";

    [JsonPropertyName("organimal")]
    public string Organimal { get; init; } = "N";

    [JsonPropertyName("exportacao")]
    public string Exportacao { get; init; } = "N";

    [JsonPropertyName("qtdliquido")]
    public string Qtdliquido { get; init; } = "1.0000";

    [JsonPropertyName("qtdbruto")]
    public string Qtdbruto { get; init; } = "1.0000";

    [JsonPropertyName("qtdmedio")]
    public string Qtdmedio { get; init; } = "1.0000";

    [JsonPropertyName("qtdm3")]
    public string Qtdm3 { get; init; } = "1.0000";

    [JsonPropertyName("codclassabc")]
    public string Codclassabc { get; init; } = "A";

    [JsonPropertyName("codclassabcf")]
    public string Codclassabcf { get; init; } = "B";

    [JsonPropertyName("prioridade")]
    public string Prioridade { get; init; } = "0";

    /// <summary>EAN (DUN14) — não extraído do XML; branco (template).</summary>
    [JsonPropertyName("ean")]
    public string Ean { get; init; } = string.Empty;

    [JsonPropertyName("aliqicms")]
    public string Aliqicms { get; init; } = "0.00";

    [JsonPropertyName("aliqipi")]
    public string Aliqipi { get; init; } = "0.00";

    [JsonPropertyName("codmensagemanter")]
    public string Codmensagemanter { get; init; } = string.Empty;

    [JsonPropertyName("codmensagempost")]
    public string Codmensagempost { get; init; } = string.Empty;

    /// <summary>Tipo de validade — "D"/"M"/"N". Default "N" (template; alimento entra em revisão).</summary>
    [JsonPropertyName("tipovalidade")]
    public string Tipovalidade { get; init; } = "N";

    [JsonPropertyName("qtddiascriticos")]
    public string Qtddiascriticos { get; init; } = "0";

    [JsonPropertyName("qtddiasvalidade")]
    public string Qtddiasvalidade { get; init; } = "0";

    [JsonPropertyName("qtddiasmaturacao")]
    public string Qtddiasmaturacao { get; init; } = "0";

    [JsonPropertyName("qtddiasvencimento")]
    public string Qtddiasvencimento { get; init; } = "0";

    /// <summary>Classificação — fixo "2" (confirmado ao vivo; "DEF" falhava para a BRF).</summary>
    [JsonPropertyName("codclassificacao")]
    public string Codclassificacao { get; init; } = "2";

    [JsonPropertyName("codclassificacaosif")]
    public string Codclassificacaosif { get; init; } = string.Empty;

    [JsonPropertyName("codgrupofatura")]
    public string Codgrupofatura { get; init; } = string.Empty;

    [JsonPropertyName("ctrunifatura")]
    public string Ctrunifatura { get; init; } = "U";

    [JsonPropertyName("percentualseguro")]
    public string Percentualseguro { get; init; } = "0";

    [JsonPropertyName("codtpprod2")]
    public string Codtpprod2 { get; init; } = "1";

    [JsonPropertyName("codtpprod3")]
    public string Codtpprod3 { get; init; } = "3";

    /// <summary>Temperatura padrão — default "25.00" (template; produto entra em revisão).</summary>
    [JsonPropertyName("temppadrao")]
    public string Temppadrao { get; init; } = "25.00";

    [JsonPropertyName("tempcongelamento")]
    public string Tempcongelamento { get; init; } = "0.00";

    [JsonPropertyName("temprcf")]
    public string Temprcf { get; init; } = "0.00";

    [JsonPropertyName("qtdmedcxpick")]
    public string Qtdmedcxpick { get; init; } = "0";

    /// <summary>Ação — só inclusão "I" (contenção; sem update).</summary>
    [JsonPropertyName("tpacao")]
    public string Tpacao { get; init; } = "I";

    /// <summary>Código tributário — fixo "040" (template).</summary>
    [JsonPropertyName("codigotributario")]
    public string Codigotributario { get; init; } = "040";

    [JsonPropertyName("codunidadefisica")]
    public string Codunidadefisica { get; init; } = string.Empty;

    [JsonPropertyName("tparmazpicking")]
    public string Tparmazpicking { get; init; } = string.Empty;

    [JsonPropertyName("qtdunembalag")]
    public string Qtdunembalag { get; init; } = "1.000";

    [JsonPropertyName("qtdpcembalag")]
    public string Qtdpcembalag { get; init; } = "1.000";

    [JsonPropertyName("codcatmanuseio")]
    public string Codcatmanuseio { get; init; } = "4";

    [JsonPropertyName("qtddiasvalcrisup")]
    public string Qtddiasvalcrisup { get; init; } = string.Empty;

    [JsonPropertyName("qtddiasvalcriinf")]
    public string Qtddiasvalcriinf { get; init; } = string.Empty;

    [JsonPropertyName("qtddiasvalncrisup")]
    public string Qtddiasvalncrisup { get; init; } = string.Empty;

    [JsonPropertyName("qtddiasvalncriinf")]
    public string Qtddiasvalncriinf { get; init; } = string.Empty;

    [JsonPropertyName("coddetalhe1")]
    public string Coddetalhe1 { get; init; } = string.Empty;

    [JsonPropertyName("coddetalhe2")]
    public string Coddetalhe2 { get; init; } = string.Empty;

    [JsonPropertyName("coddetalhe3")]
    public string Coddetalhe3 { get; init; } = string.Empty;

    [JsonPropertyName("coddetalhe4")]
    public string Coddetalhe4 { get; init; } = string.Empty;

    [JsonPropertyName("qtdunperigosa")]
    public string Qtdunperigosa { get; init; } = string.Empty;

    /// <summary>Qtd por camada — fixo "1" (se 0 não cria embalagem).</summary>
    [JsonPropertyName("qtdcxcamada")]
    public string Qtdcxcamada { get; init; } = "1";

    [JsonPropertyName("largura")]
    public string Largura { get; init; } = "1.00";

    [JsonPropertyName("altura")]
    public string Altura { get; init; } = "1.00";

    [JsonPropertyName("profundidade")]
    public string Profundidade { get; init; } = "1.00";

    [JsonPropertyName("codtipocontenedor")]
    public string Codtipocontenedor { get; init; } = "PALLET";

    /// <summary>Qtd padrão por pallet — fixo "1".</summary>
    [JsonPropertyName("qtdpadraopallet")]
    public string Qtdpadraopallet { get; init; } = "1";

    /// <summary>Peso líquido da caixa — fixo "1.000" (se 0 não cria embalagem).</summary>
    [JsonPropertyName("psindividual")]
    public string Psindividual { get; init; } = "1.000";

    /// <summary>Classificação NCM — <c>NCM</c> do XML ou branco.</summary>
    [JsonPropertyName("classificacaoncm")]
    public string Classificacaoncm { get; init; } = string.Empty;

    [JsonPropertyName("qtddiascongelamento")]
    public string Qtddiascongelamento { get; init; } = "0";

    /// <summary>CNPJ do armazém — CONFIG.</summary>
    [JsonPropertyName("cnpjArmazem")]
    public string CnpjArmazem { get; init; } = string.Empty;

    /// <summary>CNPJ da proprietária — CONFIG.</summary>
    [JsonPropertyName("cnpjProprietaria")]
    public string CnpjProprietaria { get; init; } = string.Empty;

    /// <summary>Depósito — CONFIG (BRF: JD).</summary>
    [JsonPropertyName("deposito")]
    public string Deposito { get; init; } = string.Empty;

    [JsonPropertyName("usactlindividual")]
    public string Usactlindividual { get; init; } = "N";
}
