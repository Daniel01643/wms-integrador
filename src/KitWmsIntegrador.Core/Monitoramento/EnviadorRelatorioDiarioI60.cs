using System.Globalization;
using System.Text;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Grava o relatório diário da I51/I60 (HTML + PDF) em <c>&lt;Relatorio:PastaSaida&gt;\i60</c> e, se o SMTP
/// estiver configurado, envia o e-mail. A gravação em disco falha alto (o slot não é marcado);
/// falha de e-mail só loga.
/// </summary>
public sealed class EnviadorRelatorioDiarioI60
{
    /// <summary>Subpasta de saída dentro de <c>Relatorio:PastaSaida</c>.</summary>
    public const string Subpasta = "i60";

    /// <summary>Nome do PDF anexado ao e-mail.</summary>
    public const string NomeAnexoPdf = "relatorio-i60.pdf";

    private readonly IEnviadorEmail _email;
    private readonly IGeradorRelatorioI60 _gerador;
    private readonly FonteDestinatariosRelatorioI60 _destinatarios;
    private readonly OpcoesRelatorioI60 _opcoes;
    private readonly OpcoesRelatorio _relatorio;
    private readonly OpcoesEmail _opcoesEmail;
    private readonly ILogger<EnviadorRelatorioDiarioI60> _logger;

    /// <summary>Cria o enviador.</summary>
    public EnviadorRelatorioDiarioI60(
        IEnviadorEmail email, IGeradorRelatorioI60 gerador, FonteDestinatariosRelatorioI60 destinatarios,
        IOptions<OpcoesRelatorioI60> opcoes, IOptions<OpcoesRelatorio> relatorio, IOptions<OpcoesEmail> opcoesEmail,
        ILogger<EnviadorRelatorioDiarioI60> logger)
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
    /// Grava HTML e PDF (lança <see cref="IOException"/>/<see cref="UnauthorizedAccessException"/> se o disco falhar)
    /// e tenta o e-mail. Devolve os caminhos gravados e se o e-mail foi enviado.
    /// </summary>
    public async Task<(string Html, string Pdf, bool EmailEnviado)> EnviarAsync(
        ModeloRelatorioI60 modelo, bool enviarEmail, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        var (htmlDisco, _) = _gerador.GerarHtml(modelo, ModoLogoRelatorio.DataUri);
        var pdf = _gerador.GerarPdf(modelo);

        var pasta = Path.Combine(Path.GetFullPath(_relatorio.PastaSaida), Subpasta);
        Directory.CreateDirectory(pasta);
        var carimbo = modelo.GeradoEm.ToLocalTime().ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);
        var caminhoHtml = Path.Combine(pasta, $"relatorio-i60_{carimbo}.html");
        var caminhoPdf = Path.Combine(pasta, $"relatorio-i60_{carimbo}.pdf");
        File.WriteAllText(caminhoHtml, htmlDisco, new UTF8Encoding(true));
        File.WriteAllBytes(caminhoPdf, pdf);
        _logger.LogInformation("Relatório diário I51/I60 gravado em disco: {Html} e {Pdf}.", caminhoHtml, caminhoPdf);

        var enviado = false;
        if (enviarEmail && SmtpConfigurado())
        {
            try
            {
                var (htmlEmail, imagem) = _gerador.GerarHtml(modelo, ModoLogoRelatorio.Cid);
                var assunto = $"{_opcoes.AssuntoPrefixo} {IdentidadeVisual.Titulo} - I51/I60 - {modelo.GeradoEm.ToLocalTime():dd/MM/yyyy}".Trim();
                var destinatarios = _destinatarios.Ler();
                var resultado = await _email.EnviarAsync(
                    new MensagemEmail(assunto, htmlEmail, new[] { new AnexoEmail(NomeAnexoPdf, pdf) }, imagem, destinatarios), ct)
                    .ConfigureAwait(false);
                enviado = resultado.Status == StatusEnvioEmail.Enviado;
                if (!enviado)
                {
                    _logger.LogWarning("Relatório diário I51/I60 não enviado por e-mail (status {Status}); fica em disco.", resultado.Status);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Falha ao enviar o relatório diário I51/I60 por e-mail; o arquivo permanece em disco.");
            }
        }
        else if (enviarEmail)
        {
            _logger.LogInformation("SMTP não configurado; relatório diário I51/I60 apenas em disco.");
        }

        return (caminhoHtml, caminhoPdf, enviado);
    }

    private bool SmtpConfigurado() =>
        _opcoesEmail.Habilitado
        && !string.IsNullOrWhiteSpace(_opcoesEmail.Host)
        && !string.IsNullOrWhiteSpace(_opcoesEmail.Remetente);
}
