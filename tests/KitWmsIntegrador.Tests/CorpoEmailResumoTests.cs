using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Relatorio;

namespace KitWmsIntegrador.Tests;

public class CorpoEmailResumoTests
{
    private static readonly DateTimeOffset T = new(2025, 1, 2, 8, 30, 0, TimeSpan.Zero);

    // Assinatura PNG mínima válida (8 bytes de magic number), suficiente para o corpo embutir o CID.
    private static readonly byte[] LogoPng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01 };

    private static LinhaRelatorioErro Erro(string nota, string msg) =>
        new(nota, "T1", T, msg, Integrada: false);

    [Fact]
    public void Cartoes_MostramContagensPorStatus()
    {
        var linhas = new List<LinhaRelatorioErro>
        {
            new("1", "T1", T, "", Integrada: true),
            Erro("2", "falhou"),
            new("3", "T1", T, "x", Status: StatusLinhaRelatorio.Pendente)
        };

        var html = CorpoEmailResumo.Montar(linhas, 4, T, T.AddHours(1)).Html;

        // Rótulos dos cartões.
        Assert.Contains("Total", html);
        Assert.Contains("Integradas", html);
        Assert.Contains("Pendentes", html);
        Assert.Contains("Não integradas", html);
        Assert.Contains("XMLs inválidos", html);
        Assert.Contains("Transportes", html);
        // Período em horário local, sem offset "+00:00".
        Assert.Contains(IdentidadeVisualHorario(T), html);
        Assert.DoesNotContain("+00:00", html);
    }

    [Fact]
    public void UsaTabelasECssInline_SemStyleLinkScript()
    {
        var html = CorpoEmailResumo.Montar(new[] { Erro("2", "falhou") }, 0, T, T).Html;

        Assert.Contains("<table", html);
        Assert.Contains("style=", html);
        Assert.DoesNotContain("<style", html);
        Assert.DoesNotContain("<link", html);
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public void SemLogo_NaoEmbuteImagemNemDataUri()
    {
        var corpo = CorpoEmailResumo.Montar(new[] { Erro("2", "falhou") }, 0, T, T, logoPng: null);

        Assert.Null(corpo.ImagemEmbutida);
        Assert.DoesNotContain("cid:logo-friozem", corpo.Html);
        Assert.DoesNotContain("data:image", corpo.Html);
        Assert.Contains(">Friozem<", corpo.Html);
    }

    [Fact]
    public void ComLogo_EmbuteImagemPorCid_SemDataUri()
    {
        var corpo = CorpoEmailResumo.Montar(new[] { Erro("2", "falhou") }, 0, T, T, logoPng: LogoPng);

        Assert.NotNull(corpo.ImagemEmbutida);
        Assert.Equal(CorpoEmailResumo.CidLogo, corpo.ImagemEmbutida!.ContentId);
        Assert.Equal("image/png", corpo.ImagemEmbutida.ContentType);
        Assert.Contains("src=\"cid:logo-friozem\"", corpo.Html);
        Assert.DoesNotContain("data:image", corpo.Html);
    }

    [Fact]
    public void Mensagem_EhHtmlEncoded()
    {
        var html = CorpoEmailResumo.Montar(new[] { Erro("2", "<script>alert(\"1\")</script> & fim") }, 0, T, T).Html;

        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&amp;", html);
        Assert.Contains("&quot;", html);
    }

    [Fact]
    public void Truncamento_IndicaRestantesComNotaDoPdf()
    {
        var linhas = Enumerable.Range(1, 5).Select(i => Erro(i.ToString("000"), "erro")).ToList();

        var html = CorpoEmailResumo.Montar(linhas, 0, T, T, maxLinhas: 2).Html;

        Assert.Contains("e mais 3, ver todas no PDF anexo", html);
        Assert.Contains(">001<", html);
        Assert.DoesNotContain(">003<", html);
    }

    [Fact]
    public void SoIntegradas_MostraBlocoSemPendencias_SemTabelaDeProblemas()
    {
        var html = CorpoEmailResumo.Montar(
            new[] { new LinhaRelatorioErro("1", "T1", T, "", Integrada: true) }, 0, T, T).Html;

        Assert.Contains("Todas as notas integradas, sem pendências", html);
        // Não há cabeçalho da tabela de problemas.
        Assert.DoesNotContain(">Transporte<", html);
        Assert.DoesNotContain(">Mensagem<", html);
    }

    [Fact]
    public void Assinatura_DoSistema_ComContatoESite()
    {
        var html = CorpoEmailResumo.Montar(new[] { Erro("2", "falhou") }, 0, T, T).Html;

        Assert.Contains("KitWms Integrador", html);
        Assert.Contains("mailto:sac@friozem.com.br", html);
        Assert.Contains("https://www.friozem.com.br", html);
        Assert.Contains("Mensagem automática, não responda este e-mail.", html);
    }

    private static string IdentidadeVisualHorario(DateTimeOffset instante) =>
        instante.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
}
