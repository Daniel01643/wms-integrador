namespace KitWmsIntegrador.Core.Email;

/// <summary>Anexo de e-mail (nome + conteúdo).</summary>
public sealed record AnexoEmail(string Nome, byte[] Conteudo);

/// <summary>
/// Imagem embutida no corpo HTML por <c>cid:</c> (ex.: logo). O <paramref name="ContentId"/> é
/// o identificador referenciado no HTML como <c>src="cid:&lt;ContentId&gt;"</c>; o enviador a
/// anexa como recurso vinculado (não como anexo visível).
/// </summary>
/// <param name="ContentId">Identificador do recurso, referenciado por <c>cid:</c> no HTML.</param>
/// <param name="Conteudo">Bytes da imagem.</param>
/// <param name="ContentType">Tipo MIME da imagem (default <c>image/png</c>).</param>
public sealed record ImagemInline(string ContentId, byte[] Conteudo, string ContentType = "image/png");

/// <summary>Mensagem a enviar.</summary>
/// <param name="Assunto">Assunto.</param>
/// <param name="CorpoHtml">Corpo HTML.</param>
/// <param name="Anexos">Anexos (ex.: PDF).</param>
/// <param name="ImagemEmbutida">Imagem opcional embutida por <c>cid:</c> no corpo (ex.: logo).</param>
public sealed record MensagemEmail(
    string Assunto, string CorpoHtml, IReadOnlyList<AnexoEmail> Anexos, ImagemInline? ImagemEmbutida = null);

/// <summary>Situação do envio.</summary>
public enum StatusEnvioEmail
{
    /// <summary>Enviado ao servidor SMTP.</summary>
    Enviado,

    /// <summary>E-mail desabilitado ou com configuração incompleta; nada foi feito.</summary>
    NaoConfigurado,

    /// <summary>Falha no envio (ver <see cref="ResultadoEnvioEmail.Motivo"/>).</summary>
    Falhou
}

/// <summary>Resultado do envio.</summary>
public sealed record ResultadoEnvioEmail(StatusEnvioEmail Status, string Motivo = "")
{
    /// <summary>Enviado com sucesso.</summary>
    public static ResultadoEnvioEmail Enviado() => new(StatusEnvioEmail.Enviado);

    /// <summary>Não configurado.</summary>
    public static ResultadoEnvioEmail NaoConfigurado() => new(StatusEnvioEmail.NaoConfigurado, "E-mail não configurado.");

    /// <summary>Falhou com motivo.</summary>
    public static ResultadoEnvioEmail Falhou(string motivo) => new(StatusEnvioEmail.Falhou, motivo);
}

/// <summary>Envio de e-mail (abstração para trocar SMTP por outro provedor).</summary>
public interface IEnviadorEmail
{
    /// <summary>Envia a mensagem; nunca lança exceção por falha de envio.</summary>
    Task<ResultadoEnvioEmail> EnviarAsync(MensagemEmail mensagem, CancellationToken ct);
}
