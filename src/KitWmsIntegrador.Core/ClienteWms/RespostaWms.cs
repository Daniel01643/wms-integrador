using System.Text.Json.Serialization;

namespace KitWmsIntegrador.Core.ClienteWms;

/// <summary>
/// Resposta tipada da API do WMS. Espelha fielmente o JSON observado em
/// TESTE-COMUNICACAO.md — NÃO interpreta regra de negócio (isso é do Orquestrador).
///
/// Atenção: <see cref="Sucesso"/> = "N" NEM SEMPRE é falha total. Um retorno pode
/// ter sido importado e ficar PENDENTE na interface. Use o helper de classificação
/// em <see cref="IClienteWms.Classificar"/> para derivar o resultado a partir do texto.
/// </summary>
public record RespostaWms
{
    /// <summary>ID único da transação no WMS — <c>transacao</c>. Usado para rastreio/auditoria.</summary>
    [JsonPropertyName("transacao")]
    public string Transacao { get; init; } = string.Empty;

    /// <summary>Tipo de EDI processado — <c>tipoEDI</c> (ex.: "I50").</summary>
    [JsonPropertyName("tipoEDI")]
    public string TipoEDI { get; init; } = string.Empty;

    /// <summary>Resultado geral — <c>sucesso</c> (S/N). "N" não implica erro definitivo.</summary>
    [JsonPropertyName("sucesso")]
    public string Sucesso { get; init; } = string.Empty;

    /// <summary>Itens de retorno — <c>retornos[]</c>.</summary>
    [JsonPropertyName("retornos")]
    public IReadOnlyList<RetornoWms> Retornos { get; init; } = Array.Empty<RetornoWms>();
}
