using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Gera a evidência do resumo semanal em <c>docs/exemplos</c> (HTML + PDF) a partir de dados
/// fictícios. Só grava quando <c>GERAR_EXEMPLOS=1</c>:
/// <c>$env:GERAR_EXEMPLOS='1'; dotnet test KitWmsIntegrador.sln --filter GerarExemploResumo</c>.
/// </summary>
public class ExemplosResumoSemanal
{
    private static readonly TimeSpan Fuso = TimeSpan.FromHours(-3);
    private const string PeriodoLabel = "02/06/2025 a 08/06/2025";

    private static ResumoPeriodo ResumoFicticio()
    {
        var inicio = new DateTimeOffset(2025, 6, 2, 0, 0, 0, Fuso);
        var fim = inicio.AddDays(7);

        DateTimeOffset Em(int dia, int hora) => new(2025, 6, dia, hora, 0, 0, Fuso);

        RegistroTelemetria Pacote(
            string id, DateTimeOffset fechamento, int notas, int integradas, int revisar,
            int pendentes, int naoIntegradas, int xmlInvalido, int transportes, long tempoTotalMs,
            IReadOnlyDictionary<string, int> chamadas, IReadOnlyDictionary<string, int> erros) =>
            new(
                id, fechamento.AddHours(-1), fechamento, notas, integradas, revisar, pendentes,
                naoIntegradas, xmlInvalido, transportes, tempoTotalMs,
                TempoMedioPorNotaMs: tempoTotalMs / Math.Max(1, notas),
                Tentativas: new Dictionary<string, int> { ["1"] = integradas, ["2"] = pendentes, ["3+"] = naoIntegradas },
                ChamadasWms: chamadas, ChamadasWmsAproximado: true, ErrosPorCategoria: erros);

        var pacotes = new[]
        {
            Pacote("p1", Em(2, 9), 42, 40, 2, 1, 1, 0, 3, 84000,
                new Dictionary<string, int> { ["I50"] = 42, ["I10"] = 4, ["I30"] = 3 },
                new Dictionary<string, int> { ["ProdutoNaoCadastrado"] = 1 }),
            Pacote("p2", Em(3, 14), 55, 53, 1, 1, 1, 0, 4, 99000,
                new Dictionary<string, int> { ["I50"] = 55, ["I10"] = 2 },
                new Dictionary<string, int> { ["EmpresaNaoLocalizada"] = 1 }),
            Pacote("p3", Em(4, 20), 30, 29, 0, 0, 1, 1, 2, 60000,
                new Dictionary<string, int> { ["I50"] = 30 },
                new Dictionary<string, int> { ["XmlInvalido"] = 1, ["TentativasEsgotadas"] = 1 }),
            Pacote("p4", Em(6, 10), 48, 47, 1, 0, 0, 0, 3, 90000,
                new Dictionary<string, int> { ["I50"] = 48, ["I30"] = 1 },
                new Dictionary<string, int>()),
        };

        var eventos = new[]
        {
            new EventoServico("servico-iniciado", new DateTimeOffset(2025, 5, 30, 8, 0, 0, Fuso), "1.0.0"),
            new EventoServico("servico-iniciado", Em(4, 7), "1.0.0"),
        };

        return AgregadorTelemetria.Agregar(pacotes, eventos, inicio, fim);
    }

    [Fact]
    public void GerarExemploResumo()
    {
        if (Environment.GetEnvironmentVariable("GERAR_EXEMPLOS") != "1")
        {
            return;
        }

        var raiz = AppContext.BaseDirectory;
        while (raiz is not null && !File.Exists(Path.Combine(raiz, "KitWmsIntegrador.sln")))
        {
            raiz = Path.GetDirectoryName(raiz);
        }

        Assert.NotNull(raiz);
        var pasta = Path.Combine(raiz!, "docs", "exemplos");
        Directory.CreateDirectory(pasta);

        var resumo = ResumoFicticio();
        var corpo = CorpoEmailResumoSemanal.Montar(resumo, PeriodoLabel);
        File.WriteAllText(
            Path.Combine(pasta, "resumo-semanal-exemplo.html"),
            corpo.Html, new System.Text.UTF8Encoding(true));

        var gerador = new GeradorResumoSemanal(Options.Create(new OpcoesRelatorio()));
        File.WriteAllBytes(
            Path.Combine(pasta, "resumo-semanal-exemplo.pdf"),
            gerador.GerarPdf(resumo, PeriodoLabel));
    }
}
