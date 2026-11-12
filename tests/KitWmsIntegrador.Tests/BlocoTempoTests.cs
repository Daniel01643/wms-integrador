using System.Text.Json;
using System.Text.Json.Serialization;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;
using Xunit;

namespace KitWmsIntegrador.Tests;

public class BlocoTempoTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 11, 30, 0, TimeSpan.Zero);

    private static RegistroDiario Reg(
        string nota, string tipo, string onda, double inicioS, double fimS, long wmsMs,
        string transporte = "T1", StatusLinhaRelatorio status = StatusLinhaRelatorio.Integrada, bool comInicio = true) =>
        new("arq.xml", false, nota, transporte, T0.AddSeconds(fimS), "ok", true, 1, status, "", "",
            Array.Empty<string>(), Array.Empty<string>(), null, DestinoArquivo.Processados,
            "pacote_x", tipo, tipo == "TSP" ? "PuxadaTSP" : "Puxada", onda, null,
            comInicio ? T0.AddSeconds(inicioS) : null, wmsMs);

    private static (ResumoRelatorio Resumo, MedicaoTempo Medicao) Montar(
        IReadOnlyList<RegistroDiario> regs, double fimPacoteS = 100, long? espera = 30_000, long? leitura = 4_200)
    {
        var resumo = ModeloRelatorio.Montar(regs.Select(r => r.ParaLinha()).ToList()).Resumo;
        var m = MedicaoTempo.De(regs, T0, T0.AddSeconds(fimPacoteS), espera, leitura);
        return (resumo, m);
    }

    [Fact]
    public void Etapas_SaoCalculadasDosRegistros()
    {
        var regs = new[]
        {
            Reg("1", "PUXADA", "", 40, 50, 6_000),
            Reg("2", "PUXADA", "", 50, 62, 8_000, "T2"),
        };
        var (resumo, m) = Montar(regs);
        Assert.Equal(22_000, m.IntegracaoFaseMs);
        Assert.Equal(14_000, m.ChamadasWmsMs);

        var bloco = BlocoTempo.Montar(resumo, m)!;
        Assert.Contains(bloco.Etapas, e => e.Etapa == "Espera da janela de coleta" && e.Duracao == "30 s");
        Assert.Contains(bloco.Etapas, e => e.Etapa == "Leitura do ZIP/XML" && e.Duracao == "4,2 s");
        Assert.Contains(bloco.Etapas, e => e.Etapa == "Integração no WMS" && e.Duracao == "22 s" && e.PorNota == "7,0 s");
        Assert.Contains(bloco.Resumo, l => l.Rotulo == "Duração total" && l.Valor == "1 min 40 s");
        Assert.Contains(bloco.Resumo, l => l.Rotulo == "Tempo médio por nota" && l.Valor == "50 s");
        Assert.Contains(bloco.Resumo, l => l.Rotulo == "Notas por segundo" && l.Valor == "0,02");
    }

    [Fact]
    public void Ondas_TempoDaPrimeiraAUltimaNota_SemInicioViraTraco()
    {
        var regs = new[]
        {
            Reg("1", "TSP", "TSP-20261112-01-SOROCABA", 40, 45, 2_000, "A"),
            Reg("2", "TSP", "TSP-20261112-01-SOROCABA", 45, 55, 2_000, "B"),
            Reg("3", "TSP", "TSP-20261112-02-SJC", 0, 70, 1_000, "C", comInicio: false),
        };
        var (resumo, m) = Montar(regs);
        var bloco = BlocoTempo.Montar(resumo, m)!;

        Assert.Equal(2, bloco.Ondas.Count);
        var o1 = bloco.Ondas.Single(o => o.Onda == "TSP-20261112-01-SOROCABA");
        Assert.Equal("2", o1.Notas);
        Assert.Equal("2", o1.Transportes);
        Assert.Equal("15 s", o1.Tempo);
        Assert.Equal("-", bloco.Ondas.Single(o => o.Onda == "TSP-20261112-02-SJC").Tempo);
    }

    [Fact]
    public void Puxada_NaoGeraTabelaPorOnda_MesmoComCentenasDeTransportes()
    {
        var regs = Enumerable.Range(1, 300)
            .Select(i => Reg(i.ToString(), "PUXADA", "", i, i + 1, 100, "T" + i))
            .ToList();
        var (resumo, m) = Montar(regs, 400);
        var bloco = BlocoTempo.Montar(resumo, m)!;
        Assert.Empty(bloco.Ondas);
        Assert.Contains(bloco.Resumo, l => l.Rotulo == "Notas" && l.Valor == "300");
    }

    [Fact]
    public void SemNotas_OuDuracaoZero_OmiteMedioENotasPorSegundo()
    {
        var resumo0 = ModeloRelatorio.Montar(Array.Empty<LinhaRelatorioErro>()).Resumo;
        var b0 = BlocoTempo.Montar(resumo0, MedicaoTempo.De(Array.Empty<RegistroDiario>(), T0, T0.AddSeconds(5), null, null))!;
        Assert.DoesNotContain(b0.Resumo, l => l.Rotulo == "Tempo médio por nota" || l.Rotulo == "Notas por segundo");
        Assert.Empty(b0.Etapas);

        var regs = new[] { Reg("1", "PUXADA", "", 0, 0, 0) };
        var resumo = ModeloRelatorio.Montar(regs.Select(r => r.ParaLinha()).ToList()).Resumo;
        var b = BlocoTempo.Montar(resumo, MedicaoTempo.De(regs, T0, T0, null, null))!;
        Assert.Contains(b.Resumo, l => l.Rotulo == "Duração total" && l.Valor == "0 ms");
        Assert.DoesNotContain(b.Resumo, l => l.Rotulo == "Notas por segundo");
        Assert.DoesNotContain(b.Resumo, l => l.Valor.Contains("NaN") || l.Valor.Contains("∞") || l.Valor.Contains("Infinity"));
    }

    [Fact]
    public void SemMedicao_DevolveNull()
    {
        var resumo = ModeloRelatorio.Montar(Array.Empty<LinhaRelatorioErro>()).Resumo;
        Assert.Null(BlocoTempo.Montar(resumo, null));
    }

    [Fact]
    public void EtapasDesconhecidas_SaoOmitidas()
    {
        var regs = new[] { Reg("1", "PUXADA", "", 0, 1, 0, comInicio: false) };
        var (resumo, m) = Montar(regs, 10, espera: null, leitura: null);
        var bloco = BlocoTempo.Montar(resumo, m)!;
        Assert.Empty(bloco.Etapas);
    }

    [Fact]
    public void DiarioAntigo_SemCamposNovos_Desserializa()
    {
        var json = "{\"Arquivo\":\"a.xml\",\"XmlInvalido\":false,\"NumeroNota\":\"1\",\"NumeroTransporte\":\"T\"," +
                   "\"Horario\":\"2026-11-12T11:30:00+00:00\",\"Mensagem\":\"ok\",\"Integrada\":true,\"Tentativas\":1," +
                   "\"Status\":\"Integrada\",\"IdTransacao\":\"\",\"IdDocumento\":\"\",\"ProdutosProvisorios\":[]," +
                   "\"Historico\":[],\"Categoria\":null,\"Destino\":\"Processados\"}";
        var opcoes = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var r = JsonSerializer.Deserialize<RegistroDiario>(json, opcoes)!;
        Assert.Null(r.InicioNota);
        Assert.Equal(0, r.DuracaoWmsMs);
    }
}
