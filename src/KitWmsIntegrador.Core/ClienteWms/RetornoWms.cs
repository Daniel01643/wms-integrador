using System.Text.Json.Serialization;

namespace KitWmsIntegrador.Core.ClienteWms;

/// <summary>
/// Um item de retorno da API do WMS (elemento de <c>retornos[]</c>). Desserialização
/// fiel do JSON — NÃO decide regra de negócio. A <see cref="Mensagem"/> bruta é
/// preservada para alimentar relatórios de erro/pendência.
/// </summary>
public record RetornoWms
{
    /// <summary>Documento associado — <c>documento</c>. Pode vir vazio.</summary>
    [JsonPropertyName("documento")]
    public string Documento { get; init; } = string.Empty;

    /// <summary>Id do documento no WMS — <c>idDocumento</c>. Pode vir vazio.</summary>
    [JsonPropertyName("idDocumento")]
    public string IdDocumento { get; init; } = string.Empty;

    /// <summary>Resultado do item — <c>sucesso</c> (S/N). Ver <see cref="RespostaWms.Sucesso"/>.</summary>
    [JsonPropertyName("sucesso")]
    public string Sucesso { get; init; } = string.Empty;

    /// <summary>Mensagem legível — <c>mensagem</c>. Motivo da pendência/rejeição.</summary>
    [JsonPropertyName("mensagem")]
    public string Mensagem { get; init; } = string.Empty;
}
