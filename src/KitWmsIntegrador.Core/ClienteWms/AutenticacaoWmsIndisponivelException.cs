namespace KitWmsIntegrador.Core.ClienteWms;

/// <summary>
/// O GetToken do WMS falhou (401/403/5xx/timeout/rede/resposta sem token) ou está em cooldown. NÃO é erro da
/// nota: quem processa notas deve interromper o pacote e manter os arquivos na entrada para nova tentativa.
/// Herda de <see cref="HttpRequestException"/> para que I51/I60/devolução continuem tratando como falha de comunicação.
/// </summary>
public sealed class AutenticacaoWmsIndisponivelException : HttpRequestException
{
    /// <summary>Cria a exceção.</summary>
    public AutenticacaoWmsIndisponivelException(string mensagem, int? status, bool emCooldown, DateTimeOffset? proximaTentativaUtc)
        : base(mensagem, null, status is { } s ? (System.Net.HttpStatusCode)s : null)
    {
        Status = status;
        EmCooldown = emCooldown;
        ProximaTentativaUtc = proximaTentativaUtc;
    }

    /// <summary>Status HTTP do último GetToken (null em timeout/rede).</summary>
    public int? Status { get; }

    /// <summary>Verdadeiro quando nenhuma chamada foi feita porque o cooldown global estava ativo.</summary>
    public bool EmCooldown { get; }

    /// <summary>Quando o GetToken pode ser tentado de novo.</summary>
    public DateTimeOffset? ProximaTentativaUtc { get; }
}
