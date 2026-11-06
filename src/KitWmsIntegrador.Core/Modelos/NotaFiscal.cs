namespace KitWmsIntegrador.Core.Modelos;

/// <summary>
/// Representação de domínio de uma NF-e lida de um XML (layout nacional 4.00).
/// Imutável; preenchida pelo leitor em <see cref="KitWmsIntegrador.Core.Leitura"/>.
/// </summary>
public record NotaFiscal
{
    /// <summary>Chave de acesso (44 dígitos) — <c>infNFe/@Id</c> sem o prefixo "NFe".</summary>
    public string ChaveNFe { get; init; } = string.Empty;

    /// <summary>Número da nota — <c>ide/nNF</c>.</summary>
    public string Numero { get; init; } = string.Empty;

    /// <summary>Série — <c>ide/serie</c>.</summary>
    public string Serie { get; init; } = string.Empty;

    /// <summary>Modelo — <c>ide/mod</c>.</summary>
    public string Modelo { get; init; } = string.Empty;

    /// <summary>Data/hora de emissão — <c>ide/dhEmi</c>.</summary>
    public DateTimeOffset? DataEmissao { get; init; }

    /// <summary>CNPJ do emitente — <c>emit/CNPJ</c>.</summary>
    public string EmitenteCnpj { get; init; } = string.Empty;

    /// <summary>Nome do emitente — <c>emit/xNome</c>.</summary>
    public string EmitenteNome { get; init; } = string.Empty;

    /// <summary>CNPJ do destinatário — <c>dest/CNPJ</c>.</summary>
    public string DestinatarioCnpj { get; init; } = string.Empty;

    /// <summary>Nome do destinatário — <c>dest/xNome</c>.</summary>
    public string DestinatarioNome { get; init; } = string.Empty;

    /// <summary>Endereço do destinatário — <c>dest/enderDest</c>.</summary>
    public EnderecoDestinatario? Endereco { get; init; }

    /// <summary>Itens da nota — cada <c>det/prod</c>.</summary>
    public IReadOnlyList<ItemNota> Itens { get; init; } = Array.Empty<ItemNota>();

    /// <summary>Valor total da nota — <c>total/ICMSTot/vNF</c>.</summary>
    public decimal? ValorTotal { get; init; }

    /// <summary>Volumes/pesos — <c>transp/vol</c>.</summary>
    public VolumeTransporte? Volumes { get; init; }

    /// <summary>Nome da transportadora — <c>transp/transporta/xNome</c>.</summary>
    public string? TransportadoraNome { get; init; }

    /// <summary>CNPJ da transportadora — <c>transp/transporta/CNPJ</c>.</summary>
    public string? TransportadoraCnpj { get; init; }

    /// <summary>Placa do veiculo de transporte - transp/veicTransp/placa. Vazia quando o XML nao traz veicTransp/placa.</summary>
    public string PlacaVeiculo { get; init; } = string.Empty;

    /// <summary>Protocolo de autorização — <c>protNFe/infProt/nProt</c>.</summary>
    public string? Protocolo { get; init; }

    /// <summary>
    /// Número do transporte (agrupador da onda), extraído de
    /// <c>infNFe/infAdic/infCpl</c> via "Transporte: &lt;dígitos&gt;".
    /// Mantido como string (preserva zeros à esquerda). <c>null</c> quando não encontrado.
    /// </summary>
    public string? NumeroTransporte { get; init; }

    /// <summary>
    /// Texto bruto das informações complementares —
    /// <c>infNFe/infAdic/infCpl</c>. Usado para enriquecer a I50 (Observacao).
    /// <c>null</c> quando o XML não traz o campo.
    /// </summary>
    public string? InfoComplementar { get; init; }

    /// <summary>Caminho do arquivo XML de origem.</summary>
    public string ArquivoOrigem { get; init; } = string.Empty;
}
