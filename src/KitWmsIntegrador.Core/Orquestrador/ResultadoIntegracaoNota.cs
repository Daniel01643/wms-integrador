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
}
