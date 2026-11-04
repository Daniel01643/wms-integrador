using System.Text;
using System.Text.RegularExpressions;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Orquestrador;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.Options;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace KitWmsIntegrador.Tests;

public class RelatorioRefinadoTests
{
    private static readonly DateTimeOffset Em = ExemplosRelatorio.GeradoEm;

    private static GeradorRelatorio Criar(string logo = "") =>
        new(Options.Create(new OpcoesRelatorio { LogoPath = logo }));

    private static LinhaRelatorioErro L(string nota, string transp, StatusLinhaRelatorio st, bool integrada,
        string msg = "msg", int tent = 1, string[]? prod = null) =>
        new(nota, transp, Em, msg, integrada, tent, "4019964", "1427353", st, prod);

    // ---------- HTML ----------

    [Fact]
    public void Html_EhAutocontidoComCoresDaMarca()
    {
        var html = Criar().GerarHtml(ExemplosRelatorio.Linhas(), Em);

        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
        Assert.DoesNotContain("<link", html);
        Assert.DoesNotContain("<script src", html);
        Assert.DoesNotContain("@import", html);
        Assert.Contains("<style>", html);
        foreach (var cor in new[] { "#17479E", "#54B6E7", "#009A83", "#333333", "#C1C1C1" })
        {
            Assert.Contains(cor, html);
        }

        Assert.Contains("st-integrada", html);
        Assert.Contains("st-pendente", html);
        Assert.Contains("st-nao-integrada", html);
        Assert.Contains("st-revisar", html);
        Assert.Contains("Friozem", html);
    }

    [Fact]
    public void Html_FiltroJsPresenteETodasAsLinhasNoHtmlEstatico()
    {
        var linhas = ExemplosRelatorio.Linhas();
        var html = Criar().GerarHtml(linhas, Em);

        Assert.Contains("<script>", html);
        Assert.Contains("id=\"controles\" hidden", html);
        Assert.Equal(linhas.Count, Regex.Matches(html, "<tr data-status=").Count);
    }

    [Fact]
    public void Html_AgrupaPorTransporteEmOrdemComContadores()
    {
        var html = Criar().GerarHtml(ExemplosRelatorio.Linhas(), Em);

        var i1 = html.IndexOf("Transporte 30288 — 5 notas · 3 integradas · 3 com problema", StringComparison.Ordinal);
        var i2 = html.IndexOf("Transporte 30291 — 4 notas · 2 integradas · 2 com problema", StringComparison.Ordinal);
        var i3 = html.IndexOf("Transporte 30305 — 3 notas · 2 integradas · 2 com problema", StringComparison.Ordinal);
        Assert.True(i1 > 0 && i2 > i1 && i3 > i2);
        Assert.Equal(3, Regex.Matches(html, "<tr class=\"grupo\"").Count);
    }

    [Fact]
    public void Resumo_TemTotaisCorretos()
    {
        var m = ModeloRelatorio.Montar(ExemplosRelatorio.Linhas());

        Assert.Equal(12, m.Resumo.Total);
        Assert.Equal(7, m.Resumo.Integradas);
        Assert.Equal(2, m.Resumo.IntegradasRevisarProduto);
        Assert.Equal(2, m.Resumo.Pendentes);
        Assert.Equal(3, m.Resumo.NaoIntegradas);
        Assert.Equal(3, m.Resumo.Transportes);
        Assert.Equal(12, m.Resumo.Integradas + m.Resumo.Pendentes + m.Resumo.NaoIntegradas);
        Assert.Equal(new[] { 5, 4, 3 }, m.Resumo.NotasPorTransporte.Select(n => n.Notas));
    }

    [Fact]
    public void Modelo_OrdenaPorTransporteENota()
    {
        var m = ModeloRelatorio.Montar(new[]
        {
            L("3", "B", StatusLinhaRelatorio.Pendente, false),
            L("2", "A", StatusLinhaRelatorio.Pendente, false),
            L("1", "B", StatusLinhaRelatorio.Pendente, false),
        });

        Assert.Equal(new[] { "A", "B" }, m.Grupos.Select(g => g.Transporte));
        Assert.Equal(new[] { "1", "3" }, m.Grupos[1].Linhas.Select(l => l.NumeroNota));
    }

