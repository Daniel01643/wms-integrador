using KitWmsIntegrador.Core.Configuracao;

namespace KitWmsIntegrador.Tests;

public class OpcoesZipTests
{
    [Fact]
    public void Padroes_Seguros()
    {
        var o = new OpcoesZip();
        Assert.Equal(1000, o.MaxArquivos);
        Assert.Equal(500, o.MaxTamanhoMb);
        Assert.Empty(o.Validar());
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(10, 0)]
    [InlineData(10, -5)]
    public void Limites_NaoPositivos_SaoRecusados(int arquivos, int mb)
    {
        var o = new OpcoesZip { MaxArquivos = arquivos, MaxTamanhoMb = mb };
        Assert.NotEmpty(o.Validar());
    }
}