using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Relatório HTML/PDF e e-mail de fechamento com Tipo (PUXADA/TSP), Origem (raiz/Puxada/PuxadaTSP)
/// e a TSP agrupada por onda. Dados 100% sintéticos.
/// </summary>
public sealed class RelatorioTipoOrigemTests : IDisposable
{
    private const string OndaSoro = "TSP-20261112-01-SOROCABA";
    private const string OndaSjc = "TSP-20261112-02-SJC";
    private static readonly DateTimeOffset Em = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "kitwms-rel-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_pasta, true); } catch { /* melhor esforço */ }
    }

    private static LinhaRelatorioErro L(
        string nota, string transporte, string tipo, string origem, string onda = "", bool integrada = true,
        StatusLinhaRelatorio? status = null, string mensagem = "Registrado com sucesso.") =>
        new(nota, transporte, Em, mensagem, integrada, 1, "tx" + nota, "doc" + nota,
            status ?? (integrada ? StatusLinhaRelatorio.Integrada : StatusLinhaRelatorio.NaoIntegrada),
            Array.Empty<string>(), new[] { "I50 ok" }, tipo, origem, onda, onda.Length > 0 ? "XML'S X.zip" : string.Empty);

    private static List<LinhaRelatorioErro> Cenario()
    {
        var linhas = new List<LinhaRelatorioErro>
        {
            L("10", "1000000100", "PUXADA", "raiz"),
            L("11", "1000000101", "PUXADA", "Puxada", integrada: false, status: StatusLinhaRelatorio.Pendente, mensagem: "Pendente de teste"),
        };
        var transportes = new[] { "1000000001", "1000000002", "1000000003", "1000000004", "1000000001" };
        for (var i = 0; i < 5; i++)
        {
            linhas.Add(L((2001 + i).ToString(), transportes[i], "TSP", "PuxadaTSP", OndaSoro,
                integrada: i != 4, mensagem: i == 4 ? "Falha de teste" : "Registrado com sucesso."));
        }

        linhas.Add(L("1001", "1000000010", "TSP", "PuxadaTSP", OndaSjc));
        linhas.Add(L("1002", "1000000011", "TSP", "PuxadaTSP", OndaSjc));
        return linhas;
    }

    private GeradorRelatorio Gerador() =>
        new(Options.Create(new OpcoesRelatorio { PastaSaida = _pasta }));

    [Fact]
    public void Modelo_AgrupaTspPorOnda_EPuxadaPorTransporte_PuxadaPrimeiro()
    {
        var m = ModeloRelatorio.Montar(Cenario());

        Assert.Equal(new[] { "1000000100", "1000000101", OndaSoro, OndaSjc }, m.Grupos.Select(g => g.Transporte));
        var soro = m.Grupos.Single(g => g.Transporte == OndaSoro);
        Assert.True(soro.EhTsp);
        Assert.Equal(5, soro.Notas);
        Assert.Equal(4, soro.Transportes);
        Assert.Equal(OndaSoro, soro.Onda);
        Assert.False(m.Grupos[0].EhTsp);
        Assert.Equal(string.Empty, m.Grupos[0].Onda);

        var r = m.Resumo;
        Assert.Equal(9, r.Total);
        Assert.Equal(2, r.NotasPuxada);
        Assert.Equal(7, r.NotasTsp);
        Assert.Equal(1, r.NotasRaiz);
        Assert.Equal(2, r.OndasTsp);
        Assert.Equal(2, r.Transportes);
        Assert.Contains((OndaSoro, 5, 4), r.NotasPorOnda!);
        Assert.Contains((OndaSjc, 2, 2), r.NotasPorOnda!);
    }

    [Fact]
    public void Modelo_SoPuxada_ContinuaIgual_SemOndas()
    {
        var m = ModeloRelatorio.Montar(new[]
        {
            new LinhaRelatorioErro("1", "T2", Em, "ok", true),
            new LinhaRelatorioErro("2", "T1", Em, "ok", true),
        });

        Assert.Equal(new[] { "T1", "T2" }, m.Grupos.Select(g => g.Transporte));
        Assert.Equal(2, m.Resumo.Transportes);
        Assert.Equal(2, m.Resumo.NotasPuxada);
        Assert.Equal(0, m.Resumo.NotasTsp);
        Assert.Equal(0, m.Resumo.OndasTsp);
    }

    [Fact]
    public void Modelo_LinhasTspSemOnda_FicamEmGrupoProprio_ENaoContamComoOnda()
    {
        var m = ModeloRelatorio.Montar(new[]
        {
            L("quebrado.zip", string.Empty, "TSP", "PuxadaTSP", integrada: false),
            L("2001", "1000000001", "TSP", "PuxadaTSP", OndaSoro),
        });

        Assert.Equal(1, m.Resumo.OndasTsp);
        Assert.Contains(m.Grupos, g => g.Transporte == ModeloRelatorio.SemOnda);
    }

    [Fact]
    public void Html_TemColunasTipoEOrigem_OndaComContagens_ERaizSoNasNotasDaRaiz()
    {
        var html = Gerador().GerarHtml(Cenario(), Em);

        Assert.Contains("<th>Tipo</th>", html);
        Assert.Contains("<th>Origem</th>", html);
        Assert.Contains($"Onda {OndaSoro} (TSP) — 5 notas · 4 transportes", html);
        Assert.Contains($"Onda {OndaSjc} (TSP) — 2 notas · 2 transportes", html);
        Assert.Contains("Transporte 1000000100 — 1 nota", html);
        Assert.Equal(1, Contar(html, "<span class=\"raiz\">raiz</span>"));
        Assert.Contains("PuxadaTSP", html);
        Assert.Contains("<span class=\"tipo tipo-tsp\">TSP</span>", html);
        Assert.Contains("<span class=\"tipo\">PUXADA</span>", html);
        Assert.Contains("Notas da raiz: 1", html);
        Assert.Contains("PUXADA: 2 notas", html);
        Assert.Contains("TSP: 7 notas", html);
        Assert.Contains($"{OndaSoro}: 5 notas, 4 transportes", html);
        Assert.Contains("colspan=\"10\"", html);
    }

    [Fact]
    public void Html_SemNotaDaRaiz_NaoTemEtiquetaRaizNaTabela()
    {
        var html = Gerador().GerarHtml(new[] { L("1", "T1", "PUXADA", "Puxada") }, Em);

        Assert.Equal(0, Contar(html, "<span class=\"raiz\">"));
        Assert.Contains("Notas da raiz: 0", html);
    }

    [Fact]
    public void Pdf_TemTipoOrigemOndaEContagens()
    {
        var pdf = Gerador().GerarPdf(Cenario(), Em);

        Assert.True(pdf.Length > 1000);
        var texto = PdfTextoHelper.Texto(pdf);
        Assert.Contains("Tipo", texto);
        Assert.Contains("Origem", texto);
        Assert.Contains(OndaSoro, texto);
        Assert.Contains("5 notas", texto);
        Assert.Contains("4 transportes", texto);
        Assert.Contains("TSP", texto);
        Assert.Contains("raiz", texto);
        Assert.Contains("Notas da raiz: 1", texto);
    }

    [Fact]
    public void Email_MostraContagemPorTipoRaizEOndasComNotasETransportes()
    {
        var corpo = CorpoEmailResumo.Montar(Cenario(), 0, Em, Em).Html;

        Assert.Contains("PUXADA", corpo);
        Assert.Contains(">TSP<", corpo);
        Assert.Contains("Notas da raiz", corpo);
        Assert.Contains("Ondas TSP", corpo);
        Assert.Contains("Ondas da Puxada TSP", corpo);
        Assert.Contains($"{OndaSoro}: 5 notas, 4 transportes", corpo);
        Assert.Contains($"{OndaSjc}: 2 notas, 2 transportes", corpo);
        // Tabela de problemas ganha Tipo e Origem; a nota 11 (Puxada) e a 2005 (TSP) são problema.
        Assert.Contains(">Tipo</th>", corpo);
        Assert.Contains(">Origem</th>", corpo);
        Assert.Contains("Onda " + OndaSoro, corpo);
        Assert.Contains(">PuxadaTSP</td>", corpo);
    }

    [Fact]
    public void Email_NotaDaRaizComProblema_TemEtiquetaRaiz()
    {
        var corpo = CorpoEmailResumo.Montar(
            new[] { L("10", "1000000100", "PUXADA", "raiz", integrada: false) }, 0, Em, Em).Html;

        Assert.Contains(">raiz</td>", corpo);
    }

    [Fact]
    public void Telemetria_GuardaContagemPorTipo_EAgregaNoResumo()
    {
        var registros = new List<RegistroDiario>
        {
            Registro("10", "1000000100", "PUXADA", "raiz", ""),
            Registro("11", "1000000101", "PUXADA", "Puxada", ""),
            Registro("2001", "1000000001", "TSP", "PuxadaTSP", OndaSoro),
        };

        var reg = MapeadorTelemetria.De(registros, Em, Em.AddMinutes(1), "pacote_x");

        Assert.Equal(2, reg.NotasPuxada);
        Assert.Equal(1, reg.NotasTsp);
        Assert.Equal(1, reg.NotasRaiz);

        var resumo = AgregadorTelemetria.Agregar(new[] { reg, reg }, Array.Empty<EventoServico>(), Em.AddDays(-1), Em.AddDays(1));
        Assert.Equal(4, resumo.NotasPuxada);
        Assert.Equal(2, resumo.NotasTsp);
        Assert.Equal(2, resumo.NotasRaiz);

        var html = CorpoEmailResumoSemanal.Montar(resumo, "semana de teste").Html;
        Assert.Contains("Notas por tipo", html);
        Assert.Contains("Da raiz", html);
    }

    [Fact]
    public void Telemetria_LinhaAntigaSemCamposNovos_LeComZero()
    {
        var amb = new AmbientePacote();
        try
        {
            var reg = new RegistradorTelemetria(
                Options.Create(amb.Pastas), Options.Create(new OpcoesMonitoramento()),
                NullLogger<RegistradorTelemetria>.Instance);
            Directory.CreateDirectory(Path.GetDirectoryName(reg.CaminhoTelemetria)!);
            File.WriteAllText(reg.CaminhoTelemetria,
                "{\"IdPacote\":\"velho\",\"AberturaLocal\":\"2026-11-01T10:00:00-03:00\",\"FechamentoLocal\":\"2026-11-01T10:01:00-03:00\"," +
                "\"TotalNotas\":3,\"Integradas\":3,\"IntegradasRevisarProduto\":0,\"Pendentes\":0,\"NaoIntegradas\":0,\"XmlInvalido\":0," +
                "\"Transportes\":1,\"TempoTotalMs\":60000,\"TempoMedioPorNotaMs\":20000,\"Tentativas\":{\"1\":3}," +
                "\"ChamadasWms\":{\"I50\":3},\"ChamadasWmsAproximado\":true,\"ErrosPorCategoria\":{}}\n");

            var lido = Assert.Single(reg.LerPacotes());

            Assert.Equal("velho", lido.IdPacote);
            Assert.Equal(0, lido.NotasPuxada);
            Assert.Equal(0, lido.NotasTsp);
            Assert.Equal(0, lido.NotasRaiz);
        }
        finally
        {
            amb.Dispose();
        }
    }

    private static RegistroDiario Registro(string nota, string transporte, string tipo, string origem, string onda) =>
        new("arq" + nota + ".xml", false, nota, transporte, Em, "ok", true, 1, StatusLinhaRelatorio.Integrada,
            "tx", "doc", Array.Empty<string>(), new[] { "I50 ok" }, null, DestinoArquivo.Processados, "pacote_x",
            tipo, origem, onda, null);

    private static int Contar(string texto, string trecho)
    {
        var total = 0;
        var i = 0;
        while ((i = texto.IndexOf(trecho, i, StringComparison.Ordinal)) >= 0)
        {
            total++;
            i += trecho.Length;
        }

        return total;
    }
}
