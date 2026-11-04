using System.Globalization;
using System.Text;
using KitWmsIntegrador.Core.Configuracao;
using Microsoft.Extensions.Options;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;

namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>
/// Gera o relatório de erros/pendências em HTML (via <see cref="StringBuilder"/>) e PDF
/// (via PdfSharpCore, desenho manual). O relatório é só texto/tabela — não usa imagens.
/// Pasta de saída vem de <see cref="OpcoesRelatorio.PastaSaida"/> (configurável).
/// </summary>
public sealed class GeradorRelatorio : IGeradorRelatorio
{
    /// <summary>Formato do horário do erro, com fuso (ex.: <c>01/08/2025 14:30:05 -03:00</c>).</summary>
    private const string FormatoHorario = "dd/MM/yyyy HH:mm:ss zzz";

    /// <summary>Formato da data/hora de geração exibida no cabeçalho.</summary>
    private const string FormatoGeradoEm = "dd/MM/yyyy HH:mm:ss zzz";

    private const string Titulo = "Relatório de Erros de Integração";

    private static readonly string[] Cabecalhos =
    {
        "Número da Nota",
        "Número do Transporte",
        "Horário do Erro",
        "Mensagem",
        "Integrada",
        "Tentativas",
    };

    // O build Core da PdfSharpCore exige um FontResolver para mapear fontes.
    // Registra o resolver padrão da biblioteca uma única vez (idempotente).
    static GeradorRelatorio()
    {
        GlobalFontSettings.FontResolver ??= new PdfSharpCore.Utils.FontResolver();
    }

    private readonly OpcoesRelatorio _opcoes;

    public GeradorRelatorio(IOptions<OpcoesRelatorio> opcoes)
    {
        _opcoes = opcoes.Value;
    }

    /// <inheritdoc />
    public string GerarHtml(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>");
        sb.Append("<html lang=\"pt-BR\"><head><meta charset=\"utf-8\" />");
        sb.Append("<title>").Append(EscaparHtml(Titulo)).Append("</title>");
        sb.Append("<style>table{border-collapse:collapse}th,td{border:1px solid #333;padding:4px 8px;text-align:left}</style>");
        sb.Append("</head><body>");
        sb.Append("<h1>").Append(EscaparHtml(Titulo)).Append("</h1>");
        sb.Append("<p>Gerado em: ")
          .Append(EscaparHtml(geradoEm.ToString(FormatoGeradoEm, CultureInfo.InvariantCulture)))
          .Append("</p>");

        sb.Append("<table><thead><tr>");
        foreach (var coluna in Cabecalhos)
        {
            sb.Append("<th>").Append(EscaparHtml(coluna)).Append("</th>");
        }
        sb.Append("</tr></thead><tbody>");

        foreach (var linha in linhas)
        {
            sb.Append("<tr>");
            sb.Append("<td>").Append(EscaparHtml(linha.NumeroNota)).Append("</td>");
            sb.Append("<td>").Append(EscaparHtml(linha.NumeroTransporte)).Append("</td>");
            sb.Append("<td>").Append(EscaparHtml(linha.HorarioErro.ToString(FormatoHorario, CultureInfo.InvariantCulture))).Append("</td>");
            sb.Append("<td>").Append(EscaparHtml(linha.Mensagem)).Append("</td>");
            sb.Append("<td>").Append(linha.Integrada ? "S" : "N").Append("</td>");
            sb.Append("<td>").Append(linha.Tentativas.ToString(CultureInfo.InvariantCulture)).Append("</td>");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <inheritdoc />
    public byte[] GerarPdf(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm)
    {
        using var documento = new PdfDocument();
        var pagina = documento.AddPage();
        using var gfx = XGraphics.FromPdfPage(pagina);

        var fonteTitulo = new XFont("Arial", 14, XFontStyle.Bold);
        var fonteCabecalho = new XFont("Arial", 9, XFontStyle.Bold);
        var fonteLinha = new XFont("Arial", 9, XFontStyle.Regular);

        const double margemX = 40;
        var y = 40.0;

        // Título e data de geração.
        gfx.DrawString(Titulo, fonteTitulo, XBrushes.Black, new XPoint(margemX, y));
        y += 22;
        gfx.DrawString(
            "Gerado em: " + geradoEm.ToString(FormatoGeradoEm, CultureInfo.InvariantCulture),
            fonteLinha, XBrushes.Black, new XPoint(margemX, y));
        y += 24;

        // Espaçamento fixo por coluna (simples e legível). A coluna Mensagem é mais
        // estreita para abrir espaço às colunas Integrada/Tentativas sem estourar a página.
        double[] colunasX =
        {
            margemX,        // Número da Nota
            margemX + 80,   // Número do Transporte
            margemX + 190,  // Horário do Erro
            margemX + 320,  // Mensagem
            margemX + 440,  // Integrada
            margemX + 500,  // Tentativas
        };
        const double alturaLinha = 16;

        // Cabeçalho da tabela.
        for (var i = 0; i < Cabecalhos.Length; i++)
        {
            gfx.DrawString(Cabecalhos[i], fonteCabecalho, XBrushes.Black, new XPoint(colunasX[i], y));
        }
        y += 4;
        gfx.DrawLine(XPens.Black, margemX, y, pagina.Width - margemX, y);
        y += alturaLinha;

        // Uma linha por erro.
        foreach (var linha in linhas)
        {
            var valores = new[]
            {
                linha.NumeroNota,
                linha.NumeroTransporte,
                linha.HorarioErro.ToString(FormatoHorario, CultureInfo.InvariantCulture),
                linha.Mensagem,
                linha.Integrada ? "S" : "N",
                linha.Tentativas.ToString(CultureInfo.InvariantCulture),
            };

            for (var i = 0; i < valores.Length; i++)
            {
                gfx.DrawString(valores[i] ?? string.Empty, fonteLinha, XBrushes.Black, new XPoint(colunasX[i], y));
            }

            y += alturaLinha;
        }

        using var ms = new MemoryStream();
        documento.Save(ms);
        return ms.ToArray();
    }

    /// <inheritdoc />
    public (string CaminhoHtml, string CaminhoPdf) GerarArquivos(
        IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm)
    {
        Directory.CreateDirectory(_opcoes.PastaSaida);

        var baseNome = "erros_" + geradoEm.ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture);
        var caminhoHtml = Path.Combine(_opcoes.PastaSaida, baseNome + ".html");
        var caminhoPdf = Path.Combine(_opcoes.PastaSaida, baseNome + ".pdf");

        File.WriteAllText(caminhoHtml, GerarHtml(linhas, geradoEm), Encoding.UTF8);
        File.WriteAllBytes(caminhoPdf, GerarPdf(linhas, geradoEm));

        return (caminhoHtml, caminhoPdf);
    }

    /// <summary>Escapa os caracteres HTML básicos (&amp; &lt; &gt;) do texto.</summary>
    private static string EscaparHtml(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return string.Empty;
        }

        return texto
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
