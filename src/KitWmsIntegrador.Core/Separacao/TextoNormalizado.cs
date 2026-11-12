using System.Globalization;
using System.Text;

namespace KitWmsIntegrador.Core.Separacao;

/// <summary>
/// Normalização de texto para o casamento de padrões configuráveis (mensagens do WMS):
/// decomposição FormD sem marcas de acento, minúsculas e espaços repetidos colapsados.
/// Padrão vazio NUNCA casa (um item vazio não pode "casar tudo").
/// </summary>
public static class TextoNormalizado
{
    /// <summary>Normaliza o texto (acentos removidos, minúsculas, espaços colapsados).</summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return string.Empty;
        }

        var decomposto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        var espaco = false;
        foreach (var c in decomposto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                espaco = sb.Length > 0;
                continue;
            }

            if (espaco)
            {
                sb.Append(' ');
                espaco = false;
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    /// <summary>Divide uma lista de padrões separada por <c>;</c> (itens vazios descartados).</summary>
    public static IReadOnlyList<string> Lista(string? padroes) =>
        string.IsNullOrWhiteSpace(padroes)
            ? Array.Empty<string>()
            : padroes.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Verdadeiro se o texto contém algum dos padrões (comparação normalizada).</summary>
    public static bool Casa(string? texto, IEnumerable<string> padroes)
    {
        var alvo = Normalizar(texto);
        if (alvo.Length == 0)
        {
            return false;
        }

        foreach (var padrao in padroes)
        {
            var p = Normalizar(padrao);
            if (p.Length > 0 && alvo.Contains(p, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Verdadeiro se algum dos textos casa algum dos padrões.</summary>
    public static bool CasaAlgum(IEnumerable<string> textos, IEnumerable<string> padroes)
    {
        var lista = padroes as IReadOnlyList<string> ?? padroes.ToList();
        return textos.Any(t => Casa(t, lista));
    }

    /// <summary>Corta o texto em <paramref name="max"/> caracteres (nulo vira vazio).</summary>
    public static string Truncar(string? texto, int max)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return string.Empty;
        }

        return texto.Length <= max ? texto : texto[..max];
    }
}
