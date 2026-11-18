using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Diagnostico;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>Coletor de amostra falso: devolve a fila programada (ou lança).</summary>
public sealed class FakeColetorAmostra : IColetorAmostraSistema
{
    public Queue<AmostraSistema> Fila { get; } = new();

    public AmostraSistema Padrao { get; set; } = Amostra(livreMb: 4000);

    public Exception? Falha { get; set; }

    public int Chamadas { get; private set; }

    public AmostraSistema Coletar()
    {
        Chamadas++;
        if (Falha is not null)
        {
            throw Falha;
        }

        return Fila.Count > 0 ? Fila.Dequeue() : Padrao;
    }

    public static AmostraSistema Amostra(int livreMb, int wsMb = 150, int privadoMb = 120, IReadOnlyList<ConsumidorMemoria>? consumidores = null) =>
        new(wsMb, privadoMb, 410, 38, livreMb, 8192, 6000, 12000,
            consumidores ?? new[] { new ConsumidorMemoria("node", 1070), new ConsumidorMemoria("Microsoft.PowerBI.EnterpriseGateway", 845), new ConsumidorMemoria("KitWmsIntegrador.Worker", wsMb) });
}

/// <summary>Amostragem de saúde: gravação no diário, alerta de memória livre (2 amostras, 1 e-mail por hora) e relatório.</summary>
public sealed class AmostraSaudeTests : IDisposable
{
    private readonly AmbienteDiagnostico _a = new();
    private readonly FakeColetorAmostra _coletor = new();

    public AmostraSaudeTests()
    {
        _a.Ir(18, 10, 0);
    }

    public void Dispose() => _a.Dispose();

    private ServicoAmostraSaude Criar() =>
        new(_a.Clock, _coletor, _a.Diario, _a.Email, _a.Destinatarios, Options.Create(_a.Opcoes), Options.Create(_a.OpcoesEmail),
            NullLogger<ServicoAmostraSaude>.Instance);

    private Task Amostrar(ServicoAmostraSaude s) => s.AmostrarAsync(CancellationToken.None);

    private IReadOnlyList<EventoDiagnostico> Eventos(string tipo) =>
        _a.Diario.Ler(AmbienteDiagnostico.Local(18, 0, 0), AmbienteDiagnostico.Local(18, 23, 59)).Where(e => e.Tipo == tipo).ToList();

    [Fact]
    public async Task GravaAmostraNoDiario_ComProcessoServidorEConsumidores()
    {
        _coletor.Padrao = FakeColetorAmostra.Amostra(livreMb: 3000, wsMb: 155, privadoMb: 130);

        await Amostrar(Criar());

        var e = Assert.Single(Eventos("saude-amostra"));
        Assert.Equal(155, e.ProcessoWsMb);
        Assert.Equal(130, e.ProcessoPrivadoMb);
        Assert.Equal(410, e.Handles);
        Assert.Equal(38, e.Threads);
        Assert.Equal(3000, e.LivreMb);
        Assert.Equal(8192, e.TotalMb);
        Assert.Equal(6000, e.PaginacaoUsadaMb);
        Assert.Equal(12000, e.PaginacaoTotalMb);
        Assert.Equal(3, e.Consumidores!.Count);
        Assert.Equal("node", e.Consumidores[0].Nome);
        Assert.Equal(1070, e.Consumidores[0].Mb);
        Assert.Empty(_a.Email.Enviadas);
    }

    [Fact]
    public async Task UmaAmostraAbaixoDoLimite_NaoAlerta()
    {
        _coletor.Padrao = FakeColetorAmostra.Amostra(livreMb: 300);

        await Amostrar(Criar());

        Assert.Empty(_a.Email.Enviadas);
    }

