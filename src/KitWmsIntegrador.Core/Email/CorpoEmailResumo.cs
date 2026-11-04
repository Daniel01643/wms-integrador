using System.Globalization;
using System.Net;
using System.Text;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;

namespace KitWmsIntegrador.Core.Email;

/// <summary>
/// Monta o corpo HTML do e-mail de fechamento do pacote com a identidade visual Friozem:
/// cabeçalho com logo (via CID) ou marca em texto, cartões coloridos por status, tabela de
/// problemas legível e assinatura do SISTEMA no rodapé.
/// </summary>
/// <remarks>
/// Layout em TABELAS com CSS inline (sem &lt;style&gt;/&lt;link&gt;/&lt;script&gt;, sem flexbox/grid),
/// para renderizar de forma consistente no Outlook. A logo é embutida por <c>cid:</c>
/// (<see cref="ImagemInline"/>); quando não há logo válida, o cabeçalho e a assinatura caem
/// para o texto "Friozem" na cor da marca — nunca quebra.
/// Decisão em aberto (duplicata WMS): a resposta real de duplicata do homolog
/// ("...já existe com Id[...]. Nao permitida acao de I...") chega classificada como Pendente;
/// este corpo apenas a exibe de forma legível (via <see cref="LinhaRelatorioErro.UltimoErroResumido"/>),
/// sem mexer no classificador.
/// </remarks>
public static class CorpoEmailResumo
{
    /// <summary>Limite padrão de linhas de erro/pendência no corpo.</summary>
    public const int MaxLinhasPadrao = 20;

    /// <summary>Content-Id da logo embutida, referenciado no HTML como <c>cid:logo-friozem</c>.</summary>
    public const string CidLogo = "logo-friozem";

    private const string NomeSistema = "KitWms Integrador";
    private const string DescricaoSistema = "Integração automática de notas fiscais com o WMS";
    private const string LinhaInstitucional = "A serviço da Friozem Logística — Corporativo";
    private const string EmailContato = "sac@friozem.com.br";
    private const string SiteExibicao = "www.friozem.com.br";
    private const string SiteUrl = "https://www.friozem.com.br";
    private const string AvisoAutomatico = "Mensagem automática, não responda este e-mail.";

    private const int LarguraCorpo = 640;

    /// <summary>Resultado do corpo: o HTML e a imagem opcional a embutir por CID.</summary>
    /// <param name="Html">Corpo HTML pronto para envio.</param>
    /// <param name="ImagemEmbutida">Logo a embutir por <c>cid:</c>, ou <c>null</c> sem logo válida.</param>
    public sealed record CorpoEmail(string Html, ImagemInline? ImagemEmbutida);

    /// <summary>
    /// Gera o corpo (HTML + imagem inline). Não faz I/O: recebe os bytes da logo já lidos
    /// (<paramref name="logoPng"/>); quando <c>null</c>, usa o texto "Friozem". Todo texto
    /// dinâmico é HTML-encoded.
    /// </summary>
    public static CorpoEmail Montar(
        IReadOnlyList<LinhaRelatorioErro> linhas,
        int xmlsInvalidos,
        DateTimeOffset inicio,
        DateTimeOffset fim,
        byte[]? logoPng = null,
        int maxLinhas = MaxLinhasPadrao,
        PeriodoColeta? periodo = null)
    {
        ArgumentNullException.ThrowIfNull(linhas);
        var r = ModeloRelatorio.Montar(linhas).Resumo;
        var ci = CultureInfo.InvariantCulture;
        var temLogo = logoPng is { Length: > 0 };

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>");
        sb.Append("<html lang=\"pt-BR\"><head><meta charset=\"utf-8\" /></head>");
        sb.Append("<body style=\"margin:0;padding:0;background:#f2f4f7;\">");

        // Moldura centralizada.
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" ")
          .Append("style=\"background:#f2f4f7;\"><tr><td align=\"center\" style=\"padding:20px 12px;\">");
        sb.Append("<table role=\"presentation\" width=\"").Append(LarguraCorpo.ToString(ci))
          .Append("\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:").Append(LarguraCorpo.ToString(ci))
          .Append("px;max-width:100%;background:#ffffff;border:1px solid ").Append(IdentidadeVisual.Cinza)
          .Append(";border-radius:6px;font-family:Arial,Helvetica,sans-serif;color:")
          .Append(IdentidadeVisual.Texto).Append(";font-size:14px;\">");

