namespace KitWmsIntegrador.Core.Diagnostico;

/// <summary>
/// Diário de diagnóstico técnico (uma linha JSON por evento). A implementação NUNCA lança: falha de disco
/// é só logada. Nenhum evento carrega token, senha, cabeçalho, corpo de nota nem JSON de resposta.
/// </summary>
public interface IRegistroDiagnostico
{
    /// <summary>Grava o evento. Nunca lança.</summary>
    void Registrar(EventoDiagnostico evento);
}

/// <summary>Registro que descarta tudo (usado quando o componente não recebe um diário).</summary>
public sealed class RegistroDiagnosticoNulo : IRegistroDiagnostico
{
    /// <summary>Instância única.</summary>
    public static readonly RegistroDiagnosticoNulo Instancia = new();

    private RegistroDiagnosticoNulo()
    {
    }

    /// <inheritdoc />
    public void Registrar(EventoDiagnostico evento)
    {
    }
}

/// <summary>Evento do diário de diagnóstico. Só <see cref="Tipo"/> é obrigatório; o restante depende do tipo.</summary>
public sealed record EventoDiagnostico
{
    /// <summary>Tipo do evento (gettoken, auth-queda, auth-recuperada, cadastro-gatilho, ...).</summary>
    public string Tipo { get; init; } = string.Empty;

    /// <summary>Hora local do evento; o registrador preenche (IClock) quando é o valor padrão.</summary>
    public DateTimeOffset QuandoLocal { get; init; }

    /// <summary>Detalhe do tipo (I10/I30, inicio/escalada, desfecho da I60, ...).</summary>
    public string? Subtipo { get; init; }

    /// <summary>Status HTTP, quando houve resposta.</summary>
    public int? Status { get; init; }

    /// <summary>Resultado, quando faz sentido para o tipo.</summary>
    public bool? Ok { get; init; }

    /// <summary>Chave do servidor (<c>BackendWms.Chave</c>, sem segredo).</summary>
    public string? Backend { get; init; }

    /// <summary>Texto curto; sempre sanitizado e truncado na gravação.</summary>
    public string? Mensagem { get; init; }

    /// <summary>Número da nota.</summary>
    public string? Nota { get; init; }

    /// <summary>Série da nota.</summary>
    public string? Serie { get; init; }

    /// <summary>Quantidade principal do tipo.</summary>
    public int? Quantidade { get; init; }

    /// <summary>Quantidade secundária do tipo.</summary>
    public int? Quantidade2 { get; init; }

    /// <summary>Duração em milissegundos.</summary>
    public long? DuracaoMs { get; init; }

    /// <summary>Segundos (ex.: cooldown aplicado).</summary>
    public int? Segundos { get; init; }
}