    [Fact]
    public async Task DuasAmostrasSeguidasAbaixoDoLimite_AlertamSoParaDestinatariosDeDiagnostico()
    {
        _coletor.Padrao = FakeColetorAmostra.Amostra(livreMb: 300);
        var s = Criar();

        await Amostrar(s);
        _a.Clock.Avancar(TimeSpan.FromMinutes(15));
        await Amostrar(s);

        var msg = Assert.Single(_a.Email.Enviadas);
        Assert.Equal(new[] { "ti1@exemplo.com", "ti2@exemplo.com" }, msg.DestinatariosOverride);
        Assert.Contains("memória livre", msg.Assunto);
        Assert.Contains("300 MB", msg.Assunto);
        Assert.Contains("node", msg.CorpoHtml);
        Assert.Contains("1070 MB", msg.CorpoHtml);
        Assert.Empty(msg.Anexos);
        var alerta = Assert.Single(Eventos("saude-alerta"));
        Assert.True(alerta.Ok);
    }

    [Fact]
    public async Task AmostraRecuperadaNoMeio_ZeraAContagem()
    {
        var s = Criar();
        _coletor.Fila.Enqueue(FakeColetorAmostra.Amostra(livreMb: 300));
        _coletor.Fila.Enqueue(FakeColetorAmostra.Amostra(livreMb: 2000));
        _coletor.Fila.Enqueue(FakeColetorAmostra.Amostra(livreMb: 300));

        for (var i = 0; i < 3; i++)
        {
            await Amostrar(s);
            _a.Clock.Avancar(TimeSpan.FromMinutes(15));
        }

        Assert.Empty(_a.Email.Enviadas);
    }

    [Fact]
    public async Task NoMaximoUmEmailPorHora_DepoisDeUmaHoraAlertaDeNovo()
    {
        _coletor.Padrao = FakeColetorAmostra.Amostra(livreMb: 200);
        var s = Criar();

        // 10:00, 10:15 (alerta), 10:30, 10:45, 11:00 (59 min+ depois do alerta das 10:15 = não), 11:15 (60 min = sim)
        for (var i = 0; i < 6; i++)
        {
            await Amostrar(s);
            if (i == 1)
            {
                Assert.Single(_a.Email.Enviadas);
            }

            if (i is >= 2 and <= 4)
            {
                Assert.Single(_a.Email.Enviadas);
            }

            _a.Clock.Avancar(TimeSpan.FromMinutes(15));
        }

        Assert.Equal(2, _a.Email.Enviadas.Count);
    }

    [Fact]
    public async Task ReinicioDoServico_RespeitaOAlertaJaGravadoNaUltimaHora()
    {
        _coletor.Padrao = FakeColetorAmostra.Amostra(livreMb: 200);
        var s1 = Criar();
        await Amostrar(s1);
        _a.Clock.Avancar(TimeSpan.FromMinutes(15));
        await Amostrar(s1);
        Assert.Single(_a.Email.Enviadas);

        // reinício: instância nova sobre o mesmo diário; duas amostras baixas dentro da mesma hora não reenviam
        var s2 = Criar();
        _a.Clock.Avancar(TimeSpan.FromMinutes(15));
        await Amostrar(s2);
        _a.Clock.Avancar(TimeSpan.FromMinutes(15));
        await Amostrar(s2);

        Assert.Single(_a.Email.Enviadas);
    }

    [Fact]
    public async Task EmailDesabilitado_SoRegistraOAlertaNoDiario()
    {
        _a.OpcoesEmail.Habilitado = false;
        _coletor.Padrao = FakeColetorAmostra.Amostra(livreMb: 200);
        var s = Criar();

        await Amostrar(s);
        await Amostrar(s);

        Assert.Empty(_a.Email.Enviadas);
        Assert.False(Assert.Single(Eventos("saude-alerta")).Ok);
    }

    [Fact]
    public async Task AlertaDesligadoComZero_NuncaEnviaMasContinuaAmostrando()
    {
        _a.Opcoes.AlertaMemoriaLivreMb = 0;
        _coletor.Padrao = FakeColetorAmostra.Amostra(livreMb: 10);
        var s = Criar();

        await Amostrar(s);
        await Amostrar(s);
        await Amostrar(s);

        Assert.Empty(_a.Email.Enviadas);
        Assert.Equal(3, Eventos("saude-amostra").Count);
    }

