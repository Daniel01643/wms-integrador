using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Diagnostico;
using KitWmsIntegrador.Core.Email;
using Microsoft.Extensions.Options;
using PdfSharpCore.Fonts;

namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>Gera o "Diagnóstico agente integrador" em HTML e PDF.</summary>
public interface IGeradorDiagnostico
{
    /// <summary>
    /// HTML para e-mail (logo por CID) ou para disco (logo em data URI). <paramref name="enxuto"/> = corpo do e-mail só com
    /// resumo e problemas (o detalhe fica no PDF).
    /// </summary>
    (string Html, ImagemInline? Imagem) GerarHtml(ModeloDiagnostico modelo, ModoLogoRelatorio modo, bool enxuto = false);

    /// <summary>PDF A4 paisagem completo (anexo do e-mail e cópia em disco).</summary>
    byte[] GerarPdf(ModeloDiagnostico modelo);
}

/// <summary>
/// Implementação sobre <see cref="CorpoEmailDiagnostico"/> e <see cref="RenderizadorPdfDiagnostico"/>; a logo vem de
/// <see cref="OpcoesRelatorio.LogoPath"/>. Registra o FontResolver da PdfSharpCore de forma idempotente.
/// </summary>
public sealed class GeradorDiagnostico : IGeradorDiagnostico
{
    static GeradorDiagnostico()
    {
        GlobalFontSettings.FontResolver ??= new PdfSharpCore.Utils.FontResolver();
    }

    private readonly OpcoesRelatorio _opcoes;

    /// <summary>Cria o gerador.</summary>
    public GeradorDiagnostico(IOptions<OpcoesRelatorio> opcoes)
    {
        _opcoes = opcoes.Value;
    }

    /// <inheritdoc />
    public (string Html, ImagemInline? Imagem) GerarHtml(ModeloDiagnostico modelo, ModoLogoRelatorio modo, bool enxuto = false)
    {
        var corpo = CorpoEmailDiagnostico.Montar(modelo, IdentidadeVisual.ObterLogo(_opcoes.LogoPath), modo, enxuto);
        return (corpo.Html, corpo.ImagemEmbutida);
    }

    /// <inheritdoc />
    public byte[] GerarPdf(ModeloDiagnostico modelo) =>
        RenderizadorPdfDiagnostico.Renderizar(modelo, IdentidadeVisual.ObterLogo(_opcoes.LogoPath));
}
