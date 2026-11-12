using System.Globalization;
using System.Net;
using System.Text;
using KitWmsIntegrador.Core.Relatorio;

namespace KitWmsIntegrador.Core.Email;

/// <summary>Como a logo entra no HTML.</summary>
public enum ModoLogoRelatorio
{
    /// <summary>Por <c>cid:</c> (dentro do e-mail).</summary>
    Cid,

    /// <summary>Por data URI (arquivo HTML em disco).</summary>
    DataUri,
}

/// <summary>
/// HTML do relatório diário da I51/I60 ("Relatório de Integração"). Layout em TABELAS com CSS inline
/// (sem script, sem CSS externo), compatível com Outlook. Todo texto dinâmico é HTML-encoded.
/// </summary>
public static class CorpoEmailRelatorioI60
{
    private const string Subtitulo = "Separação (I51) e associação (I60)";
    private const int LarguraCorpo = 760;

    /// <summary>Gera o HTML e, no modo cid, a imagem inline da logo.</summary>
    public static CorpoEmailResumo.CorpoEmail Montar(ModeloRelatorioI60 m, byte[]? logoPng, ModoLogoRelatorio modo)
    {
        ArgumentNullException.ThrowIfNull(m);
        var ci = CultureInfo.InvariantCulture;
        var temLogo = logoPng is { Length: > 0 };
        var srcLogo = !temLogo ? string.Empty
            : modo == ModoLogoRelatorio.Cid ? "cid:" + CorpoEmailResumo.CidLogo
            : "data:image/png;base64," + Convert.ToBase64String(logoPng!);

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\" />")
          .Append("<title>").Append(Enc(IdentidadeVisual.Titulo)).Append(" - I51/I60</title></head>");
        sb.Append("<body style=\"margin:0;padding:0;background:#f2f4f7;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f2f4f7;\"><tr><td align=\"center\" style=\"padding:20px 12px;\">");
        sb.Append("<table role=\"presentation\" width=\"").Append(LarguraCorpo.ToString(ci)).Append("\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:")
          .Append(LarguraCorpo.ToString(ci)).Append("px;max-width:100%;background:#ffffff;border:1px solid ").Append(IdentidadeVisual.Cinza)
          .Append(";border-radius:6px;font-family:Arial,Helvetica,sans-serif;color:").Append(IdentidadeVisual.Texto).Append(";font-size:13px;\">");

        Cabecalho(sb, srcLogo);
        Periodo(sb, m);
        Saude(sb, m, ci);

        if (m.SemAtividade)
        {
            Aviso(sb, "Nenhuma nota em acompanhamento e nenhum movimento na janela.", "#fff6e5", IdentidadeVisual.Ambar, "#8a5a00");
        }
        else
        {
            Cartoes(sb, m, ci);
        }

        var nota = new[] { "Nota", "Série", "Tipo", "Transporte / onda", "Pacote", "Idade", "Quando" };
        Tabela(sb, "Aguardando separação", m.ListaAguardando, nota, n => new[] { n.Numero, n.Serie, n.Tipo, TransOnda(n), n.Pacote, n.Idade, n.Quando }, "Nenhuma nota aguardando.");
        Tabela(sb, "Separadas na janela", m.SeparadasNaJanela, nota, n => new[] { n.Numero, n.Serie, n.Tipo, TransOnda(n), n.Pacote, n.Idade, n.Quando }, "Nenhuma separação na janela.");
        Tabela(sb, "I60 pendentes de envio", m.PendentesI60, nota, n => new[] { n.Numero, n.Serie, n.Tipo, TransOnda(n), n.Pacote, n.Idade, n.Quando }, "Nenhuma I60 pendente de envio.");
        Tabela(sb, "I60 associadas", m.Associadas, nota, n => new[] { n.Numero, n.Serie, n.Tipo, TransOnda(n), n.Pacote, n.Idade, n.Quando }, "Nenhuma I60 associada na janela.");

        var prob = new[] { "Nota", "Tipo", "Pacote", "Operação", "Motivo", "Mensagem do WMS", "Ressalvas" };
        Tabela(sb, "I60 com problema", m.Problemas, prob, n => new[] { n.Numero, n.Tipo, n.Pacote, n.Operacao, n.Motivo, n.Mensagem, n.Ressalvas }, "Nenhuma I60 com problema.");
        Tabela(sb, "I60 enviadas aguardando confirmação", m.AguardandoConfirmacao, nota, n => new[] { n.Numero, n.Serie, n.Tipo, TransOnda(n), n.Pacote, n.Idade, n.Quando }, "Nenhuma I60 aguardando confirmação.");

