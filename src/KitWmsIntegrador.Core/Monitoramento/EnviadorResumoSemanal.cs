using System.Globalization;
using System.Text;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Monta e envia (ou grava em disco) o resumo semanal. Reaproveita o <see cref="IEnviadorEmail"/>
/// com <c>DestinatariosOverride</c> (lidos do arquivo txt) quando o SMTP está configurado; caso
/// contrário, grava o HTML e o PDF em <c>&lt;telemetria&gt;/resumos/</c>. Espelha o
/// <c>FechadorPacote</c>: nunca lança por falha de envio.
/// </summary>
public sealed class EnviadorResumoSemanal
{
    /// <summary>Subpasta onde o resumo é gravado quando o envio por e-mail não está disponível.</summary>
    public const string SubpastaResumos = "resumos";

    /// <summary>Nome do PDF anexado ao e-mail do resumo.</summary>
    public const string NomeAnexoPdf = "resumo-semanal.pdf";

    private readonly IEnviadorEmail _email;
    private readonly IGeradorResumoSemanal _gerador;
    private readonly FonteDestinatariosResumo _destinatarios;
    private readonly RegistradorTelemetria _telemetria;
    private readonly OpcoesMonitoramento _monitoramento;
    private readonly OpcoesEmail _opcoesEmail;
    private readonly ILogger<EnviadorResumoSemanal> _logger;

    /// <summary>Cria o enviador do resumo.</summary>
    public EnviadorResumoSemanal(
        IEnviadorEmail email,
        IGeradorResumoSemanal gerador,
        FonteDestinatariosResumo destinatarios,
        RegistradorTelemetria telemetria,
        IOptions<OpcoesMonitoramento> monitoramento,
        IOptions<OpcoesEmail> opcoesEmail,
        ILogger<EnviadorResumoSemanal> logger)
    {
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _gerador = gerador ?? throw new ArgumentNullException(nameof(gerador));
        _destinatarios = destinatarios ?? throw new ArgumentNullException(nameof(destinatarios));
        _telemetria = telemetria ?? throw new ArgumentNullException(nameof(telemetria));
        _monitoramento = monitoramento?.Value ?? throw new ArgumentNullException(nameof(monitoramento));
        _opcoesEmail = opcoesEmail?.Value ?? throw new ArgumentNullException(nameof(opcoesEmail));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gera o resumo (HTML + PDF) e o envia por e-mail quando o SMTP está configurado; caso
    /// contrário (ou se o envio devolver <see cref="StatusEnvioEmail.NaoConfigurado"/>), grava o
    /// fallback em disco. Nunca lança por falha de envio.
    /// </summary>
    public async Task EnviarAsync(ResumoPeriodo resumo, string periodoLabel, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(resumo);

        var assunto = $"{_monitoramento.AssuntoPrefixo} Resumo semanal de uso - {periodoLabel}".Trim();
        var (html, imagem) = _gerador.GerarHtml(resumo, periodoLabel);
        var pdf = _gerador.GerarPdf(resumo, periodoLabel);
        var destinatarios = _destinatarios.Ler();

        if (SmtpConfigurado())
        {
            try
            {
                var msg = new MensagemEmail(
                    assunto, html,
                    new[] { new AnexoEmail(NomeAnexoPdf, pdf) },
                    imagem, destinatarios);
                var resultado = await _email.EnviarAsync(msg, ct).ConfigureAwait(false);
                if (resultado.Status == StatusEnvioEmail.Enviado)
                {
                    _logger.LogInformation(
                        "Resumo semanal enviado para {Destinatarios}.", string.Join(";", destinatarios));
                    return;
                }

                _logger.LogWarning(
                    "Resumo semanal não enviado por e-mail (status {Status}: {Motivo}); gravando em disco.",
                    resultado.Status, resultado.Motivo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao enviar o resumo semanal por e-mail; gravando em disco.");
            }
        }
        else
        {
            _logger.LogInformation("SMTP não configurado; resumo semanal será gravado em disco.");
        }

        GravarEmDisco(html, pdf);
    }

    /// <summary>SMTP utilizável: bloco Email habilitado e com Host e Remetente preenchidos.</summary>
    private bool SmtpConfigurado() =>
        _opcoesEmail.Habilitado
        && !string.IsNullOrWhiteSpace(_opcoesEmail.Host)
        && !string.IsNullOrWhiteSpace(_opcoesEmail.Remetente);

    private void GravarEmDisco(string html, byte[] pdf)
    {
        try
        {
            var pasta = Path.Combine(_telemetria.PastaBase, SubpastaResumos);
            Directory.CreateDirectory(pasta);
            var carimbo = DateTimeOffset.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);
            var caminhoHtml = Path.Combine(pasta, $"resumo_{carimbo}.html");
            var caminhoPdf = Path.Combine(pasta, $"resumo_{carimbo}.pdf");
            File.WriteAllText(caminhoHtml, html, Encoding.UTF8);
            File.WriteAllBytes(caminhoPdf, pdf);
            _logger.LogInformation(
                "Resumo semanal gravado em disco: {Html} e {Pdf}.", caminhoHtml, caminhoPdf);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao gravar o resumo semanal em disco.");
        }
    }
}
