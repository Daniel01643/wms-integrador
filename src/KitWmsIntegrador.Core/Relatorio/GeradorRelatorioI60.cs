using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using Microsoft.Extensions.Options;
using PdfSharpCore.Fonts;

namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>Gera o relatório diário da I51/I60 em HTML e PDF.</summary>
public interface IGeradorRelatorioI60
{
    /// <summary>HTML para e-mail (logo por CID) ou para disco (logo em data URI).</summary>
    (string Html, ImagemInline? Imagem) GerarHtml(ModeloRelatorioI60 modelo, ModoLogoRelatorio modo);

    /// <summary>PDF A4 paisagem.</summary>
    byte[] GerarPdf(ModeloRelatorioI60 modelo);
}

/// <summary>
/// Implementação sobre <see cref="CorpoEmailRelatorioI60"/> e <see cref="RenderizadorPdfRelatorioI60"/>. A logo vem
/// de <see cref="OpcoesRelatorio.LogoPath"/> via <see cref="IdentidadeVisual.ObterLogo"/>. Registra o FontResolver
/// da PdfSharpCore de forma idempotente.
/// </summary>
public sealed class GeradorRelatorioI60 : IGeradorRelatorioI60
{
    static GeradorRelatorioI60()
    {
        GlobalFontSettings.FontResolver ??= new PdfSharpCore.Utils.FontResolver();
    }

    private readonly OpcoesRelatorio _opcoes;

    /// <summary>Cria o gerador.</summary>
    public GeradorRelatorioI60(IOptions<OpcoesRelatorio> opcoes)
    {
        _opcoes = opcoes.Value;
    }

    /// <inheritdoc />
    public (string Html, ImagemInline? Imagem) GerarHtml(ModeloRelatorioI60 modelo, ModoLogoRelatorio modo)
    {
        var corpo = CorpoEmailRelatorioI60.Montar(modelo, IdentidadeVisual.ObterLogo(_opcoes.LogoPath), modo);
        return (corpo.Html, corpo.ImagemEmbutida);
    }

    /// <inheritdoc />
    public byte[] GerarPdf(ModeloRelatorioI60 modelo) =>
        RenderizadorPdfRelatorioI60.Renderizar(modelo, IdentidadeVisual.ObterLogo(_opcoes.LogoPath));
}
