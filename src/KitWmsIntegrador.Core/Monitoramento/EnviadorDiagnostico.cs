using System.Globalization;
using System.Text;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Diagnostico;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Grava o Diagnóstico agente integrador (HTML e PDF completo) em <c>&lt;Relatorio:PastaSaida&gt;\diagnostico</c> e, se o SMTP estiver
/// configurado, envia o e-mail SOMENTE aos destinatários de diagnóstico, com corpo enxuto (resumo + problemas) e o PDF
/// completo anexo. A gravação em disco falha alto (o slot não é marcado); falha de e-mail só loga.
/// </summary>
public sealed class EnviadorDiagnostico
{
    /// <summary>Subpasta de saída dentro de <c>Relatorio:PastaSaida</c>.</summary>
    public const string Subpasta = "diagnostico";

    /// <summary>Nome do PDF anexado ao e-mail.</summary>
    public const string NomeAnexoPdf = "diagnostico.pdf";

    private readonly IEnviadorEmail _email;
    private readonly IGeradorDiagnostico _gerador;
    private readonly FonteDestinatariosDiagnostico _destinatarios;
    private readonly OpcoesDiagnostico _opcoes;
    private readonly OpcoesRelatorio _relatorio;
    private readonly OpcoesEmail _opcoesEmail;
    private readonly ILogger<EnviadorDiagnostico> _logger;

    /// <summary>Cria o enviador.</summary>
    public EnviadorDiagnostico(
        IEnviadorEmail email, IGeradorDiagnostico gerador, FonteDestinatariosDiagnostico destinatarios,
        IOptions<OpcoesDiagnostico> opcoes, IOptions<OpcoesRelatorio> relatorio, IOptions<OpcoesEmail> opcoesEmail,
        ILogger<EnviadorDiagnostico> logger)
    {
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _gerador = gerador ?? throw new ArgumentNullException(nameof(gerador));
        _destinatarios = destinatarios ?? throw new ArgumentNullException(nameof(destinatarios));
        _opcoes = opcoes?.Value ?? throw new ArgumentNullException(nameof(opcoes));
        _relatorio = relatorio?.Value ?? throw new ArgumentNullException(nameof(relatorio));
        _opcoesEmail = opcoesEmail?.Value ?? throw new ArgumentNullException(nameof(opcoesEmail));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Grava o HTML e o PDF (lança <see cref="IOException"/>/<see cref="UnauthorizedAccessException"/> se o disco falhar) e
    /// tenta o e-mail. Devolve o caminho do HTML gravado (o PDF fica ao lado, com o mesmo nome) e se o e-mail foi enviado.
    /// </summary>
    public async Task<(string Html, bool EmailEnviado)> EnviarAsync(ModeloDiagnostico modelo, bool enviarEmail, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        var (htmlDisco, _) = _gerador.GerarHtml(modelo, ModoLogoRelatorio.DataUri);
        var pdf = _gerador.GerarPdf(modelo);

        var pasta = Path.Combine(Path.GetFullPath(_relatorio.PastaSaida), Subpasta);
        Directory.CreateDirectory(pasta);
        var carimbo = modelo.GeradoEm.ToLocalTime().ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);
        var caminho = Path.Combine(pasta, $"diagnostico_{carimbo}.html");
        File.WriteAllText(caminho, htmlDisco, new UTF8Encoding(true));
        var caminhoPdf = Path.ChangeExtension(caminho, ".pdf");
        File.WriteAllBytes(caminhoPdf, pdf);
        _logger.LogInformation("Diagnóstico agente integrador gravado em disco: {Html} e {Pdf}.", caminho, caminhoPdf);

        var enviado = false;
        if (enviarEmail && SmtpConfigurado())
        {
            try
            {
                // Corpo enxuto (resumo + problemas); o PDF anexo e as cópias em disco continuam completos.
                var (htmlEmail, imagem) = _gerador.GerarHtml(modelo, ModoLogoRelatorio.Cid, enxuto: true);
                var assunto = $"{_opcoes.AssuntoPrefixo} {CorpoEmailDiagnostico.Subtitulo} — {modelo.JanelaFim.ToLocalTime():dd/MM/yyyy}".Trim();
                var resultado = await _email.EnviarAsync(
                    new MensagemEmail(assunto, htmlEmail, new[] { new AnexoEmail(NomeAnexoPdf, pdf) }, imagem, _destinatarios.Ler()), ct)
                    .ConfigureAwait(false);
                enviado = resultado.Status == StatusEnvioEmail.Enviado;
                if (!enviado)
                {
                    _logger.LogWarning("Diagnóstico agente integrador não enviado por e-mail (status {Status}); fica em disco.", resultado.Status);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Falha ao enviar o Diagnóstico agente integrador por e-mail; o arquivo permanece em disco.");
            }
        }
        else if (enviarEmail)
        {
            _logger.LogInformation("SMTP não configurado; Diagnóstico agente integrador apenas em disco.");
        }

        return (caminho, enviado);
    }

    private bool SmtpConfigurado() =>
        _opcoesEmail.Habilitado
        && !string.IsNullOrWhiteSpace(_opcoesEmail.Host)
        && !string.IsNullOrWhiteSpace(_opcoesEmail.Remetente);
}
