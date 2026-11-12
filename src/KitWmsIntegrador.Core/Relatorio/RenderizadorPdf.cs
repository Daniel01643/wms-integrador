using System.Globalization;
using KitWmsIntegrador.Core.Pacote;
using PdfSharpCore;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>
/// Renderiza o relatório em PDF A4 paisagem com PdfSharpCore: quebra de linha nas células,
/// cabeçalho da tabela repetido a cada página e rodapé "Página X de Y". O layout é calculado
/// em uma primeira passada (para conhecer o total de páginas) e desenhado na segunda.
/// </summary>
internal static class RenderizadorPdf
{
    private const double Margem = 30;
    private const double AlturaCabecalhoPagina = 62;
    private const double AlturaRodape = 28;
    private const double EntreLinhas = 10.5;
    private const double PaddingCelula = 3;

    // Fontes Arial com codificação Unicode (acentos e travessão).
    private static readonly XPdfFontOptions Unicode = new(PdfFontEncoding.Unicode);
    private static readonly XFont FNormal = new("Arial", 8, XFontStyle.Regular, Unicode);
    private static readonly XFont FNegrito = new("Arial", 8, XFontStyle.Bold, Unicode);
    private static readonly XFont FPequena = new("Arial", 7, XFontStyle.Regular, Unicode);
    private static readonly XFont FPequenaNegrito = new("Arial", 7, XFontStyle.Bold, Unicode);
    private static readonly XFont FTitulo = new("Arial", 17, XFontStyle.Bold, Unicode);
    private static readonly XFont FMarca = new("Arial", 22, XFontStyle.Bold, Unicode);
    private static readonly XFont FSecao = new("Arial", 12, XFontStyle.Bold, Unicode);
    private static readonly XFont FNumero = new("Arial", 16, XFontStyle.Bold, Unicode);

    private static readonly XColor CAzul = XColor.FromArgb(0x17, 0x47, 0x9E);
    private static readonly XColor CAzulClaro = XColor.FromArgb(0x54, 0xB6, 0xE7);
    private static readonly XColor CVerde = XColor.FromArgb(0x00, 0x9A, 0x83);
    private static readonly XColor CTexto = XColor.FromArgb(0x33, 0x33, 0x33);
    private static readonly XColor CCinza = XColor.FromArgb(0xC1, 0xC1, 0xC1);
    private static readonly XColor CAmbar = XColor.FromArgb(0xF5, 0xA6, 0x23);
    private static readonly XColor CVermelho = XColor.FromArgb(0xD3, 0x2F, 0x2F);
    private static readonly XColor CCinzaNeutra = XColor.FromArgb(0x6B, 0x72, 0x80);
    private static readonly XColor CGrupo = XColor.FromArgb(0xE8, 0xF4, 0xFB);

    // Colunas: Nota, Transporte, Horário, Status, ID de processamento, Mensagem, Integrada, Tent.
    private static readonly string[] Cabecalhos =
    {
        "Nota", "Transporte", "Tipo", "Origem", "Horário", "Status", "ID de processamento", "Mensagem", "Integrada", "Tent.",
    };

    private const int ColMensagem = 7;
    private static readonly double[] Larguras = { 52, 62, 38, 52, 78, 92, 98, 0, 42, 32 };
    private static readonly XColor CCinzaRaiz = XColor.FromArgb(0x6B, 0x72, 0x80);

    private sealed class Bloco
    {
        public double Altura;
        public Action<XGraphics, double> Desenhar = (_, _) => { };
        public bool ComProximo;
        public Bloco? Cabecalho;
    }

    public static byte[] Renderizar(
        ModeloRelatorio modelo, DateTimeOffset geradoEm, byte[]? logoPng, PeriodoColeta? periodo = null,
        DadosPacote? pacote = null)
    {
        using var documento = new PdfDocument();
        documento.Info.Title = IdentidadeVisual.Titulo;
        documento.Info.Creator = "KitWmsIntegrador";

        var larguraPagina = XUnit.FromMillimeter(297).Point;
        var alturaPagina = XUnit.FromMillimeter(210).Point;
        var larguraUtil = larguraPagina - 2 * Margem;
        var colunas = (double[])Larguras.Clone();
        colunas[ColMensagem] = larguraUtil - colunas.Sum();

        using var medidor = XGraphics.CreateMeasureContext(
            new XSize(larguraPagina, alturaPagina), XGraphicsUnit.Point, XPageDirection.Downwards);

        var blocos = MontarBlocos(modelo, medidor, larguraUtil, colunas);

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
                paginaVazia = true;
            }

