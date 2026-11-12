using System.Globalization;
using System.Net;
using System.Text;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Relatorio;

namespace KitWmsIntegrador.Core.Email;

/// <summary>
/// Monta o corpo HTML do e-mail do RESUMO SEMANAL de monitoramento com a identidade visual
/// Friozem, seguindo o mesmo padrão de <see cref="CorpoEmailResumo"/>: moldura centralizada,
/// cabeçalho azul com logo (via CID) ou marca em texto, cartões por status com %, blocos de
/// frequência (por dia da semana e por faixa Manhã/Tarde/Noite), performance, chamadas WMS,
/// erros por categoria, saúde do serviço e assinatura do sistema.
/// </summary>
/// <remarks>
/// Layout em TABELAS com CSS inline (sem &lt;style&gt;/&lt;link&gt;/&lt;script&gt;), compatível
/// com Outlook. Reaproveita <see cref="CorpoEmailResumo.CorpoEmail"/> e
/// <see cref="CorpoEmailResumo.CidLogo"/>; quando não há logo válida, cai para o texto
/// "Friozem". Todo texto dinâmico é HTML-encoded.
/// </remarks>
public static class CorpoEmailResumoSemanal
{
    private const string NomeSistema = "KitWms Integrador";
    private const string DescricaoSistema = "Integração automática de notas fiscais com o WMS";
    private const string LinhaInstitucional = "A serviço da Friozem Logística — Corporativo";
    private const string EmailContato = "sac@friozem.com.br";
    private const string SiteExibicao = "www.friozem.com.br";
    private const string SiteUrl = "https://www.friozem.com.br";
    private const string AvisoAutomatico = "Mensagem automática, não responda este e-mail.";

    private const int LarguraCorpo = 640;

    private static readonly string[] DiasSemana =
    {
        "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado", "Domingo",
    };

