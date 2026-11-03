using KitWmsIntegrador.Core.Construtores;

namespace KitWmsIntegrador.Tests;

public class FormatoWmsTests
{
    [Fact]
    public void Truncar_Nulo_RetornaVazio()
    {
        Assert.Equal(string.Empty, FormatoWms.Truncar(null, 10));
    }

    [Fact]
    public void Truncar_StringCurta_Inalterada()
    {
        Assert.Equal("abc", FormatoWms.Truncar("abc", 10));
    }

    [Fact]
    public void Truncar_StringLonga_CortaAoLimitePreservandoInicio()
    {
        var resultado = FormatoWms.Truncar("abcdefghij", 4);

        Assert.Equal("abcd", resultado);
        Assert.Equal(4, resultado.Length);
    }

    [Fact]
    public void Atributo3_ValoresPresentes_FormataComRotulosEVirgulaDecimal()
    {
        var texto = FormatoWms.Atributo3(12644.40m, 302.675m, 275.640m, "36");

        Assert.Contains("Vlr: 12.644,40", texto);
        Assert.Contains("PB: 302,675", texto);
        Assert.Contains("PL: 275,640", texto);
        Assert.Contains("Vol: 36", texto);
    }

    [Fact]
    public void Atributo3_ValoresNulos_OmiteRotulos()
    {
        var texto = FormatoWms.Atributo3(10m, null, null, null);

        Assert.Contains("Vlr:", texto);
        Assert.DoesNotContain("PB:", texto);
        Assert.DoesNotContain("PL:", texto);
        Assert.DoesNotContain("Vol:", texto);
    }
}
