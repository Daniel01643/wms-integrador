using System.Globalization;
using KitWmsIntegrador.Core.Monitoramento;
using PdfSharpCore;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>
/// Renderiza o RESUMO SEMANAL em PDF A4 paisagem com PdfSharpCore, seguindo o padrão de
/// <see cref="RenderizadorPdf"/>: duas passadas (medir/desenhar), cabeçalho com logo + barra +
/// título, rodapé "Página X de Y", fontes Arial Unicode e cores de <see cref="IdentidadeVisual"/>.
/// Desenha o período, cartões por status com %, tabelas de frequência (dia da semana e faixa),
/// performance, chamadas WMS, erros por categoria e saúde. Sem atividade: uma página de aviso.
/// </summary>
internal static class RenderizadorPdfResumo
{
    private const string TituloDocumento = "Resumo Semanal de Monitoramento";

    private const double Margem = 30;
    private const double AlturaCabecalhoPagina = 62;
    private const double AlturaRodape = 28;
    private const double EntreLinhas = 11;
    private const double PaddingCelula = 3;

    private static readonly XPdfFontOptions Unicode = new(PdfFontEncoding.Unicode);
    private static readonly XFont FNormal = new("Arial", 9, XFontStyle.Regular, Unicode);
    private static readonly XFont FNegrito = new("Arial", 9, XFontStyle.Bold, Unicode);
    private static readonly XFont FPequena = new("Arial", 7, XFontStyle.Regular, Unicode);
    private static readonly XFont FTitulo = new("Arial", 17, XFontStyle.Bold, Unicode);
    private static readonly XFont FMarca = new("Arial", 22, XFontStyle.Bold, Unicode);
    private static readonly XFont FSecao = new("Arial", 12, XFontStyle.Bold, Unicode);
    private static readonly XFont FNumero = new("Arial", 16, XFontStyle.Bold, Unicode);
    private static readonly XFont FRotulo = new("Arial", 7, XFontStyle.Regular, Unicode);

    private static readonly XColor CAzul = XColor.FromArgb(0x17, 0x47, 0x9E);
    private static readonly XColor CAzulClaro = XColor.FromArgb(0x54, 0xB6, 0xE7);
    private static readonly XColor CVerde = XColor.FromArgb(0x00, 0x9A, 0x83);
    private static readonly XColor CTexto = XColor.FromArgb(0x33, 0x33, 0x33);
    private static readonly XColor CCinza = XColor.FromArgb(0xC1, 0xC1, 0xC1);
    private static readonly XColor CAmbar = XColor.FromArgb(0xF5, 0xA6, 0x23);
    private static readonly XColor CVermelho = XColor.FromArgb(0xD3, 0x2F, 0x2F);
    private static readonly XColor CZebra = XColor.FromArgb(0xF5, 0xF9, 0xFD);

    private static readonly string[] DiasSemana =
    {
        "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado", "Domingo",
    };

