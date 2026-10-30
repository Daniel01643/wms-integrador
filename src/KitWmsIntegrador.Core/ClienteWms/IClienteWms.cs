namespace KitWmsIntegrador.Core.ClienteWms;

/// <summary>
/// Cliente de comunicação HTTP com a API REST do WMS (SolWMS Assist).
///
/// O token (JWT) é obtido e cacheado INTERNAMENTE pela implementação — não há
/// método público de token; renovação por expiração/401 é transparente ao chamador.
///
/// A regra de negócio (reenvio, I10, alerta) NÃO é decidida aqui. O envio apenas
/// serializa o payload, aplica a query string obrigatória e desserializa a resposta
/// fielmente. Use <see cref="Classificar"/> como helper para derivar o resultado.
/// </summary>
public interface IClienteWms
{
    /// <summary>
    /// Envia um payload (objeto, serializado com System.Text.Json) para o
    /// IDENTIFICADOR indicado e retorna a resposta tipada.
    /// </summary>
    /// <param name="identificador">Qual integração EDI (I50/I60/I10).</param>
    /// <param name="payload">Objeto a serializar como corpo JSON.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<RespostaWms> EnviarAsync(InterfaceWms identificador, object payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Envia um JSON bruto (string) para o IDENTIFICADOR indicado. Útil para
    /// smoke test manual e testes com JSON literal.
    /// </summary>
    /// <param name="identificador">Qual integração EDI (I50/I60/I10).</param>
    /// <param name="jsonBruto">Corpo JSON já pronto.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<RespostaWms> EnviarAsync(InterfaceWms identificador, string jsonBruto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Helper que deriva um <see cref="ResultadoWms"/> a partir do texto da mensagem
    /// de um retorno, preservando a mensagem bruta. NÃO é regra de negócio final.
    /// </summary>
    ResultadoWms Classificar(RetornoWms retorno);
}
