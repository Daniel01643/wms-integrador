namespace KitWmsIntegrador.Core.Configuracao;

/// <summary>Quando enviar o e-mail de fechamento do pacote.</summary>
public enum EnviarQuando
{
    /// <summary>Somente se houver não integradas, pendentes, revisar produto ou XML inválido.</summary>
    ComProblema,

    /// <summary>Sempre que o pacote fechar.</summary>
    Sempre,

    /// <summary>Nunca envia.</summary>
    Nunca
}

/// <summary>
/// Opções do e-mail de fechamento do pacote (seção "Email"). A senha NÃO é versionada:
/// definir por variável de ambiente <c>Email__Senha</c> ou <c>email.settings.json</c> (ignorado pelo git).
/// </summary>
public sealed class OpcoesEmail
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string Secao = "Email";

    /// <summary>Liga o envio. Default desligado.</summary>
    public bool Habilitado { get; set; }

    /// <summary>Servidor SMTP.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Porta SMTP (STARTTLS = 587).</summary>
    public int Porta { get; set; } = 587;

    /// <summary>Usa TLS (STARTTLS). Não suporta TLS implícito (465).</summary>
    public bool UsarTls { get; set; } = true;

    /// <summary>Usuário SMTP (opcional para SMTP interno sem autenticação).</summary>
    public string Usuario { get; set; } = string.Empty;

    /// <summary>Senha SMTP. SEGREDO: nunca gravar em arquivo versionado.</summary>
    public string Senha { get; set; } = string.Empty;

    /// <summary>Endereço do remetente.</summary>
    public string Remetente { get; set; } = string.Empty;

    /// <summary>Nome exibido do remetente.</summary>
    public string RemetenteNome { get; set; } = string.Empty;

    /// <summary>Destinatários separados por ';'.</summary>
    public string Destinatarios { get; set; } = string.Empty;

    /// <summary>Prefixo do assunto.</summary>
    public string AssuntoPrefixo { get; set; } = "[KitWms]";

    /// <summary>Política de envio.</summary>
    public EnviarQuando EnviarQuando { get; set; } = EnviarQuando.ComProblema;

    /// <summary>Timeout do SMTP em segundos.</summary>
    public int TimeoutSegundos { get; set; } = 60;

    /// <summary>Destinatários já separados, sem vazios.</summary>
    public IReadOnlyList<string> ListaDestinatarios =>
        (Destinatarios ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    /// <summary>Habilitado e com host, remetente e ao menos um destinatário.</summary>
    public bool EstaConfigurado =>
        Habilitado
        && !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(Remetente)
        && ListaDestinatarios.Count > 0;
}
