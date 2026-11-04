using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public sealed class DetectorArquivoProntoTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "kitwms-det-" + Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new(new DateTimeOffset(2025, 1, 10, 12, 0, 0, TimeSpan.Zero));
    private readonly OpcoesPacote _opcoes = new() { EstabilizacaoSegundos = 3, TimeoutArquivoSegundos = 60 };

    public DetectorArquivoProntoTests() => Directory.CreateDirectory(_pasta);

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
        {
            Directory.Delete(_pasta, true);
        }
    }

    private DetectorArquivoPronto Criar() =>
        new(_clock, Options.Create(_opcoes), NullLogger<DetectorArquivoPronto>.Instance);

    private string Arquivo(string conteudo = "<a/>")
    {
        var caminho = Path.Combine(_pasta, "a.xml");
        File.WriteAllText(caminho, conteudo);
        return caminho;
    }

    [Fact]
    public void PrimeiraObservacao_NaoEhPronto_EstavelDepoisDeTempo_EhPronto()
    {
        var detector = Criar();
        var caminho = Arquivo();

        Assert.Empty(detector.Avaliar(new[] { caminho }).Prontos);

        _clock.Avancar(TimeSpan.FromSeconds(1));
        Assert.Empty(detector.Avaliar(new[] { caminho }).Prontos); // estável há 1s < 3s

        _clock.Avancar(TimeSpan.FromSeconds(3));
        Assert.Equal(new[] { caminho }, detector.Avaliar(new[] { caminho }).Prontos);
    }

    [Fact]
    public void ArquivoCrescendo_NaoFicaPronto()
    {
        var detector = Criar();
        var caminho = Arquivo("<a/>");
        detector.Avaliar(new[] { caminho });

        _clock.Avancar(TimeSpan.FromSeconds(10));
        File.AppendAllText(caminho, "<b/>"); // mudou o tamanho: reinicia a estabilização
        Assert.Empty(detector.Avaliar(new[] { caminho }).Prontos);

        _clock.Avancar(TimeSpan.FromSeconds(4));
        Assert.Single(detector.Avaliar(new[] { caminho }).Prontos);
    }

    [Fact]
    public void ArquivoTravado_NaoFicaPronto_ELiberadoFicaPronto()
    {
        var detector = Criar();
        var caminho = Arquivo();
        detector.Avaliar(new[] { caminho });
        _clock.Avancar(TimeSpan.FromSeconds(5));

        using (new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var r = detector.Avaliar(new[] { caminho });
            Assert.Empty(r.Prontos);
            Assert.Equal(new[] { caminho }, r.Aguardando);
        }

        Assert.Single(detector.Avaliar(new[] { caminho }).Prontos);
    }

    [Fact]
    public void ArquivoTravadoAcimaDoTimeout_ViraInstavel()
    {
        var detector = Criar();
        var caminho = Arquivo();
        using var trava = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.None);

        detector.Avaliar(new[] { caminho });
        _clock.Avancar(TimeSpan.FromSeconds(61));
        var r = detector.Avaliar(new[] { caminho });

        Assert.Empty(r.Prontos);
        Assert.Equal(new[] { caminho }, r.Instaveis);
    }

    [Fact]
    public void ArquivoRemovido_EhEsquecido_ENovoComMesmoNomeComecaDoZero()
    {
        var detector = Criar();
        var caminho = Arquivo();
        detector.Avaliar(new[] { caminho });
        _clock.Avancar(TimeSpan.FromSeconds(10));
        File.Delete(caminho);
        detector.Avaliar(new[] { caminho });

        Arquivo();
        Assert.Empty(detector.Avaliar(new[] { caminho }).Prontos);
    }
}
