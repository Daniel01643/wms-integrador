using System.Globalization;
using System.Text;

namespace KitWmsIntegrador.Core.Pacote;

/// <summary>
/// Monta o nome da onda da Puxada TSP: <c>TSP-AAAAMMDD-NN-NOME</c>. Peça pura (sem disco/relógio).
/// O campo <c>nroagrupseparacao</c> da I50 aceita de 0 a 30 caracteres, então o NOME é cortado
/// para que o total nunca passe de <see cref="LimiteTotal"/>.
/// </summary>
public static class NomeadorOndaTsp
{
    /// <summary>Prefixo fixo que identifica a onda da Puxada TSP no WMS.</summary>
    public const string Prefixo = "TSP-";

    /// <summary>Tamanho máximo do nome da onda (limite do <c>nroagrupseparacao</c> na I50).</summary>
    public const int LimiteTotal = 30;

    // Apóstrofos comuns (ASCII, agudo, crase, aspas tipográficas). Removidos: XML'S vira XMLS.
    private static readonly char[] Apostrofos = { '\'', '\u00B4', '`', '\u2019', '\u2018', '\u02BC' };

    /// <summary>
    /// Limpa o nome do ZIP: tira a extensão, apóstrofos, as palavras XML/XMLS e acentos; deixa em
    /// maiúsculas, só com letras, números e hífen. Devolve vazio se não sobrar nada.
    /// </summary>
    public static string LimparNome(string? nomeArquivoZip)
    {
        if (string.IsNullOrWhiteSpace(nomeArquivoZip))
        {
            return string.Empty;
        }

        var nome = Path.GetFileNameWithoutExtension(nomeArquivoZip.Trim());
        foreach (var apostrofo in Apostrofos)
        {
            nome = nome.Replace(apostrofo.ToString(), string.Empty, StringComparison.Ordinal);
        }

        var semAcento = new StringBuilder(nome.Length);
        foreach (var c in nome.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                semAcento.Append(char.ToUpperInvariant(c));
            }
        }

        // Qualquer coisa fora de A-Z/0-9 separa palavras; as palavras XML/XMLS saem do nome.
        var palavras = new List<string>();
        var atual = new StringBuilder();
        foreach (var c in semAcento.ToString())
        {
            if (c is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            {
                atual.Append(c);
                continue;
            }

            Descarregar(palavras, atual);
        }

        Descarregar(palavras, atual);
        return string.Join('-', palavras);
    }

    /// <summary>
    /// Monta <c>TSP-AAAAMMDD-NN-NOME</c> (NOME já limpo e cortado para caber em 30). Sem nome:
    /// <c>TSP-AAAAMMDD-NN</c>. NN tem 2 dígitos (3 se passar de 99).
    /// </summary>
    public static string Montar(DateTime dataLocal, int sequencial, string? nomeArquivoZip)
    {
        var fixo = Prefixo + dataLocal.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" +
                   sequencial.ToString("D2", CultureInfo.InvariantCulture);
        var nome = LimparNome(nomeArquivoZip);
        var espaco = LimiteTotal - fixo.Length - 1;
        if (nome.Length == 0 || espaco <= 0)
        {
            return fixo;
        }

        if (nome.Length > espaco)
        {
            nome = nome[..espaco].TrimEnd('-');
        }

        return nome.Length == 0 ? fixo : fixo + "-" + nome;
    }

    private static void Descarregar(List<string> palavras, StringBuilder atual)
    {
        if (atual.Length == 0)
        {
            return;
        }

        var palavra = atual.ToString();
        atual.Clear();
        if (palavra is "XML" or "XMLS")
        {
            return;
        }

        palavras.Add(palavra);
    }
}
