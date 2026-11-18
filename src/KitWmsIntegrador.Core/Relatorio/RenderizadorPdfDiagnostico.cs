using System.Globalization;
using KitWmsIntegrador.Core.Diagnostico;
using PdfSharpCore;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>
/// PDF A4 paisagem COMPLETO do "Diagnóstico agente integrador" (anexo do e-mail e cópia em disco), com a mesma identidade
/// do relatório da I51/I60 (cabeçalho com logo, barra tricolor, rodapé "Página X de Y"). O corpo do e-mail leva só o resumo
/// e os problemas; as tabelas de detalhe saem aqui. A paginação em duas passadas replica a do
/// <c>RenderizadorPdfRelatorioI60</c> sem alterá-lo.
/// </summary>
internal static class RenderizadorPdfDiagnostico
{
    private const string TituloDocumento = IdentidadeVisual.Titulo + " - Diagnóstico agente integrador";

    private const double Margem = 30;
    private const double AlturaCabecalhoPagina = 62;
    private const double AlturaRodape = 28;
    private const double EntreLinhas = 10.5;
    private const double PaddingCelula = 3;

    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
    private static readonly XPdfFontOptions Unicode = new(PdfFontEncoding.Unicode);
    private static readonly XFont FNormal = new("Arial", 8.5, XFontStyle.Regular, Unicode);
    private static readonly XFont FNegrito = new("Arial", 8.5, XFontStyle.Bold, Unicode);
    private static readonly XFont FPequena = new("Arial", 7, XFontStyle.Regular, Unicode);
    private static readonly XFont FTitulo = new("Arial", 17, XFontStyle.Bold, Unicode);
    private static readonly XFont FMarca = new("Arial", 22, XFontStyle.Bold, Unicode);
    private static readonly XFont FSecao = new("Arial", 11.5, XFontStyle.Bold, Unicode);

    private static readonly XColor CAzul = XColor.FromArgb(0x17, 0x47, 0x9E);
    private static readonly XColor CAzulClaro = XColor.FromArgb(0x54, 0xB6, 0xE7);
    private static readonly XColor CVerde = XColor.FromArgb(0x00, 0x9A, 0x83);
    private static readonly XColor CTexto = XColor.FromArgb(0x33, 0x33, 0x33);
    private static readonly XColor CCinza = XColor.FromArgb(0xC1, 0xC1, 0xC1);
    private static readonly XColor CZebra = XColor.FromArgb(0xF5, 0xF9, 0xFD);

    private sealed class Bloco
    {
        public double Altura;
        public Action<XGraphics, double> Desenhar = (_, _) => { };
        public bool ComProximo;
    }

    public static byte[] Renderizar(ModeloDiagnostico m, byte[]? logoPng)
    {
        using var documento = new PdfDocument();
        documento.Info.Title = TituloDocumento;
        documento.Info.Creator = "KitWmsIntegrador";

        var larguraPagina = XUnit.FromMillimeter(297).Point;
        var alturaPagina = XUnit.FromMillimeter(210).Point;
        var larguraUtil = larguraPagina - 2 * Margem;

        using var medidor = XGraphics.CreateMeasureContext(
            new XSize(larguraPagina, alturaPagina), XGraphicsUnit.Point, XPageDirection.Downwards);

        var blocos = MontarBlocos(m, medidor, larguraUtil);

        var topo = Margem + AlturaCabecalhoPagina;
        var limite = alturaPagina - Margem - AlturaRodape;
        var paginas = new List<List<(Bloco Bloco, double Y)>>();
        var atual = new List<(Bloco, double)>();
        paginas.Add(atual);
        var y = topo;
        for (var i = 0; i < blocos.Count; i++)
        {
            var b = blocos[i];
            var precisa = b.Altura;
            for (var j = i; blocos[j].ComProximo && j + 1 < blocos.Count;)
            {
                j++;
                precisa += blocos[j].Altura;
            }

            if (y + precisa > limite && atual.Count > 0)
            {
                atual = new List<(Bloco, double)>();
                paginas.Add(atual);
                y = topo;
            }

            atual.Add((b, y));
            y += b.Altura;
        }

        var total = paginas.Count;
        for (var p = 0; p < total; p++)
        {
            var pagina = documento.AddPage();
            pagina.Size = PageSize.A4;
            pagina.Orientation = PageOrientation.Landscape;
            if (pagina.Width.Point < pagina.Height.Point)
            {
                pagina.Width = XUnit.FromMillimeter(297);
                pagina.Height = XUnit.FromMillimeter(210);
            }

            using var gfx = XGraphics.FromPdfPage(pagina);
            DesenharCabecalhoPagina(gfx, larguraPagina, m, logoPng);
            foreach (var (bloco, by) in paginas[p])
            {
                bloco.Desenhar(gfx, by);
            }

            DesenharRodape(gfx, larguraPagina, alturaPagina, m.GeradoEm, p + 1, total);
        }

        using var ms = new MemoryStream();
        documento.Save(ms);
        return ms.ToArray();
    }

