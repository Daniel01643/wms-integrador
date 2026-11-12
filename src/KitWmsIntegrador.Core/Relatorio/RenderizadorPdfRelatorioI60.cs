using System.Globalization;
using PdfSharpCore;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>
/// PDF A4 paisagem do relatório diário da I51/I60, com a mesma identidade do resumo semanal
/// (cabeçalho com logo, barra tricolor, rodapé "Página X de Y"). A paginação em duas passadas
/// replica a do <c>RenderizadorPdfResumo</c> sem alterá-lo.
/// </summary>
internal static class RenderizadorPdfRelatorioI60
{
    private const string TituloDocumento = IdentidadeVisual.Titulo + " - I51/I60";

    private const double Margem = 30;
    private const double AlturaCabecalhoPagina = 62;
    private const double AlturaRodape = 28;
    private const double EntreLinhas = 10.5;
    private const double PaddingCelula = 3;

    private static readonly XPdfFontOptions Unicode = new(PdfFontEncoding.Unicode);
    private static readonly XFont FNormal = new("Arial", 8.5, XFontStyle.Regular, Unicode);
    private static readonly XFont FNegrito = new("Arial", 8.5, XFontStyle.Bold, Unicode);
    private static readonly XFont FPequena = new("Arial", 7, XFontStyle.Regular, Unicode);
    private static readonly XFont FTitulo = new("Arial", 17, XFontStyle.Bold, Unicode);
    private static readonly XFont FMarca = new("Arial", 22, XFontStyle.Bold, Unicode);
    private static readonly XFont FSecao = new("Arial", 11.5, XFontStyle.Bold, Unicode);
    private static readonly XFont FNumero = new("Arial", 16, XFontStyle.Bold, Unicode);
    private static readonly XFont FRotulo = new("Arial", 7, XFontStyle.Regular, Unicode);

    private static readonly XColor CAzul = XColor.FromArgb(0x17, 0x47, 0x9E);
    private static readonly XColor CAzulClaro = XColor.FromArgb(0x54, 0xB6, 0xE7);
    private static readonly XColor CVerde = XColor.FromArgb(0x00, 0x9A, 0x83);
    private static readonly XColor CTexto = XColor.FromArgb(0x33, 0x33, 0x33);
    private static readonly XColor CCinza = XColor.FromArgb(0xC1, 0xC1, 0xC1);
    private static readonly XColor CAmbar = XColor.FromArgb(0xF5, 0xA6, 0x23);
    private static readonly XColor CVermelho = XColor.FromArgb(0xD3, 0x2F, 0x2F);
    private static readonly XColor CNeutro = XColor.FromArgb(0x6B, 0x72, 0x80);
    private static readonly XColor CZebra = XColor.FromArgb(0xF5, 0xF9, 0xFD);

    private sealed class Bloco
    {
        public double Altura;
        public Action<XGraphics, double> Desenhar = (_, _) => { };
        public bool ComProximo;
    }

    public static byte[] Renderizar(ModeloRelatorioI60 m, byte[]? logoPng)
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

