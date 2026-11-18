using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Diagnostico;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Dados FICTÍCIOS do "Diagnóstico agente integrador" (sem cliente, sem XML e sem número real) e gerador do exemplo. O
/// exemplo só é gravado com <c>GERAR_EXEMPLOS=1</c>, em <c>.agents/tasks/exemplo-diagnostico.html</c> (não versionado):
/// <c>$env:GERAR_EXEMPLOS='1'; dotnet test KitWmsIntegrador.sln -c Release --filter GerarExemploDiagnostico</c>.
/// </summary>
public class ExemplosDiagnostico
{
    private static readonly TimeSpan Fuso = TimeSpan.FromHours(-3);

    /// <summary>Fim da janela do exemplo: 12/11/2026 14:00 (horário de Brasília).</summary>
    public static readonly DateTimeOffset Fim = new(2026, 11, 12, 14, 0, 0, Fuso);

    /// <summary>Início da janela do exemplo.</summary>
    public static readonly DateTimeOffset Inicio = Fim.AddHours(-24);

    /// <summary>Servidores fictícios do balanceador.</summary>
    public const string BackendA = "BIGipServerWMS#a1b2c3d4";

    /// <summary>Servidor fictício B.</summary>
    public const string BackendB = "BIGipServerWMS#e5f6a7b8";

    /// <summary>Servidor fictício C.</summary>
    public const string BackendC = "BIGipServerWMS#c9d0e1f2";

    private static DateTimeOffset H(int dia, int hora, int minuto, int segundo = 0) => new(2026, 11, dia, hora, minuto, segundo, Fuso);

    private static EventoDiagnostico Token(DateTimeOffset quando, string backend, bool ok, int? status = null, string? motivo = null) =>
        new() { Tipo = "gettoken", QuandoLocal = quando, Ok = ok, Status = status, Backend = backend, Mensagem = motivo, Quantidade = 1, DuracaoMs = 380 };

