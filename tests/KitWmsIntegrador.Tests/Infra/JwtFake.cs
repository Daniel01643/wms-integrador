using System.Text;
using System.Text.Json;

namespace KitWmsIntegrador.Tests.Infra;

/// <summary>
/// Gera um JWT de teste (header.payload.signature) com claim <c>exp</c> controlável.
/// A assinatura é um valor fixo — o cliente só lê o <c>exp</c>, não valida assinatura.
/// </summary>
public static class JwtFake
{
    /// <summary>Cria um JWT cujo <c>exp</c> é o instante informado.</summary>
    public static string ComExpiracao(DateTimeOffset exp)
    {
        var header = Base64Url("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        var payloadJson = JsonSerializer.Serialize(new { exp = exp.ToUnixTimeSeconds() });
        var payload = Base64Url(payloadJson);
        var assinatura = Base64Url("assinatura-fake");
        return $"{header}.{payload}.{assinatura}";
    }

    /// <summary>Cria um JWT sem claim <c>exp</c> (expiração desconhecida).</summary>
    public static string SemExpiracao()
    {
        var header = Base64Url("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        var payload = Base64Url("{\"sub\":\"teste\"}");
        var assinatura = Base64Url("assinatura-fake");
        return $"{header}.{payload}.{assinatura}";
    }

    private static string Base64Url(string texto)
    {
        var bytes = Encoding.UTF8.GetBytes(texto);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
