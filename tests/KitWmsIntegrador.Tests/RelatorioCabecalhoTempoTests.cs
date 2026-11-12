using System.Text;
using System.Text.RegularExpressions;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.Options;
using PdfSharpCore.Pdf.IO;

namespace KitWmsIntegrador.Tests;

/// <summary>Cabeçalho com faixa/logo, remoção de "Notas por transporte" e bloco "Tempo de integração" (HTML e PDF).</summary>
public class RelatorioCabecalhoTempoTests
{
    private static readonly DateTimeOffset Em = new(2026, 11, 12, 11, 32, 0, TimeSpan.Zero);

    private static GeradorRelatorio Gerador(string logo = "") =>
        new(Options.Create(new OpcoesRelatorio { LogoPath = logo }));

    private static PeriodoColeta Periodo() =>
        new(TempoSintetico.Inicio.AddSeconds(5), TempoSintetico.Inicio.AddSeconds(25));

    private static string Html((IReadOnlyList<LinhaRelatorioErro> Linhas, DadosPacote Dados) p, string logo = "") =>
        Gerador(logo).GerarHtml(p.Linhas, Em, Periodo(), p.Dados);

    private static byte[] Pdf((IReadOnlyList<LinhaRelatorioErro> Linhas, DadosPacote Dados) p, string logo = "") =>
        Gerador(logo).GerarPdf(p.Linhas, Em, Periodo(), p.Dados);

    [Fact]
    public void Html_NaoTemMaisALinhaNotasPorTransporte_MasMantemSecaoEContadoresDosGrupos()
    {
        var html = Html(TempoSintetico.Puxada());

        Assert.DoesNotContain("Notas por transporte:", html);
        Assert.Contains("<h2>Notas por transporte</h2>", html); // a tabela continua
        Assert.Contains("Transporte 1000000500 — 2 notas · 2 integradas · 0 com problema", html);
        Assert.Contains("Transporte 1000000502 — 1 nota · 0 integradas · 1 com problema", html);
    }

    [Fact]
    public void Pdf_NaoTemMaisALinhaNotasPorTransporte()
    {
        var texto = PdfTextoHelper.Texto(Pdf(TempoSintetico.Puxada()));
        Assert.DoesNotContain("Notas por transporte:", texto);
        Assert.Contains("Transporte 1000000500", texto);
    }

    [Fact]
    public void Html_CabecalhoTemFaixaComLogoEmbutidaEInfoOrganizada()
    {
        var html = Html(TempoSintetico.Puxada());

        Assert.Contains("<header><div class=\"faixa\">", html);
        Assert.Contains("<img alt=\"Friozem\" src=\"data:image/png;base64,", html);
        Assert.Contains("<h1>Relatório de Integração</h1>", html);
        Assert.Contains("<title>Relatório de Integração</title>", html);
        Assert.DoesNotContain("Relatório de Erros", html);
        Assert.Contains("background:#17479E", html);

        // Três itens em elementos separados.
        Assert.Contains("<span><b>Pacote</b> pacote_20261112_083015 · PUXADA</span>", html);
        Assert.Matches(new Regex(@"<span><b>Gerado em</b> \d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2}</span>"), html);
        Assert.Matches(new Regex(@"<span><b>Coleta:</b> \d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2} a \d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2}</span>"), html);
        Assert.DoesNotContain("Gerado em: ", html);
        Assert.Contains("print-color-adjust:exact", html);
    }

    [Fact]
    public void Html_E_Autocontido_SemRecursoExterno()
    {
        var html = Html(TempoSintetico.Tsp());
        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
        Assert.DoesNotContain("<link", html);
        Assert.DoesNotContain("<script src", html);
        Assert.DoesNotContain("<img src=\"http", html);
    }

    [Fact]
    public void Html_TipoDoPacote_TspPuxadaEMista()
    {
        Assert.Contains("pacote_20261112_083015 · TSP</span>", Html(TempoSintetico.Tsp()));
        Assert.Contains("pacote_20261112_083015 · PUXADA</span>", Html(TempoSintetico.Puxada()));
        Assert.Contains("pacote_20261112_083015 · PUXADA + TSP</span>", Html(TempoSintetico.Mista()));
    }

    [Fact]
    public void Html_SemIdentificador_MostraTipo_EBlocoDeTempoFicaFora()
    {
        var p = TempoSintetico.Puxada();
        var html = Gerador().GerarHtml(p.Linhas, Em, null);
        Assert.Contains("<span><b>Tipo:</b> PUXADA</span>", html);
        Assert.DoesNotContain("Coleta:", html);
        Assert.DoesNotContain("Tempo de integração", html);
    }