    /// <summary>Eventos fictícios que cobrem todas as seções do relatório.</summary>
    public static IReadOnlyList<EventoDiagnostico> EventosFicticios()
    {
        var e = new List<EventoDiagnostico>();

        for (var i = 0; i < 60; i++)
        {
            var backend = i % 3 == 0 ? BackendA : i % 3 == 1 ? BackendB : BackendC;
            e.Add(Token(H(11, 15, 0).AddMinutes(i * 20), backend, true));
        }

        // Falha de token de 12/11 09:48 a 09:56 (episódio fictício de 8 minutos, rejeitado só por um servidor).
        e.Add(Token(H(12, 9, 48, 5), BackendB, false, 401));
        e.Add(Token(H(12, 9, 48, 6), BackendB, false, 401));
        e.Add(Token(H(12, 9, 48, 7), BackendB, false, 401));
        e.Add(new EventoDiagnostico { Tipo = "auth-queda", QuandoLocal = H(12, 9, 48, 8), Subtipo = "inicio", Status = 401, Segundos = 60, Quantidade = 1 });
        e.Add(Token(H(12, 9, 49, 10), BackendA, false, null, "timeout"));
        e.Add(new EventoDiagnostico { Tipo = "auth-queda", QuandoLocal = H(12, 9, 49, 11), Subtipo = "escalada", Status = 401, Segundos = 120, Quantidade = 2 });
        e.Add(new EventoDiagnostico { Tipo = "auth-pacote-interrompido", QuandoLocal = H(12, 9, 48, 9), Subtipo = "cooldown", Status = 401, Quantidade = 14 });
        e.Add(Token(H(12, 9, 56, 1), BackendA, true));
        e.Add(new EventoDiagnostico { Tipo = "auth-recuperada", QuandoLocal = H(12, 9, 56, 2) });
        e.Add(Token(H(12, 11, 30), BackendC, false, 503));

        // Cadastros: I10 e I30 disparados pela I50.
        for (var i = 0; i < 4; i++)
        {
            e.Add(new EventoDiagnostico { Tipo = "cadastro-gatilho", QuandoLocal = H(12, 8, 10 + i), Subtipo = "I30", Nota = (6001 + i).ToString(), Mensagem = "Produto 5006566 não cadastrado" });
        }

        e.Add(new EventoDiagnostico { Tipo = "cadastro-gatilho", QuandoLocal = H(12, 8, 20), Subtipo = "I30", Nota = "6005", Mensagem = "Produto 5001203 não cadastrado" });
        e.Add(new EventoDiagnostico { Tipo = "cadastro-gatilho", QuandoLocal = H(12, 8, 30), Subtipo = "I10", Nota = "6006", Mensagem = "Empresa 99999999000199 não localizada" });
        e.Add(new EventoDiagnostico { Tipo = "cadastro-chamada", QuandoLocal = H(12, 8, 11), Subtipo = "I30", Ok = true, Nota = "6001", DuracaoMs = 700 });
        e.Add(new EventoDiagnostico { Tipo = "cadastro-chamada", QuandoLocal = H(12, 8, 12), Subtipo = "I30", Ok = true, Mensagem = "ja-existe", Nota = "6002", DuracaoMs = 650 });
        e.Add(new EventoDiagnostico { Tipo = "cadastro-chamada", QuandoLocal = H(12, 8, 21), Subtipo = "I30", Ok = false, Mensagem = "Unidade de medida inválida", Nota = "6005", DuracaoMs = 640 });
        e.Add(new EventoDiagnostico { Tipo = "cadastro-chamada", QuandoLocal = H(12, 8, 31), Subtipo = "I10", Ok = true, Nota = "6006", DuracaoMs = 910 });

        // I51 e I60.
        for (var i = 0; i < 96; i++)
        {
            e.Add(new EventoDiagnostico { Tipo = "i51-consulta", QuandoLocal = H(11, 14, 15).AddMinutes(i * 15), Subtipo = "TODOS", Ok = true, Mensagem = i % 4 == 0 ? null : "vazio" });
        }

        e.Add(new EventoDiagnostico { Tipo = "i51-consulta", QuandoLocal = H(12, 9, 50), Subtipo = "TODOS", Ok = false, Status = 502 });
        e.Add(new EventoDiagnostico { Tipo = "i51-ciclo", QuandoLocal = H(12, 10, 0), Quantidade = 7, Quantidade2 = 1 });
        e.Add(new EventoDiagnostico { Tipo = "i51-ciclo", QuandoLocal = H(12, 12, 0), Quantidade = 5, Quantidade2 = 0 });
        for (var i = 0; i < 11; i++)
        {
            e.Add(new EventoDiagnostico { Tipo = "i60-desfecho", QuandoLocal = H(12, 10, 5 + i), Subtipo = i < 9 ? "associada" : i == 9 ? "imp" : "rejeitada", Nota = (7001 + i).ToString(), Serie = "6" });
        }

        e.Add(new EventoDiagnostico { Tipo = "i60-alerta", QuandoLocal = H(12, 10, 30), Subtipo = "Rejeitada", Quantidade = 1, Ok = true });
        e.Add(new EventoDiagnostico { Tipo = "i60-alerta", QuandoLocal = H(12, 11, 0), Subtipo = "Atrasada", Quantidade = 2, Ok = true });

        // Pacotes e saúde.
        e.Add(new EventoDiagnostico { Tipo = "arquivos-ignorados", QuandoLocal = H(12, 8, 5), Quantidade = 3 });
        e.Add(new EventoDiagnostico { Tipo = "limpeza", QuandoLocal = H(12, 3, 0), Quantidade = 148, Quantidade2 = 12, Ok = true });
        return e;
    }

    private static LinhaNotaI60 Linha(string nota, string transporte, string onda, string motivo, string mensagem) =>
        new(nota, "6", "PUXADA", transporte, onda, "pacote_20261111_150000", "Problema", "20 h", "11/11 15:10", "INE", motivo, mensagem, string.Empty, 0);

