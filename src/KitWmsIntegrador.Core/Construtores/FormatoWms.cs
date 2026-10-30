using System.Globalization;

namespace KitWmsIntegrador.Core.Construtores;

/// <summary>
/// Helpers de formatação determinística e cultura-invariante para os payloads WMS.
/// Centralizados para evitar divergência entre os construtores I50/I60/I10.
///
/// Regras (do plano da Etapa 4):
/// <list type="bullet">
/// <item>Datas no formato <c>DD/MM/YYYY HH:MM:SS</c>.</item>
/// <item>Decimais com PONTO decimal (nunca vírgula), cultura invariante.</item>
/// <item>Strings de transporte preservam zeros à esquerda (não são números).</item>
/// </list>
/// </summary>
public static class FormatoWms
{
    /// <summary>Formato de data/hora exigido pelo WMS: <c>DD/MM/YYYY HH:MM:SS</c>.</summary>
    public const string FormatoData = "dd/MM/yyyy HH:mm:ss";

    /// <summary>
    /// Formata uma data no padrão WMS (<c>DD/MM/YYYY HH:MM:SS</c>, invariante).
    /// Retorna <c>null</c> quando a data é nula — para ser omitida via ignore-null.
    /// </summary>
    public static string? Data(DateTimeOffset? valor) =>
        valor?.ToString(FormatoData, CultureInfo.InvariantCulture);

    /// <summary>
    /// Formata uma data (<see cref="DateOnly"/>) no padrão WMS, à meia-noite.
    /// Retorna <c>null</c> quando a data é nula.
    /// </summary>
    public static string? Data(DateOnly? valor) =>
        valor?.ToDateTime(TimeOnly.MinValue).ToString(FormatoData, CultureInfo.InvariantCulture);

    /// <summary>
    /// Formata um decimal com ponto decimal e cultura invariante, preservando as casas
    /// relevantes (sem zeros/sufixos artificiais). Ex.: <c>22.44</c>, <c>275.64</c>.
    /// </summary>
    public static decimal Decimal(decimal valor) => valor;

    /// <summary>
    /// Formata um decimal como string com ponto decimal e número fixo de casas
    /// (cultura invariante). Útil quando o WMS exige casas fixas.
    /// </summary>
    public static string Decimal(decimal valor, int casas) =>
        valor.ToString("F" + casas.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>
    /// Garante zeros à esquerda até a <paramref name="largura"/> desejada, sem truncar
    /// strings maiores. Mantém o valor como string (não converte para número).
    /// </summary>
    public static string ZerosEsquerda(string? valor, int largura)
    {
        var texto = valor ?? string.Empty;
        return texto.Length >= largura ? texto : texto.PadLeft(largura, '0');
    }
}
