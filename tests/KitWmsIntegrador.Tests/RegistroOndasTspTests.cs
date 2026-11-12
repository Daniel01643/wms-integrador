using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public sealed class RegistroOndasTspTests : IDisposable
{
    private readonly AmbientePacote _amb = new();

    public void Dispose() => _amb.Dispose();

    private RegistroOndasTsp Criar() =>
        new(_amb.Clock, Options.Create(_amb.Pastas), NullLogger<RegistroOndasTsp>.Instance);

    private string Data => _amb.Clock.UtcNow.ToLocalTime().ToString("yyyyMMdd");

    [Fact]
    public void SequencialDoDia_ComecaEm01_ECresce()
    {
        var r = Criar();
        Assert.Equal($"TSP-{Data}-01-SOROCABA", r.ObterOuCriar("XML'S SOROCABA.zip", "h1"));
        Assert.Equal($"TSP-{Data}-02-SJC", r.ObterOuCriar("XML'S SJC.zip", "h2"));
        Assert.Equal($"TSP-{Data}-03", r.ObterOuCriar("XML'S.zip", "h3"));
    }

    [Fact]
    public void MesmoConteudo_ReaproveitaAOnda_SemConsumirSequencial()
    {
        var r = Criar();
        var a = r.ObterOuCriar("SJC.zip", "h1");
        Assert.Equal(a, r.ObterOuCriar("SJC.zip", "h1"));
        Assert.Equal(a, r.ObterOuCriar("outro nome.zip", "h1"));
        Assert.EndsWith("-02-SOROCABA", r.ObterOuCriar("SOROCABA.zip", "h2"));
    }

    [Fact]
    public void MesmoNomeComConteudoDiferente_GanhaNovoNumero()
    {
        var r = Criar();
        Assert.EndsWith("-01-SJC", r.ObterOuCriar("SJC.zip", "h1"));
        Assert.EndsWith("-02-SJC", r.ObterOuCriar("SJC.zip", "h2"));
    }

    [Fact]
    public void ViradaDeDia_ReiniciaEm01_EOntemContinuaReaproveitado()
    {
        var r = Criar();
        var ontem = r.ObterOuCriar("SJC.zip", "h1");
        r.ObterOuCriar("SOROCABA.zip", "h2");
        var dataOntem = Data;

        _amb.Clock.Avancar(TimeSpan.FromDays(1));
        var hoje = r.ObterOuCriar("SJC.zip", "h3");

        Assert.NotEqual(dataOntem, Data);
        Assert.Equal($"TSP-{Data}-01-SJC", hoje);
        Assert.Equal(ontem, r.ObterOuCriar("SJC.zip", "h1"));
    }

    [Fact]
    public void Persistencia_SobreviveAoReinicio()
    {
        var a = Criar().ObterOuCriar("SJC.zip", "h1");
        Assert.True(File.Exists(Criar().Caminho));

        var depois = Criar();
        Assert.Equal(a, depois.ObterOuCriar("SJC.zip", "h1"));
        Assert.EndsWith("-02-SOROCABA", depois.ObterOuCriar("SOROCABA.zip", "h2"));
        Assert.EndsWith(Path.Combine("Estado", "tsp", "ondas.json"), depois.Caminho);
    }

    [Fact]
    public void ArquivoCorrompido_NaoDerruba_RecomecaDo01_EPreservaOAntigo()
    {
        var r0 = Criar();
        Directory.CreateDirectory(Path.GetDirectoryName(r0.Caminho)!);
        File.WriteAllText(r0.Caminho, "{ isto nao e json");

        var onda = Criar().ObterOuCriar("SJC.zip", "h1");

        Assert.EndsWith("-01-SJC", onda);
        var preservados = Directory.GetFiles(Path.GetDirectoryName(r0.Caminho)!, "ondas.json.corrompido-*");
        Assert.Single(preservados);
        Assert.Equal("{ isto nao e json", File.ReadAllText(preservados[0]));
    }

    [Fact]
    public void SequencialAcimaDe99_UsaTresDigitos_ENaoPassaDe30()
    {
        var r = Criar();
        string? ultima = null;
        for (var i = 1; i <= 100; i++)
        {
            ultima = r.ObterOuCriar("RELACAO DE ENTREGAS GRANDE.zip", "h" + i);
        }

        Assert.StartsWith($"TSP-{Data}-100-", ultima);
        Assert.True(ultima!.Length <= 30);
    }
}