using System.Diagnostics;
using System.Text.Json;
using KitWmsIntegrador.Core.Agrupamento;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Orquestrador;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;
using KitWmsIntegrador.Core.Tempo;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests.Stress;

/// <summary>Resultado consolidado de um cenário de carga.</summary>
public sealed record ResultadoStress(
    string Cenario,
    int Notas,
    int Transportes,
    int Paralelismo,
    double LatenciaConfiguradaMs,
    double LatenciaMedidaMs,
    long ChamadasWms,
    int Pacotes,
    int Emails,
    double TempoTotalS,
    double NotasPorSeg,
    double PicoWorkingSetMb,
    double PicoGerenciadoMb,
    double AlocadoTotalMb,
    string Gc012,
    double RelatorioHtmlMs,
    double RelatorioPdfMs,
    double MaiorPdfKb,
    int ProcessadosN,
    int ErroN,
    string Integridade,
    string Extras);

/// <summary>
/// Monta a pilha real do pacote (Orquestrador REAL sobre <see cref="FakeClienteWmsCarga"/>) em
/// diretório temporário, com relógio real. Nenhuma chamada HTTP.
/// </summary>
public sealed class AmbienteStress : IDisposable
{
    public AmbienteStress(int paralelismo, int latenciaMs)
    {
        Paralelismo = paralelismo;
        Raiz = Path.Combine(Path.GetTempPath(), "kitwms-stress-" + Guid.NewGuid().ToString("N"));
        Entrada = Path.Combine(Raiz, "entrada");
        Staging = Path.Combine(Raiz, "staging");
        Relatorios = Path.Combine(Raiz, "relatorios");
        Directory.CreateDirectory(Entrada);
        Directory.CreateDirectory(Staging);

        Wms = new FakeClienteWmsCarga(latenciaMs);
        Pastas = new OpcoesPasta { Entrada = Entrada };
        Pacote = new OpcoesPacote
        {
            JanelaColetaSegundos = 1,
            EstabilizacaoSegundos = 0,
            MaxTransportesConcorrentes = paralelismo,
        };

        var clock = new SystemClock();
        var op = Options.Create(new OpcoesOperacionais
        {
            Deposito = "JD",
            CnpjArmazem = "14.605.065/0001-75",
            CnpjDepositante = "01.838.723/0098-50",
            CodigoDepositante = "3509308",
        });
        var orquestrador = new Orquestrador(
            Wms,
            new ConstrutorI50(op, Options.Create(new OpcoesI50())),
            new ConstrutorI10(op),
            new ConstrutorI30(op),
            new ConstrutorI60(op),
            new ClassificadorRetorno(),
            Options.Create(new OpcoesI60 { Habilitada = false }),
            clock,
            NullLogger<Orquestrador>.Instance);

        var diario = new DiarioPacote(Options.Create(Pastas), NullLogger<DiarioPacote>.Instance);
        var organizador = new OrganizadorArquivos(clock, Options.Create(Pastas), NullLogger<OrganizadorArquivos>.Instance);
        var processador = new ProcessadorLote(
            orquestrador, new LeitorNotaFiscalXml(), new AgrupadorTransporte(),
            new ClassificadorCategoriaErro(new ClassificadorRetorno()),
            organizador, diario, clock, Options.Create(Pacote), NullLogger<ProcessadorLote>.Instance);
        var detector = new DetectorArquivoPronto(clock, Options.Create(Pacote), NullLogger<DetectorArquivoPronto>.Instance);
        var opcoesRelatorio = Options.Create(new OpcoesRelatorio { PastaSaida = Relatorios });
        Gerador = new GeradorMedido(new GeradorRelatorio(opcoesRelatorio));
        var telemetria = new RegistradorTelemetria(
            Options.Create(Pastas), Options.Create(new OpcoesMonitoramento()),
            NullLogger<RegistradorTelemetria>.Instance);
        // Fallback de destinatários (Email:Destinatarios) para que o e-mail do pacote seja sempre
        // emitido no stress (a integridade compara e-mails == pacotes).
        var opcoesEmail = Options.Create(new OpcoesEmail { Destinatarios = "stress@exemplo.com" });
        var destinatarios = new FonteDestinatariosFechamento(
            opcoesEmail, telemetria, NullLogger<FonteDestinatariosFechamento>.Instance);
        var fechador = new FechadorPacote(
            Gerador, Email, opcoesEmail, opcoesRelatorio, telemetria,
            Options.Create(new OpcoesMonitoramento()), destinatarios, NullLogger<FechadorPacote>.Instance);
        Gerenciador = new GerenciadorPacote(
            clock, Options.Create(Pastas), Options.Create(Pacote), detector, processador, diario, fechador,
            NullLogger<GerenciadorPacote>.Instance);
    }

