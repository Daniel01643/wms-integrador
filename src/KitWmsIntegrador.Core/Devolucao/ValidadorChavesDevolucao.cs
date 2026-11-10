namespace KitWmsIntegrador.Core.Devolucao;

/// <summary>
/// Normaliza a lista de chaves da tela: remove espaços/quebras dentro de cada item, descarta
/// brancos e remove duplicadas preservando a ordem. A validação (44 dígitos + DV mod 11) é a do
/// <see cref="LocalizadorXmlDevolucao.ValidarChave"/>.
/// </summary>
public static class ValidadorChavesDevolucao
{
    /// <summary>Normaliza e deduplica (ordem original, comparação exata).</summary>
    public static IReadOnlyList<string> Normalizar(IEnumerable<string?>? chaves)
    {
        var unicas = new List<string>();
        var vistas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bruta in chaves ?? Array.Empty<string?>())
        {
            if (string.IsNullOrWhiteSpace(bruta))
            {
                continue;
            }

            var chave = new string(bruta.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (vistas.Add(chave))
            {
                unicas.Add(chave);
            }
        }

        return unicas;
    }

    /// <summary>Número da nota (nNF) derivado da chave (posições 26 a 34), sem zeros à esquerda; vazio se inválida.</summary>
    public static string NotaDaChave(string chave)
    {
        if (chave.Length != 44 || !chave.All(char.IsAsciiDigit))
        {
            return string.Empty;
        }

        var numero = chave.Substring(25, 9).TrimStart('0');
        return numero.Length == 0 ? "0" : numero;
    }

    /// <summary>
    /// Normaliza a placa (remove espaços e hífens, maiúsculas). Devolve <c>null</c> se não tiver
    /// de 5 a 8 letras/dígitos (placas BR têm 7).
    /// </summary>
    public static string? NormalizarPlaca(string? placa)
    {
        if (string.IsNullOrWhiteSpace(placa))
        {
            return null;
        }

        var p = new string(placa.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        return p.Length is >= 5 and <= 8 && p.All(char.IsAsciiLetterOrDigit) ? p : null;
    }
}
