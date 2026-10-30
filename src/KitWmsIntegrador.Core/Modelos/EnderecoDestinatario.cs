namespace KitWmsIntegrador.Core.Modelos;

/// <summary>
/// Endereço do destinatário extraído de <c>dest/enderDest</c>.
/// </summary>
public record EnderecoDestinatario
{
    /// <summary>Logradouro — <c>enderDest/xLgr</c>.</summary>
    public string? Logradouro { get; init; }

    /// <summary>Número — <c>enderDest/nro</c>.</summary>
    public string? Numero { get; init; }

    /// <summary>Bairro — <c>enderDest/xBairro</c>.</summary>
    public string? Bairro { get; init; }

    /// <summary>Município — <c>enderDest/xMun</c>.</summary>
    public string? Municipio { get; init; }

    /// <summary>UF — <c>enderDest/UF</c>.</summary>
    public string? Uf { get; init; }

    /// <summary>CEP — <c>enderDest/CEP</c>.</summary>
    public string? Cep { get; init; }
}
