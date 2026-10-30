using System.Text.Json.Serialization;

namespace KitWmsIntegrador.Core.Construtores.Dtos;

/// <summary>
/// Payload da I10 (EMPRESA). Envelope com a lista <c>EMPRESA</c>, conforme o template
/// em <c>docs/TEMPLATES-INTERFACES.md</c>. Usado no caso D (empresa não localizada),
/// montado a partir do destinatário do XML. Nomes JSON exatos do template.
/// </summary>
public sealed record PayloadI10
{
    [JsonPropertyName("EMPRESA")]
    public IReadOnlyList<EmpresaI10> Empresa { get; init; } = Array.Empty<EmpresaI10>();
}

/// <summary>Cadastro de empresa (EMPRESA).</summary>
public sealed record EmpresaI10
{
    /// <summary>
    /// Código ERP da empresa. A CONFIRMAR: usa código ERP? (o 3509308 é do depositante,
    /// não do destinatário). Branco por ora.
    /// </summary>
    [JsonPropertyName("codigoempresa")]
    public string Codigoempresa { get; init; } = string.Empty;

    /// <summary>Razão social — <c>dest/xNome</c>.</summary>
    [JsonPropertyName("razaosocial")]
    public string Razaosocial { get; init; } = string.Empty;

    /// <summary>Titular — não disponível no XML; branco.</summary>
    [JsonPropertyName("titular")]
    public string Titular { get; init; } = string.Empty;

    /// <summary>CHAVE — <c>dest/CNPJ</c>.</summary>
    [JsonPropertyName("cnpj")]
    public string Cnpj { get; init; } = string.Empty;

    /// <summary>Inscrição estadual — não extraída do XML; default "ISENTO" (template).</summary>
    [JsonPropertyName("ie")]
    public string Ie { get; init; } = "ISENTO";

    /// <summary>Endereço/logradouro — <c>enderDest/xLgr</c>.</summary>
    [JsonPropertyName("endereco")]
    public string Endereco { get; init; } = string.Empty;

    /// <summary>Número — <c>enderDest/nro</c>.</summary>
    [JsonPropertyName("numero")]
    public string Numero { get; init; } = string.Empty;

    /// <summary>Complemento — <c>enderDest/xCpl</c> (não extraído; branco).</summary>
    [JsonPropertyName("complemento")]
    public string Complemento { get; init; } = string.Empty;

    /// <summary>Bairro — <c>enderDest/xBairro</c>.</summary>
    [JsonPropertyName("bairro")]
    public string Bairro { get; init; } = string.Empty;

    /// <summary>Cidade — <c>enderDest/xMun</c>.</summary>
    [JsonPropertyName("cidade")]
    public string Cidade { get; init; } = string.Empty;

    /// <summary>UF — <c>enderDest/UF</c>.</summary>
    [JsonPropertyName("uf")]
    public string Uf { get; init; } = string.Empty;

    /// <summary>CEP — <c>enderDest/CEP</c>.</summary>
    [JsonPropertyName("cep")]
    public string Cep { get; init; } = string.Empty;

    /// <summary>Indireto — confirmado BRF: "IN" (INDIRETO).</summary>
    [JsonPropertyName("indireto")]
    public string Indireto { get; init; } = "IN";

    /// <summary>Nome abreviado — não disponível no XML; branco.</summary>
    [JsonPropertyName("nomeabrev")]
    public string Nomeabrev { get; init; } = string.Empty;

    [JsonPropertyName("codtipocliente")]
    public string Codtipocliente { get; init; } = string.Empty;

    [JsonPropertyName("prazopagto")]
    public string Prazopagto { get; init; } = "0";

    [JsonPropertyName("endfatura")]
    public string Endfatura { get; init; } = string.Empty;

    [JsonPropertyName("bairrofatura")]
    public string Bairrofatura { get; init; } = string.Empty;

    [JsonPropertyName("cidadefatura")]
    public string Cidadefatura { get; init; } = string.Empty;

    [JsonPropertyName("uffatura")]
    public string Uffatura { get; init; } = string.Empty;

    [JsonPropertyName("cepfatura")]
    public string Cepfatura { get; init; } = string.Empty;

    [JsonPropertyName("telfatura")]
    public string Telfatura { get; init; } = string.Empty;

    [JsonPropertyName("emailfatura")]
    public string Emailfatura { get; init; } = string.Empty;

    [JsonPropertyName("endcobranca")]
    public string Endcobranca { get; init; } = string.Empty;

    [JsonPropertyName("bairrocobranca")]
    public string Bairrocobranca { get; init; } = string.Empty;

    [JsonPropertyName("cidadecobranca")]
    public string Cidadecobranca { get; init; } = string.Empty;

    [JsonPropertyName("ufcobranca")]
    public string Ufcobranca { get; init; } = string.Empty;

    [JsonPropertyName("cepcobranca")]
    public string Cepcobranca { get; init; } = string.Empty;

    [JsonPropertyName("telcobranca")]
    public string Telcobranca { get; init; } = string.Empty;

    [JsonPropertyName("emailcobranca")]
    public string Emailcobranca { get; init; } = string.Empty;

    [JsonPropertyName("fax")]
    public string Fax { get; init; } = string.Empty;

    [JsonPropertyName("codtipodocto")]
    public string Codtipodocto { get; init; } = string.Empty;

    [JsonPropertyName("codtipocobranca")]
    public string Codtipocobranca { get; init; } = string.Empty;

    [JsonPropertyName("nrosif")]
    public string Nrosif { get; init; } = string.Empty;

    [JsonPropertyName("dhinifaturamento")]
    public string Dhinifaturamento { get; init; } = string.Empty;

    [JsonPropertyName("regiaodecidade")]
    public string Regiaodecidade { get; init; } = string.Empty;

    /// <summary>Tipo de operação — default "FI" (template). A CONFIRMAR p/ BRF.</summary>
    [JsonPropertyName("tpoperacao")]
    public string Tpoperacao { get; init; } = "FI";

    /// <summary>Ação — "I" inclusão (template).</summary>
    [JsonPropertyName("tpacao")]
    public string Tpacao { get; init; } = "I";

    /// <summary>
    /// Sigla única da empresa — não disponível no XML; branco. A CONFIRMAR regra de
    /// geração (o WMS pode exigir uma sigla única por CNPJ).
    /// </summary>
    [JsonPropertyName("siglaempresa")]
    public string Siglaempresa { get; init; } = string.Empty;

    /// <summary>Classe — "A" preferencial / "B" normal / "C" problemática. Default "B".</summary>
    [JsonPropertyName("classe")]
    public string Classe { get; init; } = "B";

    /// <summary>Perfil fiscal — branco (template).</summary>
    [JsonPropertyName("perfilfiscal")]
    public string Perfilfiscal { get; init; } = string.Empty;

    /// <summary>CNPJ do armazém — CONFIG.</summary>
    [JsonPropertyName("cnpjArmazem")]
    public string CnpjArmazem { get; init; } = string.Empty;

    /// <summary>CNPJ da proprietária — CONFIG.</summary>
    [JsonPropertyName("cnpjProprietaria")]
    public string CnpjProprietaria { get; init; } = string.Empty;

    /// <summary>Depósito — CONFIG (BRF: JD).</summary>
    [JsonPropertyName("deposito")]
    public string Deposito { get; init; } = string.Empty;
}
