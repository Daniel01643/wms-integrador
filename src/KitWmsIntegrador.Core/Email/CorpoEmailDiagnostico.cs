using System.Globalization;
using System.Net;
using System.Text;
using KitWmsIntegrador.Core.Diagnostico;
using KitWmsIntegrador.Core.Relatorio;

namespace KitWmsIntegrador.Core.Email;

/// <summary>
/// HTML do "Diagnóstico agente integrador" (relatório técnico diário para a TI), com a identidade visual do
/// Relatório de Integração. Layout em TABELAS com CSS inline (sem script, sem CSS externo), compatível com Outlook.
/// Todo texto dinâmico é HTML-encoded.
/// </summary>
public static class CorpoEmailDiagnostico
{
    /// <summary>Subtítulo do cabeçalho (também usado no assunto).</summary>
    public const string Subtitulo = "Diagnóstico agente integrador";

    private const int LarguraCorpo = 760;
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    /// <summary>Texto da linha que, no corpo enxuto do e-mail, aponta para o PDF anexo.</summary>
    public const string LinhaDetalhesNoPdf = "Detalhes completos no PDF anexo";

    /// <summary>
    /// Gera o HTML e, no modo cid, a imagem inline da logo. Com <paramref name="enxuto"/> (corpo do e-mail) saem só os
    /// cartões de resumo e os PROBLEMAS de cada seção (falhas, quedas, erros, notas não associadas) mais a memória, com a
    /// linha "Detalhes completos no PDF anexo"; tabelas de detalhe ficam só no PDF. Sem <paramref name="enxuto"/> o HTML é
    /// completo (arquivo em disco).
    /// </summary>
    public static CorpoEmailResumo.CorpoEmail Montar(ModeloDiagnostico m, byte[]? logoPng, ModoLogoRelatorio modo, bool enxuto = false)
    {
        ArgumentNullException.ThrowIfNull(m);
        var temLogo = logoPng is { Length: > 0 };
        var srcLogo = !temLogo ? string.Empty
            : modo == ModoLogoRelatorio.Cid ? "cid:" + CorpoEmailResumo.CidLogo
            : "data:image/png;base64," + Convert.ToBase64String(logoPng!);

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\" />")
          .Append("<title>").Append(Enc(IdentidadeVisual.Titulo)).Append(" - ").Append(Enc(Subtitulo)).Append("</title></head>");
        sb.Append("<body style=\"margin:0;padding:0;background:#f2f4f7;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f2f4f7;\"><tr><td align=\"center\" style=\"padding:20px 12px;\">");
        sb.Append("<table role=\"presentation\" width=\"").Append(LarguraCorpo.ToString(Ci)).Append("\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:")
          .Append(LarguraCorpo.ToString(Ci)).Append("px;max-width:100%;background:#ffffff;border:1px solid ").Append(IdentidadeVisual.Cinza)
          .Append(";border-radius:6px;font-family:Arial,Helvetica,sans-serif;color:").Append(IdentidadeVisual.Texto).Append(";font-size:13px;\">");

        Cabecalho(sb, srcLogo);
        Periodo(sb, m);
        if (enxuto)
        {
            Aviso(sb, LinhaDetalhesNoPdf, "#f5f9fd", IdentidadeVisual.AzulClaro, IdentidadeVisual.Azul);
        }

        Login(sb, m.Login, enxuto);
        Pacotes(sb, m.Pacotes);
        Cadastros(sb, m.Cadastros, enxuto);
        Separacao(sb, m.Separacao, enxuto);
        Devolucao(sb, m.Devolucao, enxuto);
        Saude(sb, m.Saude, enxuto);
        Memoria(sb, m.Memoria);
        Assinatura(sb, srcLogo);
        sb.Append("</table></td></tr></table></body></html>");

