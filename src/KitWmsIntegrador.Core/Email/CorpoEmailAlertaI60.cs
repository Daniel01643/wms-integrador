using System.Globalization;
using System.Net;
using System.Text;
using KitWmsIntegrador.Core.Relatorio;

namespace KitWmsIntegrador.Core.Email;

/// <summary>Linha do e-mail de alerta imediato da I60 (texto do WMS já truncado, sem corpo de nota).</summary>
public sealed record LinhaAlertaI60(
    string Evento, string Numero, string Serie, string Transporte, string Onda, string Situacao, string Motivo, string TextoWms);

/// <summary>
/// HTML do e-mail de alerta imediato da I60. Mesmo padrão do relatório diário (tabelas, CSS inline, sem script).
/// Todo texto dinâmico é HTML-encoded.
/// </summary>
public static class CorpoEmailAlertaI60
{
    private const string Subtitulo = "Alerta da separação (I51) e associação (I60)";
    private const int LarguraCorpo = 760;

    /// <summary>Dica fixa para calibrar o reconhecimento do doccarga encerrado.</summary>
    public const string Dica =
        "Se for doccarga encerrado, copie o texto do WMS para I60:PadroesDocCargaEncerrado (separe os textos por ponto e vírgula).";

    /// <summary>Gera o HTML e, no modo cid, a imagem inline da logo.</summary>
    public static CorpoEmailResumo.CorpoEmail Montar(
        IReadOnlyList<LinhaAlertaI60> linhas, DateTimeOffset geradoEm, byte[]? logoPng, ModoLogoRelatorio modo)
    {
        ArgumentNullException.ThrowIfNull(linhas);
        var ci = CultureInfo.InvariantCulture;
        var temLogo = logoPng is { Length: > 0 };
        var srcLogo = !temLogo ? string.Empty
            : modo == ModoLogoRelatorio.Cid ? "cid:" + CorpoEmailResumo.CidLogo
            : "data:image/png;base64," + Convert.ToBase64String(logoPng!);

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\" />")
          .Append("<title>").Append(Enc(IdentidadeVisual.Titulo)).Append(" - alerta I60</title></head>");
        sb.Append("<body style=\"margin:0;padding:0;background:#f2f4f7;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f2f4f7;\"><tr><td align=\"center\" style=\"padding:20px 12px;\">");
        sb.Append("<table role=\"presentation\" width=\"").Append(LarguraCorpo.ToString(ci)).Append("\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:")
          .Append(LarguraCorpo.ToString(ci)).Append("px;max-width:100%;background:#ffffff;border:1px solid ").Append(IdentidadeVisual.Cinza)
          .Append(";border-radius:6px;font-family:Arial,Helvetica,sans-serif;color:").Append(IdentidadeVisual.Texto).Append(";font-size:13px;\">");

        // Cabeçalho
        sb.Append("<tr><td style=\"background:").Append(IdentidadeVisual.Azul).Append(";padding:18px 24px;border-radius:6px 6px 0 0;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr><td style=\"vertical-align:middle;\">");
        if (srcLogo.Length > 0)
        {
            sb.Append("<img src=\"").Append(srcLogo).Append("\" alt=\"Friozem\" height=\"40\" style=\"display:block;max-height:40px;border:0;\" />");
        }
        else
        {
            sb.Append("<span style=\"font-size:26px;font-weight:bold;color:#ffffff;\">Friozem</span>");
        }

        sb.Append("</td><td align=\"right\" style=\"vertical-align:middle;color:#ffffff;\"><div style=\"font-size:17px;font-weight:bold;\">")
          .Append(Enc(IdentidadeVisual.Titulo)).Append("</div><div style=\"font-size:12px;\">").Append(Enc(Subtitulo)).Append("</div></td></tr></table></td></tr>");

        // Resumo
        sb.Append("<tr><td style=\"padding:14px 24px 0;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#fff6e5;border:1px solid ")
          .Append(IdentidadeVisual.Ambar).Append(";border-radius:4px;\"><tr><td style=\"padding:10px 14px;color:#8a5a00;font-weight:bold;\">")
          .Append(Enc("I60 precisa de atenção: " + linhas.Count.ToString(ci) + " nota(s) neste ciclo."))
          .Append("</td></tr></table></td></tr>");
        sb.Append("<tr><td style=\"padding:8px 24px 0;\"><p style=\"margin:0;font-size:13px;\"><b>Gerado em:</b> ")
          .Append(Enc(IdentidadeVisual.Horario(geradoEm))).Append("</p></td></tr>");

        // Tabela
        var cabecalhos = new[] { "Evento", "Nota", "Série", "Transporte", "Onda", "Situação", "Motivo", "Texto do WMS" };
        sb.Append("<tr><td style=\"padding:10px 24px 0;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;border:1px solid ")
          .Append(IdentidadeVisual.Cinza).Append(";font-size:12px;\"><tr>");
        foreach (var c in cabecalhos)
        {
            sb.Append("<th style=\"background:").Append(IdentidadeVisual.Azul).Append(";color:#ffffff;text-align:left;padding:6px 7px;border:1px solid ")
              .Append(IdentidadeVisual.Azul).Append(";\">").Append(Enc(c)).Append("</th>");
        }

        sb.Append("</tr>");
        for (var i = 0; i < linhas.Count; i++)
        {
            var l = linhas[i];
            sb.Append("<tr style=\"background:").Append(i % 2 == 0 ? "#ffffff" : "#f5f9fd").Append(";\">");
            foreach (var c in new[] { l.Evento, l.Numero, l.Serie, l.Transporte, l.Onda, l.Situacao, l.Motivo, l.TextoWms })
            {
                sb.Append("<td style=\"padding:5px 7px;border:1px solid ").Append(IdentidadeVisual.Cinza).Append(";vertical-align:top;\">").Append(Enc(c)).Append("</td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</table></td></tr>");

        // Dica
        sb.Append("<tr><td style=\"padding:12px 24px 0;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f5f9fd;border:1px solid ")
          .Append(IdentidadeVisual.Cinza).Append(";border-radius:4px;\"><tr><td style=\"padding:10px 14px;font-size:12px;\">")
          .Append(Enc(Dica)).Append("</td></tr></table></td></tr>");

        // Assinatura
        sb.Append("<tr><td style=\"padding:20px 24px 22px;\"><div style=\"border-top:1px solid ").Append(IdentidadeVisual.Cinza).Append(";padding-top:12px;\">")
          .Append("<div style=\"font-size:13px;font-weight:bold;color:").Append(IdentidadeVisual.Azul).Append(";\">KitWms Integrador</div>")
          .Append("<div style=\"font-size:12px;\">Integração automática de notas fiscais com o WMS</div>")
          .Append("<div style=\"font-size:11px;color:").Append(IdentidadeVisual.CinzaNeutro).Append(";margin-top:8px;\">Mensagem automática, não responda este e-mail.</div>")
          .Append("</div></td></tr>");
        sb.Append("</table></td></tr></table></body></html>");

        var imagem = temLogo && modo == ModoLogoRelatorio.Cid ? new ImagemInline(CorpoEmailResumo.CidLogo, logoPng!) : null;
        return new CorpoEmailResumo.CorpoEmail(sb.ToString(), imagem);
    }

    private static string Enc(string? texto) => WebUtility.HtmlEncode(texto ?? string.Empty);
}
