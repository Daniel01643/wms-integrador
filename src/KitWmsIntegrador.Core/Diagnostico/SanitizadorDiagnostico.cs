using System.Text.RegularExpressions;

namespace KitWmsIntegrador.Core.Diagnostico;

/// <summary>
/// Sanitização de texto livre do diário de diagnóstico (regra D): remove caracteres de controle, mascara
/// Bearer/Basic/Authorization, JWT e pares senha/password/token, e trunca.
/// </summary>
public static class SanitizadorDiagnostico
{
    private const string Mascara = "***";

    private static readonly Regex Authorization = new(
        @"authorization\s*[:=]\s*\S+(\s+\S+)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Bearer = new(@"\bbearer\s+\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Basic = new(@"\bbasic\s+[A-Za-z0-9+/=_-]{6,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Jwt = new(@"eyJ[\w-]+\.[\w-]+\.[\w-]*", RegexOptions.Compiled);

    private static readonly Regex Segredo = new(
        @"\b(senha|password|passwd|pwd|token)\s*[=:]\s*\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Devolve o texto sem controles, com segredos mascarados e truncado em <paramref name="max"/> caracteres.</summary>
    public static string? Texto(string? texto, int max = 200)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return null;
        }

        var limpo = new string(texto.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        limpo = Authorization.Replace(limpo, "Authorization: " + Mascara);
        limpo = Bearer.Replace(limpo, "Bearer " + Mascara);
        limpo = Basic.Replace(limpo, "Basic " + Mascara);
        limpo = Jwt.Replace(limpo, Mascara);
        limpo = Segredo.Replace(limpo, m => m.Groups[1].Value + "=" + Mascara);
        if (limpo.Length == 0)
        {
            return null;
        }

        return limpo.Length <= max ? limpo : limpo[..max] + "...";
    }
}