    private static readonly DayOfWeek[] OrdemDias =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    };

    /// <summary>
    /// Gera o corpo (HTML + imagem inline) do resumo. Não faz I/O: recebe os bytes da logo já
    /// lidos (<paramref name="logoPng"/>); quando <c>null</c>, usa o texto "Friozem".
    /// </summary>
    public static CorpoEmailResumo.CorpoEmail Montar(ResumoPeriodo resumo, string periodoLabel, byte[]? logoPng = null)
    {
        ArgumentNullException.ThrowIfNull(resumo);
        var ci = CultureInfo.InvariantCulture;
        var temLogo = logoPng is { Length: > 0 };

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>");
        sb.Append("<html lang=\"pt-BR\"><head><meta charset=\"utf-8\" /></head>");
        sb.Append("<body style=\"margin:0;padding:0;background:#f2f4f7;\">");

        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" ")
          .Append("style=\"background:#f2f4f7;\"><tr><td align=\"center\" style=\"padding:20px 12px;\">");
        sb.Append("<table role=\"presentation\" width=\"").Append(LarguraCorpo.ToString(ci))
          .Append("\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:").Append(LarguraCorpo.ToString(ci))
          .Append("px;max-width:100%;background:#ffffff;border:1px solid ").Append(IdentidadeVisual.Cinza)
          .Append(";border-radius:6px;font-family:Arial,Helvetica,sans-serif;color:")
          .Append(IdentidadeVisual.Texto).Append(";font-size:14px;\">");

        AppendCabecalho(sb, temLogo);
        AppendPeriodo(sb, periodoLabel);

        if (resumo.SemAtividade)
        {
            AppendSemAtividade(sb);
        }
        else
        {
            AppendCartoes(sb, resumo, ci);
            AppendVolume(sb, resumo, ci);
            AppendFrequenciaDiaSemana(sb, resumo, ci);
            AppendFrequenciaFaixa(sb, resumo, ci);
            AppendPerformance(sb, resumo, ci);
            AppendChamadasWms(sb, resumo, ci);
            AppendPorTipo(sb, resumo, ci);
        AppendErros(sb, resumo, ci);
        }

        AppendSaude(sb, resumo, ci);
        AppendAssinatura(sb, temLogo);

        sb.Append("</table></td></tr></table>");
        sb.Append("</body></html>");

        var imagem = temLogo ? new ImagemInline(CorpoEmailResumo.CidLogo, logoPng!) : null;
        return new CorpoEmailResumo.CorpoEmail(sb.ToString(), imagem);
    }

    private static void AppendCabecalho(StringBuilder sb, bool temLogo)
    {
        sb.Append("<tr><td style=\"background:").Append(IdentidadeVisual.Azul)
          .Append(";padding:18px 24px;border-radius:6px 6px 0 0;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr>");
        sb.Append("<td style=\"vertical-align:middle;\">");
        if (temLogo)
        {
            sb.Append("<img src=\"cid:").Append(CorpoEmailResumo.CidLogo)
              .Append("\" alt=\"Friozem\" height=\"40\" style=\"display:block;max-height:40px;border:0;\" />");
        }
        else
        {
            sb.Append("<span style=\"font-size:26px;font-weight:bold;color:#ffffff;letter-spacing:.5px;\">Friozem</span>");
        }

        sb.Append("</td>");
        sb.Append("<td align=\"right\" style=\"vertical-align:middle;color:#ffffff;font-size:16px;font-weight:bold;\">")
          .Append(NomeSistema).Append(" — resumo semanal</td>");
        sb.Append("</tr></table></td></tr>");
    }

    private static void AppendPeriodo(StringBuilder sb, string periodoLabel)
    {
        sb.Append("<tr><td style=\"padding:16px 24px 0;\">");
        sb.Append("<p style=\"margin:0;color:").Append(IdentidadeVisual.Texto).Append(";font-size:13px;\">")
          .Append("<b>Período:</b> ").Append(Enc(periodoLabel))
          .Append("</p></td></tr>");
    }

    private static void AppendSemAtividade(StringBuilder sb)
    {
        sb.Append("<tr><td style=\"padding:16px 24px 0;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"")
          .Append("background:#fff6e5;border:1px solid ").Append(IdentidadeVisual.Ambar)
          .Append(";border-radius:4px;\"><tr><td style=\"padding:14px 16px;color:")
          .Append("#8a5a00").Append(";font-weight:bold;\">")
          .Append("Nenhuma atividade no período")
          .Append("</td></tr></table></td></tr>");
    }

    private static void AppendCartoes(StringBuilder sb, ResumoPeriodo r, CultureInfo ci)
    {
        sb.Append("<tr><td style=\"padding:16px 24px 0;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr>");
        Cartao(sb, "Notas", r.TotalNotas, null, IdentidadeVisual.Azul, IdentidadeVisual.Azul, ci);
        Cartao(sb, "Integradas", r.Integradas, r.PercentualIntegradas, IdentidadeVisual.Verde, IdentidadeVisual.Verde, ci);
        sb.Append("</tr><tr>");
        Cartao(sb, "Integradas — revisar produto", r.IntegradasRevisarProduto, r.PercentualIntegradasRevisarProduto,
            IdentidadeVisual.Verde, IdentidadeVisual.Verde, ci, marca: true);
        Cartao(sb, "Pendentes", r.Pendentes, r.PercentualPendentes, IdentidadeVisual.Ambar, "#B8741A", ci);
        sb.Append("</tr><tr>");
        Cartao(sb, "Não integradas", r.NaoIntegradas, r.PercentualNaoIntegradas,
            IdentidadeVisual.Vermelho, IdentidadeVisual.Vermelho, ci);
        Cartao(sb, "XMLs inválidos", r.XmlInvalido, r.PercentualXmlInvalido,
            IdentidadeVisual.Vermelho, IdentidadeVisual.Vermelho, ci);
        sb.Append("</tr></table></td></tr>");
    }

    private static void Cartao(
        StringBuilder sb, string rotulo, int valor, double? percentual, string corTopo, string corNumero,
        CultureInfo ci, bool marca = false)
    {
        sb.Append("<td style=\"width:50%;padding:4px;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"")
          .Append("border:1px solid ").Append(IdentidadeVisual.Cinza)
          .Append(";border-top:4px solid ").Append(corTopo)
          .Append(";border-radius:4px;\"><tr><td style=\"padding:10px 14px;\">");
        sb.Append("<div style=\"font-size:26px;font-weight:bold;color:").Append(corNumero).Append(";\">")
          .Append(valor.ToString(ci));
        if (percentual is not null)
        {
            sb.Append(" <span style=\"font-size:14px;font-weight:normal;color:").Append(IdentidadeVisual.Texto)
              .Append(";\">(").Append(Pct(percentual.Value, ci)).Append(")</span>");
        }

        sb.Append("</div>");
        sb.Append("<div style=\"font-size:12px;color:").Append(IdentidadeVisual.Texto).Append(";\">")
          .Append(Enc(rotulo));
        if (marca)
        {
            sb.Append(" <span style=\"color:").Append(IdentidadeVisual.Azul).Append(";font-weight:bold;\">●</span>");
        }

        sb.Append("</div></td></tr></table></td>");
    }

    private static void AppendVolume(StringBuilder sb, ResumoPeriodo r, CultureInfo ci)
    {
        AppendTitulo(sb, "Volume");
        sb.Append("<tr><td style=\"padding:4px 24px 0;\">");
        sb.Append("<p style=\"margin:0;color:").Append(IdentidadeVisual.Texto).Append(";font-size:13px;\">")
          .Append("Pacotes: <b>").Append(r.TotalPacotes.ToString(ci)).Append("</b> &nbsp;|&nbsp; ")
          .Append("Média de notas por pacote: <b>")
          .Append(r.MediaNotasPorPacote.ToString("0.0", ci)).Append("</b> &nbsp;|&nbsp; ")
          .Append("Dias com atividade: <b>").Append(r.DiasComAtividade.ToString(ci)).Append("</b>")
          .Append("</p></td></tr>");
    }

    private static void AppendFrequenciaDiaSemana(StringBuilder sb, ResumoPeriodo r, CultureInfo ci)
    {
        AppendTitulo(sb, "Frequência por dia da semana");
        AppendTabelaInicio(sb, new[] { "Dia", "Pacotes", "Notas" });
        var i = 0;
        foreach (var dia in OrdemDias)
        {
            var pacotes = r.PacotesPorDiaSemana.GetValueOrDefault(dia);
            var notas = r.NotasPorDiaSemana.GetValueOrDefault(dia);
            LinhaTabela(sb, i++, DiasSemana[(int)dia == 0 ? 6 : (int)dia - 1], pacotes, notas, ci);
        }

        AppendTabelaFim(sb);
    }

    private static void AppendFrequenciaFaixa(StringBuilder sb, ResumoPeriodo r, CultureInfo ci)
    {
        AppendTitulo(sb, "Frequência por faixa do dia");
        AppendTabelaInicio(sb, new[] { "Faixa", "Pacotes", "Notas" });
        var i = 0;
        foreach (var (faixa, rotulo) in new[]
        {
            (FaixaHorario.Manha, "Manhã (06:00–11:59)"),
            (FaixaHorario.Tarde, "Tarde (12:00–17:59)"),
            (FaixaHorario.Noite, "Noite (18:00–05:59)"),
        })
        {
            LinhaTabela(sb, i++, rotulo, r.PacotesPorFaixa.GetValueOrDefault(faixa),
                r.NotasPorFaixa.GetValueOrDefault(faixa), ci);
        }

        AppendTabelaFim(sb);
    }

    private static void AppendPerformance(StringBuilder sb, ResumoPeriodo r, CultureInfo ci)
    {
        AppendTitulo(sb, "Performance");
        sb.Append("<tr><td style=\"padding:4px 24px 0;\">");
        sb.Append("<p style=\"margin:0;color:").Append(IdentidadeVisual.Texto).Append(";font-size:13px;\">")
          .Append("Tempo médio por nota: <b>").Append(FormatarMs(r.TempoMedioPorNotaMs, ci)).Append("</b><br />")
          .Append("Tempo médio por pacote: <b>").Append(FormatarMs(r.TempoMedioPorPacoteMs, ci)).Append("</b>");
        if (r.TempoWmsMedioPorNotaMs > 0)
        {
            sb.Append("<br />Integração no WMS por nota: <b>").Append(FormatarMs(r.TempoWmsMedioPorNotaMs, ci)).Append("</b>");
        }

        sb.Append("</p></td></tr>");
    }

    private static void AppendChamadasWms(StringBuilder sb, ResumoPeriodo r, CultureInfo ci)
    {
        AppendTitulo(sb, "Chamadas ao WMS");
        sb.Append("<tr><td style=\"padding:4px 24px 0;\">");
        sb.Append("<p style=\"margin:0;color:").Append(IdentidadeVisual.Texto).Append(";font-size:13px;\">")
          .Append("Total: <b>").Append(r.TotalChamadasWms.ToString(ci)).Append("</b>");
        if (r.ChamadasWmsPorInterface.Count > 0)
        {
            sb.Append(" &nbsp;(")
              .Append(Enc(string.Join(", ", r.ChamadasWmsPorInterface
                  .OrderBy(k => k.Key, StringComparer.Ordinal)
                  .Select(k => $"{k.Key}: {k.Value.ToString(ci)}"))))
              .Append(")");
        }

        if (r.ChamadasWmsAproximado)
        {
            sb.Append("<br /><span style=\"font-size:12px;color:").Append(IdentidadeVisual.Texto)
              .Append(";\">Contagem aproximada (estimada pelo histórico de cada nota).</span>");
        }

        sb.Append("</p></td></tr>");
    }

    // Notas por tipo (PUXADA x TSP) e quantas vieram soltas na raiz da entrada.
    private static void AppendPorTipo(StringBuilder sb, ResumoPeriodo r, CultureInfo ci)
    {
        AppendTitulo(sb, "Notas por tipo");
        AppendTabelaInicio(sb, new[] { "Tipo", "Notas" });
        var linhas = new[] { ("PUXADA", r.NotasPuxada), ("TSP", r.NotasTsp), ("Da raiz (dentro da PUXADA)", r.NotasRaiz) };
        var i = 0;
        foreach (var (tipo, qtd) in linhas)
        {
            var fundo = i++ % 2 == 0 ? "#ffffff" : "#f5f9fd";
            sb.Append("<tr style=\"background:").Append(fundo).Append(";\">");
            Celula(sb, tipo);
            CelulaNumero(sb, qtd.ToString(ci));
            sb.Append("</tr>");
        }

        AppendTabelaFim(sb);
    }

    private static void AppendErros(StringBuilder sb, ResumoPeriodo r, CultureInfo ci)
    {
        AppendTitulo(sb, "Erros por categoria");
        if (r.ErrosPorCategoria.Count == 0)
        {
            sb.Append("<tr><td style=\"padding:4px 24px 0;\">");
            sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"")
              .Append("background:#e7f5f1;border:1px solid ").Append(IdentidadeVisual.Verde)
              .Append(";border-radius:4px;\"><tr><td style=\"padding:12px 14px;color:")
              .Append(IdentidadeVisual.Verde).Append(";font-weight:bold;\">")
              .Append("Nenhum erro no período")
              .Append("</td></tr></table></td></tr>");
            return;
        }

        AppendTabelaInicio(sb, new[] { "Categoria", "Ocorrências" });
        var i = 0;
        foreach (var (categoria, qtd) in r.ErrosPorCategoria.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal))
        {
            var fundo = i++ % 2 == 0 ? "#ffffff" : "#f5f9fd";
            sb.Append("<tr style=\"background:").Append(fundo).Append(";\">");
            Celula(sb, categoria);
            CelulaNumero(sb, qtd.ToString(ci));
            sb.Append("</tr>");
        }

        AppendTabelaFim(sb);
    }

    private static void AppendSaude(StringBuilder sb, ResumoPeriodo r, CultureInfo ci)
    {
        AppendTitulo(sb, "Saúde do serviço");
        sb.Append("<tr><td style=\"padding:4px 24px 0;\">");
        sb.Append("<p style=\"margin:0;color:").Append(IdentidadeVisual.Texto).Append(";font-size:13px;\">")
          .Append("No ar desde: <b>")
          .Append(r.ServicoDesde is { } desde ? Enc(IdentidadeVisual.Horario(desde)) : "desconhecido")
          .Append("</b><br />")
          .Append("Inicializações no período: <b>").Append(r.InicializacoesNoPeriodo.ToString(ci)).Append("</b>")
          .Append("</p></td></tr>");
    }

    private static void AppendTitulo(StringBuilder sb, string texto)
    {
        sb.Append("<tr><td style=\"padding:18px 24px 0;\">");
        sb.Append("<div style=\"font-size:15px;font-weight:bold;color:").Append(IdentidadeVisual.Azul)
          .Append(";border-bottom:2px solid ").Append(IdentidadeVisual.AzulClaro)
          .Append(";padding-bottom:4px;\">").Append(texto).Append("</div></td></tr>");
    }

    private static void AppendTabelaInicio(StringBuilder sb, string[] cabecalhos)
    {
        sb.Append("<tr><td style=\"padding:8px 24px 0;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"")
          .Append("border-collapse:collapse;border:1px solid ").Append(IdentidadeVisual.Cinza).Append(";font-size:13px;\">");
        sb.Append("<tr>");
        for (var i = 0; i < cabecalhos.Length; i++)
        {
            var alinhar = i == 0 ? "left" : "right";
            sb.Append("<th style=\"background:").Append(IdentidadeVisual.Azul)
              .Append(";color:#ffffff;text-align:").Append(alinhar).Append(";padding:7px 9px;border:1px solid ")
              .Append(IdentidadeVisual.Azul).Append(";\">").Append(cabecalhos[i]).Append("</th>");
        }

        sb.Append("</tr>");
    }

    private static void AppendTabelaFim(StringBuilder sb) => sb.Append("</table></td></tr>");

    private static void LinhaTabela(StringBuilder sb, int indice, string rotulo, int pacotes, int notas, CultureInfo ci)
    {
        var fundo = indice % 2 == 0 ? "#ffffff" : "#f5f9fd";
        sb.Append("<tr style=\"background:").Append(fundo).Append(";\">");
        // rótulo é texto fixo (dia da semana / faixa) — sem encode, para preservar os acentos.
        CelulaRaw(sb, rotulo);
        CelulaNumero(sb, pacotes.ToString(ci));
        CelulaNumero(sb, notas.ToString(ci));
        sb.Append("</tr>");
    }

    private static void Celula(StringBuilder sb, string? texto)
    {
        sb.Append("<td style=\"padding:6px 9px;border:1px solid ").Append(IdentidadeVisual.Cinza)
          .Append(";\">").Append(Enc(texto)).Append("</td>");
    }

    private static void CelulaRaw(StringBuilder sb, string texto)
    {
        sb.Append("<td style=\"padding:6px 9px;border:1px solid ").Append(IdentidadeVisual.Cinza)
          .Append(";\">").Append(texto).Append("</td>");
    }

    private static void CelulaNumero(StringBuilder sb, string texto)
    {
        sb.Append("<td style=\"padding:6px 9px;border:1px solid ").Append(IdentidadeVisual.Cinza)
          .Append(";text-align:right;\">").Append(Enc(texto)).Append("</td>");
    }

    private static void AppendAssinatura(StringBuilder sb, bool temLogo)
    {
        sb.Append("<tr><td style=\"padding:20px 24px 22px;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"")
          .Append("border-top:1px solid ").Append(IdentidadeVisual.Cinza).Append(";\"><tr>");
        sb.Append("<td style=\"padding-top:14px;vertical-align:top;\">");

        if (temLogo)
        {
            sb.Append("<img src=\"cid:").Append(CorpoEmailResumo.CidLogo)
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

    private static string FormatarMs(long ms, CultureInfo ci)
    {
        if (ms < 1000)
        {
            return ms.ToString(ci) + " ms";
        }

        var segundos = ms / 1000d;
        return segundos.ToString("0.0", ci) + " s";
    }

    private static string Pct(double valor, CultureInfo ci) => valor.ToString("0.0", ci) + "%";

    private static string Enc(string? texto) => WebUtility.HtmlEncode(texto ?? string.Empty);
}
