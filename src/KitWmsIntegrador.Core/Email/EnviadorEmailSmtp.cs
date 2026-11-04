using System.Net;
using System.Net.Mail;
using System.Text;
using KitWmsIntegrador.Core.Configuracao;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Email;

/// <summary>
/// Envio por SMTP (<see cref="SmtpClient"/>). Limitação: sem TLS implícito (465) nem OAuth2.
/// Nunca loga a senha.
/// </summary>
public sealed class EnviadorEmailSmtp : IEnviadorEmail
{
    private readonly IOptions<OpcoesEmail> _opcoes;
    private readonly ILogger<EnviadorEmailSmtp> _logger;

    /// <summary>Cria o enviador.</summary>
    public EnviadorEmailSmtp(IOptions<OpcoesEmail> opcoes, ILogger<EnviadorEmailSmtp> logger)
    {
        _opcoes = opcoes;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ResultadoEnvioEmail> EnviarAsync(MensagemEmail mensagem, CancellationToken ct)
    {
        var o = _opcoes.Value;
        if (!o.EstaConfigurado)
        {
            _logger.LogInformation("E-mail não configurado ou desabilitado; envio ignorado.");
            return ResultadoEnvioEmail.NaoConfigurado();
        }

        try
        {
            using var msg = MontarMensagem(mensagem, o);
            using var smtp = new SmtpClient(o.Host, o.Porta)
            {
                EnableSsl = o.UsarTls,
                Timeout = Math.Max(1, o.TimeoutSegundos) * 1000
            };
            if (!string.IsNullOrWhiteSpace(o.Usuario))
            {
                smtp.Credentials = new NetworkCredential(o.Usuario, o.Senha);
            }

            await smtp.SendMailAsync(msg, ct).ConfigureAwait(false);
            _logger.LogInformation("E-mail enviado para {Destinatarios}.", string.Join(";", o.ListaDestinatarios));
            return ResultadoEnvioEmail.Enviado();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao enviar e-mail via {Host}:{Porta}.", o.Host, o.Porta);
            return ResultadoEnvioEmail.Falhou(ex.Message);
        }
    }

    /// <summary>Monta a <see cref="MailMessage"/> (anexos copiados para memória; descartar a mensagem libera-os).</summary>
    internal static MailMessage MontarMensagem(MensagemEmail mensagem, OpcoesEmail o)
    {
        var msg = new MailMessage
        {
            From = new MailAddress(o.Remetente, o.RemetenteNome),
            Subject = string.IsNullOrWhiteSpace(o.AssuntoPrefixo) ? mensagem.Assunto : $"{o.AssuntoPrefixo} {mensagem.Assunto}",
            Body = mensagem.CorpoHtml,
            IsBodyHtml = true,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8
        };

        foreach (var d in o.ListaDestinatarios)
        {
            msg.To.Add(d);
        }

        foreach (var a in mensagem.Anexos)
        {
            msg.Attachments.Add(new Attachment(new MemoryStream(a.Conteudo), a.Nome));
        }

        return msg;
    }
}