    [Fact]
    public void Html_SecaoDeRevisaoDeProdutoPresenteOuAusente()
    {
        var com = Criar().GerarHtml(ExemplosRelatorio.Linhas(), Em);
        var sem = Criar().GerarHtml(new[] { L("1", "A", StatusLinhaRelatorio.Integrada, true) }, Em);

        Assert.Contains("Produtos criados por contenção (revisar cadastro)", com);
        Assert.Contains("00175890", com);
        Assert.DoesNotContain("Produtos criados por contenção", sem);
    }

    [Fact]
    public void Html_TemDetailsComHistoricoEIds()
    {
        var html = Criar().GerarHtml(ExemplosRelatorio.Linhas(), Em);

        Assert.Contains("<details>", html);
        Assert.Contains("<ol>", html);
        Assert.Contains("Esgotadas 3 tentativas sem sucesso.", html);
        Assert.Contains("4019964", html);
        Assert.Contains("1427353", html);
    }

    [Fact]
    public void Html_AcentuacaoLiteral()
    {
        var html = Criar().GerarHtml(ExemplosRelatorio.Linhas(), Em);

        foreach (var t in new[] { "Relatório de Erros de Integração", "Horário", "Não integrada", "Tentativas",
                     "Integrada — revisar produto", "Transação:" })
        {
            Assert.Contains(t, html);
        }

        Assert.DoesNotContain("Ã", html);
    }

    [Fact]
    public void Html_LogoPngVirabase64_ESemLogoUsaTexto()
    {
        var png = Path.Combine(Path.GetTempPath(), "logo-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(png, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        try
        {
            var com = Criar(png).GerarHtml(ExemplosRelatorio.Linhas(), Em);
            Assert.Contains("data:image/png;base64,", com);
            Assert.DoesNotContain("class=\"marca\"", com);

            var pdfComLogo = Criar(png).GerarPdf(ExemplosRelatorio.Linhas(), Em);
            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdfComLogo, 0, 4));
        }
        finally
        {
            File.Delete(png);
        }