        AppendCabecalho(sb, temLogo);
        AppendPeriodo(sb, inicio, fim, periodo, ci);
        AppendCartoes(sb, r, xmlsInvalidos, ci);
        AppendProblemas(sb, linhas, maxLinhas, ci);
        AppendNotaAnexo(sb);
        AppendAssinatura(sb, temLogo);

        sb.Append("</table></td></tr></table>");
        sb.Append("</body></html>");

        var imagem = temLogo ? new ImagemInline(CidLogo, logoPng!) : null;
        return new CorpoEmail(sb.ToString(), imagem);
    }

    private static void AppendCabecalho(StringBuilder sb, bool temLogo)
    {
        sb.Append("<tr><td style=\"background:").Append(IdentidadeVisual.Azul)
          .Append(";padding:18px 24px;border-radius:6px 6px 0 0;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr>");
        sb.Append("<td style=\"vertical-align:middle;\">");
        if (temLogo)
        {
            sb.Append("<img src=\"cid:").Append(CidLogo)
              .Append("\" alt=\"Friozem\" height=\"40\" style=\"display:block;max-height:40px;border:0;\" />");
        }
        else
        {
            sb.Append("<span style=\"font-size:26px;font-weight:bold;color:#ffffff;letter-spacing:.5px;\">Friozem</span>");
        }

        sb.Append("</td>");
        sb.Append("<td align=\"right\" style=\"vertical-align:middle;color:#ffffff;font-size:16px;font-weight:bold;\">")
          .Append(NomeSistema).Append(" — fechamento do pacote</td>");
        sb.Append("</tr></table></td></tr>");
    }

    private static void AppendPeriodo(
        StringBuilder sb, DateTimeOffset inicio, DateTimeOffset fim, PeriodoColeta? periodo, CultureInfo ci)
    {
        sb.Append("<tr><td style=\"padding:16px 24px 0;\">");
        sb.Append("<p style=\"margin:0;color:").Append(IdentidadeVisual.Texto).Append(";font-size:13px;\">");
        if (periodo is null)
        {
            sb.Append("<b>Período:</b> ")
              .Append(Enc(IdentidadeVisual.Horario(inicio)))
              .Append(" a ")
              .Append(Enc(IdentidadeVisual.Horario(fim)));
        }
        else
        {
            sb.Append("<b>Coleta:</b> primeira gravação ")
              .Append(Enc(IdentidadeVisual.Horario(periodo.PrimeiraGravacao)))
              .Append(", última gravação ")
              .Append(Enc(IdentidadeVisual.Horario(periodo.UltimaGravacao)))
              .Append("<br />Pacote fechado em ")
              .Append(Enc(IdentidadeVisual.Horario(fim)));
        }

        sb.Append("</p></td></tr>");
    }

    private static void AppendCartoes(StringBuilder sb, ResumoRelatorio r, int xmlsInvalidos, CultureInfo ci)
    {
        sb.Append("<tr><td style=\"padding:16px 24px 0;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr>");
        Cartao(sb, "Total", r.Total, IdentidadeVisual.Azul, IdentidadeVisual.Azul, ci);
        Cartao(sb, "Integradas", r.Integradas, IdentidadeVisual.Verde, IdentidadeVisual.Verde, ci);
        sb.Append("</tr><tr>");
        Cartao(sb, "Integradas — revisar produto", r.IntegradasRevisarProduto,
            IdentidadeVisual.Verde, IdentidadeVisual.Verde, ci, marca: true);
        Cartao(sb, "Pendentes", r.Pendentes, IdentidadeVisual.Ambar, "#B8741A", ci);
        sb.Append("</tr><tr>");
        Cartao(sb, "Não integradas", r.NaoIntegradas, IdentidadeVisual.Vermelho, IdentidadeVisual.Vermelho, ci);
        Cartao(sb, "XMLs inválidos", xmlsInvalidos, IdentidadeVisual.Vermelho, IdentidadeVisual.Vermelho, ci);
        sb.Append("</tr><tr>");
        Cartao(sb, "Transportes", r.Transportes, IdentidadeVisual.AzulClaro, IdentidadeVisual.Azul, ci);
        sb.Append("<td style=\"width:50%;\"></td>");
        sb.Append("</tr></table></td></tr>");
    }

    private static void Cartao(
        StringBuilder sb, string rotulo, int valor, string corTopo, string corNumero, CultureInfo ci, bool marca = false)
    {
        sb.Append("<td style=\"width:50%;padding:4px;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"")
          .Append("border:1px solid ").Append(IdentidadeVisual.Cinza)
          .Append(";border-top:4px solid ").Append(corTopo)
          .Append(";border-radius:4px;\"><tr><td style=\"padding:10px 14px;\">");
        sb.Append("<div style=\"font-size:26px;font-weight:bold;color:").Append(corNumero).Append(";\">")
          .Append(valor.ToString(ci)).Append("</div>");
        sb.Append("<div style=\"font-size:12px;color:").Append(IdentidadeVisual.Texto).Append(";\">")
          .Append(rotulo);
        if (marca)
        {
            sb.Append(" <span style=\"color:").Append(IdentidadeVisual.Azul).Append(";font-weight:bold;\">●</span>");
        }

        sb.Append("</div></td></tr></table></td>");
    }

    private static void AppendProblemas(
        StringBuilder sb, IReadOnlyList<LinhaRelatorioErro> linhas, int maxLinhas, CultureInfo ci)
    {
        var problemas = linhas
            .Where(l => !l.EstaIntegrada || l.IntegradaRevisarProduto)
            .OrderBy(l => l.NumeroTransporte ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(l => l.NumeroNota ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        if (problemas.Count == 0)
        {
            sb.Append("<tr><td style=\"padding:16px 24px 0;\">");
            sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"")
              .Append("background:#e7f5f1;border:1px solid ").Append(IdentidadeVisual.Verde)
              .Append(";border-radius:4px;\"><tr><td style=\"padding:12px 14px;color:")
              .Append(IdentidadeVisual.Verde).Append(";font-weight:bold;\">")
              .Append("Todas as notas integradas, sem pendências")
              .Append("</td></tr></table></td></tr>");
            return;
        }

        sb.Append("<tr><td style=\"padding:16px 24px 0;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"")
          .Append("border-collapse:collapse;border:1px solid ").Append(IdentidadeVisual.Cinza).Append(";font-size:13px;\">");
        sb.Append("<tr>");
        foreach (var h in new[] { "Transporte", "Nota", "Status", "Mensagem" })
        {
            sb.Append("<th style=\"background:").Append(IdentidadeVisual.Azul)
              .Append(";color:#ffffff;text-align:left;padding:7px 9px;border:1px solid ")
              .Append(IdentidadeVisual.Azul).Append(";\">").Append(Enc(h)).Append("</th>");
        }

        sb.Append("</tr>");

        var i = 0;
        foreach (var l in problemas.Take(Math.Max(0, maxLinhas)))
        {
            var fundo = i % 2 == 0 ? "#ffffff" : "#f5f9fd";
            i++;
            sb.Append("<tr style=\"background:").Append(fundo).Append(";\">");
            CelulaProblema(sb, l.NumeroTransporte);
            CelulaProblema(sb, l.NumeroNota);
            sb.Append("<td style=\"padding:6px 9px;border:1px solid ").Append(IdentidadeVisual.Cinza).Append(";\">");
            AppendChipStatus(sb, l);
            sb.Append("</td>");
            CelulaProblema(sb, l.UltimoErroResumido);
            sb.Append("</tr>");
        }

        sb.Append("</table>");
        var restantes = problemas.Count - Math.Max(0, maxLinhas);
        if (restantes > 0)
        {
            sb.Append("<p style=\"margin:8px 0 0;color:").Append(IdentidadeVisual.Texto)
              .Append(";font-size:12px;\">…e mais ").Append(restantes.ToString(ci))
              .Append(", ver todas no PDF anexo.</p>");
        }

        sb.Append("</td></tr>");
    }

    private static void CelulaProblema(StringBuilder sb, string? texto)
    {
        sb.Append("<td style=\"padding:6px 9px;border:1px solid ").Append(IdentidadeVisual.Cinza)
          .Append(";vertical-align:top;\">").Append(Enc(texto)).Append("</td>");
    }

    private static void AppendChipStatus(StringBuilder sb, LinhaRelatorioErro l)
    {
        var chave = RenderizadorHtml.ChaveStatus(l);
        var (fundo, cor) = chave switch
        {
            "pendente" => (IdentidadeVisual.Ambar, IdentidadeVisual.Texto),
            "nao-integrada" => (IdentidadeVisual.Vermelho, "#ffffff"),
            _ => (IdentidadeVisual.Verde, "#ffffff"), // integrada / revisar
        };
        sb.Append("<span style=\"display:inline-block;border-radius:3px;padding:1px 8px;white-space:nowrap;background:")
          .Append(fundo).Append(";color:").Append(cor).Append(";font-weight:bold;\">")
          .Append(l.StatusDescricao).Append("</span>");
    }

    private static void AppendNotaAnexo(StringBuilder sb)
    {
        sb.Append("<tr><td style=\"padding:14px 24px 0;\">");
        sb.Append("<p style=\"margin:0;color:").Append(IdentidadeVisual.Texto)
          .Append(";font-size:13px;\">Relatório detalhado em PDF em anexo.</p></td></tr>");
    }

    private static void AppendAssinatura(StringBuilder sb, bool temLogo)
    {
        sb.Append("<tr><td style=\"padding:20px 24px 22px;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"")
          .Append("border-top:1px solid ").Append(IdentidadeVisual.Cinza).Append(";\"><tr>");
        sb.Append("<td style=\"padding-top:14px;vertical-align:top;\">");

        if (temLogo)
        {
            sb.Append("<img src=\"cid:").Append(CidLogo)
              .Append("\" alt=\"Friozem\" height=\"28\" style=\"display:block;max-height:28px;border:0;margin-bottom:6px;\" />");
        }
        else
        {
            sb.Append("<div style=\"font-size:18px;font-weight:bold;color:").Append(IdentidadeVisual.Azul)
              .Append(";margin-bottom:6px;\">Friozem</div>");
        }

        sb.Append("<div style=\"font-size:13px;font-weight:bold;color:").Append(IdentidadeVisual.Azul).Append(";\">")
          .Append(NomeSistema).Append("</div>");
        sb.Append("<div style=\"font-size:12px;color:").Append(IdentidadeVisual.Texto).Append(";\">")
          .Append(DescricaoSistema).Append("</div>");
        sb.Append("<div style=\"font-size:12px;color:").Append(IdentidadeVisual.Texto).Append(";\">")
          .Append(LinhaInstitucional).Append("</div>");
        sb.Append("<div style=\"font-size:12px;margin-top:4px;\">")
          .Append("<a href=\"mailto:").Append(EmailContato).Append("\" style=\"color:").Append(IdentidadeVisual.Azul)
          .Append(";text-decoration:none;\">").Append(EmailContato).Append("</a>")
          .Append(" &nbsp;|&nbsp; ")
          .Append("<a href=\"").Append(SiteUrl).Append("\" style=\"color:").Append(IdentidadeVisual.Azul)
          .Append(";text-decoration:none;\">").Append(SiteExibicao).Append("</a></div>");
        sb.Append("<div style=\"font-size:11px;color:").Append(IdentidadeVisual.Cinza).Append(";margin-top:10px;\">")
          .Append(AvisoAutomatico).Append("</div>");

        sb.Append("</td></tr></table></td></tr>");
    }

    private static string Enc(string? texto) => WebUtility.HtmlEncode(texto ?? string.Empty);
}
