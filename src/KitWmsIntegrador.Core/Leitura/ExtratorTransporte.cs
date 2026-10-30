using System.Text.RegularExpressions;

namespace KitWmsIntegrador.Core.Leitura;

/// <summary>
/// Extrai o número do transporte (agrupador da onda) do texto livre de
/// <c>infNFe/infAdic/infCpl</c>.
///
/// Padrão confirmado nos XMLs reais BRF: "Transporte: &lt;dígitos&gt;"
/// (ex.: "Transporte: 0130693549"). O número é mantido como string para
/// preservar zeros à esquerda. Emitentes não-BRF podem não trazer o padrão —
/// nesse caso o retorno é <c>null</c> (pendência tratada mais acima no fluxo).
/// </summary>
public static partial class ExtratorTransporte
{
    [GeneratedRegex(@"Transporte:\s*(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex RegexTransporte();

    /// <summary>
    /// Retorna o número do transporte presente em <paramref name="infCpl"/>,
    /// ou <c>null</c> quando o padrão não casa ou a entrada é nula/vazia.
    /// </summary>
    public static string? Extrair(string? infCpl)
    {
        if (string.IsNullOrWhiteSpace(infCpl))
        {
            return null;
        }

        var match = RegexTransporte().Match(infCpl);
        return match.Success ? match.Groups[1].Value : null;
    }
}
