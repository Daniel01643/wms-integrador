using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class EnviadorResumoSemanalTests : IDisposable
{
    private const string PeriodoLabel = "02/06/2025 a 08/06/2025";
    private static readonly TimeSpan Fuso = TimeSpan.FromHours(-3);

    private readonly string _pasta;
    private readonly RegistradorTelemetria _telemetria;
    private readonly FonteDestinatariosResumo _destinatarios;
    private readonly GeradorResumoSemanal _gerador;

    public EnviadorResumoSemanalTests()
    {
        _pasta = Path.Combine(Path.GetTempPath(), "wms-env-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_pasta);

        var monitoramento = new OpcoesMonitoramento { CaminhoTelemetria = _pasta, AssuntoPrefixo = "[KitWms]" };
        _telemetria = new RegistradorTelemetria(
            Options.Create(new OpcoesPasta()),
            Options.Create(monitoramento),
            NullLogger<RegistradorTelemetria>.Instance);
        _destinatarios = new FonteDestinatariosResumo(
            _telemetria, Options.Create(monitoramento), NullLogger<FonteDestinatariosResumo>.Instance);
        _gerador = new GeradorResumoSemanal(Options.Create(new OpcoesRelatorio()));

        // Lista dedicada de destinatários (fora do git) para o override.
        File.WriteAllText(_destinatarios.Caminho, "resumo@exemplo.com\n# coment\noutro@exemplo.com\n");
    }

    private static ResumoPeriodo Resumo()
    {
        var inicio = new DateTimeOffset(2025, 6, 2, 0, 0, 0, Fuso);
        var fim = inicio.AddDays(7);
        var pacotes = new[]
        {
            new RegistroTelemetria(
                "p1", inicio.AddHours(8), new DateTimeOffset(2025, 6, 2, 9, 0, 0, Fuso),
                TotalNotas: 5, Integradas: 5, IntegradasRevisarProduto: 0, Pendentes: 0, NaoIntegradas: 0,
                XmlInvalido: 0, Transportes: 1, TempoTotalMs: 10000, TempoMedioPorNotaMs: 2000,
                Tentativas: new Dictionary<string, int> { ["1"] = 5 },
                ChamadasWms: new Dictionary<string, int> { ["I50"] = 5 },
                ChamadasWmsAproximado: true,
                ErrosPorCategoria: new Dictionary<string, int>()),
        };
        return AgregadorTelemetria.Agregar(pacotes, Array.Empty<EventoServico>(), inicio, fim);
    }

    private EnviadorResumoSemanal Criar(FakeEnviadorEmail email, OpcoesEmail opcoesEmail) =>
        new(
            email, _gerador, _destinatarios, _telemetria,
            Options.Create(new OpcoesMonitoramento { CaminhoTelemetria = _pasta, AssuntoPrefixo = "[KitWms]" }),
            Options.Create(opcoesEmail),
            NullLogger<EnviadorResumoSemanal>.Instance);

    [Fact]
    public async Task SmtpConfigurado_EnviaComAnexoImagemEOverride()
    {
        var email = new FakeEnviadorEmail();
        var opcoes = new OpcoesEmail { Habilitado = true, Host = "smtp.test", Remetente = "de@test" };
        var enviador = Criar(email, opcoes);

        await enviador.EnviarAsync(Resumo(), PeriodoLabel, CancellationToken.None);

        var msg = Assert.Single(email.Enviadas);
        Assert.False(string.IsNullOrWhiteSpace(msg.CorpoHtml));
        Assert.Contains("Resumo semanal de uso - " + PeriodoLabel, msg.Assunto);
        var anexo = Assert.Single(msg.Anexos);
        Assert.Equal(EnviadorResumoSemanal.NomeAnexoPdf, anexo.Nome);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(anexo.Conteudo, 0, 4));
        // Destinatários vêm do arquivo txt (override), não de OpcoesEmail.
        Assert.NotNull(msg.DestinatariosOverride);
        Assert.Equal(new[] { "resumo@exemplo.com", "outro@exemplo.com" }, msg.DestinatariosOverride!);
        // Nada gravado em disco quando o envio deu certo.
        Assert.False(Directory.Exists(Path.Combine(_pasta, EnviadorResumoSemanal.SubpastaResumos)));
    }

    [Fact]
    public async Task SmtpDesabilitado_NaoLanca_GravaHtmlEPdfEmDisco()
    {
        var email = new FakeEnviadorEmail();
        var opcoes = new OpcoesEmail { Habilitado = false };
        var enviador = Criar(email, opcoes);

        await enviador.EnviarAsync(Resumo(), PeriodoLabel, CancellationToken.None);

        Assert.Empty(email.Enviadas);
        var pastaResumos = Path.Combine(_pasta, EnviadorResumoSemanal.SubpastaResumos);
        Assert.True(Directory.Exists(pastaResumos));
        Assert.Single(Directory.GetFiles(pastaResumos, "resumo_*.html"));
        var pdfs = Directory.GetFiles(pastaResumos, "resumo_*.pdf");
        var pdf = Assert.Single(pdfs);
        var bytes = File.ReadAllBytes(pdf);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
        {
            Directory.Delete(_pasta, recursive: true);
        }
    }

    private sealed class FakeEnviadorEmail : IEnviadorEmail
    {
        public List<MensagemEmail> Enviadas { get; } = new();

        public Task<ResultadoEnvioEmail> EnviarAsync(MensagemEmail mensagem, CancellationToken ct)
        {
            Enviadas.Add(mensagem);
            return Task.FromResult(ResultadoEnvioEmail.Enviado());
        }
    }
}
