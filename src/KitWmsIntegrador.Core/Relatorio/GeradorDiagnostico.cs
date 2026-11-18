using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Diagnostico;
using KitWmsIntegrador.Core.Email;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>Gera o "Diagnóstico agente integrador" em HTML (sem PDF).</summary>
public interface IGeradorDiagnostico
{
    /// <summary>HTML para e-mail (logo por CID) ou para disco (logo em data URI).</summary>
    (string Html, ImagemInline? Imagem) GerarHtml(ModeloDiagnostico modelo, ModoLogoRelatorio modo);
}

/// <summary>Implementação sobre <see cref="CorpoEmailDiagnostico"/>; a logo vem de <see cref="OpcoesRelatorio.LogoPath"/>.</summary>
public sealed class GeradorDiagnostico : IGeradorDiagnostico
{
    private readonly OpcoesRelatorio _opcoes;

    /// <summary>Cria o gerador.</summary>
    public GeradorDiagnostico(IOptions<OpcoesRelatorio> opcoes)
    {
        _opcoes = opcoes.Value;
    }

    /// <inheritdoc />
    public (string Html, ImagemInline? Imagem) GerarHtml(ModeloDiagnostico modelo, ModoLogoRelatorio modo)
    {
        var corpo = CorpoEmailDiagnostico.Montar(modelo, IdentidadeVisual.ObterLogo(_opcoes.LogoPath), modo);
        return (corpo.Html, corpo.ImagemEmbutida);
    }
}
