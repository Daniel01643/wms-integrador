using System.Globalization;

namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>
/// Formatação de durações e números em pt-BR ("1 min 25 s", "850 ms", "1.234"), sem depender de
/// ICU/cultura instalada no servidor (usa um <see cref="NumberFormatInfo"/> próprio).
/// </summary>
internal static class FormatadorDuracao
{
    private static readonly NumberFormatInfo PtBr = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberGroupSizes = new[] { 3 },
    };

    /// <summary>
    /// Formata uma duração em milissegundos: "850 ms", "2,5 s", "45 s", "1 min 25 s", "2 min",
    /// "1 h 05 min". Negativo vira "0 ms".
    /// </summary>
    public static string Formatar(long ms)
    {
        if (ms <= 0)
        {
            return "0 ms";
        }

        if (ms < 1000)
        {
            return Inteiro(ms) + " ms";
        }

        if (ms < 10_000)
        {
            // Uma casa decimal; se o arredondamento chegar a 10 s, cai na faixa seguinte.
            var decimos = (long)Math.Round(ms / 100.0, MidpointRounding.AwayFromZero);
            if (decimos < 100)
            {
                return Numero(decimos / 10.0, 1) + " s";
            }
        }

        // Arredonda o total em segundos ANTES de decompor (evita "60 s" / "1 min 60 s").
        var total = (long)Math.Round(ms / 1000.0, MidpointRounding.AwayFromZero);
        if (total < 60)
        {
            return Inteiro(total) + " s";
        }

        var horas = total / 3600;
        var minutos = total % 3600 / 60;
        var segundos = total % 60;
        if (horas > 0)
        {
            return Inteiro(horas) + " h " + minutos.ToString("00", CultureInfo.InvariantCulture) + " min";
        }

        return segundos == 0
            ? Inteiro(minutos) + " min"
            : Inteiro(minutos) + " min " + Inteiro(segundos) + " s";
    }

    /// <summary>Inteiro com separador de milhar pt-BR ("1.234").</summary>
    public static string Inteiro(long valor) => valor.ToString("N0", PtBr);

    /// <summary>Número com casas decimais pt-BR ("1.234,5").</summary>
    public static string Numero(double valor, int casas) => valor.ToString("N" + casas, PtBr);
}
