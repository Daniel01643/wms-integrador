using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Relatorio;

namespace KitWmsIntegrador.Tests;

public class CorpoEmailResumoTests
{
    private static readonly DateTimeOffset T = new(2025, 1, 2, 8, 30, 0, TimeSpan.Zero);

    private static LinhaRelatorioErro Erro(string nota, string msg) =>
        new(nota, "T1", T, msg, Integrada: false);

    [Fact]
    public void Contagens_AparecemNoCorpo()
    {
        var linhas = new List<LinhaRelatorioErro>
        {
            new("1", "T1", T, "", Integrada: true),
            Erro("2", "falhou"),
            new("3", "T1", T, "x", Status: StatusLinhaRelatorio.Pendente)
        };

        var html = CorpoEmailResumo.Montar(linhas, 4, T, T.AddHours(1));

        Assert.Contains("Total de notas: 3", html);
        Assert.Contains("Integradas: 1", html);
        Assert.Contains("Pendentes: 1", html);
        Assert.Contains("Não integradas: 1", html);
        Assert.Contains("XMLs inválidos: 4", html);
        Assert.Contains("02/01/2025 08:30", html);
    }

    [Fact]
    public void Mensagem_EhHtmlEncoded()
    {
        var html = CorpoEmailResumo.Montar(new[] { Erro("2", "<script>alert(1)</script>") }, 0, T, T);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Truncamento_IndicaRestantes()
    {
        var linhas = Enumerable.Range(1, 5).Select(i => Erro(i.ToString("000"), "erro")).ToList();

        var html = CorpoEmailResumo.Montar(linhas, 0, T, T, maxLinhas: 2);

        Assert.Contains("e mais 3, ver PDF", html);
        Assert.Contains("<td>001</td>", html);
        Assert.DoesNotContain("<td>003</td>", html);
    }

    [Fact]
    public void SoIntegradas_SemTabela()
    {
        var html = CorpoEmailResumo.Montar(new[] { new LinhaRelatorioErro("1", "T1", T, "", Integrada: true) }, 0, T, T);

        Assert.DoesNotContain("<table", html);
    }
}
