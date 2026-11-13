using KitWmsIntegrador.Core.Configuracao;
using Microsoft.Extensions.Configuration;
namespace KitWmsIntegrador.Tests;

/// <summary>
/// Valores do appsettings.json REAL do Worker na release 2026.11.13: defaults da I60 validados no
/// homolog, flags de ligação desligadas, placa (Puxada com placa, TSP sem) e retenção de 30 dias.
/// Os padrões das classes de opções não mudam; por isso o teste lê o arquivo versionado.
/// </summary>
public class ConfiguracaoReleaseTests
{
    private static IConfigurationRoot Config() =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
            .Build();

    [Fact]
    public void I60_QtdeVolumeZeroEQtdeTotalNotaZero_LigadosNoAppsettings()
    {
        var opcoes = new OpcoesI60();
        Config().GetSection(OpcoesI60.Secao).Bind(opcoes);
        Assert.True(opcoes.QtdeVolumeZero);
        Assert.True(opcoes.QtdeTotalNotaZero);
    }

    [Fact]
    public void I51_I60_Devolucao_ContinuamDesligadosNosArquivosVersionados()
    {
        var config = Config();
        Assert.False(config.GetValue<bool>("I51:Habilitada"));
        Assert.False(config.GetValue<bool>("I60:Habilitada"));
        Assert.False(config.GetValue<bool>("Devolucao:Habilitada"));
    }

    [Fact]
    public void Retencao_30Dias_NoAppsettings()
    {
        var config = Config();
        Assert.Equal(30, config.GetValue<int>("Limpeza:DiasRetencao"));
        Assert.Equal(30, config.GetValue<int>("I51:DiasRetencaoEstado"));
    }

    [Fact]
    public void Retencao_PadroesDasOpcoes_Sao30Dias()
    {
        Assert.Equal(30, new OpcoesLimpeza().DiasRetencao);
        Assert.Equal(30, new OpcoesI51().DiasRetencaoEstado);
    }

    [Fact]
    public void Placa_PuxadaComPlaca_TspSemPlaca_NoAppsettings()
    {
        var config = Config();
        Assert.True(config.GetValue<bool>("I50:EnviarPlacaVeiculo"));
        Assert.False(config.GetValue<bool>("I50:EnviarPlacaVeiculoTsp"));
    }
}
