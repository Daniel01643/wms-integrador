using System.Globalization;
using System.Text;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class GeradorRelatorioTests
{
    private static readonly DateTimeOffset GeradoEm =
        new(2025, 8, 1, 14, 30, 5, TimeSpan.FromHours(-3));

    private static IReadOnlyList<LinhaRelatorioErro> LinhasExemplo() => new[]
    {
        new LinhaRelatorioErro("15665807", "0130693549", GeradoEm, "Nota não encontrada no WMS"),
        new LinhaRelatorioErro("15665808", "0130693550",
            new DateTimeOffset(2025, 8, 1, 15, 0, 0, TimeSpan.FromHours(-3)),
            "Falha de associação <automática> & manual"),
    };

    private static GeradorRelatorio Criar(string? pastaSaida = null) =>
        new(Options.Create(new OpcoesRelatorio { PastaSaida = pastaSaida ?? "relatorios" }));

    [Fact]
    public void GerarHtml_ContemValoresETagsDeTabela()
    {
        var linhas = LinhasExemplo();

        var html = Criar().GerarHtml(linhas, GeradoEm);

        // Tags da tabela.
        Assert.Contains("<table", html);
        Assert.Contains("<tr", html);
        Assert.Contains("<td", html);

        // Valores das linhas.
        Assert.Contains("15665807", html);
        Assert.Contains("15665808", html);
        Assert.Contains("0130693549", html);
        Assert.Contains("Nota não encontrada no WMS", html);

        // Horário formatado com fuso.
        var horarioFormatado = GeradoEm.ToString("dd/MM/yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture);
        Assert.Contains(horarioFormatado, html);

        // Mensagem com caracteres HTML escapados.
        Assert.Contains("&lt;automática&gt; &amp; manual", html);
    }

    [Fact]
    public void GerarPdf_RetornaBytesNaoVaziosComHeaderPdf()
    {
        var linhas = LinhasExemplo();

        var pdf = Criar().GerarPdf(linhas, GeradoEm);

        Assert.NotEmpty(pdf);
        var header = Encoding.ASCII.GetString(pdf, 0, 4);
        Assert.Equal("%PDF", header);
    }

    [Fact]
    public void GerarArquivos_CriaOsDoisArquivosNaPastaConfigurada()
    {
        var pastaTemp = Path.Combine(Path.GetTempPath(), "wms-rel-" + Guid.NewGuid().ToString("N"));
        var linhas = LinhasExemplo();

        try
        {
            var (caminhoHtml, caminhoPdf) = Criar(pastaTemp).GerarArquivos(linhas, GeradoEm);

            var nomeEsperado = "erros_" + GeradoEm.ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture);

            Assert.Equal(Path.Combine(pastaTemp, nomeEsperado + ".html"), caminhoHtml);
            Assert.Equal(Path.Combine(pastaTemp, nomeEsperado + ".pdf"), caminhoPdf);

            Assert.True(File.Exists(caminhoHtml));
            Assert.True(File.Exists(caminhoPdf));
            Assert.True(new FileInfo(caminhoHtml).Length > 0);
            Assert.True(new FileInfo(caminhoPdf).Length > 0);
        }
        finally
        {
            if (Directory.Exists(pastaTemp))
            {
                Directory.Delete(pastaTemp, recursive: true);
            }
        }
    }
}
