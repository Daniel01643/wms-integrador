namespace KitWmsIntegrador.Core.ClienteWms;

/// <summary>
/// Envio de JSON bruto com IDENTIFICADOR configurável (texto) que devolve o CORPO bruto da
/// resposta. Usado pela I51, cujo identificador não cabe no enum <see cref="InterfaceWms"/> e cuja
/// resposta precisa ser gravada em disco antes de ser desserializada. Mesma instância (token,
/// lock e retry) do <see cref="IClienteWms"/>.
/// </summary>
public interface IClienteWmsBruto
{
    /// <summary>
    /// Faz o POST com o <paramref name="identificadorEdi"/> (ex.: <c>WEBAPI-I51</c>) e devolve o
    /// corpo da resposta sem interpretar. Lança <see cref="HttpRequestException"/> em erro HTTP
    /// definitivo (após o retry transitório).
    /// </summary>
    Task<string> PostBrutoAsync(string identificadorEdi, string jsonBruto, CancellationToken cancellationToken = default);
}
