using System.Text.Json;
using System.Text.Json.Serialization;

namespace KitWmsIntegrador.Core.ClienteWms;

/// <summary>
/// Política de serialização JSON compartilhada entre o <see cref="ClienteWmsHttp"/>
/// (envio) e os construtores de payload (I50/I60/I10). Centralizar aqui garante que
/// o JSON inspecionado nos testes/construtores é byte-a-byte o mesmo que vai no POST.
///
/// <list type="bullet">
/// <item><see cref="JsonSerializerDefaults.Web"/>: respeita os <c>[JsonPropertyName]</c>
/// exatos dos DTOs (não aplica camelCase automático sobre nomes já anotados).</item>
/// <item><see cref="JsonIgnoreCondition.WhenWritingNull"/>: campos <c>null</c> são
/// omitidos do corpo (ex.: <c>NroEmb</c>/embarque quando não informado).</item>
/// </list>
/// </summary>
public static class JsonWms
{
    /// <summary>Opções de serialização usadas no envio e na montagem dos payloads.</summary>
    public static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
