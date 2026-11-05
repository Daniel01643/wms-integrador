using KitWmsIntegrador.Core.Monitoramento;

namespace KitWmsIntegrador.Tests;

public class AgregadorTelemetriaTests
{
    private static readonly TimeSpan Fuso = TimeSpan.FromHours(-3);

    private static RegistroTelemetria Pacote(
        DateTimeOffset fechamento,
        int totalNotas = 10,
        int integradas = 8,
        int integradasRevisar = 1,
        int pendentes = 1,
        int naoIntegradas = 1,
        int xmlInvalido = 0,
        long tempoTotalMs = 20000,
        long tempoMedioPorNotaMs = 2000,
        IReadOnlyDictionary<string, int>? chamadasWms = null,
        bool chamadasAproximado = true,
        IReadOnlyDictionary<string, int>? errosPorCategoria = null) =>
        new(
            IdPacote: "pacote-" + fechamento.ToString("HHmmss"),
            AberturaLocal: fechamento.AddMinutes(-10),
            FechamentoLocal: fechamento,
            TotalNotas: totalNotas,
            Integradas: integradas,
            IntegradasRevisarProduto: integradasRevisar,
            Pendentes: pendentes,
            NaoIntegradas: naoIntegradas,
            XmlInvalido: xmlInvalido,
            Transportes: 3,
            TempoTotalMs: tempoTotalMs,
            TempoMedioPorNotaMs: tempoMedioPorNotaMs,
            Tentativas: new Dictionary<string, int> { ["1"] = totalNotas },
            ChamadasWms: chamadasWms ?? new Dictionary<string, int> { ["I50"] = 1 },
            ChamadasWmsAproximado: chamadasAproximado,
            ErrosPorCategoria: errosPorCategoria ?? new Dictionary<string, int>());

    [Fact]
    public void Agregar_StatusEPercentuaisCorretos()
    {
        var inicio = new DateTimeOffset(2025, 6, 2, 0, 0, 0, Fuso);
        var fim = inicio.AddDays(7);
        var pacotes = new[]
        {
            Pacote(new DateTimeOffset(2025, 6, 2, 9, 0, 0, Fuso), totalNotas: 10, integradas: 8,
                integradasRevisar: 2, pendentes: 1, naoIntegradas: 1, xmlInvalido: 0),
            Pacote(new DateTimeOffset(2025, 6, 3, 14, 0, 0, Fuso), totalNotas: 10, integradas: 10,
                integradasRevisar: 0, pendentes: 0, naoIntegradas: 0, xmlInvalido: 0),
        };

        var r = AgregadorTelemetria.Agregar(pacotes, Array.Empty<EventoServico>(), inicio, fim);

        Assert.Equal(2, r.TotalPacotes);
        Assert.Equal(20, r.TotalNotas);
        Assert.Equal(18, r.Integradas);
        Assert.Equal(2, r.IntegradasRevisarProduto);
        Assert.Equal(1, r.Pendentes);
        Assert.Equal(1, r.NaoIntegradas);
        Assert.Equal(90d, r.PercentualIntegradas, 3);
        Assert.Equal(5d, r.PercentualPendentes, 3);
        Assert.Equal(5d, r.PercentualNaoIntegradas, 3);
        Assert.False(r.SemAtividade);
    }

    [Fact]
    public void Agregar_MediaNotasPorPacoteEDiasComAtividade()
    {
        var inicio = new DateTimeOffset(2025, 6, 2, 0, 0, 0, Fuso);
        var fim = inicio.AddDays(7);
        var pacotes = new[]
        {
            Pacote(new DateTimeOffset(2025, 6, 2, 9, 0, 0, Fuso), totalNotas: 10),
            Pacote(new DateTimeOffset(2025, 6, 2, 16, 0, 0, Fuso), totalNotas: 20),
            Pacote(new DateTimeOffset(2025, 6, 4, 8, 0, 0, Fuso), totalNotas: 6),
        };

        var r = AgregadorTelemetria.Agregar(pacotes, Array.Empty<EventoServico>(), inicio, fim);

        Assert.Equal(36, r.TotalNotas);
        Assert.Equal(12d, r.MediaNotasPorPacote, 3);
        Assert.Equal(2, r.DiasComAtividade);
    }

