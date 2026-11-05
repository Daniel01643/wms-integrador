using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Monitoramento;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class MarcaUltimoResumoTests : IDisposable
{
    private readonly string _pasta;
    private readonly MarcaUltimoResumo _marca;

    public MarcaUltimoResumoTests()
    {
        _pasta = Path.Combine(Path.GetTempPath(), "wms-marca-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_pasta);

        var telemetria = new RegistradorTelemetria(
            Options.Create(new OpcoesPasta()),
            Options.Create(new OpcoesMonitoramento { CaminhoTelemetria = _pasta }),
            NullLogger<RegistradorTelemetria>.Instance);
        _marca = new MarcaUltimoResumo(telemetria, NullLogger<MarcaUltimoResumo>.Instance);
    }

    [Fact]
    public void Ausente_RetornaNull()
    {
        Assert.Null(_marca.Ler());
    }

    [Fact]
    public void GravaERele_PreservaInstante()
    {
        var slot = new DateTimeOffset(2025, 6, 2, 12, 0, 0, TimeSpan.FromHours(-3));

        _marca.Marcar(slot);
        var lido = _marca.Ler();

        Assert.NotNull(lido);
        Assert.Equal(slot, lido!.Value);
    }

    [Fact]
    public void Corrompido_RetornaNull()
    {
        File.WriteAllText(_marca.Caminho, "isto nao e uma data");

        Assert.Null(_marca.Ler());
    }

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
        {
            Directory.Delete(_pasta, recursive: true);
        }
    }
}