    public int Paralelismo { get; }

    public string Raiz { get; }

    public string Entrada { get; }

    public string Staging { get; }

    public string Relatorios { get; }

    public OpcoesPasta Pastas { get; }

    public OpcoesPacote Pacote { get; }

    public FakeClienteWmsCarga Wms { get; }

    public GravadorEmail Email { get; } = new();

    public GeradorMedido Gerador { get; }

    public GerenciadorPacote Gerenciador { get; }

    /// <summary>Nome do arquivo da nota de índice i.</summary>
    public static string NomeArquivo(int i) => $"nf_{i:D6}.xml";

    public static string NumeroNota(int i) => (100000 + i).ToString();

    public static string Transporte(int i, int transportes) => (70000 + (i % transportes)).ToString();

    /// <summary>Gera um XML (ou XML inválido) no diretório indicado e devolve o caminho.</summary>
    public static string GravarXml(string pasta, int i, int transportes, bool invalido)
    {
        var caminho = Path.Combine(pasta, NomeArquivo(i));
        File.WriteAllText(caminho, invalido ? "<nfeProc><NFe>" : AmbientePacote.Xml(NumeroNota(i), Transporte(i, transportes)));
        return caminho;
    }

    /// <summary>Roda ciclos a cada 200 ms (relógio real) até <paramref name="concluido"/> ou estourar o timeout.</summary>
    public async Task<int> RodarAsync(Func<bool> concluido, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        var ciclos = 0;
        using var cts = new CancellationTokenSource(timeout);
        while (true)
        {
            await Gerenciador.CicloAsync(cts.Token);
            ciclos++;
            if (concluido())
            {
                return ciclos;
            }

            if (sw.Elapsed > timeout)
            {
                throw new TimeoutException("Stress não concluiu em " + timeout);
            }

            await Task.Delay(200, cts.Token);
        }
    }

    public int XmlsNaEntrada() => Directory.GetFiles(Entrada, "*.xml", SearchOption.TopDirectoryOnly).Length;

    public bool Terminou() => Gerenciador.Estado == EstadoPacote.Ocioso && XmlsNaEntrada() == 0;

    /// <summary>
    /// Integridade: cada arquivo esperado termina em Processados OU Erro exatamente uma vez; nada fica
    /// na entrada; nenhuma nota repetida/perdida nos relatórios; e-mails == pacotes; categorias de erro batem.
    /// </summary>
    public (bool Ok, string Detalhe, int Processados, int Erro) VerificarIntegridade(
        IReadOnlyCollection<string> esperados, IReadOnlyDictionary<string, int>? erroPorCategoria = null)
    {
        var problemas = new List<string>();
        var proc = Listar("Processados");
        var err = Listar("Erro");
        var todos = proc.Concat(err).ToList();

        var duplicados = todos.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicados.Count > 0)
        {
            problemas.Add($"{duplicados.Count} arquivo(s) duplicado(s) ({duplicados[0]}...)");
        }

        var set = new HashSet<string>(todos, StringComparer.OrdinalIgnoreCase);
        var perdidos = esperados.Where(e => !set.Contains(e)).ToList();
        if (perdidos.Count > 0)
        {
            problemas.Add($"{perdidos.Count} arquivo(s) perdido(s) ({perdidos[0]}...)");
        }

