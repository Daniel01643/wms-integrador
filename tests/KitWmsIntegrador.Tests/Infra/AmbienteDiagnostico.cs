using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Diagnostico;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests.Infra;

/// <summary>Provedor de saúde falso: valores fixos, sem tocar o disco.</summary>
public sealed class FakeSaudeSistema : IProvedorSaudeSistema
{
    public long? Livre { get; set; } = 50L * 1024 * 1024 * 1024;

    public long? Tamanho { get; set; } = 3L * 1024 * 1024;

    public List<string> Consultados { get; } = new();

    public long? EspacoLivre(string caminho)
    {
        Consultados.Add("livre:" + caminho);
        return Livre;
    }

    public long? TamanhoPasta(string caminho)
    {
        Consultados.Add("tamanho:" + caminho);
        return Tamanho;
    }
}

/// <summary>
/// Pilha real do diagnóstico (diário, telemetria, coletor, enviador, marca e serviço) sobre pastas temporárias, com
/// relógio e e-mail falsos. <see cref="NovoServico"/> recria o serviço sobre a MESMA pasta (simula reinício).
/// </summary>
public sealed class AmbienteDiagnostico : IDisposable
{
    public AmbienteDiagnostico()
    {
        Amb = new AmbienteI51();
        Saida = Path.Combine(Amb.Raiz, "relatorios");
        Telemetria = new RegistradorTelemetria(
            Options.Create(Amb.Pastas), Options.Create(new OpcoesMonitoramento()), NullLogger<RegistradorTelemetria>.Instance);
        Diario = new RegistroDiagnosticoArquivo(
            Amb.Clock, Options.Create(Amb.Pastas), Options.Create(new OpcoesLimpeza()), NullLogger<RegistroDiagnosticoArquivo>.Instance);
        Destinatarios = new FonteDestinatariosDiagnostico(
            Telemetria, Options.Create(Opcoes), NullLogger<FonteDestinatariosDiagnostico>.Instance);
        Directory.CreateDirectory(Path.GetDirectoryName(Destinatarios.Caminho)!);
        File.WriteAllText(Destinatarios.Caminho, "# TI\nti1@exemplo.com\nti2@exemplo.com\n");
        Opcoes.Destinatarios = "opcao@exemplo.com";
        OpcoesEmail.Habilitado = true;
        OpcoesEmail.Host = "smtp.exemplo.local";
        OpcoesEmail.Remetente = "kitwms@exemplo.local";
        Marca = new MarcaUltimoDiagnostico(Telemetria, NullLogger<MarcaUltimoDiagnostico>.Instance);
        NovoServico();
    }

    public AmbienteI51 Amb { get; }

    public string Saida { get; }

    public FakeClock Clock => Amb.Clock;

    public RegistradorTelemetria Telemetria { get; }

    public RegistroDiagnosticoArquivo Diario { get; }

    public FonteDestinatariosDiagnostico Destinatarios { get; }

    public MarcaUltimoDiagnostico Marca { get; }

    public OpcoesDiagnostico Opcoes { get; } = new();

    public OpcoesEmail OpcoesEmail { get; } = new();

    public OpcoesDevolucao Devolucao { get; } = new();

    public GravadorEmail Email { get; } = new();

    public FakeSaudeSistema Saude { get; } = new();

    public ColetorDiagnostico Coletor { get; private set; } = null!;

    public ServicoDiagnostico Servico { get; private set; } = null!;

    public string PastaHtml => Path.Combine(Saida, "diagnostico");

    public string[] Htmls => Directory.Exists(PastaHtml) ? Directory.GetFiles(PastaHtml, "*.html") : Array.Empty<string>();

    /// <summary>Recria coletor, enviador e serviço sobre as mesmas pastas.</summary>
    public void NovoServico()
    {
        var monit = Options.Create(new OpcoesMonitoramento());
        var opcoesI60 = new OpcoesRelatorioI60();
        var destinatariosI60 = new FonteDestinatariosRelatorioI60(
            Telemetria, new FonteDestinatariosResumo(Telemetria, monit, NullLogger<FonteDestinatariosResumo>.Instance),
            Options.Create(opcoesI60));
        var enviadorI60 = new EnviadorRelatorioDiarioI60(
            Email, new GeradorRelatorioI60(Options.Create(new OpcoesRelatorio())), destinatariosI60, Options.Create(opcoesI60),
            Options.Create(new OpcoesRelatorio { PastaSaida = Saida }), Options.Create(OpcoesEmail),
            NullLogger<EnviadorRelatorioDiarioI60>.Instance);
        var servicoI60 = new ServicoRelatorioDiarioI60(
            Amb.Clock, Amb.Repositorio, Amb.Brutos, Amb.Orfaos,
            new MarcaUltimoRelatorioI60(Telemetria, NullLogger<MarcaUltimoRelatorioI60>.Instance), enviadorI60,
            Options.Create(Amb.I51), Options.Create(Amb.I60), Options.Create(opcoesI60), Options.Create(Devolucao),
            NullLogger<ServicoRelatorioDiarioI60>.Instance);

        Coletor = new ColetorDiagnostico(
            Diario, Telemetria, servicoI60,
            new LeitorAuditoriaDevolucao(Options.Create(Amb.Pastas), Options.Create(Devolucao), NullLogger<LeitorAuditoriaDevolucao>.Instance),
            Saude, Options.Create(Amb.Pastas), Options.Create(Amb.I51), Options.Create(Devolucao), Options.Create(Opcoes),
            NullLogger<ColetorDiagnostico>.Instance);
        var enviador = new EnviadorDiagnostico(
            Email, new GeradorDiagnostico(Options.Create(new OpcoesRelatorio())), Destinatarios, Options.Create(Opcoes),
            Options.Create(new OpcoesRelatorio { PastaSaida = Saida }), Options.Create(OpcoesEmail),
            NullLogger<EnviadorDiagnostico>.Instance);
        Servico = new ServicoDiagnostico(
            Amb.Clock, Coletor, Marca, enviador, Options.Create(Opcoes), NullLogger<ServicoDiagnostico>.Instance);
    }

    /// <summary>Leva o relógio para a data/hora LOCAL informada (novembro de 2026).</summary>
    public void Ir(int dia, int hora, int minuto)
    {
        var local = new DateTime(2026, 11, dia, hora, minuto, 0, DateTimeKind.Unspecified);
        var alvo = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        Clock.Avancar(alvo.ToUniversalTime() - Clock.UtcNow);
    }

    /// <summary>Instante LOCAL em novembro de 2026.</summary>
    public static DateTimeOffset Local(int dia, int hora, int minuto)
    {
        var local = new DateTime(2026, 11, dia, hora, minuto, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    public void Dispose() => Amb.Dispose();
}
