using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Pacotes SINTÉTICOS (nenhum dado real) com medição de tempo, usados nos testes do relatório/e-mail e
/// na geração dos exemplos de <c>docs\exemplos</c>.
/// </summary>
internal static class TempoSintetico
{
    public static readonly DateTimeOffset Inicio = new(2026, 11, 12, 11, 30, 15, TimeSpan.Zero);

    public const string IdPacote = "pacote_20261112_083015";

    private static RegistroDiario Reg(
        string nota, string transporte, double inicioS, double fimS, long wmsMs, string tipo = "PUXADA",
        string onda = "", string zip = "", bool integrada = true, string mensagem = "Registrado com sucesso.") =>
        new("arq-" + nota + ".xml", false, nota, transporte, Inicio.AddSeconds(fimS), mensagem, integrada, integrada ? 1 : 3,
            integrada ? StatusLinhaRelatorio.Integrada : StatusLinhaRelatorio.NaoIntegrada,
            "40" + nota, integrada ? "14" + nota : "", Array.Empty<string>(), Array.Empty<string>(), null,
            integrada ? DestinoArquivo.Processados : DestinoArquivo.Erro, IdPacote, tipo,
            tipo == "TSP" ? "PuxadaTSP" : "Puxada", onda, string.IsNullOrEmpty(zip) ? null : zip,
            Inicio.AddSeconds(inicioS), wmsMs);

    /// <summary>Registros de um pacote só de TSP: 2 ondas (SJC e SOROCABA).</summary>
    public static IReadOnlyList<RegistroDiario> RegistrosTsp()
    {
        const string sjc = "TSP-20261112-01-SJC";
        const string soro = "TSP-20261112-02-SOROCABA";
        return new[]
        {
            Reg("100101", "1000000001", 36, 42, 5200, "TSP", sjc, "XML'S SJC.zip"),
            Reg("100102", "1000000002", 42, 49, 5900, "TSP", sjc, "XML'S SJC.zip"),
            Reg("200101", "1000000001", 49, 56, 5800, "TSP", soro, "XML'S SOROCABA.zip"),
            Reg("200102", "1000000002", 56, 62, 5400, "TSP", soro, "XML'S SOROCABA.zip"),
            Reg("200103", "1000000003", 62, 70, 6600, "TSP", soro, "XML'S SOROCABA.zip", false, "Produto não cadastrado após 3 tentativas."),
            Reg("200104", "1000000004", 70, 77, 6000, "TSP", soro, "XML'S SOROCABA.zip"),
            Reg("200105", "1000000001", 77, 84, 6100, "TSP", soro, "XML'S SOROCABA.zip"),
        };
    }

    /// <summary>Registros de um pacote só de PUXADA: vários transportes, nenhuma onda.</summary>
    public static IReadOnlyList<RegistroDiario> RegistrosPuxada()
    {
        return new[]
        {
            Reg("300101", "1000000500", 36, 43, 6200),
            Reg("300102", "1000000500", 43, 50, 6300),
            Reg("300103", "1000000501", 50, 57, 6100),
            Reg("300104", "1000000502", 57, 64, 6500, integrada: false, mensagem: "Transporte já existe com outra carga."),
            Reg("300105", "1000000503", 64, 71, 6000),
        };
    }

    /// <summary>Medição derivada dos registros, com janela de 30 s e leitura de 4,2 s.</summary>
    public static MedicaoTempo Medicao(IReadOnlyList<RegistroDiario> registros, double fimS = 95) =>
        MedicaoTempo.De(registros, Inicio, Inicio.AddSeconds(fimS), 30_000, 4_200);

    public static (IReadOnlyList<LinhaRelatorioErro> Linhas, DadosPacote Dados) Tsp() => Montar(RegistrosTsp());

    public static (IReadOnlyList<LinhaRelatorioErro> Linhas, DadosPacote Dados) Puxada() => Montar(RegistrosPuxada());

    public static (IReadOnlyList<LinhaRelatorioErro> Linhas, DadosPacote Dados) Mista() =>
        Montar(RegistrosTsp().Concat(RegistrosPuxada()).ToList());

    private static (IReadOnlyList<LinhaRelatorioErro>, DadosPacote) Montar(IReadOnlyList<RegistroDiario> registros) =>
        (registros.Select(r => r.ParaLinha()).ToList(), new DadosPacote(IdPacote, Medicao(registros)));
}
