using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.ClienteWms;

/// <summary>Leitura do estado da autenticação do WMS (cooldown do GetToken) para quem decide abrir pacote.</summary>
public interface IEstadoAutenticacaoWms
{
    /// <summary>Verdadeiro enquanto o cooldown global do GetToken estiver ativo.</summary>
    bool Bloqueada { get; }

    /// <summary>Fim do cooldown (null se não há indisponibilidade).</summary>
    DateTimeOffset? ProximaTentativaUtc { get; }

    /// <summary>Total acumulado de GetToken que falharam de forma definitiva (por episódio, após as tentativas imediatas).</summary>
    long TotalFalhasGetToken { get; }
}

/// <summary>
/// Estado global do GetToken: Normal → (falha) Indisponível com cooldown crescente → (sucesso) Normal.
/// Thread-safe. Quem chama decide o que fazer com os booleanos devolvidos (alerta de 1ª falha / recuperação).
/// </summary>
public sealed class GuardaAutenticacaoWms : IEstadoAutenticacaoWms
{
    private readonly IClock _clock;
    private readonly OpcoesWms _opcoes;
    private readonly object _lock = new();
    private int _falhasConsecutivas;
    private long _totalFalhas;
    private DateTimeOffset? _proxima;

    /// <summary>Cria a guarda.</summary>
    public GuardaAutenticacaoWms(IClock clock, IOptions<OpcoesWms> opcoes)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _opcoes = opcoes?.Value ?? throw new ArgumentNullException(nameof(opcoes));
    }

    /// <inheritdoc />
    public bool Bloqueada
    {
        get
        {
            lock (_lock)
            {
                return _proxima is { } p && _clock.UtcNow < p;
            }
        }
    }

    /// <inheritdoc />
    public DateTimeOffset? ProximaTentativaUtc
    {
        get
        {
            lock (_lock)
            {
                return _proxima;
            }
        }
    }

    /// <inheritdoc />
    public long TotalFalhasGetToken
    {
        get
        {
            lock (_lock)
            {
                return _totalFalhas;
            }
        }
    }

    /// <summary>Falhas consecutivas desde o último sucesso.</summary>
    public int FalhasConsecutivas
    {
        get
        {
            lock (_lock)
            {
                return _falhasConsecutivas;
            }
        }
    }

    /// <summary>Em indisponibilidade (houve falha e ainda não houve sucesso), mesmo que o cooldown já tenha vencido.</summary>
    public bool Indisponivel
    {
        get
        {
            lock (_lock)
            {
                return _falhasConsecutivas > 0;
            }
        }
    }

    /// <summary>Registra falha definitiva; devolve se é a primeira da indisponibilidade (dispara o alerta).</summary>
    public bool RegistrarFalha(out DateTimeOffset proximaTentativaUtc)
    {
        lock (_lock)
        {
            var escada = _opcoes.ListaCooldown;
            var espera = escada[Math.Min(_falhasConsecutivas, escada.Count - 1)];
            var primeira = _falhasConsecutivas == 0;
            _falhasConsecutivas++;
            _totalFalhas++;
            _proxima = _clock.UtcNow + espera;
            proximaTentativaUtc = _proxima.Value;
            return primeira;
        }
    }

    /// <summary>Registra sucesso; devolve se houve recuperação de uma indisponibilidade (dispara o alerta).</summary>
    public bool RegistrarSucesso()
    {
        lock (_lock)
        {
            var recuperou = _falhasConsecutivas > 0;
            _falhasConsecutivas = 0;
            _proxima = null;
            return recuperou;
        }
    }
}
