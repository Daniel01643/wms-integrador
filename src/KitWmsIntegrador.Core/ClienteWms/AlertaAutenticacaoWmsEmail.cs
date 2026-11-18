using System.Net;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Monitoramento;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.ClienteWms;

/// <summary>Alerta de indisponibilidade/recuperação da autenticação do WMS (um por transição).</summary>
public interface IAlertaAutenticacaoWms
{
    /// <summary>A autenticação ficou indisponível (primeira falha do episódio).</summary>
    Task IndisponivelAsync(int? status, DateTimeOffset proximaTentativaUtc, CancellationToken ct);

    /// <summary>A autenticação voltou (primeiro sucesso depois da indisponibilidade).</summary>
    Task RecuperadoAsync(CancellationToken ct);
}

/// <summary>
/// Alerta por e-mail (reusa <see cref="IEnviadorEmail"/> e os destinatários técnicos do diagnóstico, só a TI). Respeita
/// <c>Email:Habilitado</c> e <c>Wms:AlertaAutenticacao</c>; nunca lança. O texto não contém credenciais nem notas.
/// </summary>
public sealed class AlertaAutenticacaoWmsEmail : IAlertaAutenticacaoWms
{
    private readonly IEnviadorEmail _email;
    private readonly FonteDestinatariosDiagnostico _destinatarios;
    private readonly OpcoesEmail _opcoesEmail;
    private readonly OpcoesWms _opcoesWms;
    private readonly ILogger<AlertaAutenticacaoWmsEmail> _logger;

    /// <summary>Cria o alerta.</summary>
    public AlertaAutenticacaoWmsEmail(
        IEnviadorEmail email,
        FonteDestinatariosDiagnostico destinatarios,
        IOptions<OpcoesEmail> opcoesEmail,
        IOptions<OpcoesWms> opcoesWms,
        ILogger<AlertaAutenticacaoWmsEmail> logger)
    {
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _destinatarios = destinatarios ?? throw new ArgumentNullException(nameof(destinatarios));
        _opcoesEmail = opcoesEmail?.Value ?? throw new ArgumentNullException(nameof(opcoesEmail));
        _opcoesWms = opcoesWms?.Value ?? throw new ArgumentNullException(nameof(opcoesWms));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task IndisponivelAsync(int? status, DateTimeOffset proximaTentativaUtc, CancellationToken ct)
    {
        var motivo = status is { } s ? "HTTP " + s : "sem resposta (timeout/rede)";
        var proxima = proximaTentativaUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        return EnviarAsync(
            "ALERTA: autenticação do WMS indisponível (GetToken)",
            "O GetToken do WMS falhou (" + motivo + "). Nenhuma nota foi movida para Erro: o pacote foi interrompido e os " +
            "arquivos aguardam na pasta de entrada. Nova tentativa automática a partir de " + proxima + " (horário do servidor). " +
            "Você receberá outro aviso quando a autenticação voltar.",
            ct);
    }

    /// <inheritdoc />
    public Task RecuperadoAsync(CancellationToken ct) =>
        EnviarAsync(
            "RECUPERADO: autenticação do WMS (GetToken)",
            "O GetToken do WMS voltou a funcionar. Os arquivos que aguardavam na entrada serão processados no próximo pacote.",
            ct);

    private async Task EnviarAsync(string assunto, string texto, CancellationToken ct)
    {
        try
        {
            if (!_opcoesWms.AlertaAutenticacao || !_opcoesEmail.Habilitado)
            {
                _logger.LogWarning("{Assunto} (e-mail de alerta desligado; apenas log).", assunto);
                return;
            }

            var destinatarios = _destinatarios.Ler();
            if (destinatarios.Count == 0)
            {
                _logger.LogWarning("{Assunto} (sem destinatários; apenas log).", assunto);
                return;
            }

            var html = "<html><body style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;\"><p>" +
                       WebUtility.HtmlEncode(texto) + "</p></body></html>";
            var resultado = await _email.EnviarAsync(
                new MensagemEmail(assunto, html, Array.Empty<AnexoEmail>(), DestinatariosOverride: destinatarios), ct)
                .ConfigureAwait(false);
            if (resultado.Status == StatusEnvioEmail.Falhou)
            {
                _logger.LogError("Falha ao enviar o alerta de autenticação do WMS: {Motivo}", resultado.Motivo);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Falha inesperada ao enviar o alerta de autenticação do WMS (ignorada).");
        }
    }
}
