namespace KitWmsIntegrador.Core.ClienteWms;

/// <summary>
/// Identifica qual integração EDI está sendo enviada ao WMS. O endpoint de POST
/// é o mesmo para todas; o que muda é o parâmetro <c>IDENTIFICADOR</c> da query
/// string (confirmado em TESTE-COMUNICACAO.md).
/// </summary>
public enum InterfaceWms
{
    /// <summary>DocCarga / pedido — <c>WEBAPI-I50-Json</c>.</summary>
    I50,

    /// <summary>Associação de nota ao DocCarga — <c>WEBAPI-I60-Json</c>.</summary>
    I60,

    /// <summary>Cadastro de empresa — <c>WEBAPI-I10-Json</c>.</summary>
    I10,
}

/// <summary>
/// Mapeia <see cref="InterfaceWms"/> para a string exata do parâmetro
/// <c>IDENTIFICADOR</c> esperada pela API, evitando strings mágicas no chamador.
/// </summary>
public static class InterfaceWmsExtensions
{
    /// <summary>Retorna o valor de <c>IDENTIFICADOR</c> correspondente.</summary>
    public static string ParaIdentificador(this InterfaceWms interfaceWms) => interfaceWms switch
    {
        InterfaceWms.I50 => "WEBAPI-I50-Json",
        InterfaceWms.I60 => "WEBAPI-I60-Json",
        InterfaceWms.I10 => "WEBAPI-I10-Json",
        _ => throw new ArgumentOutOfRangeException(nameof(interfaceWms), interfaceWms, "Interface WMS desconhecida."),
    };
}
