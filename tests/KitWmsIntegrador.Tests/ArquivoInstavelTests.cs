using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>Arquivos que nunca estabilizam: sem travar o pacote, sem loop de foto e sem CPU à toa.</summary>
public sealed class ArquivoInstavelTests : IDisposable
{
    private readonly AmbientePacote _amb = new();
    private readonly OrquestradorFalso _orq = new();

    public void Dispose() => _amb.Dispose();

    private sealed class ColetorLogger<T> : ILogger<T>
    {
        public List<(LogLevel Nivel, string Mensagem)> Entradas { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entradas.Add((logLevel, formatter(state, exception)));
    }

    private async Task Ciclo(GerenciadorPacote g, int segundos = 10)
    {
        _amb.Clock.Avancar(TimeSpan.FromSeconds(segundos));
        await g.CicloAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ArquivoTravadoAlemDoTimeout_NuncaAbrePacoteSozinhoENaoGeraFotoEmLoop()
    {
        var g = _amb.CriarGerenciador(_orq);
        var travado = _amb.EscreverXml("travado.xml", "100", "70001");
        var trava = new FileStream(travado, FileMode.Open, FileAccess.Read, FileShare.None);
        try
        {
            // Passa bem do timeout de 300 s.
            for (var i = 0; i < 60; i++)
            {
                await Ciclo(g);
                Assert.NotEqual(EstadoPacote.Processando, g.Estado);
                Assert.NotEqual(EstadoPacote.Fechando, g.Estado);
            }

            Assert.Equal(EstadoPacote.Ocioso, g.Estado);
            Assert.Empty(_orq.Chamadas);
            Assert.Empty(_amb.RelatoriosGerados(".pdf"));
            Assert.Empty(_amb.Email.Enviadas);

            // Já instável: nenhuma foto nova é tentada (a próxima foto não muda mais).
            var proxima = g.ProximaFotoUtc;
            for (var i = 0; i < 10; i++)
            {
                await Ciclo(g);
                Assert.Equal(EstadoPacote.Ocioso, g.Estado);
                Assert.Equal(proxima, g.ProximaFotoUtc);
            }
        }
        finally
        {
            trava.Dispose();
        }

        // Liberado: entra na foto seguinte e é processado.
        for (var i = 0; i < 40 && _amb.Email.Enviadas.Count == 0; i++)
        {
            await Ciclo(g);
        }

        Assert.Single(_orq.Chamadas);
        Assert.Single(_amb.Email.Enviadas);
        Assert.Single(_amb.Arquivos("Processados"));
    }

    [Fact]
    public void Detector_LogaOAlertaDeTimeoutUmaUnicaVez()
    {
        var logger = new ColetorLogger<DetectorArquivoPronto>();
        var detector = new DetectorArquivoPronto(_amb.Clock, Options.Create(_amb.Pacote), logger);
        var travado = _amb.EscreverXml("travado.xml", "100", "70001");
        using var trava = new FileStream(travado, FileMode.Open, FileAccess.Read, FileShare.None);

        for (var i = 0; i < 80; i++)
        {
            _amb.Clock.Avancar(TimeSpan.FromSeconds(10));
            detector.Avaliar(new[] { travado });
        }

        Assert.Single(logger.Entradas, e => e.Nivel == LogLevel.Warning);
        Assert.Contains("travado.xml", logger.Entradas.Single(e => e.Nivel == LogLevel.Warning).Mensagem);
    }

    [Fact]
    public async Task ArquivoSempreEmGravacao_FotosVaziasComBackoffExponencialAteOTeto()
    {
        _amb.Pacote.TimeoutArquivoSegundos = 1_000_000;
        var g = _amb.CriarGerenciador(_orq);
        var gravando = _amb.Caminho("gravando.xml");
        var esperas = new List<double>();
        var ultimaProxima = g.ProximaFotoUtc;

        for (var i = 0; i < 80; i++)
        {
            File.AppendAllText(gravando, "x"); // muda tamanho a cada ciclo: nunca estabiliza
            _amb.Clock.Avancar(TimeSpan.FromSeconds(10));
            var antes = g.ProximaFotoUtc;
            await g.CicloAsync(CancellationToken.None);

            // Nunca abre foto antes da hora.
            if (_amb.Clock.UtcNow < antes)
            {
                Assert.Equal(EstadoPacote.Ocioso, g.Estado);
            }

            if (g.ProximaFotoUtc != ultimaProxima)
            {
                ultimaProxima = g.ProximaFotoUtc;
                esperas.Add((g.ProximaFotoUtc - _amb.Clock.UtcNow).TotalSeconds);
            }
        }

        Assert.Equal(new double[] { 20, 40, 80, 120, 120 }, esperas.Take(5));
        Assert.All(esperas, e => Assert.InRange(e, 20, 120));
        Assert.Empty(_orq.Chamadas);
        Assert.Empty(_amb.RelatoriosGerados(".pdf"));
        Assert.Empty(_amb.Email.Enviadas);
    }

    [Fact]
    public async Task ArquivoEmGravacao_NaoImpedeOutrosDeFormarPacote()
    {
        var g = _amb.CriarGerenciador(_orq);
        var gravando = _amb.Caminho("gravando.xml");
        _amb.EscreverXml("ok.xml", "100", "70001");

        for (var i = 0; i < 4; i++)
        {
            File.AppendAllText(gravando, "x");
            await Ciclo(g, i == 0 ? 0 : 5);
        }

        Assert.Single(_orq.Chamadas);
        Assert.True(File.Exists(gravando));
        Assert.Single(_amb.Email.Enviadas);
    }
}
