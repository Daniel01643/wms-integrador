using System.Text;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class ResumoSemanalRenderTests
{
    private const string PeriodoLabel = "02/06/2025 a 08/06/2025";

    // Assinatura PNG mínima válida (magic number), suficiente para embutir o CID.
    private static readonly byte[] LogoPng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01 };

    private static readonly TimeSpan Fuso = TimeSpan.FromHours(-3);

    private static ResumoPeriodo ResumoComAtividade()
    {
        var inicio = new DateTimeOffset(2025, 6, 2, 0, 0, 0, Fuso);
        var fim = inicio.AddDays(7);
        var pacotes = new[]
        {
            new RegistroTelemetria(
                "p1", inicio.AddHours(8), new DateTimeOffset(2025, 6, 2, 9, 0, 0, Fuso),
                TotalNotas: 10, Integradas: 8, IntegradasRevisarProduto: 1, Pendentes: 1, NaoIntegradas: 1,
                XmlInvalido: 0, Transportes: 2, TempoTotalMs: 20000, TempoMedioPorNotaMs: 2000,
                Tentativas: new Dictionary<string, int> { ["1"] = 10 },
                ChamadasWms: new Dictionary<string, int> { ["I50"] = 5, ["I10"] = 2 },
                ChamadasWmsAproximado: true,
                ErrosPorCategoria: new Dictionary<string, int> { ["ProdutoNaoEncontrado"] = 1 }),
        };
        var eventos = new[]
        {
            new EventoServico("servico-iniciado", new DateTimeOffset(2025, 5, 30, 8, 0, 0, Fuso), "1.0.0"),
        };
        return AgregadorTelemetria.Agregar(pacotes, eventos, inicio, fim);
    }

    private static ResumoPeriodo ResumoSemAtividade()
    {
        var inicio = new DateTimeOffset(2025, 6, 2, 0, 0, 0, Fuso);
        var fim = inicio.AddDays(7);
        return AgregadorTelemetria.Agregar(
            Array.Empty<RegistroTelemetria>(), Array.Empty<EventoServico>(), inicio, fim);
    }

    private static GeradorResumoSemanal Gerador(string? logoPath = null) =>
        new(Options.Create(new OpcoesRelatorio { LogoPath = logoPath ?? string.Empty }));

    [Fact]
    public void Montar_HtmlNaoVazio_ComMarcadoresFriozem()
    {
        var corpo = CorpoEmailResumoSemanal.Montar(ResumoComAtividade(), PeriodoLabel);

        Assert.False(string.IsNullOrWhiteSpace(corpo.Html));
        Assert.Contains("Friozem", corpo.Html);
        Assert.Contains("KitWms Integrador", corpo.Html);
        Assert.Contains("mailto:sac@friozem.com.br", corpo.Html);
        Assert.Contains(PeriodoLabel, corpo.Html);
        // Blocos esperados do resumo.
        Assert.Contains("Frequência por dia da semana", corpo.Html);
        Assert.Contains("Manhã", corpo.Html);
        Assert.Contains("Chamadas ao WMS", corpo.Html);
        Assert.Contains("Saúde do serviço", corpo.Html);
        // Sem style/link/script (compatível com Outlook).
        Assert.DoesNotContain("<style", corpo.Html);
        Assert.DoesNotContain("<script", corpo.Html);
    }

    [Fact]
    public void Montar_SemLogo_NaoEmbuteImagem()
    {
        var corpo = CorpoEmailResumoSemanal.Montar(ResumoComAtividade(), PeriodoLabel, logoPng: null);

        Assert.Null(corpo.ImagemEmbutida);
        Assert.DoesNotContain("cid:logo-friozem", corpo.Html);
    }

    [Fact]
    public void Montar_ComLogo_EmbuteImagemPorCid()
    {
        var corpo = CorpoEmailResumoSemanal.Montar(ResumoComAtividade(), PeriodoLabel, logoPng: LogoPng);

        Assert.NotNull(corpo.ImagemEmbutida);
        Assert.Equal(CorpoEmailResumo.CidLogo, corpo.ImagemEmbutida!.ContentId);
        Assert.Contains("src=\"cid:logo-friozem\"", corpo.Html);
    }

    [Fact]
    public void Montar_SemAtividade_MostraAviso()
    {
        var corpo = CorpoEmailResumoSemanal.Montar(ResumoSemAtividade(), PeriodoLabel);

        Assert.Contains("Nenhuma atividade no período", corpo.Html);
    }

    [Fact]
    public void GerarHtml_DoGerador_DevolveHtmlEImagemComLogo()
    {
        var pastaTemp = Path.Combine(Path.GetTempPath(), "wms-logo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pastaTemp);
        var caminhoLogo = Path.Combine(pastaTemp, "logo.png");
        File.WriteAllBytes(caminhoLogo, LogoPng);

        try
        {
            var (html, imagem) = Gerador(caminhoLogo).GerarHtml(ResumoComAtividade(), PeriodoLabel);

            Assert.False(string.IsNullOrWhiteSpace(html));
            Assert.NotNull(imagem);
            Assert.Equal(CorpoEmailResumo.CidLogo, imagem!.ContentId);
        }
        finally
        {
            Directory.Delete(pastaTemp, recursive: true);
        }
    }

    [Fact]
    public void GerarPdf_ComAtividade_RetornaPdfValido()
    {
        var pdf = Gerador().GerarPdf(ResumoComAtividade(), PeriodoLabel);

        Assert.NotEmpty(pdf);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void GerarPdf_SemAtividade_RetornaPdfValido()
    {
        var pdf = Gerador().GerarPdf(ResumoSemAtividade(), PeriodoLabel);

        Assert.NotEmpty(pdf);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
