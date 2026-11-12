using KitWmsIntegrador.Core.Configuracao;

namespace KitWmsIntegrador.Tests;

public class OpcoesPastaValidacaoTests
{
    private static readonly string Raiz = Path.Combine(Path.GetTempPath(), "kitwms-validacao", "entrada");

    private static OpcoesPasta Op(string? estado = null, string? processados = null, string? erro = null) => new()
    {
        Entrada = Raiz,
        Estado = estado ?? "Estado",
        Processados = processados ?? "Processados",
        Erro = erro ?? "Erro",
    };

    [Fact]
    public void ConfiguracaoPadrao_ERelatorioForaDaEntrada_SemProblemas()
    {
        var relatorios = Path.Combine(Path.GetTempPath(), "kitwms-validacao", "relatorios");
        Assert.Empty(Op().Validar());
        Assert.Empty(Op().Validar(relatorios));
        Assert.Empty(Op().Validar("relatorios"));
    }

    [Theory]
    [InlineData("Processados")]
    [InlineData("Processados\\Estado")]
    [InlineData("Erro")]
    [InlineData("Erro\\Sub\\Estado")]
    public void Estado_IgualOuDentroDeProcessadosOuErro_EhRecusado(string estado)
    {
        var problemas = Op(estado: estado).Validar();

        Assert.Contains(problemas, p => p.Contains("Estado"));
    }

    [Theory]
    [InlineData("Processados")]
    [InlineData("Processados\\relatorios")]
    [InlineData("Erro")]
    [InlineData("Erro\\relatorios")]
    public void Relatorios_IgualOuDentroDeProcessadosOuErro_EhRecusado(string subpasta)
    {
        var relatorios = Path.Combine(Raiz, subpasta);

        var problemas = Op().Validar(relatorios);

        Assert.Contains(problemas, p => p.Contains("Relatorio"));
    }

    [Fact]
    public void Estado_AbsolutoDentroDeProcessadosCustomizado_EhRecusado()
    {
        var processados = Path.Combine(Path.GetTempPath(), "kitwms-validacao", "ok");
        var problemas = Op(estado: Path.Combine(processados, "x"), processados: processados).Validar();

        Assert.Contains(problemas, p => p.Contains("Estado"));
    }

    [Fact]
    public void Validar_SemParametro_ContinuaValidandoEstado()
    {
        Assert.NotEmpty(Op(estado: "Erro").Validar());
    }

    [Fact]
    public void Padroes_PuxadaPuxadaTspERaiz()
    {
        var o = Op();
        Assert.Equal("Puxada", o.Puxada);
        Assert.Equal("PuxadaTSP", o.PuxadaTsp);
        Assert.True(o.AceitarRaiz);
        Assert.Equal(Path.Combine(Raiz, "Puxada"), o.CaminhoPuxada);
        Assert.Equal(Path.Combine(Raiz, "PuxadaTSP"), o.CaminhoPuxadaTsp);
    }

    [Theory]
    [InlineData("", "PuxadaTSP")]
    [InlineData("Puxada", " ")]
    public void Puxadas_Vazias_SaoRecusadas(string puxada, string tsp)
    {
        var o = Op();
        o.Puxada = puxada;
        o.PuxadaTsp = tsp;
        Assert.Contains(o.Validar(), p => p.Contains("Puxada"));
    }

    [Theory]
    [InlineData("Puxada", "Puxada")]
    [InlineData("Puxada", "Puxada\\TSP")]
    [InlineData("Processados", "PuxadaTSP")]
    [InlineData("Puxada", "Erro")]
    [InlineData("Estado", "PuxadaTSP")]
    [InlineData("Puxada", "Processados\\TSP")]
    [InlineData(".", "PuxadaTSP")]
    public void Puxadas_IguaisOuAninhadas_SaoRecusadas(string puxada, string tsp)
    {
        var o = Op();
        o.Puxada = puxada;
        o.PuxadaTsp = tsp;
        Assert.NotEmpty(o.Validar());
    }
}
