using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Relatorio;

namespace KitWmsIntegrador.Tests;

public class PoliticaEnvioEmailTests
{
    private static ResumoRelatorio Resumo(int integ = 0, int revisar = 0, int pend = 0, int nao = 0) =>
        new(integ + pend + nao, integ, revisar, pend, nao, 0, 1, new List<(string, int)>());

    private static OpcoesEmail Op(EnviarQuando q) => new() { EnviarQuando = q };

    [Fact]
    public void ComProblema_SoIntegradas_NaoEnvia() =>
        Assert.False(PoliticaEnvioEmail.Deve(Resumo(integ: 3), 0, Op(EnviarQuando.ComProblema)));

    [Fact]
    public void PadraoDasOpcoes_ESempre() =>
        Assert.Equal(EnviarQuando.Sempre, new OpcoesEmail().EnviarQuando);

    [Theory]
    [InlineData(0, 0, 1, 0)]
    [InlineData(0, 1, 0, 0)]
    [InlineData(1, 0, 0, 0)]
    [InlineData(0, 0, 0, 1)]
    public void ComProblema_EnviaQuandoHaProblema(int nao, int pend, int revisar, int xmlsInvalidos) =>
        Assert.True(PoliticaEnvioEmail.Deve(Resumo(integ: 2, revisar: revisar, pend: pend, nao: nao), xmlsInvalidos, Op(EnviarQuando.ComProblema)));

    [Fact]
    public void Sempre_EnviaMesmoSemProblema() =>
        Assert.True(PoliticaEnvioEmail.Deve(Resumo(integ: 1), 0, Op(EnviarQuando.Sempre)));

    [Fact]
    public void Nunca_NaoEnviaMesmoComProblema() =>
        Assert.False(PoliticaEnvioEmail.Deve(Resumo(nao: 5), 2, Op(EnviarQuando.Nunca)));
}