    [Fact]
    public void Agregar_DistribuicaoPorDiaSemana()
    {
        // 2025-06-02 = segunda; 2025-06-03 = terça.
        var inicio = new DateTimeOffset(2025, 6, 2, 0, 0, 0, Fuso);
        var fim = inicio.AddDays(7);
        var pacotes = new[]
        {
            Pacote(new DateTimeOffset(2025, 6, 2, 9, 0, 0, Fuso), totalNotas: 10),
            Pacote(new DateTimeOffset(2025, 6, 2, 10, 0, 0, Fuso), totalNotas: 5),
            Pacote(new DateTimeOffset(2025, 6, 3, 14, 0, 0, Fuso), totalNotas: 7),
        };

        var r = AgregadorTelemetria.Agregar(pacotes, Array.Empty<EventoServico>(), inicio, fim);

        Assert.Equal(2, r.PacotesPorDiaSemana[DayOfWeek.Monday]);
        Assert.Equal(15, r.NotasPorDiaSemana[DayOfWeek.Monday]);
        Assert.Equal(1, r.PacotesPorDiaSemana[DayOfWeek.Tuesday]);
        Assert.Equal(7, r.NotasPorDiaSemana[DayOfWeek.Tuesday]);
    }

    [Theory]
    // Bordas das faixas: 05:59 Noite, 06:00 Manhã, 11:59 Manhã, 12:00 Tarde, 17:59 Tarde, 18:00 Noite.
    [InlineData(5, 59, FaixaHorario.Noite)]
    [InlineData(6, 0, FaixaHorario.Manha)]
    [InlineData(11, 59, FaixaHorario.Manha)]
    [InlineData(12, 0, FaixaHorario.Tarde)]
    [InlineData(17, 59, FaixaHorario.Tarde)]
    [InlineData(18, 0, FaixaHorario.Noite)]
    public void Agregar_DistribuicaoPorFaixaComBordas(int hora, int minuto, FaixaHorario esperada)
    {
        var inicio = new DateTimeOffset(2025, 6, 2, 0, 0, 0, Fuso);
        var fim = inicio.AddDays(7);
        var fechamento = new DateTimeOffset(2025, 6, 2, hora, minuto, 0, Fuso);
        var pacotes = new[] { Pacote(fechamento, totalNotas: 4) };

        var r = AgregadorTelemetria.Agregar(pacotes, Array.Empty<EventoServico>(), inicio, fim);

        Assert.Equal(1, r.PacotesPorFaixa[esperada]);
        Assert.Equal(4, r.NotasPorFaixa[esperada]);
    }

    [Fact]
    public void FaixaHorario_ClassificaBordasDiretamente()
    {
        Assert.Equal(FaixaHorario.Noite, FaixaHorarioExtensoes.De(new DateTimeOffset(2025, 6, 2, 5, 59, 0, Fuso)));
        Assert.Equal(FaixaHorario.Manha, FaixaHorarioExtensoes.De(new DateTimeOffset(2025, 6, 2, 6, 0, 0, Fuso)));
        Assert.Equal(FaixaHorario.Manha, FaixaHorarioExtensoes.De(new DateTimeOffset(2025, 6, 2, 11, 59, 0, Fuso)));
        Assert.Equal(FaixaHorario.Tarde, FaixaHorarioExtensoes.De(new DateTimeOffset(2025, 6, 2, 12, 0, 0, Fuso)));
        Assert.Equal(FaixaHorario.Tarde, FaixaHorarioExtensoes.De(new DateTimeOffset(2025, 6, 2, 17, 59, 0, Fuso)));
        Assert.Equal(FaixaHorario.Noite, FaixaHorarioExtensoes.De(new DateTimeOffset(2025, 6, 2, 18, 0, 0, Fuso)));
        Assert.Equal(FaixaHorario.Noite, FaixaHorarioExtensoes.De(new DateTimeOffset(2025, 6, 2, 0, 0, 0, Fuso)));
    }

    [Fact]
    public void Agregar_TemposMediosPonderados()
    {
        var inicio = new DateTimeOffset(2025, 6, 2, 0, 0, 0, Fuso);
        var fim = inicio.AddDays(7);
        var pacotes = new[]
        {
            Pacote(new DateTimeOffset(2025, 6, 2, 9, 0, 0, Fuso), totalNotas: 10,
                tempoTotalMs: 20000, tempoMedioPorNotaMs: 2000),
            Pacote(new DateTimeOffset(2025, 6, 3, 9, 0, 0, Fuso), totalNotas: 30,
                tempoTotalMs: 30000, tempoMedioPorNotaMs: 1000),
        };

        var r = AgregadorTelemetria.Agregar(pacotes, Array.Empty<EventoServico>(), inicio, fim);

        // Ponderado por notas: (2000*10 + 1000*30) / 40 = 1250.
        Assert.Equal(1250L, r.TempoMedioPorNotaMs);
        // Por pacote: (20000 + 30000) / 2 = 25000.
        Assert.Equal(25000L, r.TempoMedioPorPacoteMs);
    }

