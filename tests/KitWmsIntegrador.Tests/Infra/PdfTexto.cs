using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace KitWmsIntegrador.Tests.Infra;

/// <summary>
/// Extrai o texto de um PDF gerado pela PdfSharpCore (fontes Unicode/Identity-H): descomprime os streams, monta o mapa
/// glifo → Unicode a partir dos <c>/ToUnicode</c> e decodifica as strings hexadecimais dos operadores de texto. Só para
/// testes (verificar que uma lista está ou não dentro do PDF).
/// </summary>
public static class PdfTexto
{
    private static readonly Regex Stream = new(@"(?<!end)stream\r?\n", RegexOptions.Compiled);
    private static readonly Regex Par = new(@"<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]{4,8})>", RegexOptions.Compiled);
    private static readonly Regex Faixa = new(@"<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]{4})>", RegexOptions.Compiled);
    private static readonly Regex Hex = new(@"/F(\d+)\s+[\d.]+\s+Tf|<([0-9A-Fa-f\s]+)>\s*Tj|\[((?:\s*<[0-9A-Fa-f\s]+>\s*-?[0-9.]*)+)\]\s*TJ", RegexOptions.Compiled);

    /// <summary>Texto de todo o PDF (uma linha por operação de texto), ou vazio se nada foi decodificado.</summary>
    public static string Extrair(byte[] pdf)
    {
        var latin = Encoding.Latin1.GetString(pdf);
        var conteudos = new List<string>();
        var cmaps = new List<string>();
        foreach (Match m in Stream.Matches(latin))
        {
            var inicio = m.Index + m.Length;
            var fim = latin.IndexOf("endstream", inicio, StringComparison.Ordinal);
            if (fim < 0)
            {
                continue;
            }

            var bytes = pdf.AsSpan(inicio, fim - inicio).ToArray();
            string? texto = null;
            try
            {
                using var origem = new MemoryStream(bytes);
                using var z = new ZLibStream(origem, CompressionMode.Decompress);
                using var destino = new MemoryStream();
                z.CopyTo(destino);
                texto = Encoding.Latin1.GetString(destino.ToArray());
            }
            catch (Exception)
            {
                texto = Encoding.Latin1.GetString(bytes);
            }

            if (texto.Contains("beginbfchar", StringComparison.Ordinal) || texto.Contains("beginbfrange", StringComparison.Ordinal))
            {
                cmaps.Add(texto);
            }
            else if (texto.Contains("Tj", StringComparison.Ordinal) || texto.Contains("TJ", StringComparison.Ordinal))
            {
                conteudos.Add(texto);
            }
        }

        // Um mapa glifo → Unicode por fonte (na ordem em que os /ToUnicode aparecem: F0, F1, ...).
        var mapas = new List<Dictionary<int, string>>();
        foreach (var cmap in cmaps)
        {
            var mapa = new Dictionary<int, string>();
            foreach (var bloco in Regex.Split(cmap, "beginbfrange").Skip(1))
            {
                var corpo = bloco.Split("endbfrange")[0];
                foreach (Match f in Faixa.Matches(corpo))
                {
                    var lo = Convert.ToInt32(f.Groups[1].Value, 16);
                    var hi = Convert.ToInt32(f.Groups[2].Value, 16);
                    var dst = Convert.ToInt32(f.Groups[3].Value, 16);
                    for (var g = lo; g <= hi; g++)
                    {
                        mapa[g] = char.ConvertFromUtf32(dst + g - lo);
                    }
                }
            }

            foreach (var bloco in Regex.Split(cmap, "beginbfchar").Skip(1))
            {
                var corpo = bloco.Split("endbfchar")[0];
                foreach (Match p in Par.Matches(corpo))
                {
                    var uni = p.Groups[2].Value;
                    mapa[Convert.ToInt32(p.Groups[1].Value, 16)] = char.ConvertFromUtf32(Convert.ToInt32(uni.Length > 4 ? uni[^4..] : uni, 16));
                }
            }

            mapas.Add(mapa);
        }

        string Glifo(int fonte, int gid)
        {
            if (fonte >= 0 && fonte < mapas.Count && mapas[fonte].TryGetValue(gid, out var certo))
            {
                return certo;
            }

            foreach (var outro in mapas)
            {
                if (outro.TryGetValue(gid, out var ch))
                {
                    return ch;
                }
            }

            return "?";
        }

        var sb = new StringBuilder();
        foreach (var c in conteudos)
        {
            var fonteAtual = 0;
            foreach (Match op in Hex.Matches(c))
            {
                if (op.Groups[1].Success)
                {
                    fonteAtual = int.Parse(op.Groups[1].Value);
                    continue;
                }

                var hex = op.Groups[2].Success
                    ? op.Groups[2].Value
                    : string.Concat(Regex.Matches(op.Groups[3].Value, "<([0-9A-Fa-f\\s]+)>").Select(x => x.Groups[1].Value));
                hex = Regex.Replace(hex, "\\s+", string.Empty);
                for (var i = 0; i + 4 <= hex.Length; i += 4)
                {
                    sb.Append(Glifo(fonteAtual, Convert.ToInt32(hex.Substring(i, 4), 16)));
                }

                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}