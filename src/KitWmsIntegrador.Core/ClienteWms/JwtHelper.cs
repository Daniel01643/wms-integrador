using System.Text;
using System.Text.Json;

namespace KitWmsIntegrador.Core.ClienteWms;

/// <summary>
/// Utilitário mínimo para extrair o claim <c>exp</c> de um JWT. NÃO valida a
/// assinatura — só precisamos do instante de expiração para decidir o cache/renovação
/// do token, então evitamos dependências externas.
/// </summary>
internal static class JwtHelper
{
    /// <summary>
    /// Lê o claim <c>exp</c> (epoch em segundos) do payload do JWT e retorna o
    /// instante de expiração em UTC. Retorna <c>null</c> se o token não tiver o
    /// formato esperado ou não contiver <c>exp</c> — nesse caso o cliente renova
    /// sob 401.
    /// </summary>
    public static DateTimeOffset? ObterExpiracao(string jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt))
        {
            return null;
        }

        var partes = jwt.Split('.');
        if (partes.Length < 2)
        {
            return null;
        }

        try
        {
            var payloadJson = DecodificarBase64Url(partes[1]);
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("exp", out var exp) &&
                exp.TryGetInt64(out var epochSegundos))
            {
                return DateTimeOffset.FromUnixTimeSeconds(epochSegundos);
            }
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string DecodificarBase64Url(string valor)
    {
        var normalizado = valor.Replace('-', '+').Replace('_', '/');
        switch (normalizado.Length % 4)
        {
            case 2: normalizado += "=="; break;
            case 3: normalizado += "="; break;
        }

        var bytes = Convert.FromBase64String(normalizado);
        return Encoding.UTF8.GetString(bytes);
    }
}
