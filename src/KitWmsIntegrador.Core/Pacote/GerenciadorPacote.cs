using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Pacote;

/// <summary>Estado do pacote.</summary>
public enum EstadoPacote
{
    /// <summary>Sem arquivos na entrada e sem pacote aberto.</summary>
    Ocioso,

    /// <summary>Pacote aberto, aguardando a janela de coleta.</summary>
    Coletando,

    /// <summary>Processando lotes; fecha após a quietude com a entrada vazia.</summary>
    Processando,

    /// <summary>Gerando relatório/e-mail.</summary>
    Fechando,
}

/// <summary>Resumo do pacote em andamento.</summary>
public sealed record InfoPacote(DateTimeOffset InicioUtc, int Notas);

/// <summary>
/// Máquina de estados do pacote (Ocioso → Coletando → Processando → Fechando → Ocioso).
/// <see cref="CicloAsync"/> executa um ciclo determinístico (sem espera interna), para que o
/// Worker controle o intervalo e os testes controlem o relógio.
/// </summary>
public sealed class GerenciadorPacote
{
    private readonly IClock _clock;
    private readonly OpcoesPasta _pastas;
    private readonly OpcoesPacote _opcoes;
    private readonly DetectorArquivoPronto _detector;
    private readonly ProcessadorLote _processador;
    private readonly DiarioPacote _diario;
    private readonly FechadorPacote _fechador;
    private readonly ILogger<GerenciadorPacote> _logger;

    private readonly List<RegistroDiario> _registros = new();
    private readonly HashSet<string> _conhecidos = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _inicio;
    private DateTimeOffset _ultimoNovo;
    private DateTimeOffset _ultimaAtividade;
    private bool _retomado;

    /// <summary>Cria o gerenciador.</summary>
    public GerenciadorPacote(
        IClock clock,
        IOptions<OpcoesPasta> pastas,
        IOptions<OpcoesPacote> opcoes,
        DetectorArquivoPronto detector,
        ProcessadorLote processador,
        DiarioPacote diario,
        FechadorPacote fechador,
        ILogger<GerenciadorPacote> logger)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _pastas = pastas?.Value ?? throw new ArgumentNullException(nameof(pastas));
        _opcoes = opcoes?.Value ?? throw new ArgumentNullException(nameof(opcoes));
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _processador = processador ?? throw new ArgumentNullException(nameof(processador));
        _diario = diario ?? throw new ArgumentNullException(nameof(diario));
        _fechador = fechador ?? throw new ArgumentNullException(nameof(fechador));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Estado atual.</summary>
    public EstadoPacote Estado { get; private set; } = EstadoPacote.Ocioso;

    /// <summary>Pacote aberto (null quando Ocioso).</summary>
    public InfoPacote? PacoteAtual => _inicio is null ? null : new InfoPacote(_inicio.Value, _registros.Count);

    /// <summary>Executa um ciclo. Propaga apenas o cancelamento.</summary>
    public async Task CicloAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var agora = _clock.UtcNow;

        if (!_retomado)
        {
            _retomado = true;
            Retomar(agora);
        }

        var arquivos = ListarXml();
        var deteccao = _detector.Avaliar(arquivos);

        if (Estado == EstadoPacote.Fechando)
        {
            await FecharAsync(ct).ConfigureAwait(false);
            return;
        }

        if (Estado == EstadoPacote.Ocioso)
        {
            if (arquivos.Count == 0)
            {
                return;
            }

            _inicio = agora;
            _registros.Clear();
            _conhecidos.Clear();
            _ultimoNovo = agora;
            _ultimaAtividade = agora;
            Estado = EstadoPacote.Coletando;
            _logger.LogInformation("Pacote aberto: {Arquivos} arquivo(s) na entrada.", arquivos.Count);
        }

        _conhecidos.IntersectWith(arquivos);
        var novos = false;
        foreach (var a in arquivos)
        {
            novos |= _conhecidos.Add(a);
        }

        if (novos)
        {
            _ultimoNovo = agora;
            _ultimaAtividade = agora;
        }

        if (Estado == EstadoPacote.Coletando)
        {
            if ((agora - _ultimoNovo).TotalSeconds < _opcoes.JanelaColetaSegundos)
            {
                return;
            }

            Estado = EstadoPacote.Processando;
            _logger.LogInformation("Janela de coleta encerrada; processando.");
        }

        if (deteccao.Prontos.Count > 0)
        {
            var novosRegistros = await _processador.ProcessarAsync(deteccao.Prontos, ct).ConfigureAwait(false);
            _registros.AddRange(novosRegistros);
            if (novosRegistros.Count > 0)
            {
                _ultimaAtividade = _clock.UtcNow;
            }
        }

        // Fechamento: nada aguardando na entrada (instáveis acima do timeout e moves pendentes não contam).
        var ignorar = new HashSet<string>(deteccao.Instaveis, StringComparer.OrdinalIgnoreCase);
        ignorar.UnionWith(_processador.MovePendentes);
        var aguardando = ListarXml().Count(a => !ignorar.Contains(a));

        if (aguardando == 0 && (_clock.UtcNow - _ultimaAtividade).TotalSeconds >= _opcoes.QuietudeSegundos)
        {
            await FecharAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task FecharAsync(CancellationToken ct)
    {
        Estado = EstadoPacote.Fechando;
        var inicio = _inicio ?? _clock.UtcNow;
        bool ok;
        try
        {
            ok = await _fechador.FecharAsync(_registros.ToList(), inicio, _clock.UtcNow, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Falha inesperada ao fechar o pacote; nova tentativa no próximo ciclo.");
            ok = false;
        }

        if (!ok)
        {
            return;
        }

        _diario.Apagar();
        _registros.Clear();
        _conhecidos.Clear();
        _inicio = null;
        Estado = EstadoPacote.Ocioso;
        _logger.LogInformation("Pacote fechado.");
    }

    private void Retomar(DateTimeOffset agora)
    {
        var lidos = _diario.Ler();
        if (lidos.Count == 0)
        {
            return;
        }

        // Uma queda entre registrar e mover pode ter gerado 2 linhas do mesmo arquivo: mantém a primeira.
        _registros.AddRange(lidos.GroupBy(r => r.Arquivo, StringComparer.OrdinalIgnoreCase).Select(g => g.First()));
        _inicio = _registros.Min(r => r.Horario);
        _ultimoNovo = agora;
        _ultimaAtividade = agora;
        Estado = EstadoPacote.Processando;
        _logger.LogInformation("Pacote retomado do diário com {Registros} resultado(s).", _registros.Count);
    }

    private IReadOnlyList<string> ListarXml()
    {
        var entrada = _pastas.CaminhoEntrada;
        if (!Directory.Exists(entrada))
        {
            _logger.LogWarning("Pasta de entrada não encontrada: {Pasta}", entrada);
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(entrada, "*.xml", SearchOption.TopDirectoryOnly)
            .Where(f => string.Equals(Path.GetExtension(f), ".xml", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