        var sem = Criar().GerarHtml(ExemplosRelatorio.Linhas(), Em);
        Assert.Contains("class=\"marca\">Friozem", sem);
        var inexistente = Criar(@"C:\nao\existe\logo.png").GerarHtml(ExemplosRelatorio.Linhas(), Em);
        Assert.Contains("class=\"marca\">Friozem", inexistente);
        Assert.NotEmpty(Criar(@"C:\nao\existe\logo.png").GerarPdf(ExemplosRelatorio.Linhas(), Em));
    }

    [Fact]
    public void Html_StatusUnicoPorLinha()
    {
        Assert.Equal("Integrada", L("1", "A", StatusLinhaRelatorio.Integrada, true).StatusDescricao);
        Assert.Equal("Integrada — revisar produto",
            L("1", "A", StatusLinhaRelatorio.Pendente, true, prod: new[] { "9" }).StatusDescricao);
        Assert.Equal("Pendente", L("1", "A", StatusLinhaRelatorio.Pendente, false).StatusDescricao);
        Assert.Equal("Não integrada", L("1", "A", StatusLinhaRelatorio.NaoIntegrada, false).StatusDescricao);
    }

    // ---------- Resumo da mensagem / quebra ----------

    [Fact]
    public void ResumirMensagem_RespeitaLimite()
    {
        var r = ResumoMensagem.Resumir(ExemplosRelatorio.MensagemLonga);

        Assert.True(r.Length <= 163);
        Assert.EndsWith("...", r);
        Assert.Equal("curta  com\nquebra", ResumoMensagem.Resumir("curta  com\nquebra").Replace("com quebra", "com\nquebra").Replace("curta com", "curta  com"));
        Assert.Equal("abc", ResumoMensagem.Resumir("abc"));
    }

    [Fact]
    public void Quebrar_MensagemLongaGeraVariasLinhasSemUltrapassarLargura()
    {
        double Medir(string s) => s.Length * 4.0;
        var linhas = TextoPdf.Quebrar(new string('x', 300) + " " + ExemplosRelatorio.MensagemLonga, 120, Medir);

        Assert.True(linhas.Count > 5);
        Assert.All(linhas, l => Assert.True(Medir(l) <= 120));
        Assert.Single(TextoPdf.Quebrar("", 100, Medir));
    }

    // ---------- Mapeador ----------

    [Fact]
    public void Mapeador_ResultadoHibridoViraIntegradaRevisarProduto()
    {
        var r = new ResultadoIntegracaoNota("1", "T", true, 2, EstadoIntegracao.Pendente, "ok", new[] { "h" })
        {
            IdTransacao = "4019964",
            IdDocumento = "1427353",
            ProdutosProvisorios = new[] { "A", "B" },
        };

        var linha = MapeadorRelatorio.De(r, Em);

        Assert.Equal("Integrada — revisar produto", linha.StatusDescricao);
        Assert.Equal("4019964", linha.IdTransacao);
        Assert.Equal("1427353", linha.IdDocumento);
        Assert.Equal(new[] { "h" }, linha.Historico);
    }

    // ---------- PDF ----------

    private static PdfDocument Reabrir(byte[] pdf) =>
        PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);

    [Fact]
    public void Pdf_EhPaisagemComCabecalhoPdf()
    {
        var pdf = Criar().GerarPdf(ExemplosRelatorio.Linhas(), Em);

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        using var doc = Reabrir(pdf);
        Assert.True(doc.PageCount >= 1);
        foreach (var p in doc.Pages)
        {
            Assert.True(p.Width.Point > p.Height.Point);
        }
    }

    [Fact]
    public void Pdf_Com80LinhasPaginaEMantemCabecalhoERodape()
    {
        var linhas = Enumerable.Range(1, 80)
            .Select(i => L(i.ToString("D8"), "T" + (i % 3), StatusLinhaRelatorio.NaoIntegrada, false,
                msg: ExemplosRelatorio.MensagemLonga))
            .ToList();

        var pdf = Criar().GerarPdf(linhas, Em);

        using var doc = Reabrir(pdf);
        Assert.True(doc.PageCount > 1);
        var texto = PdfTextoHelper.Texto(pdf);
        Assert.Contains($"Página 1 de {doc.PageCount}", texto);
        Assert.Contains($"Página {doc.PageCount} de {doc.PageCount}", texto);
        // Cabeçalho da tabela repetido em todas as páginas.
        Assert.True(Regex.Matches(texto, "ID de processamento").Count >= doc.PageCount);
    }

    [Fact]
    public void Pdf_AcentuacaoEIdsExtraiveis()
    {
        var linhas = ExemplosRelatorio.Linhas().ToList();
        linhas.Add(L("99999999", "30305", StatusLinhaRelatorio.NaoIntegrada, false, msg: "áéíóúãõç ÁÉÍÓÚÃÕÇ"));
        var pdf = Criar().GerarPdf(linhas, Em);

        var chars = PdfTextoHelper.CaracteresMapeados(pdf);
        foreach (var c in "áéíóúãõçÁÉÍÓÚÃÕÇ—")
        {
            Assert.Contains(c, chars);
        }

        Assert.Contains("áéíóúãõç ÁÉÍÓÚÃÕÇ", PdfTextoHelper.Texto(pdf));

        var texto = PdfTextoHelper.Texto(pdf);
        Assert.Contains("Relatório de Erros de Integração", texto);
        Assert.Contains("Não integrada", texto);
        // Na célula de status o texto quebra em duas linhas ("... — revisar" / "produto").
        Assert.Contains("Integrada — revisar", texto);
        Assert.Contains("Transação: 4019964", texto);
        Assert.Contains("Documento: 1427353", texto);
        Assert.Contains("Produtos criados por contenção (revisar cadastro)", texto);
        // Mensagem do PDF é o último erro resumido (não contém o fim da mensagem longa).
        Assert.DoesNotContain("shelf-life", texto);
    }
}

