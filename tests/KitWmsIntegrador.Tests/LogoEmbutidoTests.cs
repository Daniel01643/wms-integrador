using KitWmsIntegrador.Core.Relatorio;
using Xunit;

namespace KitWmsIntegrador.Tests;

/// <summary>Logo Friozem embutida no assembly e a ordem de resolução LogoPath -> embutida -> texto.</summary>
public class LogoEmbutidoTests
{
    private static (int Largura, int Altura) Dimensoes(byte[] png) =>
        (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
         System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));

    [Fact]
    public void RecursoEmbutido_EPngPequenoEnegativo560x313()
    {
        var logo = IdentidadeVisual.LogoEmbutidoPng;
        Assert.NotNull(logo);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, logo!.Take(8).ToArray());
        Assert.Equal((560, 313), Dimensoes(logo));
        Assert.True(logo.Length < 100 * 1024, "logo embutida deve ser pequena");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("caminho-inexistente.png")]
    public void ObterLogo_SemCaminhoValido_DevolveAEmbutida(string? caminho)
    {
        Assert.Same(IdentidadeVisual.LogoEmbutidoPng, IdentidadeVisual.ObterLogo(caminho));
    }

    [Fact]
    public void ObterLogo_ArquivoInvalido_DevolveAEmbutida()
    {
        var arq = Path.Combine(Path.GetTempPath(), "logo-invalido-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllText(arq, "isto nao e um png");
            Assert.Same(IdentidadeVisual.LogoEmbutidoPng, IdentidadeVisual.ObterLogo(arq));
        }
        finally
        {
            File.Delete(arq);
        }
    }

    [Fact]
    public void ObterLogo_PngValido_DevolveOdoArquivo()
    {
        var arq = Path.Combine(Path.GetTempPath(), "logo-ok-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var bytes = IdentidadeVisual.LogoEmbutidoPng!.ToArray();
            bytes[^1] ^= 0x01; // diferente da embutida, mas ainda com a assinatura PNG
            File.WriteAllBytes(arq, bytes);
            Assert.Equal(bytes, IdentidadeVisual.ObterLogo(arq));
        }
        finally
        {
            File.Delete(arq);
        }
    }

    private static ResumoRelatorio Resumo(int puxada, int tsp) =>
        new(puxada + tsp, puxada + tsp, 0, 0, 0, 0, 0, Array.Empty<(string, int)>(), puxada, tsp);

    [Fact]
    public void RotuloTipoPacote_TresCasos_ESemNotas()
    {
        Assert.Equal("PUXADA", IdentidadeVisual.RotuloTipoPacote(Resumo(3, 0)));
        Assert.Equal("TSP", IdentidadeVisual.RotuloTipoPacote(Resumo(0, 4)));
        Assert.Equal("PUXADA + TSP", IdentidadeVisual.RotuloTipoPacote(Resumo(2, 2)));
        Assert.Equal("PUXADA", IdentidadeVisual.RotuloTipoPacote(Resumo(0, 0)));
    }

    [Fact]
    public void Titulo_EORelatorioDeIntegracao()
    {
        Assert.Equal("Relatório de Integração", IdentidadeVisual.Titulo);
    }
}