    /// <summary>Estado fictício das notas da I51/I60.</summary>
    public static ModeloRelatorioI60 ModeloI60Ficticio() => new()
    {
        GeradoEm = Fim,
        JanelaInicio = Inicio,
        JanelaFim = Fim,
        Aguardando = 9,
        Atrasadas = 2,
        Ajustes = new SecaoI60<LinhaAjusteI60>(new[] { new LinhaAjusteI60("7003", 1, "PROD-A", 24, 20, -4), new LinhaAjusteI60("7004", 2, "PROD-B", 12, 10, -2) }, 2),
        Problemas = new SecaoI60<LinhaNotaI60>(new[]
        {
            Linha("7010", "1000003", string.Empty, "Rejeitada", "Documento já associado a outro transporte"),
            Linha("7011", "1000004", "TSP-20261111-02-EXEMPLO", "ENC/IMP", "Doccarga encerrado; I60 reenviada como IMP"),
        }, 2),
        ListaAtrasadas = new SecaoI60<LinhaNotaI60>(new[]
        {
            Linha("7020", "1000005", string.Empty, string.Empty, string.Empty),
            Linha("7021", "1000005", string.Empty, string.Empty, string.Empty),
        }, 2),
    };

    /// <summary>Entrada fictícia completa (6 seções com conteúdo).</summary>
    public static EntradaDiagnostico EntradaFicticia(IReadOnlyList<EventoDiagnostico>? eventos = null)
    {
        var resumo = AgregadorTelemetria.Agregar(
            new[]
            {
                new RegistroTelemetria(
                    "pacote_20261112_080000", H(12, 8, 0), H(12, 8, 6), TotalNotas: 42, Integradas: 38, IntegradasRevisarProduto: 2, Pendentes: 1,
                    NaoIntegradas: 3, XmlInvalido: 0, Transportes: 4, TempoTotalMs: 126000, TempoMedioPorNotaMs: 3000,
                    Tentativas: new Dictionary<string, int> { ["1"] = 42 },
                    ChamadasWms: new Dictionary<string, int> { ["I50"] = 42, ["I30"] = 5, ["I10"] = 1 },
                    ChamadasWmsAproximado: false,
                    ErrosPorCategoria: new Dictionary<string, int> { ["ProdutoNaoCadastrado"] = 2, ["CamposObrigatorios"] = 1 },
                    ChamadasWmsMs: 84000),
            },
            Array.Empty<EventoServico>(), Inicio, Fim);

        return new EntradaDiagnostico(
            Fim, Inicio, Fim,
            eventos ?? EventosFicticios(),
            resumo,
            ModeloI60Ficticio(),
            DevolucaoHabilitada: true,
            new[] { new LinhaUsuarioDevolucao("USUARIO.A", 5, 4, 1), new LinhaUsuarioDevolucao("USUARIO.B", 2, 2, 0) },
            new DadosSaude("2026.11.18-1 (exemplo)", H(12, 7, 30), 1, 220L * 1024 * 1024 * 1024, 95L * 1024 * 1024 * 1024, 4200L * 1024 * 1024, 35L * 1024 * 1024, 310L * 1024 * 1024));
    }

    [Fact]
    public void GerarExemploDiagnostico()
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
        var pasta = Path.Combine(raiz!, ".agents", "tasks");
        Directory.CreateDirectory(pasta);
        var modelo = MontadorDiagnostico.Montar(EntradaFicticia());
        var gerador = new GeradorDiagnostico(Options.Create(new OpcoesRelatorio()));
        File.WriteAllText(
            Path.Combine(pasta, "exemplo-diagnostico.html"),
            gerador.GerarHtml(modelo, ModoLogoRelatorio.DataUri).Html, new System.Text.UTF8Encoding(true));
    }
}
