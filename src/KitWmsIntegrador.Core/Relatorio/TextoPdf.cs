using PdfSharpCore.Drawing;

namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>Quebra de texto por largura para as células do PDF.</summary>
internal static class TextoPdf
{
    /// <summary>
    /// Quebra <paramref name="texto"/> em linhas que cabem em <paramref name="larguraMax"/>
    /// (pontos), medindo com <paramref name="gfx"/>. Quebra por palavras; palavra maior que a
    /// coluna é quebrada por caractere. Respeita quebras de linha explícitas. Sempre devolve
    /// ao menos uma linha.
    /// </summary>
    public static List<string> Quebrar(string? texto, XFont fonte, double larguraMax, XGraphics gfx)
    {
        return Quebrar(texto, larguraMax, s => gfx.MeasureString(s, fonte).Width);
    }

    /// <summary>Versão com função de medição injetada (testável sem gráficos).</summary>
    public static List<string> Quebrar(string? texto, double larguraMax, Func<string, double> medir)
    {
        var linhas = new List<string>();
        if (string.IsNullOrEmpty(texto))
        {
            linhas.Add(string.Empty);
            return linhas;
        }

        foreach (var paragrafo in texto.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var atual = string.Empty;
            foreach (var palavra in paragrafo.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var tentativa = atual.Length == 0 ? palavra : atual + " " + palavra;
                if (medir(tentativa) <= larguraMax)
                {
                    atual = tentativa;
                    continue;
                }

                if (atual.Length > 0)
                {
                    linhas.Add(atual);
                    atual = string.Empty;
                }

                // Palavra isolada maior que a coluna: quebra por caractere.
                var resto = palavra;
                while (medir(resto) > larguraMax && resto.Length > 1)
                {
                    var n = 1;
                    while (n < resto.Length && medir(resto[..(n + 1)]) <= larguraMax)
                    {
                        n++;
                    }

                    linhas.Add(resto[..n]);
                    resto = resto[n..];
                }

                atual = resto;
            }

            linhas.Add(atual);
        }

        return linhas;
    }
}
