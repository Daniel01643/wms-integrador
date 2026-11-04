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
}