    [Fact]
    public async Task FalhaNoEnvioDoEmail_NaoLanca()
    {
        _a.Email.Resultado = ResultadoEnvioEmail.Falhou("smtp fora");
        _coletor.Padrao = FakeColetorAmostra.Amostra(livreMb: 200);
        var s = Criar();

        await Amostrar(s);
        await Amostrar(s);

        Assert.False(Assert.Single(Eventos("saude-alerta")).Ok);
    }

    [Fact]
    public async Task FalhaNaColeta_NuncaLanca_ENaoGravaAmostra()
    {
        _coletor.Falha = new InvalidOperationException("contador indisponível");

        await Amostrar(Criar());

        Assert.Empty(Eventos("saude-amostra"));
        Assert.Empty(_a.Email.Enviadas);
    }

    [Fact]
    public async Task SemMedidaDeMemoriaLivre_NaoAlertaENaoQuebra()
    {
        _coletor.Padrao = new AmostraSistema(150, 120, 400, 30, null, null, null, null, Array.Empty<ConsumidorMemoria>());
        var s = Criar();

        await Amostrar(s);
        await Amostrar(s);

        Assert.Empty(_a.Email.Enviadas);
        Assert.Equal(2, Eventos("saude-amostra").Count);
    }

    [Fact]
    public void ColetorReal_NaoLancaEDevolveOProcessoEAteCincoConsumidores()
    {
        var a = new ColetorAmostraSistema().Coletar();

        Assert.True(a.ProcessoWsMb > 0);
        Assert.True(a.Handles > 0);
        Assert.True(a.Threads > 0);
        Assert.InRange(a.Consumidores.Count, 1, ColetorAmostraSistema.MaxConsumidores);
        Assert.All(a.Consumidores, c => Assert.DoesNotContain("\\", c.Nome));   // só o nome do processo, nunca caminho/linha de comando
        if (OperatingSystem.IsWindows())
        {
            Assert.True(a.MemoriaFisicaTotalMb > 0);
            Assert.True(a.MemoriaFisicaLivreMb > 0);
            Assert.True(a.PaginacaoTotalMb >= a.PaginacaoUsadaMb);
        }

        Assert.True(a.Consumidores.SequenceEqual(a.Consumidores.OrderByDescending(c => c.Mb)));
    }

    [Fact]
    public void Opcoes_ValidaIntervaloEAlerta()
    {
        Assert.Empty(new OpcoesDiagnostico().Validar());
        Assert.Equal(15, new OpcoesDiagnostico().IntervaloAmostraMinutos);
        Assert.Equal(500, new OpcoesDiagnostico().AlertaMemoriaLivreMb);
        Assert.Empty(new OpcoesDiagnostico { IntervaloAmostraMinutos = 0, AlertaMemoriaLivreMb = 0 }.Validar());
        Assert.Contains(new OpcoesDiagnostico { IntervaloAmostraMinutos = -1 }.Validar(), p => p.Contains("IntervaloAmostraMinutos"));
        Assert.Contains(new OpcoesDiagnostico { AlertaMemoriaLivreMb = -5 }.Validar(), p => p.Contains("AlertaMemoriaLivreMb"));
    }

    // ---- Relatório das 14:00 ----

    private static EventoDiagnostico Amostra(DateTimeOffset quando, int ws, int privado, int livre, params ConsumidorMemoria[] consumidores) =>
        new()
        {
            Tipo = "saude-amostra", QuandoLocal = quando, ProcessoWsMb = ws, ProcessoPrivadoMb = privado, Handles = ws * 3, Threads = 30 + ws / 10,
            LivreMb = livre, TotalMb = 8192, PaginacaoUsadaMb = 7000 - livre, PaginacaoTotalMb = 12000, Consumidores = consumidores,
        };

