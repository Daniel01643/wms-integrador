using KitWmsIntegrador.Core.Devolucao;
using KitWmsIntegrador.Tests.Infra;

namespace KitWmsIntegrador.Tests;

public sealed class ValidadorChavesDevolucaoTests
{
    [Fact]
    public void Normalizar_RemoveBrancosEspacosInternosEDuplicadas_PreservandoOrdem()
    {
        var r = ValidadorChavesDevolucao.Normalizar(new string?[]
        {
            " " + AmbienteDevolucao.Chave2 + " ", "", null, AmbienteDevolucao.Chave1,
            AmbienteDevolucao.Chave2, AmbienteDevolucao.Chave1[..22] + " " + AmbienteDevolucao.Chave1[22..],
        });

        Assert.Equal(new[] { AmbienteDevolucao.Chave2, AmbienteDevolucao.Chave1 }, r);
    }

    [Fact]
    public void ChavesDeTeste_TemDigitoVerificadorValido()
    {
        Assert.Null(LocalizadorXmlDevolucao.ValidarChave(AmbienteDevolucao.Chave1));
        Assert.Null(LocalizadorXmlDevolucao.ValidarChave(AmbienteDevolucao.Chave2));
    }

    [Fact]
    public void ValidarChave_DvErrado_EhRecusado()
    {
        var errada = AmbienteDevolucao.Chave1[..43] + (AmbienteDevolucao.Chave1[43] == '0' ? '1' : '0');

        Assert.Contains("verificador", LocalizadorXmlDevolucao.ValidarChave(errada));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("3525120183872300985055006015665807160449161A")]
    public void ValidarChave_TamanhoOuCaractereInvalido_EhRecusado(string chave)
    {
        Assert.NotNull(LocalizadorXmlDevolucao.ValidarChave(chave));
    }

    [Fact]
    public void NotaDaChave_ExtraiONumeroSemZerosAEsquerda()
    {
        Assert.Equal(AmbienteDevolucao.Nota1, ValidadorChavesDevolucao.NotaDaChave(AmbienteDevolucao.Chave1));
        Assert.Equal(string.Empty, ValidadorChavesDevolucao.NotaDaChave("abc"));
    }

    [Theory]
    [InlineData("epr7871", "EPR7871")]
    [InlineData("ABC-1D23", "ABC1D23")]
    [InlineData(" abc 1234 ", "ABC1234")]
    public void NormalizarPlaca_AceitaPlacasValidas(string entrada, string esperado)
    {
        Assert.Equal(esperado, ValidadorChavesDevolucao.NormalizarPlaca(entrada));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("AB1")]
    [InlineData("ABC123456")]
    [InlineData("ABC!234")]
    public void NormalizarPlaca_RecusaPlacasInvalidas(string? entrada)
    {
        Assert.Null(ValidadorChavesDevolucao.NormalizarPlaca(entrada));
    }
}
