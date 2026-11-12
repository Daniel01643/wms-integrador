using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Monitoramento;
using Microsoft.Extensions.Options;
using PdfSharpCore.Fonts;

namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>
/// Gera o resumo semanal em HTML (<see cref="CorpoEmailResumoSemanal"/>) e PDF
/// (<see cref="RenderizadorPdfResumo"/>). A logo vem de <see cref="OpcoesRelatorio.LogoPath"/>
/// via <see cref="IdentidadeVisual.ObterLogo"/>, reaproveitando o mesmo padrão de
/// <see cref="GeradorRelatorio"/>. Registra o FontResolver da PdfSharpCore de forma idempotente,
/// para o gerador rodar isolado (inclusive nos testes).
/// </summary>
public sealed class GeradorResumoSemanal : IGeradorResumoSemanal
{
    static GeradorResumoSemanal()
    {
        GlobalFontSettings.FontResolver ??= new PdfSharpCore.Utils.FontResolver();
    }

    private readonly OpcoesRelatorio _opcoes;

    public GeradorResumoSemanal(IOptions<OpcoesRelatorio> opcoes)
    {
        _opcoes = opcoes.Value;
    }

    /// <inheritdoc />
    public (string Html, ImagemInline? Imagem) GerarHtml(ResumoPeriodo resumo, string periodoLabel)
    {
        var corpo = CorpoEmailResumoSemanal.Montar(resumo, periodoLabel, IdentidadeVisual.ObterLogo(_opcoes.LogoPath));
        return (corpo.Html, corpo.ImagemEmbutida);
    }

    /// <inheritdoc />
    public byte[] GerarPdf(ResumoPeriodo resumo, string periodoLabel) =>
        RenderizadorPdfResumo.Renderizar(
            resumo, periodoLabel, IdentidadeVisual.ObterLogo(_opcoes.LogoPath), DateTimeOffset.Now);
}
