using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Extrai texto de PDFs gerados pelo PdfSharpCore (fonte Type0/Identity-H): descomprime os
/// streams Flate, lê o CMap /ToUnicode (glifo -> Unicode) e decodifica os operadores de texto.
/// </summary>
internal static class PdfTextoHelper
{
    public static List<string> Streams(byte[] pdf)
    {
        var latin1 = Encoding.Latin1.GetString(pdf);
        var resultado = new List<string>();
        var pos = 0;
        while (true)
        {
            var ini = latin1.IndexOf("stream", pos, StringComparison.Ordinal);
            if (ini < 0) break;
            var inicioDados = ini + 6;
            if (latin1[inicioDados] == '\r') inicioDados++;
            if (latin1[inicioDados] == '\n') inicioDados++;
            var fim = latin1.IndexOf("endstream", inicioDados, StringComparison.Ordinal);
            if (fim < 0) break;
            try
            {
                using var origem = new MemoryStream(pdf, inicioDados, fim - inicioDados);
                using var z = new ZLibStream(origem, CompressionMode.Decompress);
                using var saida = new MemoryStream();
                z.CopyTo(saida);
                resultado.Add(Encoding.Latin1.GetString(saida.ToArray()));
            }
            catch (InvalidDataException)
            {
                resultado.Add(latin1[inicioDados..fim]);
            }

            pos = fim + 9;
        }

        return resultado;
    }

    /// <summary>Todos os caracteres Unicode mapeados nos CMaps /ToUnicode do PDF.</summary>
    public static HashSet<char> CaracteresMapeados(byte[] pdf)
    {
        var set = new HashSet<char>();
        foreach (var (_, uni) in MapaGlifos(pdf))
        {
            set.Add(uni);
        }

        return set;
    }

    private static List<List<(int Glifo, char Unicode)>> MapasPorFonte(byte[] pdf)
    {
        var mapas = new List<List<(int, char)>>();
        foreach (var s in Streams(pdf).Where(s => s.Contains("beginbfchar") || s.Contains("beginbfrange")))
        {
            var lista = new List<(int, char)>();
            var corpo = s[s.IndexOf("beginbf", StringComparison.Ordinal)..];
            foreach (Match m in Regex.Matches(corpo, @"<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]{4})>(?!\s*<)"))
            {
                lista.Add((Convert.ToInt32(m.Groups[1].Value, 16), (char)Convert.ToInt32(m.Groups[2].Value, 16)));
            }

            foreach (Match m in Regex.Matches(corpo, @"<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]{4})>"))
            {
                var a = Convert.ToInt32(m.Groups[1].Value, 16);
                var b = Convert.ToInt32(m.Groups[2].Value, 16);
                var u = Convert.ToInt32(m.Groups[3].Value, 16);
                for (var g = a; g <= b; g++) lista.Add((g, (char)(u + g - a)));
            }

            mapas.Add(lista);
        }

        return mapas;
    }

    private static List<(int Glifo, char Unicode)> MapaGlifos(byte[] pdf) => MapasPorFonte(pdf).SelectMany(m => m).ToList();
    /// <summary>
    /// Texto decodificado dos operadores Tj. Cada fonte tem seu CMap; como o PDF não é
    /// interpretado por completo, cada Tj é decodificado com todos os mapas que cobrem todos
    /// os seus glifos (os candidatos são concatenados; serve para asserções "contém").
    /// </summary>
    public static string Texto(byte[] pdf)
    {
        var mapas = MapasPorFonte(pdf).Select(m => m.GroupBy(x => x.Glifo).ToDictionary(g => g.Key, g => g.First().Unicode)).ToList();
        var sb = new StringBuilder();
        foreach (var s in Streams(pdf))
        {
            foreach (Match m in Regex.Matches(s, @"<([0-9A-Fa-f\s]+)>\s*Tj"))
            {
                var hex = Regex.Replace(m.Groups[1].Value, @"\s+", "");
                var gids = new List<int>();
                for (var i = 0; i + 3 < hex.Length; i += 4) gids.Add(Convert.ToInt32(hex.Substring(i, 4), 16));
                foreach (var mapa in mapas.Where(mp => gids.Count(mp.ContainsKey) >= gids.Count * 0.8))
                {
                    sb.Append(new string(gids.Select(g => mapa.TryGetValue(g, out var c) ? c : ' ').ToArray())).Append('\n');
                }
            }
        }

        return sb.ToString();
    }
}