    private static void DesenharCabecalhoPagina(XGraphics gfx, double larguraPagina, ModeloDiagnostico m, byte[]? logoPng)
    {
        var xTitulo = Margem;
        var desenhouLogo = false;
        if (logoPng is not null)
        {
            try
            {
                using var img = XImage.FromStream(() => new MemoryStream(logoPng));
                var largura = 110.0;
                var altura = img.PixelWidth > 0 ? largura * img.PixelHeight / img.PixelWidth : 36;
                if (altura > 38)
                {
                    altura = 38;
                    largura = altura * img.PixelWidth / img.PixelHeight;
                }

                var yLogo = Margem - 6 + (38 - altura) / 2;
                gfx.DrawRoundedRectangle(new XSolidBrush(CAzul), Margem - 6, yLogo - 4, largura + 12, altura + 8, 6, 6);
                gfx.DrawImage(img, Margem, yLogo, largura, altura);
                xTitulo = Margem + Math.Max(largura, 99) + 18;
                desenhouLogo = true;
            }
            catch (Exception)
            {
                desenhouLogo = false;
            }
        }

        if (!desenhouLogo)
        {
            gfx.DrawString("Friozem", FMarca, new XSolidBrush(CAzul), Margem, Margem - 4, XStringFormats.TopLeft);
            xTitulo = Margem + 100;
        }

        gfx.DrawString(TituloDocumento, FTitulo, new XSolidBrush(CAzul), xTitulo, Margem - 3, XStringFormats.TopLeft);
        gfx.DrawString(
            "Período: " + IdentidadeVisual.Horario(m.JanelaInicio) + " a " + IdentidadeVisual.Horario(m.JanelaFim),
            FNormal, new XSolidBrush(CTexto), xTitulo, Margem + 18, XStringFormats.TopLeft);
        DesenharBarra(gfx, xTitulo, Margem + 32);
        var yLinha = Margem + 44;
        gfx.DrawLine(new XPen(CAzul, 1.5), Margem, yLinha, larguraPagina - Margem, yLinha);
    }

    private static void DesenharBarra(XGraphics gfx, double x, double y)
    {
        gfx.DrawRectangle(new XSolidBrush(CAzul), x, y, 22, 3.5);
        gfx.DrawRectangle(new XSolidBrush(CAzulClaro), x + 22, y, 22, 3.5);
        gfx.DrawRectangle(new XSolidBrush(CVerde), x + 44, y, 22, 3.5);
    }

