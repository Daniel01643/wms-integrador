using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Tests.Infra;

namespace KitWmsIntegrador.Tests;

/// <summary>Classificação por pasta: raiz, Puxada e PuxadaTSP (tipo e origem decididos só pela pasta).</summary>
public sealed class ClassificarPastasTests : IDisposable
{
    private readonly AmbientePacote _amb = new();

    public void Dispose() => _amb.Dispose();

    private static string Nomes(IEnumerable<EntradaArquivo> e) =>
        string.Join("|", e.Select(x => Path.GetFileName(x.Caminho)).OrderBy(n => n, StringComparer.Ordinal));

    [Fact]
    public void TipoEOrigemVemDaPasta()
    {
        _amb.EscreverXml("solto.xml", "1", "T1");
        _amb.EscreverZip("", "solto.zip", ("a.xml", "<x/>"));
        _amb.EscreverZip("Puxada", "normal.zip", ("a.xml", "<x/>"));
        File.WriteAllText(Path.Combine(_amb.Pasta("Puxada"), "avulso.xml"), "<x/>");
        _amb.EscreverZip("PuxadaTSP", "XML'S SJC.zip", ("a.xml", "<x/>"));

        var r = ClassificadorEntrada.ClassificarPastas(_amb.Pastas);

        Assert.Empty(r.NaoXml);
        var porNome = r.Aceitos.ToDictionary(e => Path.GetFileName(e.Caminho));
        Assert.Equal(5, porNome.Count);
        Assert.Equal((TipoNota.Puxada, "raiz"), (porNome["solto.xml"].Tipo, porNome["solto.xml"].Origem));
        Assert.Equal((TipoNota.Puxada, "raiz"), (porNome["solto.zip"].Tipo, porNome["solto.zip"].Origem));
        Assert.Equal((TipoNota.Puxada, "Puxada"), (porNome["normal.zip"].Tipo, porNome["normal.zip"].Origem));
        Assert.Equal((TipoNota.Puxada, "Puxada"), (porNome["avulso.xml"].Tipo, porNome["avulso.xml"].Origem));
        Assert.Equal((TipoNota.Tsp, "PuxadaTSP"), (porNome["XML'S SJC.zip"].Tipo, porNome["XML'S SJC.zip"].Origem));
        Assert.True(porNome["XML'S SJC.zip"].EhZip);
    }

    [Fact]
    public void RaizAceita_PorPadrao_ERecusadaQuandoDesligada()
    {
        _amb.EscreverXml("solto.xml", "1", "T1");
        _amb.EscreverNaoXml("nota.pdf");

        var ligada = ClassificadorEntrada.ClassificarPastas(_amb.Pastas);
        Assert.Equal("solto.xml", Nomes(ligada.Aceitos));
        Assert.Equal("nota.pdf", Nomes(ligada.NaoXml));

        _amb.Pastas.AceitarRaiz = false;
        var desligada = ClassificadorEntrada.ClassificarPastas(_amb.Pastas);
        Assert.Empty(desligada.Aceitos);
        Assert.Empty(desligada.NaoXml);
    }

    [Fact]
    public void XmlSoltoNaPuxadaTsp_EhAceito_OutroArquivoViraNaoXml()
    {
        File.WriteAllText(Path.Combine(_amb.Pasta("PuxadaTSP"), "solto.xml"), "<x/>");
        File.WriteAllText(Path.Combine(_amb.Pasta("PuxadaTSP"), "nota.pdf"), "x");
        _amb.EscreverZip("PuxadaTSP", "ok.zip", ("a.xml", "<x/>"));

        var r = ClassificadorEntrada.ClassificarPastas(_amb.Pastas);

        Assert.Equal("ok.zip|solto.xml", Nomes(r.Aceitos));
        Assert.All(r.Aceitos, e => Assert.Equal((TipoNota.Tsp, "PuxadaTSP"), (e.Tipo, e.Origem)));
        Assert.Equal("nota.pdf", Nomes(r.NaoXml));
        Assert.All(r.NaoXml, e => Assert.Equal(TipoNota.Tsp, e.Tipo));
    }

    [Fact]
    public void Temporarios_FicamDeFora_ENadaEhLidoDeProcessadosErroEstado()
    {
        File.WriteAllText(Path.Combine(_amb.Pasta("Puxada"), "copiando.zip.part"), "x");
        _amb.EscreverZip("Processados", "velho.zip", ("a.xml", "<x/>"));
        _amb.EscreverZip("Erro", "erro.zip", ("a.xml", "<x/>"));
        _amb.EscreverZip("Estado", "estado.zip", ("a.xml", "<x/>"));
        File.WriteAllText(Path.Combine(_amb.Pasta("Processados\\pacote_1"), "a.xml"), "<x/>");

        var r = ClassificadorEntrada.ClassificarPastas(_amb.Pastas);

        Assert.Empty(r.Aceitos);
        Assert.Empty(r.NaoXml);
    }

    [Fact]
    public void NomesDasPastasVemDaConfiguracao()
    {
        _amb.Pastas.Puxada = "Normais";
        _amb.Pastas.PuxadaTsp = "Tsp";
        _amb.EscreverZip("Normais", "a.zip", ("a.xml", "<x/>"));
        _amb.EscreverZip("Tsp", "b.zip", ("a.xml", "<x/>"));

        var r = ClassificadorEntrada.ClassificarPastas(_amb.Pastas);

        Assert.Equal("Normais", r.Aceitos.Single(e => e.Caminho.EndsWith("a.zip")).Origem);
        Assert.Equal("Tsp", r.Aceitos.Single(e => e.Caminho.EndsWith("b.zip")).Origem);
    }
}