    [Fact]
    public void Html_BlocoDeTempo_Puxada_TemResumoEEtapas_SemTabelaPorOnda()
    {
        var html = Html(TempoSintetico.Puxada());

        Assert.Contains("<h2>Tempo de integração</h2>", html);
        Assert.Contains("Resumo do pacote", html);
        Assert.Contains("Duração total", html);
        Assert.Contains("1 min 35 s", html);
        Assert.Contains("Tempo médio por nota", html);
        Assert.Contains("Notas por segundo", html);
        Assert.Contains("Espera da janela de coleta", html);
        Assert.Contains("Leitura do ZIP/XML", html);
        Assert.Contains("Integração no WMS", html);
        Assert.Contains("4,2 s", html);
        Assert.DoesNotContain("Por onda (Puxada TSP)", html);
        Assert.DoesNotContain("Tempo da onda", html);

        // O bloco vem no lugar da linha removida: depois do resumo e antes da tabela principal.
        Assert.True(html.IndexOf("<h2>Resumo</h2>", StringComparison.Ordinal)
                    < html.IndexOf("<h2>Tempo de integração</h2>", StringComparison.Ordinal));
        Assert.True(html.IndexOf("<h2>Tempo de integração</h2>", StringComparison.Ordinal)
                    < html.IndexOf("<h2>Notas por transporte</h2>", StringComparison.Ordinal));
    }

    [Fact]
    public void Html_BlocoDeTempo_Tsp_TemTabelaPorOnda_SemRepetirOsChips()
    {
        var html = Html(TempoSintetico.Tsp());

        Assert.Contains("Por onda (Puxada TSP)", html);
        Assert.Contains("Tempo da onda", html);
        Assert.Contains("<td>TSP-20261112-01-SJC</td><td class=\"num\">2</td><td class=\"num\">2</td><td class=\"num\">13 s</td>", html);
        Assert.Contains("<td>TSP-20261112-02-SOROCABA</td><td class=\"num\">5</td><td class=\"num\">4</td><td class=\"num\">35 s</td>", html);
        Assert.DoesNotContain("Ondas da Puxada TSP:", html);
    }

    [Fact]
    public void Html_SemMedicao_NaoTemBloco_EMantemOsChipsDasOndas()
    {
        var p = TempoSintetico.Tsp();
        var html = Gerador().GerarHtml(p.Linhas, Em, Periodo(), new DadosPacote(TempoSintetico.IdPacote, null));
        Assert.DoesNotContain("Tempo de integração", html);
        Assert.Contains("Ondas da Puxada TSP:", html);
    }

    [Fact]
    public void Html_TextoDinamicoDoBloco_VemCodificado()
    {
        var registros = TempoSintetico.RegistrosTsp()
            .Select(r => r.Onda == "TSP-20261112-01-SJC" ? r with { Onda = "TSP-<b>X</b>&Y" } : r)
            .ToList();
        var linhas = registros.Select(r => r.ParaLinha()).ToList();
        var dados = new DadosPacote("pacote_<i>", TempoSintetico.Medicao(registros));

        var html = Gerador().GerarHtml(linhas, Em, null, dados);

        Assert.DoesNotContain("<b>X</b>", html);
        Assert.Contains("TSP-&lt;b&gt;X&lt;/b&gt;&amp;Y", html);
        Assert.DoesNotContain("pacote_<i>", html);
        Assert.Contains("pacote_&lt;i&gt;", html);
    }

    [Fact]
    public void Html_ZeroNotas_DuracaoZero_NaoQuebra()
    {
        var m = MedicaoTempo.De(Array.Empty<RegistroDiario>(), TempoSintetico.Inicio, TempoSintetico.Inicio, null, null);
        var html = Gerador().GerarHtml(Array.Empty<LinhaRelatorioErro>(), Em, null, new DadosPacote("pacote_x", m));
        Assert.Contains("Duração total", html);
        Assert.Contains("0 ms", html);
        Assert.DoesNotContain("NaN", html);
        Assert.DoesNotContain("Infinity", html);
    }

    // ----- PDF -----

