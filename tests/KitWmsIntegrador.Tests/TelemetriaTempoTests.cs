using System.Text.Json;
using System.Text.Json.Serialization;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>Campos de tempo da telemetria: retrocompatibilidade, mapeamento e média de WMS por nota.</summary>
public class TelemetriaTempoTests
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private const string LinhaAntiga =
        "{\"IdPacote\":\"pacote_20260101_080000\",\"AberturaLocal\":\"2026-01-01T08:00:00-03:00\"," +
        "\"FechamentoLocal\":\"2026-01-01T08:01:00-03:00\",\"TotalNotas\":3,\"Integradas\":2,\"IntegradasRevisarProduto\":0," +
        "\"Pendentes\":0,\"NaoIntegradas\":1,\"XmlInvalido\":0,\"Transportes\":2,\"TempoTotalMs\":60000," +
        "\"TempoMedioPorNotaMs\":20000,\"Tentativas\":{\"1\":2,\"2\":1,\"3+\":0},\"ChamadasWms\":{\"I50\":3}," +
        "\"ChamadasWmsAproximado\":true,\"ErrosPorCategoria\":{\"TentativasEsgotadas\":1}}";

    private static RegistroTelemetria Novo(int notas, long chamadasMs, string id = "p") => new(
        id,
        new DateTimeOffset(2026, 11, 12, 8, 0, 0, TimeSpan.FromHours(-3)),
        new DateTimeOffset(2026, 11, 12, 8, 1, 0, TimeSpan.FromHours(-3)),
        notas, notas, 0, 0, 0, 0, 1, 60_000, 60_000 / Math.Max(1, notas),
        new Dictionary<string, int>(), new Dictionary<string, int>(), true, new Dictionary<string, int>(),
        EsperaJanelaMs: 30_000, LeituraMs: 4_200, IntegracaoWmsMs: 25_000, ChamadasWmsMs: chamadasMs, TempoRelatorioMs: 800,
        Ondas: new[] { new TelemetriaOnda("TSP-20261112-01-SJC", notas, 1, 12_000) });

    [Fact]
    public void LinhaAntiga_Desserializa_ComNovosCamposZerados()
    {
        var r = JsonSerializer.Deserialize<RegistroTelemetria>(LinhaAntiga, Json)!;
        Assert.Equal(3, r.TotalNotas);
        Assert.Equal(0, r.EsperaJanelaMs);
        Assert.Equal(0, r.LeituraMs);
        Assert.Equal(0, r.IntegracaoWmsMs);
        Assert.Equal(0, r.ChamadasWmsMs);
        Assert.Equal(0, r.TempoRelatorioMs);
        Assert.Null(r.Ondas);
    }

    [Fact]
    public void IdaEVolta_PreservaOsCamposNovos()
    {
        var json = JsonSerializer.Serialize(Novo(4, 40_000), Json);
        var r = JsonSerializer.Deserialize<RegistroTelemetria>(json, Json)!;
        Assert.Equal(30_000, r.EsperaJanelaMs);
        Assert.Equal(4_200, r.LeituraMs);
        Assert.Equal(25_000, r.IntegracaoWmsMs);
        Assert.Equal(40_000, r.ChamadasWmsMs);
        Assert.Equal(800, r.TempoRelatorioMs);
        var onda = Assert.Single(r.Ondas!);
        Assert.Equal(("TSP-20261112-01-SJC", 4, 1, 12_000L), (onda.Onda, onda.Notas, onda.Transportes, onda.DuracaoMs));
    }

    [Fact]
    public void Registrador_LeArquivoQueMisturaLinhaAntigaENova()
    {
        var entrada = Path.Combine(Path.GetTempPath(), "wms-tel-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(entrada);
            var pastas = new OpcoesPasta { Entrada = entrada, Estado = "Estado" };
            var reg = new RegistradorTelemetria(
                Options.Create(pastas), Options.Create(new OpcoesMonitoramento()), NullLogger<RegistradorTelemetria>.Instance);
            Directory.CreateDirectory(reg.PastaBase);
            File.WriteAllText(reg.CaminhoTelemetria, LinhaAntiga + Environment.NewLine);
            reg.RegistrarPacote(Novo(2, 5_000, "novo"));

            var lidos = reg.LerPacotes();
            Assert.Equal(2, lidos.Count);
            Assert.Equal(0, lidos[0].ChamadasWmsMs);
            Assert.Equal(5_000, lidos[1].ChamadasWmsMs);
        }
        finally
        {
            if (Directory.Exists(entrada))
            {
                Directory.Delete(entrada, true);
            }
        }
    }

    [Fact]
    public void Mapeador_SemMedicao_DeixaZero_ComMedicao_Preenche()
    {
        var t0 = new DateTimeOffset(2026, 11, 12, 11, 30, 0, TimeSpan.Zero);
        var reg = new RegistroDiario(
            "a.xml", false, "1", "T1", t0.AddSeconds(10), "ok", true, 1, StatusLinhaRelatorio.Integrada, "", "",
            Array.Empty<string>(), Array.Empty<string>(), null, DestinoArquivo.Processados,
            "pacote_x", "TSP", "PuxadaTSP", "TSP-20261112-01-SJC", "a.zip", t0.AddSeconds(4), 2_000);
        var registros = new[] { reg };

        var sem = MapeadorTelemetria.De(registros, t0, t0.AddSeconds(20), "pacote_x");
        Assert.Equal(0, sem.ChamadasWmsMs);
        Assert.Equal(0, sem.EsperaJanelaMs);
        Assert.Equal(0, sem.TempoRelatorioMs);

        var medicao = MedicaoTempo.De(registros, t0, t0.AddSeconds(20), 3_000, 500);
        var com = MapeadorTelemetria.De(registros, t0, t0.AddSeconds(20), "pacote_x", medicao, 700);
        Assert.Equal(3_000, com.EsperaJanelaMs);
        Assert.Equal(500, com.LeituraMs);
        Assert.Equal(6_000, com.IntegracaoWmsMs);
        Assert.Equal(2_000, com.ChamadasWmsMs);
        Assert.Equal(700, com.TempoRelatorioMs);
        var onda = Assert.Single(com.Ondas!);
        Assert.Equal(6_000, onda.DuracaoMs);
    }

    private static ResumoPeriodo Agregar(params RegistroTelemetria[] pacotes) =>
        AgregadorTelemetria.Agregar(
            pacotes, Array.Empty<EventoServico>(),
            new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.FromHours(-3)),
            new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.FromHours(-3)));

    [Fact]
    public void Agregador_TempoWmsPorNota_IgnoraPacotesAntigos()
    {
        var antigo = JsonSerializer.Deserialize<RegistroTelemetria>(
            LinhaAntiga.Replace("2026-01-01T08:00:00-03:00", "2026-11-12T08:00:00-03:00")
                       .Replace("2026-01-01T08:01:00-03:00", "2026-11-12T08:01:00-03:00"), Json)!;
        // 10 notas com 50 s de chamadas = 5 s por nota; o pacote antigo (3 notas) não puxa a média para baixo.
        var r = Agregar(antigo, Novo(10, 50_000, "novo"));
        Assert.Equal(5_000, r.TempoWmsMedioPorNotaMs);

        Assert.Equal(0, Agregar(antigo).TempoWmsMedioPorNotaMs);
    }

    [Fact]
    public void ResumoSemanal_LinhaDeWmsPorNota_SoAparecendoQuandoMaiorQueZero()
    {
        var antigo = JsonSerializer.Deserialize<RegistroTelemetria>(
            LinhaAntiga.Replace("2026-01-01T08:00:00-03:00", "2026-11-12T08:00:00-03:00")
                       .Replace("2026-01-01T08:01:00-03:00", "2026-11-12T08:01:00-03:00"), Json)!;
        var sem = CorpoEmailResumoSemanal.Montar(Agregar(antigo), "09/11 a 15/11");
        Assert.DoesNotContain("Integração no WMS por nota", sem.Html);

        var com = CorpoEmailResumoSemanal.Montar(Agregar(Novo(10, 50_000)), "09/11 a 15/11");
        Assert.Contains("Integração no WMS por nota: <b>5.0 s</b>", com.Html);
    }
}
