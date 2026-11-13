using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Core.Modelos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Regra da placa na I50: PUXADA governada por I50:EnviarPlacaVeiculo; TSP por
/// I50:EnviarPlacaVeiculoTsp (padrão false = sempre sem placa). Inclui a prova de que o
/// override por variável de ambiente (I50__...) chega em <see cref="OpcoesI50"/>.
/// </summary>
public class PlacaTspConfiguracaoTests
{
    private const string Xml1 = "NFE35251201838723009850550060156658071604491612.XML";

    private static NotaFiscal Nota() =>
        new LeitorNotaFiscalXml().ParseArquivo(Path.Combine(AppContext.BaseDirectory, "Recursos", Xml1));

    private static ConstrutorI50 Criar(OpcoesI50 i50) => new(
        Options.Create(new OpcoesOperacionais
        {
            Deposito = "JD",
            IdDeposito = 8,
            CnpjArmazem = "14.605.065/0001-75",
            CnpjDepositante = "01.838.723/0098-50",
            CodigoDepositante = "3509308",
        }),
        Options.Create(i50));

    private static string Placa(OpcoesI50 i50, bool ehTsp) =>
        Assert.Single(Criar(i50).Construir(Nota(), null, ehTsp).Pedido).PlacaVeiculo;

    [Fact]
    public void Tsp_PadraoDasOpcoes_VaiSemPlaca()
    {
        Assert.False(new OpcoesI50().EnviarPlacaVeiculoTsp);
        Assert.Equal(string.Empty, Placa(new OpcoesI50(), ehTsp: true));
    }

    [Fact]
    public void Tsp_MesmoComEnviarPlacaVeiculoTrue_VaiSemPlaca()
    {
        Assert.Equal(string.Empty, Placa(new OpcoesI50 { EnviarPlacaVeiculo = true }, ehTsp: true));
    }

    [Fact]
    public void Puxada_EnviarPlacaTrue_EnviaPlaca()
    {
        Assert.Equal("AYC8G72", Placa(new OpcoesI50 { EnviarPlacaVeiculo = true }, ehTsp: false));
    }

    [Fact]
    public void Puxada_EnviarPlacaFalse_VaiSemPlaca()
    {
        Assert.Equal(string.Empty, Placa(new OpcoesI50 { EnviarPlacaVeiculo = false }, ehTsp: false));
    }

    [Fact]
    public void Puxada_NaoDependeDeEnviarPlacaVeiculoTsp()
    {
        Assert.Equal(string.Empty,
            Placa(new OpcoesI50 { EnviarPlacaVeiculo = false, EnviarPlacaVeiculoTsp = true }, ehTsp: false));
    }

    [Fact]
    public void Tsp_EnviarPlacaVeiculoTspTrue_EnviaPlaca()
    {
        Assert.Equal("AYC8G72", Placa(new OpcoesI50 { EnviarPlacaVeiculoTsp = true }, ehTsp: true));
    }

    [Fact]
    public void ConstruirJson_Tsp_TemPlacaEmBranco()
    {
        var json = Criar(new OpcoesI50()).ConstruirJson(Nota(), null, ehTsp: true);

        Assert.Contains("\"placaVeiculo\":\"\"", json);
    }

    [Fact]
    public void Config_InMemory_FazBindDasDuasChaves()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["I50:EnviarPlacaVeiculo"] = "false",
                ["I50:EnviarPlacaVeiculoTsp"] = "true",
            })
            .Build();

        var opcoes = new OpcoesI50();
        config.GetSection(OpcoesI50.Secao).Bind(opcoes);

        Assert.False(opcoes.EnviarPlacaVeiculo);
        Assert.True(opcoes.EnviarPlacaVeiculoTsp);
    }

    // Reproduz o registro do Program (Host.CreateApplicationBuilder + AddOptions<OpcoesI50>().Bind(...))
    // e prova que as variáveis de ambiente I50__* vencem o appsettings.json e chegam ao construtor.
    [Fact]
    public void VariaveisDeAmbiente_SobrescrevemAppsettings_NoMesmoRegistroDoWorker()
    {
        const string chave = "I50__EnviarPlacaVeiculo";
        const string chaveTsp = "I50__EnviarPlacaVeiculoTsp";
        var antes = (Environment.GetEnvironmentVariable(chave), Environment.GetEnvironmentVariable(chaveTsp));
        try
        {
            Environment.SetEnvironmentVariable(chave, "false");
            Environment.SetEnvironmentVariable(chaveTsp, "true");

            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                ContentRootPath = AppContext.BaseDirectory,
            });
            // appsettings.json do Worker diz EnviarPlacaVeiculo=true; o env deve vencer.
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["I50:EnviarPlacaVeiculo"] = "true",
            });
            builder.Configuration.AddEnvironmentVariables();
            builder.Services.AddOptions<OpcoesI50>().Bind(builder.Configuration.GetSection(OpcoesI50.Secao));
            builder.Services.AddOptions<OpcoesOperacionais>();
            builder.Services.AddSingleton<IConstrutorI50, ConstrutorI50>();

            using var host = builder.Build();
            var opcoes = host.Services.GetRequiredService<IOptions<OpcoesI50>>().Value;
            var construtor = host.Services.GetRequiredService<IConstrutorI50>();
            var nota = Nota();

            Assert.False(opcoes.EnviarPlacaVeiculo);
            Assert.True(opcoes.EnviarPlacaVeiculoTsp);
            Assert.Equal(string.Empty, Assert.Single(construtor.Construir(nota, null, ehTsp: false).Pedido).PlacaVeiculo);
            Assert.Equal("AYC8G72", Assert.Single(construtor.Construir(nota, null, ehTsp: true).Pedido).PlacaVeiculo);
        }
        finally
        {
            Environment.SetEnvironmentVariable(chave, antes.Item1);
            Environment.SetEnvironmentVariable(chaveTsp, antes.Item2);
        }
    }

    [Fact]
    public void AppsettingsDoWorker_TrazTspSemPlacaEPuxadaComPlaca()
    {
        var caminho = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var config = new ConfigurationBuilder().AddJsonFile(caminho, optional: false).Build();

        var opcoes = new OpcoesI50();
        config.GetSection(OpcoesI50.Secao).Bind(opcoes);

        Assert.True(opcoes.EnviarPlacaVeiculo);
        Assert.False(opcoes.EnviarPlacaVeiculoTsp);
    }
}