    [Fact]
    public void Pdf_TemTituloNovo_LogoEmbutida_EBlocoDeTempo()
    {
        var bytes = Pdf(TempoSintetico.Tsp());
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));

        using var ms = new MemoryStream(bytes);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.ReadOnly);
        Assert.Equal("Relatório de Integração", doc.Info.Title);
        var pagina = doc.Pages[0];
        Assert.True(pagina.Width.Point > pagina.Height.Point, "A4 paisagem");
        Assert.Contains("/Subtype /Image", Encoding.Latin1.GetString(bytes));

        var texto = PdfTextoHelper.Texto(bytes);
        Assert.Contains("Relatório de Integração", texto);
        Assert.Contains("KitWms Integrador", texto);
        Assert.Contains("pacote_20261112_083015", texto);
        Assert.Contains("Gerado em", texto);
        Assert.Contains("Coleta:", texto);
        Assert.Contains("Tempo de integração", texto);
        Assert.Contains("Duração total", texto);
        Assert.Contains("1 min 35 s", texto);
        Assert.Contains("TSP-20261112-02-SOROCABA", texto);
        Assert.Contains("Tempo da onda", texto);
        Assert.Contains("35 s", texto);
        Assert.DoesNotContain("Ondas da Puxada TSP:", texto);
    }

    [Fact]
    public void Pdf_Puxada_SemTabelaPorOnda()
    {
        var texto = PdfTextoHelper.Texto(Pdf(TempoSintetico.Puxada()));
        Assert.Contains("Integração no WMS", texto);
        Assert.DoesNotContain("Tempo da onda", texto);
        Assert.Contains("PUXADA", texto);
    }

    [Fact]
    public void Pdf_ComLogoDoArquivo_EhValido()
    {
        var arq = Path.Combine(Path.GetTempPath(), "logo-pdf-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(arq, IdentidadeVisual.LogoEmbutidoPng!);
            var bytes = Pdf(TempoSintetico.Puxada(), arq);
            Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Contains("/Subtype /Image", Encoding.Latin1.GetString(bytes));
        }
        finally
        {
            File.Delete(arq);
        }
    }

    [Fact]
    public void Pdf_SemNenhumaLogo_UsaTexto_SemExcecao()
    {
        var p = TempoSintetico.Tsp();
        var bytes = RenderizadorPdf.Renderizar(ModeloRelatorio.Montar(p.Linhas), Em, null, Periodo(), p.Dados);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.DoesNotContain("/Subtype /Image", Encoding.Latin1.GetString(bytes));
        Assert.Contains("Friozem", PdfTextoHelper.Texto(bytes));
    }

    [Fact]
    public void Pdf_MuitasOndasTsp_PaginamSemErro()
    {
        var registros = new List<RegistroDiario>();
        for (var i = 1; i <= 60; i++)
        {
            var onda = $"TSP-20261112-{i:00}-DESTINO{i}";
            registros.Add(new RegistroDiario(
                $"arq{i}.xml", false, (500000 + i).ToString(), "100000" + i, TempoSintetico.Inicio.AddSeconds(40 + i), "ok", true, 1,
                StatusLinhaRelatorio.Integrada, "", "", Array.Empty<string>(), Array.Empty<string>(), null,
                DestinoArquivo.Processados, "p", "TSP", "PuxadaTSP", onda, "z.zip",
                TempoSintetico.Inicio.AddSeconds(39 + i), 900));
        }

        var linhas = registros.Select(r => r.ParaLinha()).ToList();
        var dados = new DadosPacote("pacote_x", TempoSintetico.Medicao(registros, 200));
        var bytes = Gerador().GerarPdf(linhas, Em, null, dados);

        using var ms = new MemoryStream(bytes);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.ReadOnly);
        Assert.True(doc.PageCount >= 2);
        Assert.Contains("TSP-20261112-60-DESTINO60", PdfTextoHelper.Texto(bytes));
    }

    [Fact]
    public void Pdf_SemMedicaoOuZeroNotas_NaoQuebra()
    {
        var p = TempoSintetico.Puxada();
        Assert.NotEmpty(Gerador().GerarPdf(p.Linhas, Em, null, new DadosPacote("pacote_x", null)));
        Assert.DoesNotContain("Tempo de integração",
            PdfTextoHelper.Texto(Gerador().GerarPdf(p.Linhas, Em, null, new DadosPacote("pacote_x", null))));

        var m = MedicaoTempo.De(Array.Empty<RegistroDiario>(), TempoSintetico.Inicio, TempoSintetico.Inicio, null, null);
        Assert.NotEmpty(Gerador().GerarPdf(Array.Empty<LinhaRelatorioErro>(), Em, null, new DadosPacote("pacote_x", m)));
    }
}
