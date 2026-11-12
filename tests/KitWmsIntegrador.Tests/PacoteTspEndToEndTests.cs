using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Tests.Infra;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Ponta a ponta da Puxada e da Puxada TSP: pastas Puxada e PuxadaTSP com ZIPs SINTÉTICOS (SJC com
/// 2 notas/2 transportes, SOROCABA com 5 notas/4 transportes), XML solto na raiz e na Puxada,
/// GerenciadorPacote real, relatório HTML/PDF reais e e-mail gravador. Sem rede, sem dados reais.
/// </summary>
public sealed class PacoteTspEndToEndTests : IDisposable
{
    private readonly AmbientePacote _amb = new();
    private readonly OrquestradorFalso _orq = new();

    public void Dispose() => _amb.Dispose();

    private string Data => _amb.Clock.UtcNow.ToLocalTime().ToString("yyyyMMdd");

    private string[] Sobras(string subpasta)
    {
        var pasta = string.IsNullOrEmpty(subpasta) ? _amb.Entrada : Path.Combine(_amb.Entrada, subpasta);
        return Directory.Exists(pasta) ? Directory.GetFiles(pasta, "*", SearchOption.TopDirectoryOnly) : Array.Empty<string>();
    }

    private string[] Todos(string subpasta) =>
        Directory.Exists(Path.Combine(_amb.Entrada, subpasta))
            ? Directory.GetFiles(Path.Combine(_amb.Entrada, subpasta), "*", SearchOption.AllDirectories)
            : Array.Empty<string>();

    private void PrepararCenario()
    {
        // TSP: dois ZIPs sintéticos.
        _amb.EscreverZipBrf("PuxadaTSP", "XML'S SJC.zip", 1001, "1000000001", "1000000002");
        _amb.EscreverZipBrf("PuxadaTSP", "XML'S SOROCABA.zip", 2001,
            "1000000001", "1000000002", "1000000003", "1000000004", "1000000001");
        File.WriteAllText(Path.Combine(_amb.Pasta("PuxadaTSP"), "esquecido.pdf"), "x");

        // Puxada normal: um ZIP e um XML avulso; raiz: um XML solto.
        _amb.EscreverZipBrf("Puxada", "Entregas.zip", 3001, "1000000500", "1000000501");
        File.WriteAllText(Path.Combine(_amb.Pasta("Puxada"), AmbientePacote.NomeXmlBrf(3101)), AmbientePacote.XmlBrf(3101, "1000000500"));
        File.WriteAllText(Path.Combine(_amb.Entrada, AmbientePacote.NomeXmlBrf(3201)), AmbientePacote.XmlBrf(3201, "1000000600"));
    }

    [Fact]
    public async Task PuxadaETsp_FecharPacote_ComTipoOrigemOndaNoRelatorioENoEmail()
    {
        PrepararCenario();
        var g = _amb.CriarGerenciador(_orq);

        await _amb.RodarAteOciosoAsync(g);

        // Orquestrador: TSP -> uma onda por ZIP (nome na ordem de chegada); PUXADA -> onda = transporte.
        var ondaSjc = $"TSP-{Data}-01-SJC";
        var ondaSoro = $"TSP-{Data}-02-SOROCABA";
        Assert.Equal(2 + 5 + 2 + 1 + 1, _orq.Chamadas.Count);
        Assert.Equal(2, _orq.Chamadas.Count(c => c.Transporte == ondaSjc));
        Assert.Equal(5, _orq.Chamadas.Count(c => c.Transporte == ondaSoro));
        var normais = _orq.Chamadas.Where(c => !c.Transporte.StartsWith("TSP-")).ToList();
        Assert.Equal(
            new[] { "1000000500", "1000000501", "1000000500", "1000000600" }.OrderBy(t => t),
            normais.Select(c => c.Transporte).OrderBy(t => t));

        // Nada sobra nas pastas de depósito; o layout Processados\pacote_*\ é mantido.
        Assert.Empty(Sobras(""));
        Assert.Empty(Sobras("Puxada"));
        Assert.Empty(Sobras("PuxadaTSP"));
        var processados = Todos("Processados");
        Assert.Equal(3, processados.Count(a => a.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2 + 5 + 2 + 1 + 1, processados.Count(a => a.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)));
        Assert.All(processados, a => Assert.StartsWith("pacote_", Path.GetFileName(Path.GetDirectoryName(a))));
        Assert.Contains(Todos("Erro"), a => a.EndsWith("esquecido.pdf"));
        var tmp = Path.Combine(_amb.Pastas.CaminhoEstado, "tmp");
        Assert.True(!Directory.Exists(tmp) || Directory.GetFileSystemEntries(tmp).Length == 0);

        // Relatório HTML e PDF.
        var html = File.ReadAllText(Assert.Single(_amb.RelatoriosGerados(".html")));
        Assert.Contains("<th>Tipo</th>", html);
        Assert.Contains("<th>Origem</th>", html);
        Assert.Contains($"Onda {ondaSoro} (TSP) — 5 notas · 4 transportes", html);
        Assert.Contains($"Onda {ondaSjc} (TSP) — 2 notas · 2 transportes", html);
        Assert.Contains("Transporte 1000000500", html);
        Assert.Equal(1, Contar(html, "<span class=\"raiz\">raiz</span>"));
        Assert.Contains(">Puxada<", html);
        Assert.Contains(">PuxadaTSP<", html);
        Assert.Contains("Notas da raiz: 1", html);
        var pdf = File.ReadAllBytes(Assert.Single(_amb.RelatoriosGerados(".pdf")));
        var textoPdf = PdfTextoHelper.Texto(pdf);
        Assert.Contains(ondaSoro, textoPdf);
        Assert.Contains("Origem", textoPdf);

        // E-mail de fechamento.
        var email = Assert.Single(_amb.Email.Enviadas);
        Assert.Contains($"{ondaSoro}: 5 notas, 4 transportes", email.CorpoHtml);
        Assert.Contains($"{ondaSjc}: 2 notas, 2 transportes", email.CorpoHtml);
        Assert.Contains("Notas da raiz", email.CorpoHtml);
        Assert.Contains("Ondas TSP", email.CorpoHtml);
        Assert.Equal(EstadoPacote.Ocioso, g.Estado);
    }

    [Fact]
    public async Task ZipInvalidoENaoXml_AparecemNoRelatorio_SemChamarOWms()
    {
        File.WriteAllBytes(Path.Combine(_amb.Pasta("PuxadaTSP"), "quebrado.zip"), new byte[] { 1, 2, 3, 4, 5 });
        _amb.EscreverZip("Puxada", "so-txt.zip", ("leia-me.txt", "oi"));
        var g = _amb.CriarGerenciador(_orq);

        await _amb.RodarAteOciosoAsync(g);

        Assert.Empty(_orq.Chamadas);
        Assert.Equal(2, Todos("Erro").Count(a => a.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)));
        Assert.All(
            Todos("Erro").Where(a => a.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)),
            a => Assert.Equal("ZipInvalido", Path.GetFileName(Path.GetDirectoryName(a))));
        var html = File.ReadAllText(Assert.Single(_amb.RelatoriosGerados(".html")));
        Assert.Contains("quebrado.zip", html);
        Assert.Contains("so-txt.zip", html);
        Assert.Contains("ZIP inválido", html);
    }

    [Fact]
    public async Task RaizRecusada_DeixaOArquivoSoltoParado_ENaoMexeNasPastas()
    {
        _amb.Pastas.AceitarRaiz = false;
        var solto = Path.Combine(_amb.Entrada, AmbientePacote.NomeXmlBrf(3201));
        File.WriteAllText(solto, AmbientePacote.XmlBrf(3201, "1000000600"));
        _amb.EscreverZipBrf("Puxada", "Entregas.zip", 3001, "1000000500");
        var g = _amb.CriarGerenciador(_orq);

        await _amb.RodarAteOciosoAsync(g);

        Assert.True(File.Exists(solto));
        Assert.Single(_orq.Chamadas);
        Assert.Empty(Sobras("Puxada"));
        var html = File.ReadAllText(Assert.Single(_amb.RelatoriosGerados(".html")));
        Assert.Contains("Notas da raiz: 0", html);
    }

    [Fact]
    public async Task ReprocessoDoMesmoZip_EmOutroPacote_ReaproveitaAOnda()
    {
        var zip = _amb.EscreverZipBrf("PuxadaTSP", "XML'S SJC.zip", 1001, "1000000001", "1000000002");
        var copia = Path.Combine(_amb.Raiz, "copia.zip");
        File.Copy(zip, copia);
        var g = _amb.CriarGerenciador(_orq);

        await _amb.RodarAteOciosoAsync(g);
        File.Copy(copia, zip);
        _amb.Clock.Avancar(TimeSpan.FromMinutes(5));
        await _amb.RodarAteOciosoAsync(g);

        var onda = $"TSP-{Data}-01-SJC";
        Assert.Equal(4, _orq.Chamadas.Count);
        Assert.All(_orq.Chamadas, c => Assert.Equal(onda, c.Transporte));
        Assert.Equal(2, _amb.RelatoriosGerados(".html").Length);
    }

    private static int Contar(string texto, string trecho)
    {
        var total = 0;
        var i = 0;
        while ((i = texto.IndexOf(trecho, i, StringComparison.Ordinal)) >= 0)
        {
            total++;
            i += trecho.Length;
        }

        return total;
    }
}