    private static void DesenharCabecalhoPagina(XGraphics gfx, double larguraPagina, ModeloRelatorioI60 m, byte[]? logoPng)
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
            "Janela: " + IdentidadeVisual.Horario(m.JanelaInicio) + " a " + IdentidadeVisual.Horario(m.JanelaFim),
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
            "Friozem · " + TituloDocumento + " · " + geradoEm.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            FPequena, cor, Margem, yLinha + 4, XStringFormats.TopLeft);
        var texto = $"Página {numero} de {total}";
        var largura = gfx.MeasureString(texto, FPequena).Width;
        gfx.DrawString(texto, FPequena, cor, larguraPagina - Margem - largura, yLinha + 4, XStringFormats.TopLeft);
    }

    private static List<Bloco> MontarBlocos(ModeloRelatorioI60 m, XGraphics medidor, double larguraUtil)
    {
        var ci = CultureInfo.InvariantCulture;
        var blocos = new List<Bloco>();

        blocos.Add(Titulo("Saúde da consulta"));
        blocos.Add(LinhaTexto(
            $"Última consulta OK: {(m.UltimaConsultaOkEm is { } ok ? IdentidadeVisual.Horario(ok) : "nunca")}   ·   " +
            $"Falhas de consulta seguidas: {m.FalhasConsulta.ToString(ci)}   ·   Respostas em falhos: {m.BrutosFalhos.ToString(ci)}   ·   " +
            $"Documentos sem nota na janela: {m.OrfaosNaJanela.ToString(ci)}   ·   " +
            $"Notas com falhas de comunicação acima do alerta: {m.NotasComAlertaComunicacao.ToString(ci)}", medidor, larguraUtil));

        if (m.SemAtividade)
        {
            blocos.Add(LinhaTexto("Nenhuma nota em acompanhamento e nenhum movimento na janela.", medidor, larguraUtil, negrito: true));
        }
        else
        {
            blocos.Add(Titulo("Resumo"));
            blocos.Add(Cartoes(m, larguraUtil, ci));
        }

        var nota = new[] { "Nota", "Série", "Tipo", "Transporte / onda", "Pacote", "Idade", "Quando" };
        var larNota = new[] { 0.10, 0.06, 0.08, 0.24, 0.22, 0.10, 0.20 };
        string[] ColNota(LinhaNotaI60 n) => new[] { n.Numero, n.Serie, n.Tipo, string.IsNullOrEmpty(n.Onda) ? n.Transporte : n.Transporte + " / " + n.Onda, n.Pacote, n.Idade, n.Quando };

        Tabela(blocos, medidor, larguraUtil, "Aguardando separação", m.ListaAguardando, nota, larNota, ColNota, "Nenhuma nota aguardando.");
        Tabela(blocos, medidor, larguraUtil, "Separadas na janela", m.SeparadasNaJanela, nota, larNota, ColNota, "Nenhuma separação na janela.");
        Tabela(blocos, medidor, larguraUtil, "I60 pendentes de envio", m.PendentesI60, nota, larNota, ColNota, "Nenhuma I60 pendente de envio.");
        Tabela(blocos, medidor, larguraUtil, "I60 associadas", m.Associadas, nota, larNota, ColNota, "Nenhuma I60 associada na janela.");

        var prob = new[] { "Nota", "Tipo", "Pacote", "Operação", "Motivo", "Mensagem do WMS", "Ressalvas" };
        var larProb = new[] { 0.08, 0.06, 0.14, 0.07, 0.14, 0.31, 0.20 };
        string[] ColProb(LinhaNotaI60 n) => new[] { n.Numero, n.Tipo, n.Pacote, n.Operacao, n.Motivo, n.Mensagem, n.Ressalvas };
        Tabela(blocos, medidor, larguraUtil, "I60 com problema", m.Problemas, prob, larProb, ColProb, "Nenhuma I60 com problema.");
        Tabela(blocos, medidor, larguraUtil, "I60 enviadas aguardando confirmação", m.AguardandoConfirmacao, nota, larNota, ColNota, "Nenhuma I60 aguardando confirmação.");

        blocos.Add(Titulo($"Ajustes de quantidade ({m.Ajustes.Total.ToString(ci)})"));
        if (m.Ajustes.Total == 0)
        {
            blocos.Add(LinhaTexto("Nenhum ajuste de quantidade na janela.", medidor, larguraUtil));
        }
        else
        {
            var larAj = new[] { 0.14, 0.08, 0.28, 0.16, 0.16, 0.18 };
            AdicionarLinhas(blocos, medidor, larguraUtil, new[] { "Nota", "Item", "Produto", "Original", "Separada", "Diferença" }, larAj,
                m.Ajustes.Linhas.Select(a => new[] { a.Nota, a.Item.ToString(ci), a.Produto, Q(a.Original, ci), Q(a.Separada, ci), Q(a.Diferenca, ci) }).ToList(),
                m.Ajustes.NaoExibidas);
        }

        Tabela(blocos, medidor, larguraUtil, "Atrasadas", m.ListaAtrasadas, new[] { "Nota", "Série", "Tipo", "Transporte / onda", "Pacote", "Idade", "UNICO tentados" }, larNota,
            n => new[] { n.Numero, n.Serie, n.Tipo, string.IsNullOrEmpty(n.Onda) ? n.Transporte : n.Transporte + " / " + n.Onda, n.Pacote, n.Idade, n.Tentativas.ToString(ci) }, "Nenhuma nota atrasada.");

        blocos.Add(Titulo($"Visão por pacote ({m.PorPacote.Total.ToString(ci)})"));
        if (m.PorPacote.Total == 0)
        {
            blocos.Add(LinhaTexto("Nenhum pacote com pendência ou movimento.", medidor, larguraUtil));
        }
        else
        {
            AdicionarLinhas(blocos, medidor, larguraUtil,
                new[] { "Pacote", "Tipo (ondas)", "Notas", "Aguard.", "Atras.", "Separ.", "Enviad.", "Assoc.", "IMP", "Rejeit." },
                new[] { 0.19, 0.25, 0.07, 0.07, 0.07, 0.07, 0.08, 0.07, 0.06, 0.07 },
                m.PorPacote.Linhas.Select(p => new[]
                {
                    p.Pacote, string.IsNullOrEmpty(p.Ondas) ? p.Tipo : p.Tipo + " (" + p.Ondas + ")", p.Notas.ToString(ci), p.Aguardando.ToString(ci),
                    p.Atrasadas.ToString(ci), p.Separadas.ToString(ci), p.Enviadas.ToString(ci), p.Associadas.ToString(ci), p.Imp.ToString(ci), p.Rejeitadas.ToString(ci),
                }).ToList(),
                m.PorPacote.NaoExibidas);
        }

        if (m.MostrarCandidatasI40)
        {
            blocos.Add(Titulo($"Candidatas à devolução pela I40 - última alternativa ({m.CandidatasI40.Total.ToString(ci)})"));
            blocos.Add(LinhaTexto(
                m.DevolucaoDesligada
                    ? "A tela de devolução pela I40 está desligada (Devolucao:Habilitada=false)."
                    : "A tela de devolução pela I40 está habilitada.", medidor, larguraUtil));
            if (m.CandidatasI40.Total == 0)
            {
                blocos.Add(LinhaTexto("Nenhuma candidata.", medidor, larguraUtil));
            }
            else
            {
                AdicionarLinhas(blocos, medidor, larguraUtil, prob, larProb, m.CandidatasI40.Linhas.Select(ColProb).ToList(), m.CandidatasI40.NaoExibidas);
            }
        }

        return blocos;
    }

    private static void Tabela(
        List<Bloco> blocos, XGraphics medidor, double larguraUtil, string titulo, SecaoI60<LinhaNotaI60> secao, string[] cabecalhos,
        double[] proporcoes, Func<LinhaNotaI60, string[]> colunas, string vazio)
    {
        blocos.Add(Titulo($"{titulo} ({secao.Total.ToString(CultureInfo.InvariantCulture)})"));
        if (secao.Total == 0)
        {
            blocos.Add(LinhaTexto(vazio, medidor, larguraUtil));
            return;
        }

        AdicionarLinhas(blocos, medidor, larguraUtil, cabecalhos, proporcoes, secao.Linhas.Select(colunas).ToList(), secao.NaoExibidas);
    }

    private static void AdicionarLinhas(
        List<Bloco> blocos, XGraphics medidor, double larguraUtil, string[] cabecalhos, double[] proporcoes,
        List<string[]> linhas, int naoExibidas)
    {
        var larguras = proporcoes.Select(p => p * larguraUtil).ToArray();
        blocos.Add(CabecalhoTabela(cabecalhos, larguras));
        var zebra = false;
        foreach (var linha in linhas)
        {
            blocos.Add(LinhaTabela(linha, larguras, zebra, medidor));
            zebra = !zebra;
        }

        if (naoExibidas > 0)
        {
            blocos.Add(LinhaTexto($"e mais {naoExibidas.ToString(CultureInfo.InvariantCulture)} não exibidas.", medidor, larguraUtil));
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

    private static Bloco LinhaTexto(string texto, XGraphics medidor, double larguraUtil, bool negrito = false)
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

    private static Bloco Cartoes(ModeloRelatorioI60 m, double larguraUtil, CultureInfo ci)
    {
        var itens = new (string Rotulo, int Valor, XColor Cor)[]
        {
            ("Aguardando separação", m.Aguardando, CAzulClaro),
            ("Atrasadas", m.Atrasadas, CVermelho),
            ("Separadas (I60 por enviar)", m.SeparadasPendentesI60, CAzul),
            ("I60 associadas (janela)", m.AssociadasNaJanela, CVerde),
            ("I60 como IMP / ENC (janela)", m.ImpNaJanela, CAmbar),
            ("I60 aguardando confirmação", m.EnviadasAguardandoConfirmacao, CNeutro),
            ("Rejeitadas", m.Rejeitadas, CVermelho),
        };
        const double espaco = 6;
        var largura = (larguraUtil - espaco * (itens.Length - 1)) / itens.Length;
        return new Bloco
        {
            Altura = 56,
            Desenhar = (g, y) =>
            {
                for (var i = 0; i < itens.Length; i++)
                {
                    var x = Margem + i * (largura + espaco);
                    g.DrawRectangle(new XPen(CCinza, 0.75), x, y, largura, 48);
                    g.DrawRectangle(new XSolidBrush(itens[i].Cor), x, y, largura, 3.5);
                    g.DrawString(itens[i].Valor.ToString(ci), FNumero, new XSolidBrush(CAzul), x + 6, y + 8, XStringFormats.TopLeft);
                    g.DrawString(itens[i].Rotulo, FRotulo, new XSolidBrush(CTexto), x + 6, y + 33, XStringFormats.TopLeft);
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

    private static string Q(decimal v, CultureInfo ci) => v.ToString("0.####", ci);
}