            // Cabeçalho da tabela repetido no topo de cada página que continua a tabela.
            if (paginaVazia && b.Cabecalho is not null && !ReferenceEquals(b, b.Cabecalho))
            {
                atual.Add((b.Cabecalho, y));
                y += b.Cabecalho.Altura;
            }

            atual.Add((b, y));
            y += b.Altura;
        }

        // Passada 2: desenha, agora com o total de páginas conhecido.
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
            DesenharCabecalhoPagina(gfx, larguraPagina, geradoEm, logoPng, periodo);
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

    private static void DesenharCabecalhoPagina(
        XGraphics gfx, double larguraPagina, DateTimeOffset geradoEm, byte[]? logoPng, PeriodoColeta? periodo)
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

                gfx.DrawImage(img, Margem, Margem - 6 + (38 - altura) / 2, largura, altura);
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

        gfx.DrawString(IdentidadeVisual.Titulo, FTitulo, new XSolidBrush(CAzul), xTitulo, Margem - 3, XStringFormats.TopLeft);
        gfx.DrawString(
            "Gerado em: " + IdentidadeVisual.Horario(geradoEm)
            + RenderizadorHtml.TextoColeta(periodo),
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

    private static void DesenharRodape(
        XGraphics gfx, double larguraPagina, double alturaPagina, DateTimeOffset geradoEm, int numero, int total)
    {
        var yLinha = alturaPagina - Margem - 14;
        gfx.DrawLine(new XPen(CCinza, 0.75), Margem, yLinha, larguraPagina - Margem, yLinha);
        var cinza = new XSolidBrush(CTexto);
        gfx.DrawString(
            "Friozem · " + IdentidadeVisual.Titulo + " · " +
            geradoEm.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            FPequena, cinza, Margem, yLinha + 4, XStringFormats.TopLeft);
        var texto = $"Página {numero} de {total}";
        var largura = gfx.MeasureString(texto, FPequena).Width;
        gfx.DrawString(texto, FPequena, cinza, larguraPagina - Margem - largura, yLinha + 4, XStringFormats.TopLeft);
    }

    // ---------- Montagem dos blocos ----------

    private static List<Bloco> MontarBlocos(
        ModeloRelatorio modelo, XGraphics medidor, double larguraUtil, double[] colunas)
    {
        var blocos = new List<Bloco>();
        var r = modelo.Resumo;

        // Resumo: título + cartões + notas por transporte.
        blocos.Add(Titulo("Resumo", comProximo: true));
        blocos.Add(Cartoes(r, larguraUtil));
        var porTransporte = "Notas por transporte: " + string.Join("   ·   ",
            r.NotasPorTransporte.Select(n => $"{n.Transporte}: {IdentidadeVisual.Plural(n.Notas, "nota", "notas")}"));
        var porTipo = $"Por tipo: PUXADA {r.NotasPuxada}   ·   TSP {r.NotasTsp}   ·   Notas da raiz: {r.NotasRaiz}";
        if (r.NotasPorOnda is { Count: > 0 })
        {
            porTipo += "\nOndas da Puxada TSP: " + string.Join("   ·   ", r.NotasPorOnda.Select(o =>
                $"{o.Onda}: {IdentidadeVisual.Plural(o.Notas, "nota", "notas")}, {IdentidadeVisual.Plural(o.Transportes, "transporte", "transportes")}"));
        }

        var linhasPorte = TextoPdf.Quebrar(porTransporte, FNormal, larguraUtil, medidor)
            .Concat(porTipo.Split('\n').SelectMany(s => TextoPdf.Quebrar(s, FNormal, larguraUtil, medidor)))
            .ToList();
        blocos.Add(new Bloco
        {
            Altura = linhasPorte.Count * EntreLinhas + 8,
            Desenhar = (g, y) =>
            {
                for (var i = 0; i < linhasPorte.Count; i++)
                {
                    g.DrawString(linhasPorte[i], FNormal, new XSolidBrush(CTexto), Margem, y + 2 + i * EntreLinhas, XStringFormats.TopLeft);
                }
            },
        });

        // Produtos criados por contenção.
        if (modelo.ProdutosRevisao.Count > 0)
        {
            blocos.Add(Titulo("Produtos criados por contenção (revisar cadastro)", comProximo: true));
            var largs = new[] { 70.0, 80.0, larguraUtil - 150 };
            var cab = CabecalhoTabela(new[] { "Nota", "Transporte", "Produtos provisórios" }, largs);
            blocos.Add(cab);
            foreach (var p in modelo.ProdutosRevisao)
            {
                var linhas = TextoPdf.Quebrar(string.Join(", ", p.Produtos), FNormal, largs[2] - 2 * PaddingCelula, medidor);
                var altura = Math.Max(1, linhas.Count) * EntreLinhas + 2 * PaddingCelula;
                var nota = p.Nota;
                var transp = p.Transporte;
                blocos.Add(new Bloco
                {
                    Altura = altura,
                    Cabecalho = cab,
                    Desenhar = (g, y) =>
                    {
                        Texto(g, nota, FNormal, CTexto, Margem + PaddingCelula, y + PaddingCelula);
                        Texto(g, transp, FNormal, CTexto, Margem + largs[0] + PaddingCelula, y + PaddingCelula);
                        for (var i = 0; i < linhas.Count; i++)
                        {
                            Texto(g, linhas[i], FNormal, CTexto, Margem + largs[0] + largs[1] + PaddingCelula,
                                y + PaddingCelula + i * EntreLinhas);
                        }

                        Divisoria(g, y + altura, larguraUtil);
                    },
                });
            }
        }

        // Tabela principal agrupada por transporte.
        blocos.Add(Titulo("Notas por transporte", comProximo: true));
        var cabecalho = CabecalhoTabela(Cabecalhos, colunas);
        blocos.Add(cabecalho);
        foreach (var grupo in modelo.Grupos)
        {
            var texto = (grupo.EhTsp
                            ? $"Onda {grupo.Transporte} (TSP) — {IdentidadeVisual.Plural(grupo.Notas, "nota", "notas")}" +
                              $" · {IdentidadeVisual.Plural(grupo.Transportes, "transporte", "transportes")}"
                            : $"Transporte {grupo.Transporte} — {IdentidadeVisual.Plural(grupo.Notas, "nota", "notas")}") +
                        $" · {IdentidadeVisual.Plural(grupo.Integradas, "integrada", "integradas")}" +
                        $" · {grupo.ComProblema} com problema";
            blocos.Add(new Bloco
            {
                Altura = 16,
                ComProximo = true,
                Cabecalho = cabecalho,
                Desenhar = (g, y) =>
                {
                    g.DrawRectangle(new XSolidBrush(CGrupo), Margem, y, larguraUtil, 16);
                    g.DrawRectangle(new XSolidBrush(CAzulClaro), Margem, y, 3, 16);
                    Texto(g, texto, FNegrito, CAzul, Margem + 8, y + 3.5);
                },
            });

            foreach (var linha in grupo.Linhas)
            {
                blocos.Add(LinhaTabela(linha, medidor, colunas, cabecalho));
            }
        }

        return blocos;
    }

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

    private static Bloco Cartoes(ResumoRelatorio r, double larguraUtil)
    {
        var itens = new (string Rotulo, int Valor, XColor Cor)[]
        {
            ("Notas processadas", r.Total, CAzulClaro),
            ("Integradas", r.Integradas, CVerde),
            ("Integradas — revisar produto", r.IntegradasRevisarProduto, CVerde),
            ("Pendentes", r.Pendentes, CAmbar),
            ("Não integradas", r.NaoIntegradas, CVermelho),
            ("Arquivos ignorados", r.ArquivosIgnorados, CCinzaNeutra),
            ("Transportes", r.Transportes, CAzulClaro),
        };
        const double espaco = 8;
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
                    g.DrawString(itens[i].Valor.ToString(CultureInfo.InvariantCulture), FNumero,
                        new XSolidBrush(CAzul), x + 8, y + 8, XStringFormats.TopLeft);
                    g.DrawString(itens[i].Rotulo, FPequena, new XSolidBrush(CTexto), x + 8, y + 31, XStringFormats.TopLeft);
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
                Texto(g, titulos[i], FNegrito, XColors.White, x + PaddingCelula, y + 3.5);
                x += larguras[i];
            }
        },
    };

    private static Bloco LinhaTabela(LinhaRelatorioErro l, XGraphics medidor, double[] colunas, Bloco cabecalho)
    {
        var largura = (int i) => colunas[i] - 2 * PaddingCelula;
        var horario = IdentidadeVisual.Horario(l.HorarioErro);

        var cNota = TextoPdf.Quebrar(l.NumeroNota, FNormal, largura(0), medidor);
        var cTransp = TextoPdf.Quebrar(l.NumeroTransporte, FNormal, largura(1), medidor);
        var cTipo = TextoPdf.Quebrar(l.EhTsp ? "TSP" : "PUXADA", FNegrito, largura(2), medidor);
        var cOrigem = TextoPdf.Quebrar(string.IsNullOrWhiteSpace(l.Origem) ? "-" : l.Origem, l.EhRaiz ? FPequena : FNormal, largura(3), medidor);
        var cHorario = TextoPdf.Quebrar(horario, FNormal, largura(4), medidor);
        var cStatus = TextoPdf.Quebrar(l.StatusDescricao, FNegrito, largura(5) - 6, medidor);
        var cProdutos = l.RevisaoProdutoPendente
            ? TextoPdf.Quebrar("revisar: " + string.Join(", ", l.ProdutosProvisorios!), FPequena, largura(5), medidor)
            : new List<string>();

        var ids = new List<string>();
        if (!string.IsNullOrWhiteSpace(l.IdTransacao))
        {
            ids.Add("Transação: " + l.IdTransacao);
        }

        if (!string.IsNullOrWhiteSpace(l.IdDocumento))
        {
            ids.Add("Documento: " + l.IdDocumento);
        }

        if (ids.Count == 0)
        {
            ids.Add("-");
        }

        var cIds = ids.SelectMany(t => TextoPdf.Quebrar(t, FNormal, largura(6), medidor)).ToList();
        var cMsg = TextoPdf.Quebrar(l.UltimoErroResumido, FNormal, largura(ColMensagem), medidor);
        var cInt = new List<string> { l.EstaIntegrada ? "S" : "N" };
        var cTent = new List<string> { l.Tentativas.ToString(CultureInfo.InvariantCulture) };

        var linhasStatus = cStatus.Count * EntreLinhas + 4 + (cProdutos.Count > 0 ? cProdutos.Count * 9 + 2 : 0);
        var altura = new[]
        {
            cNota.Count * EntreLinhas, cTransp.Count * EntreLinhas, cTipo.Count * EntreLinhas, cOrigem.Count * EntreLinhas,
            cHorario.Count * EntreLinhas, linhasStatus, cIds.Count * EntreLinhas, cMsg.Count * EntreLinhas, EntreLinhas, EntreLinhas,
        }.Max() + 2 * PaddingCelula;

        var (corFundo, corTexto) = l.IntegradaRevisarProduto || l.EstaIntegrada ? (CVerde, XColors.White)
            : l.ArquivoIgnorado ? (CCinzaNeutra, XColors.White)
            : l.StatusEfetivo == StatusLinhaRelatorio.Pendente ? (CAmbar, XColor.FromArgb(0x33, 0x33, 0x33))
            : (CVermelho, XColors.White);

        return new Bloco
        {
            Altura = altura,
            Cabecalho = cabecalho,
            Desenhar = (g, y) =>
            {
                var x = Margem;
                var ty = y + PaddingCelula;
                void Coluna(int i, List<string> linhas, XFont fonte, XColor cor)
                {
                    for (var k = 0; k < linhas.Count; k++)
                    {
                        Texto(g, linhas[k], fonte, cor, x + PaddingCelula, ty + k * EntreLinhas);
                    }

                    x += colunas[i];
                }

                Coluna(0, cNota, FNormal, CTexto);
                Coluna(1, cTransp, FNormal, CTexto);
                Coluna(2, cTipo, FNegrito, l.EhTsp ? CAzul : CTexto);
                // Origem: "raiz" em cinza discreto; Puxada/PuxadaTSP em texto normal.
                Coluna(3, cOrigem, l.EhRaiz ? FPequena : FNormal, l.EhRaiz ? CCinzaRaiz : CTexto);
                Coluna(4, cHorario, FNormal, CTexto);

                // Status: etiqueta preenchida + marca discreta de produto a revisar.
                var alturaEtiqueta = cStatus.Count * EntreLinhas + 2;
                g.DrawRectangle(new XSolidBrush(corFundo), x + 2, ty - 1, colunas[5] - 4, alturaEtiqueta);
                for (var k = 0; k < cStatus.Count; k++)
                {
                    Texto(g, cStatus[k], FNegrito, corTexto, x + PaddingCelula + 2, ty + k * EntreLinhas);
                }

                for (var k = 0; k < cProdutos.Count; k++)
                {
                    Texto(g, cProdutos[k], FPequena, CAzul, x + PaddingCelula, ty + alturaEtiqueta + 2 + k * 9);
                }

                x += colunas[5];

                Coluna(6, cIds, FNormal, CTexto);
                Coluna(ColMensagem, cMsg, FNormal, CTexto);
                Coluna(8, cInt, FNormal, CTexto);
                Coluna(9, cTent, FNormal, CTexto);
                Divisoria(g, y + altura, colunas.Sum());
            },
        };
    }

    private static void Texto(XGraphics g, string texto, XFont fonte, XColor cor, double x, double y) =>
        g.DrawString(texto, fonte, new XSolidBrush(cor), x, y, XStringFormats.TopLeft);

    private static void Divisoria(XGraphics g, double y, double largura) =>
        g.DrawLine(new XPen(CCinza, 0.5), Margem, y, Margem + largura, y);
}