        TabelaAjustes(sb, m, ci);
        Tabela(sb, "Atrasadas", m.ListaAtrasadas, new[] { "Nota", "Série", "Tipo", "Transporte / onda", "Pacote", "Idade", "UNICO tentados" },
            n => new[] { n.Numero, n.Serie, n.Tipo, TransOnda(n), n.Pacote, n.Idade, n.Tentativas.ToString(ci) }, "Nenhuma nota atrasada.");
        TabelaPacotes(sb, m, ci);

        if (m.MostrarCandidatasI40)
        {
            var aviso = m.DevolucaoDesligada
                ? "A tela de devolução pela I40 está desligada (Devolucao:Habilitada=false)."
                : "A tela de devolução pela I40 está habilitada.";
            Tabela(sb, "Notas sem associação: candidatas à devolução pela I40 (última alternativa)", m.CandidatasI40, prob,
                n => new[] { n.Numero, n.Tipo, n.Pacote, n.Operacao, n.Motivo, n.Mensagem, n.Ressalvas }, "Nenhuma candidata.", aviso);
        }

        Assinatura(sb, srcLogo);
        sb.Append("</table></td></tr></table></body></html>");

        var imagem = temLogo && modo == ModoLogoRelatorio.Cid ? new ImagemInline(CorpoEmailResumo.CidLogo, logoPng!) : null;
        return new CorpoEmailResumo.CorpoEmail(sb.ToString(), imagem);
    }

    private static string TransOnda(LinhaNotaI60 n) =>
        string.IsNullOrEmpty(n.Onda) ? n.Transporte : n.Transporte + " / " + n.Onda;

    private static void Cabecalho(StringBuilder sb, string srcLogo)
    {
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
    }

    private static void Periodo(StringBuilder sb, ModeloRelatorioI60 m)
    {
        sb.Append("<tr><td style=\"padding:14px 24px 0;\"><p style=\"margin:0;font-size:13px;\"><b>Gerado em:</b> ")
          .Append(Enc(IdentidadeVisual.Horario(m.GeradoEm))).Append(" &nbsp;|&nbsp; <b>Janela:</b> ")
          .Append(Enc(IdentidadeVisual.Horario(m.JanelaInicio))).Append(" a ").Append(Enc(IdentidadeVisual.Horario(m.JanelaFim)))
          .Append("</p></td></tr>");
    }

    private static void Saude(StringBuilder sb, ModeloRelatorioI60 m, CultureInfo ci)
    {
        Titulo(sb, "Saúde da consulta");
        sb.Append("<tr><td style=\"padding:4px 24px 0;\"><p style=\"margin:0;font-size:13px;\">Última consulta OK: <b>")
          .Append(m.UltimaConsultaOkEm is { } ok ? Enc(IdentidadeVisual.Horario(ok)) : "nunca")
          .Append("</b><br />Falhas de consulta seguidas: <b>").Append(m.FalhasConsulta.ToString(ci))
          .Append("</b> &nbsp;|&nbsp; Respostas em falhos: <b>").Append(m.BrutosFalhos.ToString(ci))
          .Append("</b><br />Documentos sem nota correspondente na janela: <b>").Append(m.OrfaosNaJanela.ToString(ci))
          .Append("</b> &nbsp;|&nbsp; Notas com falhas de comunicação acima do alerta: <b>").Append(m.NotasComAlertaComunicacao.ToString(ci))
          .Append("</b></p></td></tr>");
    }

    private static void Cartoes(StringBuilder sb, ModeloRelatorioI60 m, CultureInfo ci)
    {
        var itens = new (string Rotulo, int Valor, string Cor)[]
        {
            ("Aguardando separação", m.Aguardando, IdentidadeVisual.AzulClaro),
            ("Atrasadas", m.Atrasadas, IdentidadeVisual.Vermelho),
            ("Separadas (I60 por enviar)", m.SeparadasPendentesI60, IdentidadeVisual.Azul),
            ("I60 associadas (janela)", m.AssociadasNaJanela, IdentidadeVisual.Verde),
            ("I60 como IMP / ENC (janela)", m.ImpNaJanela, IdentidadeVisual.Ambar),
            ("I60 aguardando confirmação", m.EnviadasAguardandoConfirmacao, IdentidadeVisual.CinzaNeutro),
            ("Rejeitadas", m.Rejeitadas, IdentidadeVisual.Vermelho),
        };
        Titulo(sb, "Resumo");
        sb.Append("<tr><td style=\"padding:8px 24px 0;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\">");
        for (var i = 0; i < itens.Length; i += 2)
        {
            sb.Append("<tr>");
            for (var j = i; j < Math.Min(i + 2, itens.Length); j++)
            {
                var (rotulo, valor, cor) = itens[j];
                sb.Append("<td style=\"width:50%;padding:4px;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border:1px solid ")
                  .Append(IdentidadeVisual.Cinza).Append(";border-top:4px solid ").Append(cor).Append(";border-radius:4px;\"><tr><td style=\"padding:8px 12px;\">")
                  .Append("<div style=\"font-size:24px;font-weight:bold;color:").Append(IdentidadeVisual.Azul).Append(";\">").Append(valor.ToString(ci))
                  .Append("</div><div style=\"font-size:12px;\">").Append(Enc(rotulo)).Append("</div></td></tr></table></td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</table></td></tr>");
    }

    private static void Tabela(
        StringBuilder sb, string titulo, SecaoI60<LinhaNotaI60> secao, string[] cabecalhos, Func<LinhaNotaI60, string[]> colunas,
        string vazio, string? aviso = null)
    {
        Titulo(sb, titulo + " (" + secao.Total.ToString(CultureInfo.InvariantCulture) + ")");
        if (aviso is not null)
        {
            sb.Append("<tr><td style=\"padding:4px 24px 0;font-size:12px;\">").Append(Enc(aviso)).Append("</td></tr>");
        }

        if (secao.Total == 0)
        {
            Aviso(sb, vazio, "#f5f9fd", IdentidadeVisual.Cinza, IdentidadeVisual.Texto);
            return;
        }

        InicioTabela(sb, cabecalhos);
        var i = 0;
        foreach (var linha in secao.Linhas)
        {
            Linha(sb, i++, colunas(linha));
        }

        FimTabela(sb, secao.NaoExibidas);
    }

    private static void TabelaAjustes(StringBuilder sb, ModeloRelatorioI60 m, CultureInfo ci)
    {
        Titulo(sb, "Ajustes de quantidade (" + m.Ajustes.Total.ToString(ci) + ")");
        if (m.Ajustes.Total == 0)
        {
            Aviso(sb, "Nenhum ajuste de quantidade na janela.", "#f5f9fd", IdentidadeVisual.Cinza, IdentidadeVisual.Texto);
            return;
        }

        InicioTabela(sb, new[] { "Nota", "Item", "Produto", "Original", "Separada", "Diferença" });
        var i = 0;
        foreach (var a in m.Ajustes.Linhas)
        {
            Linha(sb, i++, new[] { a.Nota, a.Item.ToString(ci), a.Produto, Qtde(a.Original, ci), Qtde(a.Separada, ci), Qtde(a.Diferenca, ci) });
        }

        FimTabela(sb, m.Ajustes.NaoExibidas);
    }

    private static void TabelaPacotes(StringBuilder sb, ModeloRelatorioI60 m, CultureInfo ci)
    {
        Titulo(sb, "Visão por pacote (" + m.PorPacote.Total.ToString(ci) + ")");
        if (m.PorPacote.Total == 0)
        {
            Aviso(sb, "Nenhum pacote com pendência ou movimento.", "#f5f9fd", IdentidadeVisual.Cinza, IdentidadeVisual.Texto);
            return;
        }

        InicioTabela(sb, new[] { "Pacote", "Tipo (ondas)", "Notas", "Aguard.", "Atras.", "Separ.", "Enviad.", "Assoc.", "IMP", "Rejeit." });
        var i = 0;
        foreach (var p in m.PorPacote.Linhas)
        {
            Linha(sb, i++, new[]
            {
                p.Pacote, string.IsNullOrEmpty(p.Ondas) ? p.Tipo : p.Tipo + " (" + p.Ondas + ")", p.Notas.ToString(ci), p.Aguardando.ToString(ci),
                p.Atrasadas.ToString(ci), p.Separadas.ToString(ci), p.Enviadas.ToString(ci), p.Associadas.ToString(ci), p.Imp.ToString(ci), p.Rejeitadas.ToString(ci),
            });
        }

        FimTabela(sb, m.PorPacote.NaoExibidas);
    }

    private static string Qtde(decimal v, CultureInfo ci) => v.ToString("0.####", ci);

    private static void Titulo(StringBuilder sb, string texto)
    {
        sb.Append("<tr><td style=\"padding:18px 24px 0;\"><div style=\"font-size:15px;font-weight:bold;color:").Append(IdentidadeVisual.Azul)
          .Append(";border-bottom:2px solid ").Append(IdentidadeVisual.AzulClaro).Append(";padding-bottom:4px;\">").Append(Enc(texto)).Append("</div></td></tr>");
    }

    private static void Aviso(StringBuilder sb, string texto, string fundo, string borda, string cor)
    {
        sb.Append("<tr><td style=\"padding:8px 24px 0;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:")
          .Append(fundo).Append(";border:1px solid ").Append(borda).Append(";border-radius:4px;\"><tr><td style=\"padding:10px 14px;color:")
          .Append(cor).Append(";font-weight:bold;\">").Append(Enc(texto)).Append("</td></tr></table></td></tr>");
    }

    private static void InicioTabela(StringBuilder sb, string[] cabecalhos)
    {
        sb.Append("<tr><td style=\"padding:8px 24px 0;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;border:1px solid ")
          .Append(IdentidadeVisual.Cinza).Append(";font-size:12px;\"><tr>");
        foreach (var c in cabecalhos)
        {
            sb.Append("<th style=\"background:").Append(IdentidadeVisual.Azul).Append(";color:#ffffff;text-align:left;padding:6px 7px;border:1px solid ")
              .Append(IdentidadeVisual.Azul).Append(";\">").Append(Enc(c)).Append("</th>");
        }

        sb.Append("</tr>");
    }

    private static void Linha(StringBuilder sb, int indice, string[] colunas)
    {
        sb.Append("<tr style=\"background:").Append(indice % 2 == 0 ? "#ffffff" : "#f5f9fd").Append(";\">");
        foreach (var c in colunas)
        {
            sb.Append("<td style=\"padding:5px 7px;border:1px solid ").Append(IdentidadeVisual.Cinza).Append(";vertical-align:top;\">").Append(Enc(c)).Append("</td>");
        }

        sb.Append("</tr>");
    }

    private static void FimTabela(StringBuilder sb, int naoExibidas)
    {
        sb.Append("</table>");
        if (naoExibidas > 0)
        {
            sb.Append("<div style=\"font-size:12px;padding-top:4px;\">e mais ").Append(naoExibidas.ToString(CultureInfo.InvariantCulture)).Append(" não exibidas.</div>");
        }

        sb.Append("</td></tr>");
    }

    private static void Assinatura(StringBuilder sb, string srcLogo)
    {
        sb.Append("<tr><td style=\"padding:20px 24px 22px;\"><div style=\"border-top:1px solid ").Append(IdentidadeVisual.Cinza).Append(";padding-top:12px;\">");
        if (srcLogo.Length > 0)
        {
            sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin-bottom:6px;\"><tr><td style=\"background:")
              .Append(IdentidadeVisual.Azul).Append(";padding:5px 9px;border-radius:4px;\"><img src=\"").Append(srcLogo)
              .Append("\" alt=\"Friozem\" height=\"28\" style=\"display:block;max-height:28px;border:0;\" /></td></tr></table>");
        }

        sb.Append("<div style=\"font-size:13px;font-weight:bold;color:").Append(IdentidadeVisual.Azul).Append(";\">KitWms Integrador</div>")
          .Append("<div style=\"font-size:12px;\">Integração automática de notas fiscais com o WMS</div>")
          .Append("<div style=\"font-size:11px;color:").Append(IdentidadeVisual.CinzaNeutro).Append(";margin-top:8px;\">Mensagem automática, não responda este e-mail.</div>")
          .Append("</div></td></tr>");
    }

    private static string Enc(string? texto) => WebUtility.HtmlEncode(texto ?? string.Empty);
}