    private static readonly DayOfWeek[] OrdemDias =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    };

    private sealed class Bloco
    {
        public double Altura;
        public Action<XGraphics, double> Desenhar = (_, _) => { };
        public bool ComProximo;
    }

    public static byte[] Renderizar(ResumoPeriodo resumo, string periodoLabel, byte[]? logoPng, DateTimeOffset geradoEm)
    {
        using var documento = new PdfDocument();
        documento.Info.Title = TituloDocumento;
        documento.Info.Creator = "KitWmsIntegrador";

        var larguraPagina = XUnit.FromMillimeter(297).Point;
        var alturaPagina = XUnit.FromMillimeter(210).Point;
        var larguraUtil = larguraPagina - 2 * Margem;

        using var medidor = XGraphics.CreateMeasureContext(
            new XSize(larguraPagina, alturaPagina), XGraphicsUnit.Point, XPageDirection.Downwards);

        var blocos = MontarBlocos(resumo, periodoLabel, medidor, larguraUtil);

        // Passada 1: distribui os blocos em páginas.
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

            var paginaVazia = atual.Count == 0;
            if (y + precisa > limite && !paginaVazia)
            {
                atual = new List<(Bloco, double)>();
                paginas.Add(atual);
                y = topo;
            }

            atual.Add((b, y));
            y += b.Altura;
        }

        // Passada 2: desenha com o total de páginas conhecido.
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
            DesenharCabecalhoPagina(gfx, larguraPagina, periodoLabel, logoPng);
            foreach (var (bloco, by) in paginas[p])
            {
                bloco.Desenhar(gfx, by);
            }

            DesenharRodape(gfx, larguraPagina, alturaPagina, geradoEm, p + 1, total);
        }

        using var ms = new MemoryStream();
        documento.Save(ms);
        return ms.ToArray();
    }

    // ---------- Cabeçalho / rodapé de página ----------

    private static void DesenharCabecalhoPagina(XGraphics gfx, double larguraPagina, string periodoLabel, byte[]? logoPng)
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

                // A logo é a versão negativa (branca): selo azul atrás dela para ter contraste.
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
        gfx.DrawString("Período: " + periodoLabel, FNormal, new XSolidBrush(CTexto), xTitulo, Margem + 18, XStringFormats.TopLeft);

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

    private static void DesenharRodape(
        XGraphics gfx, double larguraPagina, double alturaPagina, DateTimeOffset geradoEm, int numero, int total)
    {
        var yLinha = alturaPagina - Margem - 14;
        gfx.DrawLine(new XPen(CCinza, 0.75), Margem, yLinha, larguraPagina - Margem, yLinha);
        var cinza = new XSolidBrush(CTexto);
        gfx.DrawString(
            "Friozem · " + TituloDocumento + " · " +
            geradoEm.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            FPequena, cinza, Margem, yLinha + 4, XStringFormats.TopLeft);
        var texto = $"Página {numero} de {total}";
        var largura = gfx.MeasureString(texto, FPequena).Width;
        gfx.DrawString(texto, FPequena, cinza, larguraPagina - Margem - largura, yLinha + 4, XStringFormats.TopLeft);
    }

    // ---------- Montagem dos blocos ----------

    private static List<Bloco> MontarBlocos(ResumoPeriodo r, string periodoLabel, XGraphics medidor, double larguraUtil)
    {
        var ci = CultureInfo.InvariantCulture;
        var blocos = new List<Bloco>();

        if (r.SemAtividade)
        {
            blocos.Add(Titulo("Resumo do período", comProximo: true));
            blocos.Add(new Bloco
            {
                Altura = 40,
                Desenhar = (g, y) =>
                {
                    g.DrawRectangle(new XSolidBrush(CAmbar), Margem, y, 3, 30);
                    Texto(g, "Nenhuma atividade no período.", FNegrito, CTexto, Margem + 12, y + 8);
                },
            });
            AppendSaude(blocos, r, ci);
            return blocos;
        }

        // Cartões por status com %.
        blocos.Add(Titulo("Resumo por status", comProximo: true));
        blocos.Add(Cartoes(r, larguraUtil, ci));

        // Volume.
        blocos.Add(Titulo("Volume", comProximo: true));
        blocos.Add(LinhaTexto(
            $"Pacotes: {r.TotalPacotes.ToString(ci)}   ·   Média de notas por pacote: {r.MediaNotasPorPacote.ToString("0.0", ci)}" +
            $"   ·   Dias com atividade: {r.DiasComAtividade.ToString(ci)}", medidor, larguraUtil));

        // Frequência por dia da semana.
        blocos.Add(Titulo("Frequência por dia da semana", comProximo: true));
        var cabDia = CabecalhoTabela(new[] { "Dia", "Pacotes", "Notas" }, ColunasFreq(larguraUtil));
        blocos.Add(cabDia);
        foreach (var dia in OrdemDias)
        {
            var rotulo = DiasSemana[(int)dia == 0 ? 6 : (int)dia - 1];
            var pacotes = r.PacotesPorDiaSemana.GetValueOrDefault(dia);
            var notas = r.NotasPorDiaSemana.GetValueOrDefault(dia);
            blocos.Add(LinhaTabela(rotulo, pacotes, notas, larguraUtil, ci));
        }

        // Frequência por faixa.
        blocos.Add(Titulo("Frequência por faixa do dia", comProximo: true));
        var cabFaixa = CabecalhoTabela(new[] { "Faixa", "Pacotes", "Notas" }, ColunasFreq(larguraUtil));
        blocos.Add(cabFaixa);
        foreach (var (faixa, rotulo) in new[]
        {
            (FaixaHorario.Manha, "Manhã (06:00-11:59)"),
            (FaixaHorario.Tarde, "Tarde (12:00-17:59)"),
            (FaixaHorario.Noite, "Noite (18:00-05:59)"),
        })
        {
            blocos.Add(LinhaTabela(rotulo, r.PacotesPorFaixa.GetValueOrDefault(faixa),
                r.NotasPorFaixa.GetValueOrDefault(faixa), larguraUtil, ci));
        }

        // Performance.
        blocos.Add(Titulo("Performance", comProximo: true));
        blocos.Add(LinhaTexto(
            $"Tempo médio por nota: {FormatarMs(r.TempoMedioPorNotaMs, ci)}   ·   " +
            $"Tempo médio por pacote: {FormatarMs(r.TempoMedioPorPacoteMs, ci)}" +
            (r.TempoWmsMedioPorNotaMs > 0
                ? $"   ·   Integração no WMS por nota: {FormatarMs(r.TempoWmsMedioPorNotaMs, ci)}"
                : string.Empty), medidor, larguraUtil));

        // Chamadas WMS.
        blocos.Add(Titulo("Chamadas ao WMS", comProximo: true));
        var detalheWms = r.ChamadasWmsPorInterface.Count > 0
            ? " (" + string.Join(", ", r.ChamadasWmsPorInterface
                .OrderBy(k => k.Key, StringComparer.Ordinal)
                .Select(k => $"{k.Key}: {k.Value.ToString(ci)}")) + ")"
            : string.Empty;
        var textoWms = $"Total: {r.TotalChamadasWms.ToString(ci)}{detalheWms}" +
            (r.ChamadasWmsAproximado ? "  —  contagem aproximada (estimada pelo histórico)." : string.Empty);
        blocos.Add(LinhaTexto(textoWms, medidor, larguraUtil));

        // Notas por tipo (PUXADA x TSP) e as que vieram soltas na raiz.
        blocos.Add(Titulo("Notas por tipo", comProximo: true));
        var cabTipo = CabecalhoTabela(new[] { "Tipo", "Notas" }, ColunasErro(larguraUtil));
        blocos.Add(cabTipo);
        blocos.Add(LinhaDupla("PUXADA", r.NotasPuxada.ToString(ci), ColunasErro(larguraUtil)));
        blocos.Add(LinhaDupla("TSP", r.NotasTsp.ToString(ci), ColunasErro(larguraUtil)));
        blocos.Add(LinhaDupla("Da raiz (dentro da PUXADA)", r.NotasRaiz.ToString(ci), ColunasErro(larguraUtil)));
        // Erros por categoria.
        blocos.Add(Titulo("Erros por categoria", comProximo: true));
        if (r.ErrosPorCategoria.Count == 0)
        {
            blocos.Add(LinhaTexto("Nenhum erro no período.", medidor, larguraUtil));
        }
        else
        {
            var cabErro = CabecalhoTabela(new[] { "Categoria", "Ocorrências" }, ColunasErro(larguraUtil));
            blocos.Add(cabErro);
            foreach (var (categoria, qtd) in r.ErrosPorCategoria
                .OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal))
            {
                blocos.Add(LinhaDupla(categoria, qtd.ToString(ci), ColunasErro(larguraUtil)));
            }
        }

        AppendSaude(blocos, r, ci);
        return blocos;
    }

    private static void AppendSaude(List<Bloco> blocos, ResumoPeriodo r, CultureInfo ci)
    {
        blocos.Add(Titulo("Saúde do serviço", comProximo: true));
        var desde = r.ServicoDesde is { } d ? IdentidadeVisual.Horario(d) : "desconhecido";
        blocos.Add(new Bloco
        {
            Altura = 2 * EntreLinhas + 6,
            Desenhar = (g, y) =>
            {
                Texto(g, "No ar desde: " + desde, FNormal, CTexto, Margem, y + 2);
                Texto(g, "Inicializações no período: " + r.InicializacoesNoPeriodo.ToString(ci),
                    FNormal, CTexto, Margem, y + 2 + EntreLinhas);
            },
        });
    }

    private static double[] ColunasFreq(double larguraUtil) => new[] { larguraUtil - 240, 120.0, 120.0 };

    private static double[] ColunasErro(double larguraUtil) => new[] { larguraUtil - 140, 140.0 };

    private static Bloco Titulo(string texto, bool comProximo) => new()
    {
        Altura = 28,
        ComProximo = comProximo,
        Desenhar = (g, y) =>
        {
            Texto(g, texto, FSecao, CAzul, Margem, y + 4);
            DesenharBarra(g, Margem, y + 21);
        },
    };

    private static Bloco LinhaTexto(string texto, XGraphics medidor, double larguraUtil)
    {
        var linhas = TextoPdf.Quebrar(texto, FNormal, larguraUtil, medidor);
        return new Bloco
        {
            Altura = linhas.Count * EntreLinhas + 6,
            Desenhar = (g, y) =>
            {
                for (var i = 0; i < linhas.Count; i++)
                {
                    Texto(g, linhas[i], FNormal, CTexto, Margem, y + 2 + i * EntreLinhas);
                }
            },
        };
    }

    private static Bloco Cartoes(ResumoPeriodo r, double larguraUtil, CultureInfo ci)
    {
        var itens = new (string Rotulo, int Valor, double? Pct, XColor Cor)[]
        {
            ("Notas", r.TotalNotas, null, CAzulClaro),
            ("Integradas", r.Integradas, r.PercentualIntegradas, CVerde),
            ("Integradas — revisar produto", r.IntegradasRevisarProduto, r.PercentualIntegradasRevisarProduto, CVerde),
            ("Pendentes", r.Pendentes, r.PercentualPendentes, CAmbar),
            ("Não integradas", r.NaoIntegradas, r.PercentualNaoIntegradas, CVermelho),
            ("XMLs inválidos", r.XmlInvalido, r.PercentualXmlInvalido, CVermelho),
        };
        const double espaco = 8;
        var largura = (larguraUtil - espaco * (itens.Length - 1)) / itens.Length;
        return new Bloco
        {
            Altura = 62,
            Desenhar = (g, y) =>
            {
                for (var i = 0; i < itens.Length; i++)
                {
                    var x = Margem + i * (largura + espaco);
                    g.DrawRectangle(new XPen(CCinza, 0.75), x, y, largura, 54);
                    g.DrawRectangle(new XSolidBrush(itens[i].Cor), x, y, largura, 3.5);
                    g.DrawString(itens[i].Valor.ToString(ci), FNumero, new XSolidBrush(CAzul), x + 8, y + 8, XStringFormats.TopLeft);
                    if (itens[i].Pct is { } pct)
                    {
                        g.DrawString(Pct(pct, ci), FRotulo, new XSolidBrush(CTexto), x + 8, y + 30, XStringFormats.TopLeft);
                    }

                    g.DrawString(itens[i].Rotulo, FRotulo, new XSolidBrush(CTexto), x + 8, y + 40, XStringFormats.TopLeft);
                }
            },
        };
    }

    private static Bloco CabecalhoTabela(string[] titulos, double[] larguras) => new()
    {
        Altura = 16,
        ComProximo = true,
        Desenhar = (g, y) =>
        {
            g.DrawRectangle(new XSolidBrush(CAzul), Margem, y, larguras.Sum(), 16);
            var x = Margem;
            for (var i = 0; i < titulos.Length; i++)
            {
                var alinhaDireita = i > 0;
                var tx = alinhaDireita
                    ? x + larguras[i] - PaddingCelula - g.MeasureString(titulos[i], FNegrito).Width
                    : x + PaddingCelula;
                Texto(g, titulos[i], FNegrito, XColors.White, tx, y + 3.5);
                x += larguras[i];
            }
        },
    };

    private static Bloco LinhaTabela(string rotulo, int pacotes, int notas, double larguraUtil, CultureInfo ci) =>
        LinhaTripla(rotulo, pacotes.ToString(ci), notas.ToString(ci), ColunasFreq(larguraUtil));

    private static Bloco LinhaTripla(string c0, string c1, string c2, double[] larguras)
    {
        var zebra = false;
        return new Bloco
        {
            Altura = EntreLinhas + 2 * PaddingCelula,
            Desenhar = (g, y) =>
            {
                if (zebra)
                {
                    g.DrawRectangle(new XSolidBrush(CZebra), Margem, y, larguras.Sum(), EntreLinhas + 2 * PaddingCelula);
                }

                Texto(g, c0, FNormal, CTexto, Margem + PaddingCelula, y + PaddingCelula);
                TextoDireita(g, c1, FNormal, CTexto, Margem + larguras[0] + larguras[1] - PaddingCelula, y + PaddingCelula);
                TextoDireita(g, c2, FNormal, CTexto, Margem + larguras.Sum() - PaddingCelula, y + PaddingCelula);
                Divisoria(g, y + EntreLinhas + 2 * PaddingCelula, larguras.Sum());
            },
        };
    }

    private static Bloco LinhaDupla(string c0, string c1, double[] larguras)
    {
        var zebra = false;
        return new Bloco
        {
            Altura = EntreLinhas + 2 * PaddingCelula,
            Desenhar = (g, y) =>
            {
                if (zebra)
                {
                    g.DrawRectangle(new XSolidBrush(CZebra), Margem, y, larguras.Sum(), EntreLinhas + 2 * PaddingCelula);
                }

                Texto(g, c0, FNormal, CTexto, Margem + PaddingCelula, y + PaddingCelula);
                TextoDireita(g, c1, FNormal, CTexto, Margem + larguras.Sum() - PaddingCelula, y + PaddingCelula);
                Divisoria(g, y + EntreLinhas + 2 * PaddingCelula, larguras.Sum());
            },
        };
    }

    private static void Texto(XGraphics g, string texto, XFont fonte, XColor cor, double x, double y) =>
        g.DrawString(texto, fonte, new XSolidBrush(cor), x, y, XStringFormats.TopLeft);

    private static void TextoDireita(XGraphics g, string texto, XFont fonte, XColor cor, double xDireita, double y)
    {
        var largura = g.MeasureString(texto, fonte).Width;
        g.DrawString(texto, fonte, new XSolidBrush(cor), xDireita - largura, y, XStringFormats.TopLeft);
    }

    private static void Divisoria(XGraphics g, double y, double largura) =>
        g.DrawLine(new XPen(CCinza, 0.5), Margem, y, Margem + largura, y);

    private static string FormatarMs(long ms, CultureInfo ci)
    {
        if (ms < 1000)
        {
            return ms.ToString(ci) + " ms";
        }

        return (ms / 1000d).ToString("0.0", ci) + " s";
    }

    private static string Pct(double valor, CultureInfo ci) => valor.ToString("0.0", ci) + "%";
}
