using System.Globalization;
using System.Net;
using System.Text;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;

namespace KitWmsIntegrador.Core.Email;

/// <summary>Monta o corpo HTML simples do e-mail de fechamento do pacote.</summary>
public static class CorpoEmailResumo
{
    /// <summary>Limite padrão de linhas de erro/pendência no corpo.</summary>
    public const int MaxLinhasPadrao = 20;

    /// <summary>Gera o HTML (todo texto dinâmico é HTML-encoded).</summary>
    public static string Montar(
        IReadOnlyList<LinhaRelatorioErro> linhas,
        int xmlsInvalidos,
        DateTimeOffset inicio,
        DateTimeOffset fim,
        int maxLinhas = MaxLinhasPadrao,
        PeriodoColeta? periodo = null)
    {
        ArgumentNullException.ThrowIfNull(linhas);
        var r = ModeloRelatorio.Montar(linhas).Resumo;
        var ci = CultureInfo.InvariantCulture;

        var sb = new StringBuilder();
        sb.Append("<html><body style=\"font-family:Arial,sans-serif;font-size:14px\">");
        sb.Append("<h2>KitWms Integrador - fechamento do pacote</h2>");
        if (periodo is null)
        {
            sb.Append("<p>Período: ")
              .Append(Enc(inicio.ToLocalTime().ToString("dd/MM/yyyy HH:mm", ci)))
              .Append(" a ")
              .Append(Enc(fim.ToLocalTime().ToString("dd/MM/yyyy HH:mm", ci)))
              .Append("</p><ul>");
        }
        else
        {
            sb.Append("<p>Coleta: primeira gravação ")
              .Append(Enc(periodo.PrimeiraGravacao.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", ci)))
              .Append(", última gravação ")
              .Append(Enc(periodo.UltimaGravacao.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", ci)))
              .Append("; pacote fechado em ")
              .Append(Enc(fim.ToLocalTime().ToString("dd/MM/yyyy HH:mm", ci)))
              .Append("</p><ul>");
        }
        sb.Append("<li>Total de notas: ").Append(r.Total.ToString(ci)).Append("</li>");
        sb.Append("<li>Integradas: ").Append(r.Integradas.ToString(ci)).Append("</li>");
        sb.Append("<li>Integradas - revisar produto: ").Append(r.IntegradasRevisarProduto.ToString(ci)).Append("</li>");
        sb.Append("<li>Pendentes: ").Append(r.Pendentes.ToString(ci)).Append("</li>");
        sb.Append("<li>Não integradas: ").Append(r.NaoIntegradas.ToString(ci)).Append("</li>");
        sb.Append("<li>XMLs inválidos: ").Append(xmlsInvalidos.ToString(ci)).Append("</li>");
        sb.Append("<li>Transportes: ").Append(r.Transportes.ToString(ci)).Append("</li>");
        sb.Append("</ul>");

        var problemas = linhas
            .Where(l => !l.EstaIntegrada || l.IntegradaRevisarProduto)
            .OrderBy(l => l.NumeroTransporte ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(l => l.NumeroNota ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        if (problemas.Count > 0)
        {
            sb.Append("<table border=\"1\" cellpadding=\"4\" cellspacing=\"0\" style=\"border-collapse:collapse\">");
            sb.Append("<tr><th>Transporte</th><th>Nota</th><th>Status</th><th>Mensagem</th></tr>");
            foreach (var l in problemas.Take(Math.Max(0, maxLinhas)))
            {
                sb.Append("<tr><td>").Append(Enc(l.NumeroTransporte))
                  .Append("</td><td>").Append(Enc(l.NumeroNota))
                  .Append("</td><td>").Append(Enc(l.StatusDescricao))
                  .Append("</td><td>").Append(Enc(l.UltimoErroResumido))
                  .Append("</td></tr>");
            }

            sb.Append("</table>");
            var restantes = problemas.Count - Math.Max(0, maxLinhas);
            if (restantes > 0)
            {
                sb.Append("<p>…e mais ").Append(restantes.ToString(ci)).Append(", ver PDF.</p>");
            }
        }

        sb.Append("<p>Relatório detalhado em PDF em anexo.</p></body></html>");
        return sb.ToString();
    }

    private static string Enc(string? texto) => WebUtility.HtmlEncode(texto ?? string.Empty);
}
