using System.Text.RegularExpressions;

namespace KitWmsIntegrador.Core.Separacao;

/// <summary>
/// Extrai as ressalvas de uma mensagem do WMS no formato
/// <c>... Ressalvas : {texto 1}{texto 2}</c>. Sem o marcador, devolve lista vazia.
/// </summary>
public static class ParserRessalvas
{
    private static readonly Regex Marcador =
        new(@"Ressalvas\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex Bloco =
        new(@"\{([^{}]*)\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Lista as ressalvas da mensagem (itens vazios descartados).</summary>
    public static IReadOnlyList<string> Extrair(string? mensagem)
    {
        if (string.IsNullOrWhiteSpace(mensagem))
        {
            return Array.Empty<string>();
        }

        try
        {
            var m = Marcador.Match(mensagem);
            if (!m.Success)
            {
                return Array.Empty<string>();
            }

            var resto = mensagem[(m.Index + m.Length)..];
            var itens = new List<string>();
            foreach (Match b in Bloco.Matches(resto))
            {
                var t = b.Groups[1].Value.Trim();
                if (t.Length > 0)
                {
                    itens.Add(t);
                }
            }

            if (itens.Count == 0)
            {
                var solto = resto.Trim().Trim('{', '}').Trim();
                if (solto.Length > 0)
                {
                    itens.Add(solto);
                }
            }

            return itens;
        }
        catch (RegexMatchTimeoutException)
        {
            return Array.Empty<string>();
        }
    }
}
