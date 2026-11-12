using System.Text.RegularExpressions;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;
using KitWmsIntegrador.Tests.Infra;

namespace KitWmsIntegrador.Tests;

/// <summary>Bloco "Tempo de integração" no e-mail de fechamento: tabelas, CSS inline, mesmos números do relatório.</summary>
public class CorpoEmailTempoTests
{
    // WebUtility.HtmlEncode grava acentos como entidades (&#231;); os asserts comparam o texto decodificado.
    private static string Corpo((IReadOnlyList<LinhaRelatorioErro> Linhas, DadosPacote Dados) p) =>
        System.Net.WebUtility.HtmlDecode(CorpoEmailResumo.Montar(
            p.Linhas, 0, TempoSintetico.Inicio, TempoSintetico.Inicio.AddSeconds(95), null, pacote: p.Dados).Html)!;

    [Fact]
    public void Tsp_TemBlocoEmCartao_ComTabelaDeOndas()
    {
        var html = Corpo(TempoSintetico.Tsp());

        Assert.Contains("Tempo de integração", html);
        Assert.Contains("Duração total", html);
        Assert.Contains("1 min 35 s", html);
        Assert.Contains("Espera da janela de coleta", html);
        Assert.Contains("Leitura do ZIP/XML", html);
        Assert.Contains("Integração no WMS", html);
        Assert.Contains("Por onda (Puxada TSP)", html);
        Assert.Contains("TSP-20261112-01-SJC", html);
        Assert.Contains("TSP-20261112-02-SOROCABA", html);
        Assert.Contains("Tempo da onda", html);
        Assert.DoesNotContain("Ondas da Puxada TSP</p>", html);
        // Cara de cartão como os demais: borda cinza + faixa superior ciano.
        Assert.Contains("border-top:4px solid #54B6E7", html);
    }

    [Fact]
    public void Puxada_TemResumoEEtapas_SemTabelaDeOndas()
    {
        var html = Corpo(TempoSintetico.Puxada());
        Assert.Contains("Tempo de integração", html);
        Assert.Contains("Integração no WMS", html);
        Assert.DoesNotContain("Por onda (Puxada TSP)", html);
        Assert.DoesNotContain("Tempo da onda", html);
    }

    [Fact]
    public void SemMedicao_NaoTemBloco_EMantemOParagrafoDasOndas()
    {
        var p = TempoSintetico.Tsp();
        var html = CorpoEmailResumo.Montar(p.Linhas, 0, TempoSintetico.Inicio, TempoSintetico.Inicio.AddSeconds(95)).Html;
        Assert.DoesNotContain("Tempo de integração", html);
        Assert.Contains("Ondas da Puxada TSP", html);
    }

    [Fact]
    public void Layout_SomenteTabelasECssInline_CompativelComOutlook()
    {
        var html = Corpo(TempoSintetico.Tsp());
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("display:flex", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("display:grid", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TextoDinamico_EHtmlEncoded()
    {
        var registros = TempoSintetico.RegistrosTsp()
            .Select(r => r.Onda == "TSP-20261112-01-SJC" ? r with { Onda = "TSP-<b>X</b>&Y" } : r)
            .ToList();
        var linhas = registros.Select(r => r.ParaLinha()).ToList();
        var html = CorpoEmailResumo.Montar(
            linhas, 0, TempoSintetico.Inicio, TempoSintetico.Inicio.AddSeconds(95), null,
            pacote: new DadosPacote("p", TempoSintetico.Medicao(registros))).Html;

        Assert.DoesNotContain("<b>X</b>", html);
        Assert.Contains("TSP-&lt;b&gt;X&lt;/b&gt;&amp;Y", html);
    }

    [Fact]
    public void MesmosNumerosNoRelatorioENoEmail()
    {
        var p = TempoSintetico.Tsp();
        var bloco = BlocoTempo.Montar(ModeloRelatorio.Montar(p.Linhas).Resumo, p.Dados.Medicao)!;
        var email = Corpo(p);
        var relatorio = new KitWmsIntegrador.Core.Configuracao.OpcoesRelatorio();
        var html = new GeradorRelatorio(Microsoft.Extensions.Options.Options.Create(relatorio))
            .GerarHtml(p.Linhas, TempoSintetico.Inicio, null, p.Dados);

        var textos = bloco.Resumo.Select(l => l.Valor)
            .Concat(bloco.Etapas.SelectMany(e => new[] { e.Duracao, e.PorNota }))
            .Concat(bloco.Ondas.SelectMany(o => new[] { o.Notas, o.Transportes, o.Tempo }))
            .Distinct();
        foreach (var t in textos)
        {
            Assert.Contains(">" + t + "<", email);
            Assert.Contains(">" + t + "<", html);
        }
    }

    [Fact]
    public async Task Fechamento_EnviaNoEmailAsMesmasDuracoesDoRelatorioGravado()
    {
        using var amb = new AmbientePacote();
        var orq = new OrquestradorFalso
        {
            Comportamento = (nota, transporte) =>
            {
                amb.Clock.Avancar(TimeSpan.FromSeconds(2));
                return Task.FromResult(OrquestradorFalso.Sucesso(nota.Numero, transporte) with { DuracaoWmsMs = 1500 });
            },
        };
        amb.EscreverXml("a.xml", "1", "T1");
        amb.EscreverXml("b.xml", "2", "T1");
        var g = amb.CriarGerenciador(orq);

        await amb.RodarAteOciosoAsync(g);

        var email = Assert.Single(amb.Email.Enviadas).CorpoHtml;
        var html = File.ReadAllText(Assert.Single(amb.RelatoriosGerados(".html")));
        Assert.Contains("Tempo de integração", email);
        Assert.Contains("Tempo de integração", html);

        // Todo valor de duração do bloco no relatório aparece igual no e-mail.
        var bloco = Regex.Match(html, "<h2>Tempo de integração</h2>.*?<p class=\"nota\">", RegexOptions.Singleline).Value;
        var valores = Regex.Matches(bloco, "<td class=\"num\">([^<]+)</td>").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(valores);
        foreach (var v in valores)
        {
            Assert.Contains(">" + v + "<", email);
        }

        Assert.Contains(">1,5 s<", email); // por nota: soma das chamadas (3 s) dividida por 2 notas
    }
}