        var extras = set.Where(n => !esperados.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
        if (extras.Count > 0)
        {
            problemas.Add($"{extras.Count} arquivo(s) inesperado(s)");
        }

        if (XmlsNaEntrada() != 0)
        {
            problemas.Add($"{XmlsNaEntrada()} arquivo(s) ainda na entrada");
        }

        var linhas = Gerador.LinhasPorPacote.SelectMany(p => p).ToList();
        if (linhas.Count != esperados.Count)
        {
            problemas.Add($"relatórios somam {linhas.Count} linha(s) para {esperados.Count} arquivo(s)");
        }

        var notasRepetidas = linhas.GroupBy(l => l.NumeroNota).Count(g => g.Count() > 1);
        if (notasRepetidas > 0)
        {
            problemas.Add($"{notasRepetidas} nota(s) repetida(s) nos relatórios");
        }

        if (Email.Enviadas.Count != Gerador.LinhasPorPacote.Count)
        {
            problemas.Add($"{Email.Enviadas.Count} e-mail(s) para {Gerador.LinhasPorPacote.Count} pacote(s)");
        }

        if (erroPorCategoria is not null)
        {
            foreach (var par in erroPorCategoria)
            {
                var real = Listar(Path.Combine("Erro", par.Key)).Count;
                if (real != par.Value)
                {
                    problemas.Add($"Erro\\{par.Key}: {real} (esperado {par.Value})");
                }
            }

            var totalErro = erroPorCategoria.Values.Sum();
            if (err.Count != totalErro)
            {
                problemas.Add($"Erro total {err.Count} (esperado {totalErro})");
            }
        }

        return (problemas.Count == 0, problemas.Count == 0 ? "OK" : string.Join("; ", problemas), proc.Count, err.Count);
    }

    public List<string> Listar(string subpasta)
    {
        var dir = Path.Combine(Entrada, subpasta);
        return Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.xml", SearchOption.AllDirectories).Select(f => Path.GetFileName(f)!).ToList()
            : new List<string>();
    }

    public ResultadoStress Resultado(
        string cenario, int notas, int transportes, TimeSpan tempo, MedidorRecursos medidor,
        (bool Ok, string Detalhe, int Processados, int Erro) integridade, string extras = "") => new(
        cenario, notas, transportes, Paralelismo, Wms.LatenciaMs, Math.Round(Wms.LatenciaMediaMedidaMs, 1),
        Wms.Chamadas, Gerador.LinhasPorPacote.Count, Email.Enviadas.Count,
        Math.Round(tempo.TotalSeconds, 2), Math.Round(notas / tempo.TotalSeconds, 2),
        Math.Round(medidor.PicoWorkingSetMb, 1), Math.Round(medidor.PicoGerenciadoMb, 1),
        Math.Round(medidor.AlocadoTotalMb, 1), $"{medidor.Gen0}/{medidor.Gen1}/{medidor.Gen2}",
        Math.Round(Gerador.TempoHtmlMs, 1), Math.Round(Gerador.TempoPdfMs, 1),
        Math.Round(Gerador.MaiorPdfBytes / 1024.0, 1), integridade.Processados, integridade.Erro,
        integridade.Detalhe, extras);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Raiz))
            {
                Directory.Delete(Raiz, true);
            }
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Acumula os resultados em JSONL (STRESS_SAIDA; padrão na pasta temporária).</summary>
public static class ColetorResultados
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static string Caminho =>
        Environment.GetEnvironmentVariable("STRESS_SAIDA")
        ?? Path.Combine(Path.GetTempPath(), "kitwms-stress-resultados.jsonl");

    public static void Gravar(ResultadoStress r, Xunit.Abstractions.ITestOutputHelper saida)
    {
        var linha = JsonSerializer.Serialize(r, Json);
        File.AppendAllText(Caminho, linha + Environment.NewLine);
        saida.WriteLine(linha);
        Console.WriteLine("[STRESS] " + linha);
    }
}
