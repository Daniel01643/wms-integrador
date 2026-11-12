using KitWmsIntegrador.Core.Relatorio;
using Xunit;

namespace KitWmsIntegrador.Tests;

public class FormatadorDuracaoTests
{
    [Theory]
    [InlineData(-5, "0 ms")]
    [InlineData(0, "0 ms")]
    [InlineData(1, "1 ms")]
    [InlineData(850, "850 ms")]
    [InlineData(999, "999 ms")]
    [InlineData(1000, "1,0 s")]
    [InlineData(2500, "2,5 s")]
    [InlineData(9949, "9,9 s")]
    [InlineData(9950, "10 s")]
    [InlineData(45_000, "45 s")]
    [InlineData(59_499, "59 s")]
    [InlineData(59_500, "1 min")]
    [InlineData(60_000, "1 min")]
    [InlineData(85_000, "1 min 25 s")]
    [InlineData(120_000, "2 min")]
    [InlineData(3_600_000, "1 h 00 min")]
    [InlineData(3_905_000, "1 h 05 min")]
    public void Formatar_DevolveTextoLegivel(long ms, string esperado)
    {
        Assert.Equal(esperado, FormatadorDuracao.Formatar(ms));
    }

    [Fact]
    public void Inteiro_UsaPontoComoMilhar()
    {
        Assert.Equal("1.234", FormatadorDuracao.Inteiro(1234));
        Assert.Equal("12", FormatadorDuracao.Inteiro(12));
    }

    [Fact]
    public void Numero_UsaVirgulaDecimalEPontoMilhar()
    {
        Assert.Equal("1.234,5", FormatadorDuracao.Numero(1234.5, 1));
        Assert.Equal("0,8", FormatadorDuracao.Numero(0.8, 1));
    }
}
