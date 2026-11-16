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

    /// <summary>
    /// Total acumulado de GetToken rejeitados/sem resposta por servidor (identificador de backend: nome e hash curto do
    /// cookie de afinidade, ou cabeçalho Server). Conta cada tentativa que falhou, inclusive as recuperadas por nova
    /// tentativa. Cópia (snapshot); nunca contém token, usuário nem senha.
    /// </summary>
    IReadOnlyDictionary<string, long> FalhasPorBackend => new Dictionary<string, long>();
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
    private readonly Dictionary<string, long> _porBackend = new();
    private readonly Queue<DateTimeOffset> _chamadasGetToken = new();
    private const int MaxBackendsDistintos = 20;
    private static readonly TimeSpan JanelaThrottle = TimeSpan.FromSeconds(60);

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

    /// <inheritdoc />
    public IReadOnlyDictionary<string, long> FalhasPorBackend
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<string, long>(_porBackend);
            }
        }
    }

    /// <summary>Conta uma tentativa de GetToken que falhou no servidor <paramref name="backend"/> (limite de chaves distintas).</summary>
    public void RegistrarFalhaBackend(string backend)
    {
        var chave = string.IsNullOrWhiteSpace(backend) ? "(sem identificador)" : backend;
        lock (_lock)
        {
            if (!_porBackend.ContainsKey(chave) && _porBackend.Count >= MaxBackendsDistintos)
            {
                chave = "(outros)";
            }

            _porBackend[chave] = _porBackend.GetValueOrDefault(chave) + 1;
        }
    }

    /// <summary>
    /// Reserva uma chamada ao GetToken na janela deslizante de 60 s (<see cref="OpcoesWms.MaxGetTokenPorMinuto"/>).
    /// Devolve <see cref="TimeSpan.Zero"/> se reservou; senão, quanto falta para a janela liberar (não reservou).
    /// </summary>
    public TimeSpan ReservarGetToken()
    {
        var max = _opcoes.MaxGetTokenPorMinuto;
        if (max <= 0)
        {
            return TimeSpan.Zero;
        }

        lock (_lock)
        {
            var agora = _clock.UtcNow;
            while (_chamadasGetToken.Count > 0 && agora - _chamadasGetToken.Peek() >= JanelaThrottle)
            {
                _chamadasGetToken.Dequeue();
            }

            if (_chamadasGetToken.Count < max)
            {
                _chamadasGetToken.Enqueue(agora);
                return TimeSpan.Zero;
            }

            var espera = _chamadasGetToken.Peek() + JanelaThrottle - agora;
            return espera > TimeSpan.Zero ? espera : TimeSpan.FromMilliseconds(1);
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