    [Fact]
    public void Relatorio_MostraPicoMediaMinimoLivreEConsumidoresDoMomentoDoPico()
    {
        var eventos = new List<EventoDiagnostico>
        {
            Amostra(AmbienteDiagnostico.Local(18, 8, 0), 140, 100, 3000, new ConsumidorMemoria("chrome", 900)),
            Amostra(AmbienteDiagnostico.Local(18, 9, 0), 200, 150, 1200, new ConsumidorMemoria("node", 1070), new ConsumidorMemoria("PowerBI", 845)),
            Amostra(AmbienteDiagnostico.Local(18, 10, 0), 110, 90, 400, new ConsumidorMemoria("sqlservr", 3100), new ConsumidorMemoria("node", 1000)),
            new() { Tipo = "saude-alerta", QuandoLocal = AmbienteDiagnostico.Local(18, 10, 15), Ok = true, LivreMb = 400 },
        };
        var entrada = ExemplosDiagnostico.EntradaFicticia(eventos);

        var m = MontadorDiagnostico.Montar(entrada).Memoria!;

        Assert.Equal(3, m.Amostras);
        Assert.Equal(200, m.PicoWsMb);
        Assert.Equal(AmbienteDiagnostico.Local(18, 9, 0), m.QuandoPicoWs);
        Assert.Equal(150d, m.MediaWsMb!.Value, 3);
        Assert.Equal(150, m.PicoPrivadoMb);
        Assert.Equal(400, m.MinimoLivreMb);
        Assert.Equal(AmbienteDiagnostico.Local(18, 10, 0), m.QuandoMinimoLivre);
        Assert.Equal(8192, m.TotalMb);
        Assert.Equal(new[] { "node", "PowerBI" }, m.ConsumidoresNoPico.Select(c => c.Nome));
        Assert.Equal(new[] { "sqlservr", "node" }, m.ConsumidoresNoMinimoLivre.Select(c => c.Nome));
        Assert.Equal(1, m.AlertasMemoria);
        Assert.Equal(1, m.AlertasMemoriaEnviados);

        var html = System.Net.WebUtility.HtmlDecode(new KitWmsIntegrador.Core.Relatorio.GeradorDiagnostico(Options.Create(new OpcoesRelatorio()))
            .GerarHtml(MontadorDiagnostico.Montar(entrada), ModoLogoRelatorio.DataUri, enxuto: true).Html);
        Assert.Contains("7. Memória do integrador e do servidor", html);
        Assert.Contains("Pico do integrador (working set)", html);
        Assert.Contains("200 MB", html);
        Assert.Contains("150 MB", html);
        Assert.Contains("400 MB de 8.192 MB", html);
        Assert.Contains("Maiores consumidores no pico do integrador", html);
        Assert.Contains("PowerBI", html);
        Assert.Contains("sqlservr", html);
    }

    [Fact]
    public void Relatorio_SemAmostras_DizQueNaoHaDado()
    {
        var m = MontadorDiagnostico.Montar(ExemplosDiagnostico.EntradaFicticia()).Memoria!;

        Assert.Equal(0, m.Amostras);
        Assert.Null(m.PicoWsMb);
        Assert.Null(m.MinimoLivreMb);
        var html = System.Net.WebUtility.HtmlDecode(new KitWmsIntegrador.Core.Relatorio.GeradorDiagnostico(Options.Create(new OpcoesRelatorio()))
            .GerarHtml(MontadorDiagnostico.Montar(ExemplosDiagnostico.EntradaFicticia()), ModoLogoRelatorio.DataUri).Html);
        Assert.Contains("Nenhuma amostra de memória no período", html);
    }

    [Fact]
    public void Relatorio_NomeDeProcessoComSegredoSemeado_ESanitizado()
    {
        var eventos = new List<EventoDiagnostico>
        {
            Amostra(AmbienteDiagnostico.Local(18, 9, 0), 200, 150, 1200, new ConsumidorMemoria("app senha=abc123", 50)),
        };

        var m = MontadorDiagnostico.Montar(ExemplosDiagnostico.EntradaFicticia(eventos)).Memoria!;

        Assert.DoesNotContain("abc123", m.ConsumidoresNoPico[0].Nome);
    }
}
