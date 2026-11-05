using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Monitoramento;

namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>
/// Gera o resumo semanal de monitoramento em HTML (corpo de e-mail com logo embutida por CID) e
/// PDF (A4 paisagem com a identidade Friozem) a partir de um <see cref="ResumoPeriodo"/>.
/// Sem comunicação de rede.
/// </summary>
public interface IGeradorResumoSemanal
{
    /// <summary>
    /// Monta o corpo HTML do resumo e a imagem opcional a embutir por <c>cid:</c>
    /// (a logo, quando configurada e válida).
    /// </summary>
    (string Html, ImagemInline? Imagem) GerarHtml(ResumoPeriodo resumo, string periodoLabel);

    /// <summary>Monta o resumo como PDF A4 paisagem (bytes), com a identidade Friozem.</summary>
    byte[] GerarPdf(ResumoPeriodo resumo, string periodoLabel);
}