    [Fact]
    public void Agregar_ChamadasWmsAgregadasEAproximado()
    {
        var inicio = new DateTimeOffset(2025, 6, 2, 0, 0, 0, Fuso);
        var fim = inicio.AddDays(7);
        var pacotes = new[]
        {
            Pacote(new DateTimeOffset(2025, 6, 2, 9, 0, 0, Fuso),
                chamadasWms: new Dictionary<string, int> { ["I50"] = 5, ["I10"] = 2 }, chamadasAproximado: true),
            Pacote(new DateTimeOffset(2025, 6, 3, 9, 0, 0, Fuso),
                chamadasWms: new Dictionary<string, int> { ["I50"] = 3, ["I30"] = 1 }, chamadasAproximado: false),
        };

        var r = AgregadorTelemetria.Agregar(pacotes, Array.Empty<EventoServico>(), inicio, fim);

        Assert.Equal(11, r.TotalChamadasWms);
        Assert.Equal(8, r.ChamadasWmsPorInterface["I50"]);
        Assert.Equal(2, r.ChamadasWmsPorInterface["I10"]);
        Assert.Equal(1, r.ChamadasWmsPorInterface["I30"]);
        Assert.True(r.ChamadasWmsAproximado);
    }

    [Fact]
    public void Agregar_ErrosPorCategoriaSomados()
    {
        var inicio = new DateTimeOffset(2025, 6, 2, 0, 0, 0, Fuso);
        var fim = inicio.AddDays(7);
        var pacotes = new[]
        {
            Pacote(new DateTimeOffset(2025, 6, 2, 9, 0, 0, Fuso),
                errosPorCategoria: new Dictionary<string, int> { ["ProdutoNaoEncontrado"] = 2, ["Duplicidade"] = 1 }),
            Pacote(new DateTimeOffset(2025, 6, 3, 9, 0, 0, Fuso),
                errosPorCategoria: new Dictionary<string, int> { ["ProdutoNaoEncontrado"] = 3 }),
        };

        var r = AgregadorTelemetria.Agregar(pacotes, Array.Empty<EventoServico>(), inicio, fim);

        Assert.Equal(5, r.ErrosPorCategoria["ProdutoNaoEncontrado"]);
        Assert.Equal(1, r.ErrosPorCategoria["Duplicidade"]);
    }

    [Fact]
    public void Agregar_FiltraPelaJanela()
    {
        var inicio = new DateTimeOffset(2025, 6, 2, 0, 0, 0, Fuso);
        var fim = inicio.AddDays(7); // 2025-06-09 00:00
        var pacotes = new[]
        {
            Pacote(inicio.AddDays(-1), totalNotas: 100),            // antes da janela
            Pacote(inicio, totalNotas: 10),                          // na borda inicial (incluso)
            Pacote(new DateTimeOffset(2025, 6, 5, 9, 0, 0, Fuso), totalNotas: 20), // dentro
            Pacote(fim, totalNotas: 999),                            // na borda final (excluso)
        };

        var r = AgregadorTelemetria.Agregar(pacotes, Array.Empty<EventoServico>(), inicio, fim);

        Assert.Equal(2, r.TotalPacotes);
        Assert.Equal(30, r.TotalNotas);
    }

    [Fact]
    public void Agregar_ServicoDesdeEInicializacoesNoPeriodo()
    {
        var inicio = new DateTimeOffset(2025, 6, 2, 0, 0, 0, Fuso);
        var fim = inicio.AddDays(7);
        var eventos = new[]
        {
            new EventoServico("servico-iniciado", new DateTimeOffset(2025, 5, 20, 8, 0, 0, Fuso), "1.0.0"),
            new EventoServico("servico-iniciado", new DateTimeOffset(2025, 6, 3, 8, 0, 0, Fuso), "1.0.0"),
            new EventoServico("servico-iniciado", fim, "1.0.0"), // fora da janela (borda final excl.)
        };
        var pacotes = new[] { Pacote(new DateTimeOffset(2025, 6, 2, 9, 0, 0, Fuso)) };

        var r = AgregadorTelemetria.Agregar(pacotes, eventos, inicio, fim);

        Assert.Equal(new DateTimeOffset(2025, 5, 20, 8, 0, 0, Fuso), r.ServicoDesde);
        Assert.Equal(1, r.InicializacoesNoPeriodo);
    }

    [Fact]
    public void Agregar_SemPacotes_SemAtividade()
    {
        var inicio = new DateTimeOffset(2025, 6, 2, 0, 0, 0, Fuso);
        var fim = inicio.AddDays(7);

        var r = AgregadorTelemetria.Agregar(
            Array.Empty<RegistroTelemetria>(), Array.Empty<EventoServico>(), inicio, fim);

        Assert.True(r.SemAtividade);
        Assert.Equal(0, r.TotalPacotes);
        Assert.Equal(0, r.TotalNotas);
        Assert.Equal(0d, r.MediaNotasPorPacote);
        Assert.Equal(0d, r.PercentualIntegradas);
        Assert.Equal(0L, r.TempoMedioPorNotaMs);
        Assert.Equal(0, r.TotalChamadasWms);
        Assert.Null(r.ServicoDesde);
    }
}