        var imagem = temLogo && modo == ModoLogoRelatorio.Cid ? new ImagemInline(CorpoEmailResumo.CidLogo, logoPng!) : null;
        return new CorpoEmailResumo.CorpoEmail(sb.ToString(), imagem);
    }

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

    private static void Periodo(StringBuilder sb, ModeloDiagnostico m)
    {
        sb.Append("<tr><td style=\"padding:14px 24px 0;\"><p style=\"margin:0;font-size:13px;\"><b>Gerado em:</b> ")
          .Append(Enc(IdentidadeVisual.Horario(m.GeradoEm))).Append(" &nbsp;|&nbsp; <b>Período:</b> ")
          .Append(Enc(IdentidadeVisual.Horario(m.JanelaInicio))).Append(" a ").Append(Enc(IdentidadeVisual.Horario(m.JanelaFim)))
          .Append("</p></td></tr>");
    }

    private static void Login(StringBuilder sb, SecaoLogin s, bool enxuto)
    {
        Titulo(sb, "1. Login do WMS (GetToken)");
        Cartoes(sb, new (string, string, string)[]
        {
            ("Tentativas", N(s.Total), IdentidadeVisual.AzulClaro),
            ("Sucessos", N(s.Sucessos), IdentidadeVisual.Verde),
            ("Falhas", N(s.Falhas), s.Falhas > 0 ? IdentidadeVisual.Vermelho : IdentidadeVisual.CinzaNeutro),
            ("Ativações de cooldown", N(s.AtivacoesCooldown), s.AtivacoesCooldown > 0 ? IdentidadeVisual.Ambar : IdentidadeVisual.CinzaNeutro),
            ("Pacotes interrompidos", N(s.PacotesInterrompidos), s.PacotesInterrompidos > 0 ? IdentidadeVisual.Vermelho : IdentidadeVisual.CinzaNeutro),
            ("Notas deixadas aguardando", N(s.NotasAguardando), s.NotasAguardando > 0 ? IdentidadeVisual.Ambar : IdentidadeVisual.CinzaNeutro),
        });

        Subtitulo2(sb, "Falhas por classe");
        TabelaContagem(sb, "Classe", s.FalhasPorClasse, "Nenhuma falha de login no período.");
        if (!enxuto)
        {
            Subtitulo2(sb, "Por servidor do balanceador");
        }

        if (enxuto)
        {
            // detalhe por servidor fica só no PDF
        }
        else if (s.PorBackend.Count == 0)
        {
            Aviso(sb, "Nenhuma tentativa de login no período.", "#f5f9fd", IdentidadeVisual.Cinza, IdentidadeVisual.Texto);
        }
        else
        {
            InicioTabela(sb, new[] { "Servidor", "Tentativas", "Falhas", "% de falha" });
            var i = 0;
            foreach (var b in s.PorBackend)
            {
                Linha(sb, i++, new[] { b.Backend, N(b.Tentativas), N(b.Falhas), b.PercentualFalha.ToString("0.0", Ci) + "%" });
            }

            FimTabela(sb, 0);
        }

        Subtitulo2(sb, "Janelas de indisponibilidade");
        if (s.Quedas.Count == 0)
        {
            Aviso(sb, "Nenhuma queda de autenticação no período.", "#eaf7f4", IdentidadeVisual.Verde, "#006b5b");
        }
        else
        {
            InicioTabela(sb, new[] { "Início", "Fim", "Duração", "Situação" });
            var i = 0;
            foreach (var q in s.Quedas)
            {
                Linha(sb, i++, new[]
                {
                    q.Inicio is { } ini ? IdentidadeVisual.Horario(ini) : "antes da janela",
                    q.Fim is { } f ? IdentidadeVisual.Horario(f) : "em andamento",
                    q.Duracao is { } d ? Duracao(d) : "-",
                    q.Situacao,
                });
            }

            FimTabela(sb, 0);
        }
    }

    private static void Pacotes(StringBuilder sb, SecaoPacotes s)
    {
        var r = s.Resumo;
        Titulo(sb, "2. Pacotes e notas");
        Cartoes(sb, new (string, string, string)[]
        {
            ("Pacotes", N(r.TotalPacotes), IdentidadeVisual.AzulClaro),
            ("Notas", N(r.TotalNotas), IdentidadeVisual.Azul),
            ("Integradas", N(r.Integradas), IdentidadeVisual.Verde),
            ("Revisar produto", N(r.IntegradasRevisarProduto), IdentidadeVisual.Ambar),
            ("Pendentes", N(r.Pendentes), r.Pendentes > 0 ? IdentidadeVisual.Ambar : IdentidadeVisual.CinzaNeutro),
            ("Não integradas", N(r.NaoIntegradas), r.NaoIntegradas > 0 ? IdentidadeVisual.Vermelho : IdentidadeVisual.CinzaNeutro),
            ("XML inválido", N(r.XmlInvalido), r.XmlInvalido > 0 ? IdentidadeVisual.Vermelho : IdentidadeVisual.CinzaNeutro),
            ("Arquivos ignorados", N(s.ArquivosIgnorados), IdentidadeVisual.CinzaNeutro),
        });
        Texto(sb, "Tempo médio por nota: <b>" + Enc(Duracao(TimeSpan.FromMilliseconds(r.TempoMedioPorNotaMs)))
            + "</b> &nbsp;|&nbsp; tempo do WMS por nota: <b>"
            + (r.TempoWmsMedioPorNotaMs > 0 ? Enc(Duracao(TimeSpan.FromMilliseconds(r.TempoWmsMedioPorNotaMs))) : "sem medição")
            + "</b> &nbsp;|&nbsp; lotes divididos: <b>" + Enc(s.LotesDivididos) + "</b>");
        Subtitulo2(sb, "Erros por categoria");
        TabelaContagem(sb, "Categoria",
            r.ErrosPorCategoria.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new ContagemRotulo(kv.Key, kv.Value)).ToList(),
            "Nenhum erro por categoria no período.");
    }

    private static void Cadastros(StringBuilder sb, SecaoCadastros s, bool enxuto)
    {
        Titulo(sb, "3. Cadastros automáticos (I10 e I30)");
        foreach (var i in s.Interfaces)
        {
            Subtitulo2(sb, i.Interface == "I10" ? "I10 (cadastro de empresa)" : "I30 (cadastro de produto)");
            Cartoes(sb, new (string, string, string)[]
            {
                ("Disparos pela I50", N(i.Gatilhos), IdentidadeVisual.AzulClaro),
                ("Notas distintas", N(i.NotasDistintas), IdentidadeVisual.Azul),
                ("Chamadas à " + i.Interface, N(i.Chamadas), IdentidadeVisual.AzulClaro),
                ("Sucesso", N(i.Sucessos), IdentidadeVisual.Verde),
                ("Já existia", N(i.JaExistia), IdentidadeVisual.CinzaNeutro),
                ("Erros da " + i.Interface, N(i.Erros), i.Erros > 0 ? IdentidadeVisual.Vermelho : IdentidadeVisual.CinzaNeutro),
            });
            if (!enxuto)
            {
                TabelaContagem(sb, "Mensagem da I50 que disparou", i.GatilhosPorMensagem, "Nenhum disparo no período.");
            }

            if (i.ErrosPorMensagem.Count > 0)
            {
                TabelaContagem(sb, "Mensagem de erro da " + i.Interface, i.ErrosPorMensagem, string.Empty);
            }
        }
    }

    private static void Separacao(StringBuilder sb, SecaoSeparacao s, bool enxuto)
    {
        Titulo(sb, "4. Separação (I51) e associação (I60)");
        if (!s.I51Habilitada)
        {
            Aviso(sb, "I51 desligada (I51:Habilitada=false).", "#fff6e5", IdentidadeVisual.Ambar, "#8a5a00");
        }

        Cartoes(sb, new (string, string, string)[]
        {
            ("Consultas I51", N(s.Consultas), IdentidadeVisual.AzulClaro),
            ("Com dados", N(s.ConsultasOk), IdentidadeVisual.Verde),
            ("Vazias", N(s.ConsultasVazias), IdentidadeVisual.CinzaNeutro),
            ("Falhas", N(s.ConsultasFalhas.Sum(f => f.Quantidade)), s.ConsultasFalhas.Count > 0 ? IdentidadeVisual.Vermelho : IdentidadeVisual.CinzaNeutro),
            ("Novas separadas", N(s.NovasSeparadas), IdentidadeVisual.Azul),
            ("Novas atrasadas", N(s.NovasAtrasadas), s.NovasAtrasadas > 0 ? IdentidadeVisual.Vermelho : IdentidadeVisual.CinzaNeutro),
            ("Ajustes de quantidade", N(s.AjustesQuantidade), IdentidadeVisual.Ambar),
            ("Aguardando separação", N(s.Aguardando), IdentidadeVisual.AzulClaro),
            ("Atrasadas (agora)", N(s.Atrasadas), s.Atrasadas > 0 ? IdentidadeVisual.Vermelho : IdentidadeVisual.CinzaNeutro),
        });
        if (s.ConsultasFalhas.Count > 0)
        {
            TabelaContagem(sb, "Falhas de consulta da I51", s.ConsultasFalhas, string.Empty);
        }

        if (!enxuto)
        {
            Subtitulo2(sb, "I60 por desfecho");
            TabelaContagem(sb, "Desfecho", s.I60PorDesfecho, "Nenhuma I60 processada no período.");
        }

        Subtitulo2(sb, "Alertas imediatos da I60 (notas)");
        TabelaContagem(sb, "Evento", s.AlertasPorEvento, "Nenhum alerta da I60 no período.");

        Subtitulo2(sb, "Notas não associadas (" + N(s.NaoAssociadas.Total) + ")");
        if (s.NaoAssociadas.Total == 0)
        {
            Aviso(sb, "Nenhuma nota com problema ou atrasada.", "#eaf7f4", IdentidadeVisual.Verde, "#006b5b");
            return;
        }

        InicioTabela(sb, new[] { "Nota", "Série", "Transporte / onda", "Motivo", "Mensagem do WMS" });
        var i = 0;
        foreach (var n in s.NaoAssociadas.Linhas)
        {
            Linha(sb, i++, new[] { n.Nota, n.Serie, string.IsNullOrEmpty(n.Onda) ? n.Transporte : n.Transporte + " / " + n.Onda, n.Motivo, n.Mensagem });
        }

        FimTabela(sb, s.NaoAssociadas.NaoExibidas);
    }

    private static void Devolucao(StringBuilder sb, SecaoDevolucao s, bool enxuto)
    {
        Titulo(sb, "5. Devolução pela I40");
        if (s.Total == 0)
        {
            Aviso(sb, s.Habilitada ? "Nenhum envio de devolução no período." : "Sem envios (tela de devolução desligada).",
                "#f5f9fd", IdentidadeVisual.Cinza, IdentidadeVisual.Texto);
            return;
        }

        Cartoes(sb, new (string, string, string)[]
        {
            ("Envios", N(s.Total), IdentidadeVisual.AzulClaro),
            ("Com sucesso", N(s.Sucessos), IdentidadeVisual.Verde),
            ("Com falha", N(s.Falhas), s.Falhas > 0 ? IdentidadeVisual.Vermelho : IdentidadeVisual.CinzaNeutro),
        });
        if (enxuto)
        {
            return;
        }

        InicioTabela(sb, new[] { "Usuário (declarado na tela)", "Envios", "Sucesso", "Falha" });
        var i = 0;
        foreach (var u in s.PorUsuario)
        {
            Linha(sb, i++, new[] { u.Usuario, N(u.Total), N(u.Sucessos), N(u.Falhas) });
        }

        FimTabela(sb, 0);
    }

    private static void Saude(StringBuilder sb, SecaoSaude s, bool enxuto)
    {
        Titulo(sb, "6. Saúde do serviço");
        if (enxuto)
        {
            Texto(sb, "Versão <b>" + Enc(s.Versao) + "</b> &nbsp;|&nbsp; reinícios na janela: <b>" + N(s.Reinicios)
                + "</b> &nbsp;|&nbsp; espaço livre (disco da aplicação): <b>" + Enc(Bytes(s.EspacoLivreAplicacao)) + "</b>");
            return;
        }

        InicioTabela(sb, new[] { "Item", "Valor" });
        var itens = new List<string[]>
        {
            new[] { "Versão", s.Versao },
            new[] { "Último início do serviço na janela", s.UltimoInicio is { } u ? IdentidadeVisual.Horario(u) : "nenhum" },
            new[] { "Reinícios na janela", N(s.Reinicios) },
            new[] { "Espaço livre (pasta de entrada)", Bytes(s.EspacoLivreEntrada) },
            new[] { "Espaço livre (disco da aplicação)", Bytes(s.EspacoLivreAplicacao) },
            new[] { "Tamanho de Processados", Bytes(s.TamanhoProcessados) },
            new[] { "Tamanho de Erro", Bytes(s.TamanhoErro) },
            new[] { "Tamanho de Estado", Bytes(s.TamanhoEstado) },
            new[]
            {
                "Última limpeza na janela",
                s.Limpeza is { } l
                    ? IdentidadeVisual.Horario(l.Quando) + ": " + N(l.Arquivos) + " arquivos e " + N(l.Pastas) + " pastas removidos" + (l.SemErros ? string.Empty : " (com erros)")
                    : "nenhuma",
            },
            new[] { "Warning/Error do log", s.LogWarningError },
        };
        var i = 0;
        foreach (var item in itens)
        {
            Linha(sb, i++, item);
        }

        FimTabela(sb, 0);
    }

    private static void Memoria(StringBuilder sb, SecaoMemoria? s)
    {
        Titulo(sb, "7. Memória do integrador e do servidor");
        if (s is null || s.Amostras == 0)
        {
            Aviso(sb, "Nenhuma amostra de memória no período (amostragem desligada ou serviço iniciado há pouco).",
                "#f5f9fd", IdentidadeVisual.Cinza, IdentidadeVisual.Texto);
            return;
        }

        var baixa = s.MinimoLivreMb is { } min && s.TotalMb is { } tot && tot > 0 && min * 100L < tot * 10L;
        Cartoes(sb, new (string, string, string)[]
        {
            ("Amostras", N(s.Amostras), IdentidadeVisual.AzulClaro),
            ("Pico do integrador (working set)", Mb(s.PicoWsMb), IdentidadeVisual.Azul),
            ("Média do integrador (working set)", Mb(s.MediaWsMb), IdentidadeVisual.Azul),
            ("Pico de memória privada", Mb(s.PicoPrivadoMb), IdentidadeVisual.AzulClaro),
            ("Pico de handles / threads", Handles(s), IdentidadeVisual.CinzaNeutro),
            ("Mínimo de memória livre do servidor", Mb(s.MinimoLivreMb) + (s.TotalMb is { } t ? " de " + N(t) + " MB" : string.Empty),
                baixa ? IdentidadeVisual.Vermelho : IdentidadeVisual.Verde),
        });
        Texto(sb, "Pico de memória virtual comprometida (RAM + arquivo de paginação): <b>" + Enc(Mb(s.PicoPaginacaoUsadaMb))
            + "</b> de <b>" + Enc(Mb(s.PaginacaoTotalMb)) + "</b>"
            + (s.MinimoLivreMb is not null && s.QuandoMinimoLivre is { } q ? " &nbsp;|&nbsp; mínimo de memória livre às <b>" + Enc(IdentidadeVisual.Horario(q)) + "</b>" : string.Empty)
            + (s.AlertasMemoria > 0 ? " &nbsp;|&nbsp; alertas de memória livre baixa: <b>" + N(s.AlertasMemoria) + "</b> (e-mails enviados: " + N(s.AlertasMemoriaEnviados) + ")" : string.Empty));

        Subtitulo2(sb, "Maiores consumidores no pico do integrador" + (s.QuandoPicoWs is { } p ? " (" + IdentidadeVisual.Horario(p) + ")" : string.Empty));
        TabelaConsumidores(sb, s.ConsumidoresNoPico);
        if (s.ConsumidoresNoMinimoLivre.Count > 0 && s.QuandoMinimoLivre != s.QuandoPicoWs)
        {
            Subtitulo2(sb, "Maiores consumidores no mínimo de memória livre" + (s.QuandoMinimoLivre is { } m ? " (" + IdentidadeVisual.Horario(m) + ")" : string.Empty));
            TabelaConsumidores(sb, s.ConsumidoresNoMinimoLivre);
        }
    }

    private static void TabelaConsumidores(StringBuilder sb, IReadOnlyList<ConsumidorMemoria> consumidores)
    {
        if (consumidores.Count == 0)
        {
            Aviso(sb, "Lista de processos não coletada nesta amostra.", "#f5f9fd", IdentidadeVisual.Cinza, IdentidadeVisual.Texto);
            return;
        }

        InicioTabela(sb, new[] { "Processo", "Memória (MB)" });
        var i = 0;
        foreach (var c in consumidores)
        {
            Linha(sb, i++, new[] { c.Nome, N(c.Mb) });
        }

        FimTabela(sb, 0);
    }

    private static string Handles(SecaoMemoria s) =>
        (s.PicoHandles is { } h ? N(h) : "?") + " / " + (s.PicoThreads is { } t ? N(t) : "?");

    private static string Mb(int? v) => v is { } x ? N(x) + " MB" : "sem dado";

    private static string Mb(double? v) => v is { } x ? N((int)Math.Round(x)) + " MB" : "sem dado";

    private static string Bytes(long? valor)
    {
        if (valor is not { } v)
        {
            return "não medido";
        }

        string[] unidades = { "B", "KB", "MB", "GB", "TB" };
        var x = (double)v;
        var u = 0;
        while (x >= 1024 && u < unidades.Length - 1)
        {
            x /= 1024;
            u++;
        }

        return x.ToString(u == 0 ? "0" : "0.0", Ci) + " " + unidades[u];
    }

    private static string Duracao(TimeSpan d) =>
        d.TotalSeconds < 1 ? "menos de 1 s"
        : d.TotalMinutes < 1 ? d.TotalSeconds.ToString("0", Ci) + " s"
        : d.TotalHours < 1 ? Math.Floor(d.TotalMinutes).ToString("0", Ci) + " min " + d.Seconds.ToString("0", Ci) + " s"
        : Math.Floor(d.TotalHours).ToString("0", Ci) + " h " + d.Minutes.ToString("0", Ci) + " min";

    private static string N(int v) => v.ToString("#,##0", Ci).Replace(',', '.');

    private static void Titulo(StringBuilder sb, string texto)
    {
        sb.Append("<tr><td style=\"padding:18px 24px 0;\"><div style=\"font-size:15px;font-weight:bold;color:").Append(IdentidadeVisual.Azul)
          .Append(";border-bottom:2px solid ").Append(IdentidadeVisual.AzulClaro).Append(";padding-bottom:4px;\">").Append(Enc(texto)).Append("</div></td></tr>");
    }

    private static void Subtitulo2(StringBuilder sb, string texto)
    {
        sb.Append("<tr><td style=\"padding:12px 24px 0;\"><div style=\"font-size:13px;font-weight:bold;color:").Append(IdentidadeVisual.Azul)
          .Append(";\">").Append(Enc(texto)).Append("</div></td></tr>");
    }

    private static void Texto(StringBuilder sb, string htmlJaCodificado)
    {
        sb.Append("<tr><td style=\"padding:8px 24px 0;font-size:13px;\">").Append(htmlJaCodificado).Append("</td></tr>");
    }

    private static void Cartoes(StringBuilder sb, (string Rotulo, string Valor, string Cor)[] itens)
    {
        const int porLinha = 3;
        sb.Append("<tr><td style=\"padding:8px 24px 0;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\">");
        for (var i = 0; i < itens.Length; i += porLinha)
        {
            sb.Append("<tr>");
            for (var j = i; j < i + porLinha; j++)
            {
                if (j >= itens.Length)
                {
                    sb.Append("<td style=\"width:33%;padding:3px;\"></td>");
                    continue;
                }

                var (rotulo, valor, cor) = itens[j];
                sb.Append("<td style=\"width:33%;padding:3px;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border:1px solid ")
                  .Append(IdentidadeVisual.Cinza).Append(";border-top:4px solid ").Append(cor).Append(";border-radius:4px;\"><tr><td style=\"padding:7px 10px;\">")
                  .Append("<div style=\"font-size:20px;font-weight:bold;color:").Append(IdentidadeVisual.Azul).Append(";\">").Append(Enc(valor))
                  .Append("</div><div style=\"font-size:11px;\">").Append(Enc(rotulo)).Append("</div></td></tr></table></td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</table></td></tr>");
    }

    private static void TabelaContagem(StringBuilder sb, string cabecalho, IReadOnlyList<ContagemRotulo> itens, string vazio)
    {
        if (itens.Count == 0)
        {
            if (vazio.Length > 0)
            {
                Aviso(sb, vazio, "#f5f9fd", IdentidadeVisual.Cinza, IdentidadeVisual.Texto);
            }

            return;
        }

        InicioTabela(sb, new[] { cabecalho, "Quantidade" });
        var i = 0;
        foreach (var c in itens)
        {
            Linha(sb, i++, new[] { c.Rotulo, N(c.Quantidade) });
        }

        FimTabela(sb, 0);
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
            sb.Append("<div style=\"font-size:12px;padding-top:4px;\">e mais ").Append(naoExibidas.ToString(Ci)).Append(" não exibidas.</div>");
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
          .Append("<div style=\"font-size:12px;\">Diagnóstico técnico enviado somente à TI</div>")
          .Append("<div style=\"font-size:11px;color:").Append(IdentidadeVisual.CinzaNeutro).Append(";margin-top:8px;\">Mensagem automática, não responda este e-mail.</div>")
          .Append("</div></td></tr>");
    }

    private static string Enc(string? texto) => WebUtility.HtmlEncode(texto ?? string.Empty);
}
