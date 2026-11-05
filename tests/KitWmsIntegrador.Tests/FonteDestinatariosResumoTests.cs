using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Monitoramento;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class FonteDestinatariosResumoTests : IDisposable
{
    private readonly string _pasta;
    private readonly FonteDestinatariosResumo _fonte;
    private readonly string _caminho;

    public FonteDestinatariosResumoTests()
    {
        _pasta = Path.Combine(Path.GetTempPath(), "wms-dest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_pasta);

        var monitoramento = new OpcoesMonitoramento { CaminhoTelemetria = _pasta };
        var telemetria = new RegistradorTelemetria(
            Options.Create(new OpcoesPasta()),
            Options.Create(monitoramento),
            NullLogger<RegistradorTelemetria>.Instance);
        _fonte = new FonteDestinatariosResumo(
            telemetria, Options.Create(monitoramento), NullLogger<FonteDestinatariosResumo>.Instance);
        _caminho = _fonte.Caminho;
    }

    [Fact]
    public void UmPorLinha_IgnoraComentariosEVazias_ComTrim()
    {
        File.WriteAllText(
            _caminho,
            "# comentario\n  a@exemplo.com \n\n#b@exemplo.com\n  c@exemplo.com\n");

        var lista = _fonte.Ler();

        Assert.Equal(new[] { "a@exemplo.com", "c@exemplo.com" }, lista);
    }

    [Fact]
    public void Ausente_RetornaPadraoMinimo()
    {
        var lista = _fonte.Ler();

        Assert.Equal(new[] { FonteDestinatariosResumo.DestinatarioPadrao }, lista);
    }

    [Fact]
    public void Vazio_RetornaPadraoMinimo()
    {
        File.WriteAllText(_caminho, "# só comentário\n\n   \n");

        var lista = _fonte.Ler();

        Assert.Equal(new[] { FonteDestinatariosResumo.DestinatarioPadrao }, lista);
    }

    [Fact]
    public void Recarrega_AposEditarOArquivo()
    {
        File.WriteAllText(_caminho, "a@exemplo.com\n");
        Assert.Equal(new[] { "a@exemplo.com" }, _fonte.Ler());

        File.WriteAllText(_caminho, "a@exemplo.com\nb@exemplo.com\n");
        Assert.Equal(new[] { "a@exemplo.com", "b@exemplo.com" }, _fonte.Ler());
    }

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
        {
            Directory.Delete(_pasta, recursive: true);
        }
    }
}
