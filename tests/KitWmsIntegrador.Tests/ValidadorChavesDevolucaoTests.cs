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

    [Fact]
    public void ExtracaoDaChave_CnpjSerieENumero_SaoOsDasPosicoesDoLayout()
    {
        // 35 2512 01838723009850 55 006 015665807 1 60449161 2
        Assert.Equal("01838723009850", ValidadorChavesDevolucao.CnpjDaChave(AmbienteDevolucao.Chave1));
        Assert.Equal(6, ValidadorChavesDevolucao.SerieDaChave(AmbienteDevolucao.Chave1));
        Assert.Equal(15665807, ValidadorChavesDevolucao.NumeroDaChave(AmbienteDevolucao.Chave1));
    }

    [Fact]
    public void ExtracaoDaChave_ChaveInvalida_DevolveVazioOuNulo()
    {
        Assert.Equal(string.Empty, ValidadorChavesDevolucao.CnpjDaChave("123"));
        Assert.Null(ValidadorChavesDevolucao.SerieDaChave("123"));
        Assert.Null(ValidadorChavesDevolucao.NumeroDaChave(AmbienteDevolucao.Chave1[..43] + "X"));
    }

    [Fact]
    public void ExtracaoDaChave_IgnoraZerosAEsquerda()
    {
        var chave = AmbienteDevolucao.ChaveDe(AmbienteDevolucao.CnpjDepositanteDigitos, 0, 12);

        Assert.Equal(0, ValidadorChavesDevolucao.SerieDaChave(chave));
        Assert.Equal(12, ValidadorChavesDevolucao.NumeroDaChave(chave));
        Assert.Equal("12", ValidadorChavesDevolucao.NotaDaChave(chave));
    }

    [Theory]
    [InlineData("15671289", "6", 15671289, 6)]
    [InlineData(" 015671289 ", "006", 15671289, 6)]
    [InlineData("1", "0", 1, 0)]
    [InlineData("999999999", "999", 999999999, 999)]
    public void ValidarNumeroSerie_Aceita(string numero, string serie, int numeroEsperado, int serieEsperada)
    {
        var erro = ValidadorChavesDevolucao.ValidarNumeroSerie(numero, serie, out var n, out var s);

        Assert.Null(erro);
        Assert.Equal(numeroEsperado, n);
        Assert.Equal(serieEsperada, s);
    }

    [Theory]
    [InlineData(null, "6")]
    [InlineData("", "6")]
    [InlineData("   ", "6")]
    [InlineData("1234567890", "6")]
    [InlineData("12a4", "6")]
    [InlineData("-5", "6")]
    [InlineData("15671289", null)]
    [InlineData("15671289", "")]
    [InlineData("15671289", "1234")]
    [InlineData("15671289", "6x")]
    [InlineData("1 5", "6")]
    public void ValidarNumeroSerie_Recusa(string? numero, string? serie)
    {
        Assert.NotNull(ValidadorChavesDevolucao.ValidarNumeroSerie(numero, serie, out _, out _));
    }

    [Fact]
    public void SoDigitos_RemovePontuacaoDoCnpj()
    {
        Assert.Equal("01838723009850", ValidadorChavesDevolucao.SoDigitos("01.838.723/0098-50"));
    }
}
