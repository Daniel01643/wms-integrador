using System.Diagnostics;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;

namespace KitWmsIntegrador.Tests.Stress;

/// <summary>
/// Decorator de <see cref="IGeradorRelatorio"/> que mede o tempo de HTML/PDF, o tamanho do PDF e
/// captura as linhas de cada pacote (uma entrada por chamada de GerarPdf) para a checagem de integridade.
/// </summary>
public sealed class GeradorMedido : IGeradorRelatorio
{
    private readonly IGeradorRelatorio _interno;

    public GeradorMedido(IGeradorRelatorio interno)
    {
        _interno = interno;
    }

    public List<IReadOnlyList<LinhaRelatorioErro>> LinhasPorPacote { get; } = new();

    public double TempoHtmlMs { get; private set; }

    public double TempoPdfMs { get; private set; }

    public double MaiorHtmlMs { get; private set; }

    public double MaiorPdfMs { get; private set; }

    public int MaiorPdfBytes { get; private set; }

    public string GerarHtml(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm, PeriodoColeta? periodo = null)
    {
        var sw = Stopwatch.StartNew();
        var html = _interno.GerarHtml(linhas, geradoEm, periodo);
        var ms = sw.Elapsed.TotalMilliseconds;
        TempoHtmlMs += ms;
        MaiorHtmlMs = Math.Max(MaiorHtmlMs, ms);
        return html;
    }

    public byte[] GerarPdf(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm, PeriodoColeta? periodo = null)
    {
        var sw = Stopwatch.StartNew();
        var pdf = _interno.GerarPdf(linhas, geradoEm, periodo);
        var ms = sw.Elapsed.TotalMilliseconds;
        TempoPdfMs += ms;
        MaiorPdfMs = Math.Max(MaiorPdfMs, ms);
        MaiorPdfBytes = Math.Max(MaiorPdfBytes, pdf.Length);
        LinhasPorPacote.Add(linhas.ToList());
        return pdf;
    }

    public (string CaminhoHtml, string CaminhoPdf) GerarArquivos(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm) =>
        _interno.GerarArquivos(linhas, geradoEm);
}
