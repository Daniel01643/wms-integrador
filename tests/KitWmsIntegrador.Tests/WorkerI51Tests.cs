using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Separacao;
using KitWmsIntegrador.Tests.Infra;
using KitWmsIntegrador.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class AppsettingsPadroesTests
{
    private static IConfigurationRoot Config()
    {
        var raiz = AppContext.BaseDirectory;
        while (raiz is not null && !File.Exists(Path.Combine(raiz, "KitWmsIntegrador.sln")))
        {
            raiz = Path.GetDirectoryName(raiz);
        }

        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(raiz!, "src", "KitWmsIntegrador.Worker", "appsettings.json"))
            .Build();
    }

    [Fact]
    public void AsTresChavesHabilitadaVemDesligadas()
    {
        var c = Config();
        Assert.False(c.GetValue<bool>("I51:Habilitada"));
        Assert.False(c.GetValue<bool>("I60:Habilitada"));
        Assert.False(c.GetValue<bool>("Devolucao:Habilitada"));
        Assert.Equal("INE", c["I60:TipoOperacaoNota"]);
    }

    [Fact]
    public void SecoesNovasBindamEValidam()
    {
        var c = Config();
        var i51 = c.GetSection("I51").Get<OpcoesI51>()!;
        var i60 = c.GetSection("I60").Get<OpcoesI60>()!;
        var rel = c.GetSection("RelatorioI60").Get<OpcoesRelatorioI60>()!;
        Assert.Empty(i51.Validar());
        Assert.Empty(i60.Validar());
        Assert.Empty(rel.Validar());
        Assert.Equal("WEBAPI-I51", i51.Identificador);
        Assert.Equal("UPD", i60.TpAcaoReenvioImp);
    }
}

public class ReenfileirarI60Tests : IDisposable
{
    private readonly AmbienteI51 _amb = new();

    public void Dispose() => _amb.Dispose();

    private int Rodar(params string[] args)
    {
        var cmd = new ComandosI51(new CaminhosI51(Options.Create(_amb.Pastas)), _amb.Clock, NullLogger<ComandosI51>.Instance);
        return ReenfileirarI60.Executar(args, cmd, TextWriter.Null);
    }

    [Fact]
    public void ParametroFaltando_ou_MotivoInvalido_Sai2()
    {
        Assert.Equal(2, Rodar("--reenfileirar-i60"));
        Assert.Equal(2, Rodar("--reenfileirar-i60", "--motivo", "Nenhum"));
        Assert.Equal(2, Rodar("--reenfileirar-i60", "--motivo", "Banana"));
    }

    [Fact]
    public async Task PorMotivo_GravaPedido_OWorkerAplicaNoCicloSeguinte()
    {
        _amb.RegistrarNota(100, new[] { ("A", 5m) });
        _amb.Wms.Separar("100", AmbienteI51.CnpjDestinatario, new[] { new FakeWmsI51.ItemDoc(1, "A", 5, 5) });
        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.Recusa("Quantidade caixa nao confere com a quantidade");
        await _amb.Servico.CicloAsync(CancellationToken.None);
        Assert.Equal(SituacaoNota.Rejeitada, _amb.Nota(100).Situacao);

        Assert.Equal(0, Rodar("--reenfileirar-i60", "--motivo", "QuantidadeDiferente"));
        Assert.Single(Directory.GetFiles(_amb.Caminhos.Comandos, "reenfileirar_*.json"));
        Assert.Equal(SituacaoNota.Rejeitada, _amb.Nota(100).Situacao);

        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.Sucesso();
        await _amb.Servico.CicloAsync(CancellationToken.None);
        Assert.Equal(SituacaoNota.Associada, _amb.Nota(100).Situacao);
    }

    [Fact]
    public async Task ComandoCorrompido_VaiParaFalhos()
    {
        Directory.CreateDirectory(_amb.Caminhos.Comandos);
        File.WriteAllText(Path.Combine(_amb.Caminhos.Comandos, "reenfileirar_x.json"), "{nao e json");
        await _amb.Servico.CicloAsync(CancellationToken.None);
        Assert.Single(Directory.GetFiles(_amb.Caminhos.ComandosFalhos));
    }
}

public class ConsultaI51WorkerTests : IDisposable
{
    private readonly AmbienteI51 _amb = new();

    public void Dispose() => _amb.Dispose();

    private ConsultaI51Worker Criar() =>
        new(_amb.Servico, Options.Create(_amb.I51), Options.Create(_amb.I60), NullLogger<ConsultaI51Worker>.Instance);

    [Fact]
    public async Task Desligado_TerminaSemConsultar()
    {
        _amb.I51.Habilitada = false;
        var w = Criar();
        await w.StartAsync(CancellationToken.None);
        await (w.ExecuteTask ?? Task.CompletedTask);
        Assert.Empty(_amb.Wms.Consultas);
        Assert.False(Directory.Exists(_amb.Caminhos.Base));
        await w.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OpcoesInvalidas_NaoChamamOWms()
    {
        _amb.I51.IntervaloMinutos = 0;
        _amb.RegistrarNota(100, new[] { ("A", 1m) });
        var w = Criar();
        await w.StartAsync(CancellationToken.None);
        await (w.ExecuteTask ?? Task.CompletedTask);
        Assert.Empty(_amb.Wms.Consultas);
        await w.StopAsync(CancellationToken.None);
    }
}
