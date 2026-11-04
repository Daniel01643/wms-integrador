namespace KitWmsIntegrador.Core.Orquestrador;

/// <summary>
/// Resultado da integração de UMA nota, consumido pelo relatório de erros/pendências.
/// Registra se a nota foi integrada, quantas tentativas foram feitas, o estado final e a
/// última mensagem observada (motivo), além de um histórico das mensagens/transações.
/// </summary>
/// <param name="NumeroNota">Número da nota fiscal.</param>
/// <param name="NumeroTransporte">Número do transporte (DocCarga) associado.</param>
/// <param name="Integrada">Verdadeiro quando a nota foi aceita pelo WMS.</param>
/// <param name="Tentativas">Quantidade de tentativas de envio da I50 realizadas.</param>
/// <param name="Estado">Estado final da integração.</param>
/// <param name="UltimaMensagem">Última mensagem observada (motivo da pendência/falha).</param>
/// <param name="Historico">Mensagens/transações observadas ao longo das tentativas.</param>
public sealed record ResultadoIntegracaoNota(
    string NumeroNota,
    string NumeroTransporte,
    bool Integrada,
    int Tentativas,
    EstadoIntegracao Estado,
    string UltimaMensagem,
    IReadOnlyList<string> Historico)
{
    /// <summary>Histórico vazio por padrão, evitando null nos call-sites.</summary>
    public IReadOnlyList<string> Historico { get; init; } = Historico ?? Array.Empty<string>();

    /// <summary>
    /// ID da transação (<c>transacao</c>) da ÚLTIMA chamada da I50 (a que originou a
    /// mensagem exibida). Vazio quando não houve chamada.
    /// </summary>
    public string IdTransacao { get; init; } = string.Empty;

    /// <summary>Primeiro <c>retornos[].idDocumento</c> não vazio da última chamada da I50.</summary>
    public string IdDocumento { get; init; } = string.Empty;

    /// <summary>
    /// Códigos dos produtos enviados à I30 por contenção (produto provisório). É a lista de
    /// TODOS os itens enviados: o WMS não informa quais foram de fato criados.
    /// </summary>
    public IReadOnlyList<string> ProdutosProvisorios { get; init; } = Array.Empty<string>();

    /// <summary>Verdadeiro quando há produto provisório a revisar no cadastro.</summary>
    public bool RevisaoProdutoPendente => ProdutosProvisorios.Count > 0;
}
