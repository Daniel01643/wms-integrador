using System.Globalization;
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

    /// <summary>Tamanho máximo do número da nota (nNF, 9 dígitos na chave).</summary>
    public const int MaxDigitosNumero = 9;

    /// <summary>Tamanho máximo da série (3 dígitos na chave).</summary>
    public const int MaxDigitosSerie = 3;

    /// <summary>CNPJ do emitente (posições 7 a 20 da chave), 14 dígitos; vazio se a chave não tiver 44 dígitos.</summary>
    public static string CnpjDaChave(string chave) => EhChave44Digitos(chave) ? chave.Substring(6, 14) : string.Empty;

    /// <summary>Série (posições 23 a 25 da chave) como inteiro; <c>null</c> se a chave não tiver 44 dígitos.</summary>
    public static int? SerieDaChave(string chave) =>
        EhChave44Digitos(chave) ? int.Parse(chave.AsSpan(22, 3), NumberStyles.None, CultureInfo.InvariantCulture) : null;

    /// <summary>Número da nota (posições 26 a 34 da chave) como inteiro; <c>null</c> se a chave não tiver 44 dígitos.</summary>
    public static int? NumeroDaChave(string chave) =>
        EhChave44Digitos(chave) ? int.Parse(chave.AsSpan(25, 9), NumberStyles.None, CultureInfo.InvariantCulture) : null;

    /// <summary>
    /// Valida os campos da busca por número + série: somente dígitos, número com 1 a 9 e série
    /// com 1 a 3 dígitos (zeros à esquerda são aceitos). Devolve <c>null</c> se válidos ou a mensagem do problema.
    /// </summary>
    public static string? ValidarNumeroSerie(string? numero, string? serie, out int numeroInt, out int serieInt)
    {
        numeroInt = 0;
        serieInt = 0;
        var n = numero?.Trim() ?? string.Empty;
        var s = serie?.Trim() ?? string.Empty;

        if (n.Length == 0 || n.Length > MaxDigitosNumero || !n.All(char.IsAsciiDigit))
        {
            return $"O número da nota deve ter de 1 a {MaxDigitosNumero} dígitos, somente números.";
        }

        if (s.Length == 0 || s.Length > MaxDigitosSerie || !s.All(char.IsAsciiDigit))
        {
            return $"A série deve ter de 1 a {MaxDigitosSerie} dígitos, somente números.";
        }

        numeroInt = int.Parse(n, NumberStyles.None, CultureInfo.InvariantCulture);
        serieInt = int.Parse(s, NumberStyles.None, CultureInfo.InvariantCulture);
        return null;
    }

    /// <summary>Somente os dígitos do texto (para comparar CNPJs).</summary>
    public static string SoDigitos(string? texto) =>
        new((texto ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    private static bool EhChave44Digitos(string? chave) => chave is { Length: 44 } && chave.All(char.IsAsciiDigit);
}
