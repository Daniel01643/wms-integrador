using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>O alerta de queda/recuperação do GetToken vai só aos destinatários de diagnóstico (TI), nunca aos do fechamento.</summary>
public sealed class AlertaAutenticacaoDestinatariosTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "wms-alertadest-" + Guid.NewGuid().ToString("N"));
    private readonly GravadorEmail _email = new();

    public AlertaAutenticacaoDestinatariosTests() => Directory.CreateDirectory(_pasta);

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
        {
            Directory.Delete(_pasta, recursive: true);
        }
    }

    private AlertaAutenticacaoWmsEmail Criar(
        OpcoesEmail opEmail, OpcoesDiagnostico opDiagnostico, bool alertaLigado = true)
    {
        var telemetria = new RegistradorTelemetria(
            Options.Create(new OpcoesPasta()),
            Options.Create(new OpcoesMonitoramento { CaminhoTelemetria = _pasta }),
            NullLogger<RegistradorTelemetria>.Instance);
        var fonte = new FonteDestinatariosDiagnostico(
            telemetria, Options.Create(opDiagnostico), NullLogger<FonteDestinatariosDiagnostico>.Instance);
        return new AlertaAutenticacaoWmsEmail(
            _email, fonte, Options.Create(opEmail), Options.Create(new OpcoesWms { AlertaAutenticacao = alertaLigado }),
            NullLogger<AlertaAutenticacaoWmsEmail>.Instance);
    }

    [Fact]
    public async Task QuedaERecuperacao_VaoSoAosDestinatariosDeDiagnostico_NuncaAosDoFechamento()
    {
        // Fechamento configurado por txt E por opção: nenhum dos dois pode receber o alerta técnico.
        var txtFechamento = Path.Combine(_pasta, "fechamento.txt");
        File.WriteAllText(txtFechamento, "logistica@exemplo.com\n");
        var opEmail = new OpcoesEmail
        {
            Habilitado = true,
            Destinatarios = "gerente@exemplo.com",
            CaminhoDestinatarios = txtFechamento,
        };
        var alerta = Criar(opEmail, new OpcoesDiagnostico { Destinatarios = "ti@exemplo.com" });

        await alerta.IndisponivelAsync(401, Agora.AddMinutes(1), CancellationToken.None);
        await alerta.RecuperadoAsync(CancellationToken.None);

        Assert.Equal(2, _email.Enviadas.Count);
        Assert.Contains("indisponível", _email.Enviadas[0].Assunto);
        Assert.Contains("RECUPERADO", _email.Enviadas[1].Assunto);
        foreach (var mensagem in _email.Enviadas)
        {
            Assert.Equal(new[] { "ti@exemplo.com" }, mensagem.DestinatariosOverride);
            Assert.DoesNotContain("logistica@exemplo.com", mensagem.DestinatariosOverride!);
            Assert.DoesNotContain("gerente@exemplo.com", mensagem.DestinatariosOverride!);
        }
    }

    [Fact]
    public async Task TxtDeDiagnostico_VenceAOpcaoDeDiagnostico()
    {
        var opDiagnostico = new OpcoesDiagnostico { Destinatarios = "opcao@exemplo.com" };
        File.WriteAllText(Path.Combine(_pasta, "destinatarios-diagnostico.txt"), "ti1@exemplo.com\nti2@exemplo.com\n");
        var alerta = Criar(new OpcoesEmail { Habilitado = true, Destinatarios = "gerente@exemplo.com" }, opDiagnostico);

        await alerta.IndisponivelAsync(null, Agora, CancellationToken.None);

        var enviada = Assert.Single(_email.Enviadas);
        Assert.Equal(new[] { "ti1@exemplo.com", "ti2@exemplo.com" }, enviada.DestinatariosOverride);
    }

    [Fact]
    public async Task SemNenhumaConfiguracaoDeDiagnostico_VaiParaATiPadrao_ENuncaParaOFechamento()
    {
        var alerta = Criar(
            new OpcoesEmail { Habilitado = true, Destinatarios = "gerente@exemplo.com" },
            new OpcoesDiagnostico { Destinatarios = string.Empty });

        await alerta.RecuperadoAsync(CancellationToken.None);

        Assert.Equal(new[] { "ti@friozem.com.br" }, Assert.Single(_email.Enviadas).DestinatariosOverride);
    }

    [Fact]
    public async Task EmailDesligadoOuAlertaDesligado_SoLogam()
    {
        var emailDesligado = Criar(new OpcoesEmail { Habilitado = false }, new OpcoesDiagnostico());
        await emailDesligado.IndisponivelAsync(401, Agora, CancellationToken.None);
        await emailDesligado.RecuperadoAsync(CancellationToken.None);

        var alertaDesligado = Criar(new OpcoesEmail { Habilitado = true }, new OpcoesDiagnostico(), alertaLigado: false);
        await alertaDesligado.IndisponivelAsync(401, Agora, CancellationToken.None);
        await alertaDesligado.RecuperadoAsync(CancellationToken.None);

        Assert.Empty(_email.Enviadas);
    }
}