    private static void DesenharRodape(XGraphics gfx, double larguraPagina, double alturaPagina, DateTimeOffset geradoEm, int numero, int total)
    {
        var yLinha = alturaPagina - Margem - 14;
        gfx.DrawLine(new XPen(CCinza, 0.75), Margem, yLinha, larguraPagina - Margem, yLinha);
        var cor = new XSolidBrush(CTexto);
        gfx.DrawString(
            "Friozem · " + TituloDocumento + " · " + geradoEm.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Ci),
            FPequena, cor, Margem, yLinha + 4, XStringFormats.TopLeft);
        var texto = $"Página {numero} de {total}";
        var largura = gfx.MeasureString(texto, FPequena).Width;
        gfx.DrawString(texto, FPequena, cor, larguraPagina - Margem - largura, yLinha + 4, XStringFormats.TopLeft);
    }

    private static List<Bloco> MontarBlocos(ModeloDiagnostico m, XGraphics g, double w)
    {
        var b = new List<Bloco>();

        // 1. Login
        var l = m.Login;
        b.Add(Titulo("1. Login do WMS (GetToken)"));
        b.Add(Linha(g, w,
            $"Tentativas: {N(l.Total)}   ·   Sucessos: {N(l.Sucessos)}   ·   Falhas: {N(l.Falhas)}   ·   Ativações de cooldown: {N(l.AtivacoesCooldown)}   ·   " +
            $"Pacotes interrompidos: {N(l.PacotesInterrompidos)}   ·   Notas deixadas aguardando: {N(l.NotasAguardando)}", l.Falhas > 0));
        Contagens(b, g, w, "Falhas por classe", "Classe", l.FalhasPorClasse, "Nenhuma falha de login no período.");
        b.Add(Subtitulo("Por servidor do balanceador"));
        if (l.PorBackend.Count == 0)
        {
            b.Add(Linha(g, w, "Nenhuma tentativa de login no período."));
        }
        else
        {
            Tabela(b, g, w, new[] { "Servidor", "Tentativas", "Falhas", "% de falha" }, new[] { 0.55, 0.15, 0.15, 0.15 },
                l.PorBackend.Select(x => new[] { x.Backend, N(x.Tentativas), N(x.Falhas), x.PercentualFalha.ToString("0.0", Ci) + "%" }).ToList(), 0);
        }

        b.Add(Subtitulo("Janelas de indisponibilidade"));
        if (l.Quedas.Count == 0)
        {
            b.Add(Linha(g, w, "Nenhuma queda de autenticação no período."));
        }
        else
        {
            Tabela(b, g, w, new[] { "Início", "Fim", "Duração", "Situação" }, new[] { 0.28, 0.28, 0.2, 0.24 },
                l.Quedas.Select(q => new[]
                {
                    q.Inicio is { } qi ? IdentidadeVisual.Horario(qi) : "antes da janela",
                    q.Fim is { } qf ? IdentidadeVisual.Horario(qf) : "em andamento",
                    q.Duracao is { } qd ? Duracao(qd) : "-",
                    q.Situacao,
                }).ToList(), 0);
        }

        // 2. Pacotes
        var r = m.Pacotes.Resumo;
        b.Add(Titulo("2. Pacotes e notas"));
        b.Add(Linha(g, w,
            $"Pacotes: {N(r.TotalPacotes)}   ·   Notas: {N(r.TotalNotas)}   ·   Integradas: {N(r.Integradas)}   ·   Revisar produto: {N(r.IntegradasRevisarProduto)}   ·   " +
            $"Pendentes: {N(r.Pendentes)}   ·   Não integradas: {N(r.NaoIntegradas)}   ·   XML inválido: {N(r.XmlInvalido)}   ·   Arquivos ignorados: {N(m.Pacotes.ArquivosIgnorados)}"));
        b.Add(Linha(g, w,
            "Tempo médio por nota: " + Duracao(TimeSpan.FromMilliseconds(r.TempoMedioPorNotaMs)) + "   ·   tempo do WMS por nota: "
            + (r.TempoWmsMedioPorNotaMs > 0 ? Duracao(TimeSpan.FromMilliseconds(r.TempoWmsMedioPorNotaMs)) : "sem medição")
            + "   ·   lotes divididos: " + m.Pacotes.LotesDivididos));
        Contagens(b, g, w, "Erros por categoria", "Categoria",
            r.ErrosPorCategoria.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new ContagemRotulo(kv.Key, kv.Value)).ToList(),
            "Nenhum erro por categoria no período.");

        // 3. I10 e I30
        b.Add(Titulo("3. Cadastros automáticos (I10 e I30)"));
        foreach (var i in m.Cadastros.Interfaces)
        {
            b.Add(Subtitulo(i.Interface == "I10" ? "I10 (cadastro de empresa)" : "I30 (cadastro de produto)"));
            b.Add(Linha(g, w,
                $"Disparos pela I50: {N(i.Gatilhos)}   ·   Notas distintas: {N(i.NotasDistintas)}   ·   Chamadas: {N(i.Chamadas)}   ·   Sucesso: {N(i.Sucessos)}   ·   " +
                $"Já existia: {N(i.JaExistia)}   ·   Erros: {N(i.Erros)}", i.Erros > 0));
            Contagens(b, g, w, "Mensagem da I50 que disparou", "Mensagem", i.GatilhosPorMensagem, "Nenhum disparo no período.");
            if (i.ErrosPorMensagem.Count > 0)
            {
                Contagens(b, g, w, "Mensagem de erro da " + i.Interface, "Mensagem", i.ErrosPorMensagem, string.Empty);
            }
        }

        // 4. I51 e I60
        var s = m.Separacao;
        b.Add(Titulo("4. Separação (I51) e associação (I60)"));
        if (!s.I51Habilitada)
        {
            b.Add(Linha(g, w, "I51 desligada (I51:Habilitada=false).", true));
        }

        b.Add(Linha(g, w,
            $"Consultas I51: {N(s.Consultas)}   ·   Com dados: {N(s.ConsultasOk)}   ·   Vazias: {N(s.ConsultasVazias)}   ·   Falhas: {N(s.ConsultasFalhas.Sum(f => f.Quantidade))}   ·   " +
            $"Novas separadas: {N(s.NovasSeparadas)}   ·   Novas atrasadas: {N(s.NovasAtrasadas)}   ·   Ajustes de quantidade: {N(s.AjustesQuantidade)}   ·   " +
            $"Aguardando separação: {N(s.Aguardando)}   ·   Atrasadas (agora): {N(s.Atrasadas)}"));
        if (s.ConsultasFalhas.Count > 0)
        {
            Contagens(b, g, w, "Falhas de consulta da I51", "Falha", s.ConsultasFalhas, string.Empty);
        }

        Contagens(b, g, w, "I60 por desfecho", "Desfecho", s.I60PorDesfecho, "Nenhuma I60 processada no período.");
        Contagens(b, g, w, "Alertas imediatos da I60 (notas)", "Evento", s.AlertasPorEvento, "Nenhum alerta da I60 no período.");
        b.Add(Subtitulo("Notas não associadas (" + N(s.NaoAssociadas.Total) + ")"));
        if (s.NaoAssociadas.Total == 0)
        {
            b.Add(Linha(g, w, "Nenhuma nota com problema ou atrasada."));
        }
        else
        {
            Tabela(b, g, w, new[] { "Nota", "Série", "Transporte / onda", "Motivo", "Mensagem do WMS" }, new[] { 0.1, 0.06, 0.2, 0.18, 0.46 },
                s.NaoAssociadas.Linhas.Select(n => new[] { n.Nota, n.Serie, string.IsNullOrEmpty(n.Onda) ? n.Transporte : n.Transporte + " / " + n.Onda, n.Motivo, n.Mensagem }).ToList(),
                s.NaoAssociadas.NaoExibidas);
        }

        // 5. Devolução
        var dev = m.Devolucao;
        b.Add(Titulo("5. Devolução pela I40"));
        if (dev.Total == 0)
        {
            b.Add(Linha(g, w, dev.Habilitada ? "Nenhum envio de devolução no período." : "Sem envios (tela de devolução desligada)."));
        }
        else
        {
            b.Add(Linha(g, w, $"Envios: {N(dev.Total)}   ·   Com sucesso: {N(dev.Sucessos)}   ·   Com falha: {N(dev.Falhas)}"));
            Tabela(b, g, w, new[] { "Usuário (declarado na tela)", "Envios", "Sucesso", "Falha" }, new[] { 0.55, 0.15, 0.15, 0.15 },
                dev.PorUsuario.Select(u => new[] { u.Usuario, N(u.Total), N(u.Sucessos), N(u.Falhas) }).ToList(), 0);
        }

        // 6. Saúde
        var h = m.Saude;
        b.Add(Titulo("6. Saúde do serviço"));
        Tabela(b, g, w, new[] { "Item", "Valor" }, new[] { 0.4, 0.6 },
            new List<string[]>
            {
                new[] { "Versão", h.Versao },
                new[] { "Último início do serviço na janela", h.UltimoInicio is { } ult ? IdentidadeVisual.Horario(ult) : "nenhum" },
                new[] { "Reinícios na janela", N(h.Reinicios) },
                new[] { "Espaço livre (pasta de entrada)", Bytes(h.EspacoLivreEntrada) },
                new[] { "Espaço livre (disco da aplicação)", Bytes(h.EspacoLivreAplicacao) },
                new[] { "Tamanho de Processados", Bytes(h.TamanhoProcessados) },
                new[] { "Tamanho de Erro", Bytes(h.TamanhoErro) },
                new[] { "Tamanho de Estado", Bytes(h.TamanhoEstado) },
                new[]
                {
                    "Última limpeza na janela",
                    h.Limpeza is { } lp
                        ? IdentidadeVisual.Horario(lp.Quando) + ": " + N(lp.Arquivos) + " arquivos e " + N(lp.Pastas) + " pastas removidos" + (lp.SemErros ? string.Empty : " (com erros)")
                        : "nenhuma",
                },
                new[] { "Warning/Error do log", h.LogWarningError },
            }, 0);

        // 7. Memória
        b.Add(Titulo("7. Memória do integrador e do servidor"));
        var mem = m.Memoria;
        if (mem is null || mem.Amostras == 0)
        {
            b.Add(Linha(g, w, "Nenhuma amostra de memória no período (amostragem desligada ou serviço iniciado há pouco)."));
        }
        else
        {
            b.Add(Linha(g, w,
                $"Amostras: {N(mem.Amostras)}   ·   Pico do integrador (working set): {Mb(mem.PicoWsMb)}   ·   Média: {Mb(mem.MediaWsMb)}   ·   " +
                $"Pico de memória privada: {Mb(mem.PicoPrivadoMb)} (média {Mb(mem.MediaPrivadoMb)})   ·   Pico de handles: {Num(mem.PicoHandles)}   ·   Pico de threads: {Num(mem.PicoThreads)}"));
            b.Add(Linha(g, w,
                $"Mínimo de memória livre do servidor: {Mb(mem.MinimoLivreMb)}" + (mem.TotalMb is { } tot ? $" de {N(tot)} MB" : string.Empty)
                + (mem.QuandoMinimoLivre is { } qml ? " às " + IdentidadeVisual.Horario(qml) : string.Empty)
                + $"   ·   Pico de memória virtual comprometida: {Mb(mem.PicoPaginacaoUsadaMb)} de {Mb(mem.PaginacaoTotalMb)}   ·   " +
                $"Alertas de memória livre baixa: {N(mem.AlertasMemoria)} (e-mails enviados: {N(mem.AlertasMemoriaEnviados)})", mem.AlertasMemoria > 0));
            Consumidores(b, g, w, "Maiores consumidores no pico do integrador" + (mem.QuandoPicoWs is { } qp ? " (" + IdentidadeVisual.Horario(qp) + ")" : string.Empty), mem.ConsumidoresNoPico);
            if (mem.ConsumidoresNoMinimoLivre.Count > 0 && mem.QuandoMinimoLivre != mem.QuandoPicoWs)
            {
                Consumidores(b, g, w, "Maiores consumidores no mínimo de memória livre" + (mem.QuandoMinimoLivre is { } mq ? " (" + IdentidadeVisual.Horario(mq) + ")" : string.Empty), mem.ConsumidoresNoMinimoLivre);
            }
        }

        return b;
    }

    private static void Consumidores(List<Bloco> b, XGraphics g, double w, string titulo, IReadOnlyList<ConsumidorMemoria> lista)
    {
        b.Add(Subtitulo(titulo));
        if (lista.Count == 0)
        {
            b.Add(Linha(g, w, "Lista de processos não coletada nesta amostra."));
            return;
        }

        Tabela(b, g, w, new[] { "Processo", "Memória (MB)" }, new[] { 0.6, 0.4 }, lista.Select(c => new[] { c.Nome, N(c.Mb) }).ToList(), 0);
    }

    private static void Contagens(List<Bloco> b, XGraphics g, double w, string titulo, string cabecalho, IReadOnlyList<ContagemRotulo> itens, string vazio)
    {
        if (itens.Count == 0)
        {
            if (vazio.Length > 0)
            {
                b.Add(Subtitulo(titulo));
                b.Add(Linha(g, w, vazio));
            }

            return;
        }

        b.Add(Subtitulo(titulo));
        Tabela(b, g, w, new[] { cabecalho, "Quantidade" }, new[] { 0.8, 0.2 }, itens.Select(c => new[] { c.Rotulo, N(c.Quantidade) }).ToList(), 0);
    }

    private static void Tabela(List<Bloco> b, XGraphics g, double w, string[] cabecalhos, double[] proporcoes, List<string[]> linhas, int naoExibidas)
    {
        var larguras = proporcoes.Select(p => p * w).ToArray();
        b.Add(CabecalhoTabela(cabecalhos, larguras));
        var zebra = false;
        foreach (var linha in linhas)
        {
            b.Add(LinhaTabela(linha, larguras, zebra, g));
            zebra = !zebra;
        }

        if (naoExibidas > 0)
        {
            b.Add(Linha(g, w, $"e mais {N(naoExibidas)} não exibidas."));
        }
    }

    private static Bloco Titulo(string texto) => new()
    {
        Altura = 26,
        ComProximo = true,
        Desenhar = (g, y) =>
        {
            Texto(g, texto, FSecao, CAzul, Margem, y + 4);
            DesenharBarra(g, Margem, y + 19);
        },
    };

    private static Bloco Subtitulo(string texto) => new()
    {
        Altura = 16,
        ComProximo = true,
        Desenhar = (g, y) => Texto(g, texto, FNegrito, CAzul, Margem, y + 4),
    };

    private static Bloco Linha(XGraphics medidor, double larguraUtil, string texto, bool negrito = false)
    {
        var fonte = negrito ? FNegrito : FNormal;
        var linhas = TextoPdf.Quebrar(texto, fonte, larguraUtil, medidor);
        return new Bloco
        {
            Altura = linhas.Count * EntreLinhas + 6,
            Desenhar = (g, y) =>
            {
                for (var i = 0; i < linhas.Count; i++)
                {
                    Texto(g, linhas[i], fonte, CTexto, Margem, y + 2 + i * EntreLinhas);
                }
            },
        };
    }

    private static Bloco CabecalhoTabela(string[] titulos, double[] larguras) => new()
    {
        Altura = 15,
        ComProximo = true,
        Desenhar = (g, y) =>
        {
            g.DrawRectangle(new XSolidBrush(CAzul), Margem, y, larguras.Sum(), 15);
            var x = Margem;
            for (var i = 0; i < titulos.Length; i++)
            {
                Texto(g, titulos[i], FNegrito, XColors.White, x + PaddingCelula, y + 3);
                x += larguras[i];
            }
        },
    };

    private static Bloco LinhaTabela(string[] colunas, double[] larguras, bool zebra, XGraphics medidor)
    {
        var quebradas = colunas
            .Select((c, i) => TextoPdf.Quebrar(c, FNormal, larguras[i] - 2 * PaddingCelula, medidor))
            .ToList();
        var altura = quebradas.Max(q => q.Count) * EntreLinhas + 2 * PaddingCelula;
        return new Bloco
        {
            Altura = altura,
            Desenhar = (g, y) =>
            {
                if (zebra)
                {
                    g.DrawRectangle(new XSolidBrush(CZebra), Margem, y, larguras.Sum(), altura);
                }

                var x = Margem;
                for (var i = 0; i < quebradas.Count; i++)
                {
                    for (var l = 0; l < quebradas[i].Count; l++)
                    {
                        Texto(g, quebradas[i][l], FNormal, CTexto, x + PaddingCelula, y + PaddingCelula + l * EntreLinhas);
                    }

                    x += larguras[i];
                }

                g.DrawLine(new XPen(CCinza, 0.5), Margem, y + altura, Margem + larguras.Sum(), y + altura);
            },
        };
    }

    private static void Texto(XGraphics g, string texto, XFont fonte, XColor cor, double x, double y) =>
        g.DrawString(texto, fonte, new XSolidBrush(cor), x, y, XStringFormats.TopLeft);

    private static string N(int v) => v.ToString("#,##0", Ci).Replace(',', '.');

    private static string Num(int? v) => v is { } x ? N(x) : "sem dado";

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
}
