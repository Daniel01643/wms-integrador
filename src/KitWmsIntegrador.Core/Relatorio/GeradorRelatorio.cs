using System.Globalization;
using System.Text;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Pacote;
using Microsoft.Extensions.Options;
using PdfSharpCore.Fonts;

namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>
/// Gera o relatório de erros/pendências em HTML (autocontido) e PDF (A4 paisagem, via
/// PdfSharpCore). Ambos partem do mesmo <see cref="ModeloRelatorio"/>, com resumo, agrupamento
/// por transporte e identidade visual Friozem. Pasta de saída e logo vêm de
/// <see cref="OpcoesRelatorio"/> (configuráveis).
/// </summary>
public sealed class GeradorRelatorio : IGeradorRelatorio
{
    // O build Core da PdfSharpCore exige um FontResolver para mapear fontes (Arial vem
    // das fontes do sistema). Registra o resolver padrão uma única vez (idempotente).
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
    public string GerarHtml(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm, PeriodoColeta? periodo = null) =>
        RenderizadorHtml.Renderizar(
            ModeloRelatorio.Montar(linhas), geradoEm, IdentidadeVisual.LerLogoPng(_opcoes.LogoPath), periodo);

    /// <inheritdoc />
    public byte[] GerarPdf(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm, PeriodoColeta? periodo = null) =>
        RenderizadorPdf.Renderizar(
            ModeloRelatorio.Montar(linhas), geradoEm, IdentidadeVisual.LerLogoPng(_opcoes.LogoPath), periodo);

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
}
