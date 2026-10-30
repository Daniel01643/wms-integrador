using KitWmsIntegrador.Core.Leitura;

namespace KitWmsIntegrador.Tests;

public class ExtratorTransporteTests
{
    [Fact]
    public void Extrair_ComPadrao_PreservaZerosEsquerda()
    {
        var texto = "VD: 00842983 Transporte: 0130693549 ** Trib aprox.";

        var resultado = ExtratorTransporte.Extrair(texto);

        Assert.Equal("0130693549", resultado);
    }

    [Fact]
    public void Extrair_SemEspacoAposDoisPontos_AindaCasa()
    {
        var resultado = ExtratorTransporte.Extrair("Transporte:0130693549");

        Assert.Equal("0130693549", resultado);
    }

    [Fact]
    public void Extrair_TextoSemPadrao_RetornaNull()
    {
        var resultado = ExtratorTransporte.Extrair("PED CLI: 0774534 sem transporte aqui");

        Assert.Null(resultado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Extrair_EntradaVaziaOuNula_RetornaNull(string? entrada)
    {
        Assert.Null(ExtratorTransporte.Extrair(entrada));
    }
}
