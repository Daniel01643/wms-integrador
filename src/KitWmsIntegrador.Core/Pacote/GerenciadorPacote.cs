using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Pacote;

/// <summary>Estado do pacote.</summary>
public enum EstadoPacote
{
    /// <summary>Sem pacote aberto (pode haver arquivos aguardando a próxima foto).</summary>
    Ocioso,

    /// <summary>Pacote aberto, aguardando a janela de coleta fixa terminar.</summary>
    Coletando,

    /// <summary>Processando a foto (conjunto fixo de arquivos) do pacote.</summary>
    Processando,

    /// <summary>Gerando relatório/e-mail do pacote.</summary>
    Fechando,
}

/// <summary>Resumo do pacote em andamento.</summary>
/// <param name="InicioUtc">Quando o pacote foi aberto.</param>
/// <param name="Notas">Resultados já registrados no pacote.</param>
/// <param name="Periodo">Primeira/última gravação dos arquivos da foto (null antes da foto).</param>
public sealed record InfoPacote(DateTimeOffset InicioUtc, int Notas, PeriodoColeta? Periodo = null);

/// <summary>
/// Máquina de estados do pacote (Ocioso → Coletando → Processando → Fechando → Ocioso).
/// Ao detectar arquivos, aguarda a janela de coleta FIXA e tira uma "foto" da pasta: os arquivos
/// prontos naquele instante formam o pacote (processado por transporte, em sequência). Arquivos que
/// chegam depois formam o PRÓXIMO pacote. Ao terminar a foto, gera o relatório e envia um e-mail só
/// daquele pacote; então volta a Ocioso. <see cref="CicloAsync"/> executa um ciclo determinístico
/// (sem espera interna), para que o Worker controle o intervalo e os testes controlem o relógio.
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
    private DateTimeOffset? _inicio;
    private PeriodoColeta? _periodo;
    private DateTimeOffset _proximaFoto = DateTimeOffset.MinValue;
    private int _fotosVazias;
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
    public InfoPacote? PacoteAtual => _inicio is null ? null : new InfoPacote(_inicio.Value, _registros.Count, _periodo);

    /// <summary>
    /// Instante a partir do qual uma nova foto pode ser iniciada (backoff após fotos sem nenhum
    /// arquivo pronto). <see cref="DateTimeOffset.MinValue"/> quando não há backoff.
    /// </summary>
    public DateTimeOffset ProximaFotoUtc => _proximaFoto;

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

        if (Estado == EstadoPacote.Fechando)
        {
            await FecharAsync(ct).ConfigureAwait(false);
            return;
        }

        if (Estado == EstadoPacote.Ocioso)
        {
            _processador.RepetirMovesPendentes();
        }

        var arquivos = ListarXml();
        var deteccao = _detector.Avaliar(arquivos);
        var pendentes = new HashSet<string>(_processador.MovePendentes, StringComparer.OrdinalIgnoreCase);

        if (Estado == EstadoPacote.Ocioso)
        {
            // Instáveis nunca abrem pacote sozinhos; moves pendentes já foram enviados ao WMS.
            var candidatos = deteccao.Prontos.Concat(deteccao.Aguardando).Count(a => !pendentes.Contains(a));
            if (candidatos == 0 || agora < _proximaFoto)
            {
                return;
            }

            _inicio = agora;
            _periodo = null;
            Estado = EstadoPacote.Coletando;
            _logger.LogInformation(
                "Pacote aberto: {Arquivos} arquivo(s) na entrada; janela de coleta de {Janela}s.",
                candidatos, _opcoes.JanelaColetaSegundos);
        }

        if (Estado == EstadoPacote.Coletando)
        {
            // Janela FIXA: não é adiada por novos arquivos (eles só entram se já estiverem prontos).
            if ((agora - _inicio!.Value).TotalSeconds < _opcoes.JanelaColetaSegundos)
            {
                return;
            }

            // FOTO: lista congelada dos arquivos prontos neste instante.
            var foto = deteccao.Prontos.Where(a => !pendentes.Contains(a)).ToList();
            if (foto.Count == 0)
            {
                EncerrarFotoVazia(agora);
                if (Estado != EstadoPacote.Fechando)
                {
                    return;
                }
            }
            else
            {
                _fotosVazias = 0;
                _proximaFoto = DateTimeOffset.MinValue;
                _periodo = PeriodoColeta.Calcular(foto);
                Estado = EstadoPacote.Processando;
                _logger.LogInformation(
                    "Foto da pasta: {Arquivos} arquivo(s); primeira gravação {Primeira:dd/MM/yyyy HH:mm:ss}, última {Ultima:dd/MM/yyyy HH:mm:ss}.",
                    foto.Count, _periodo?.PrimeiraGravacao, _periodo?.UltimaGravacao);

                var novos = await _processador.ProcessarAsync(foto, ct).ConfigureAwait(false);
                _registros.AddRange(novos);
                Estado = EstadoPacote.Fechando;
            }
        }

        if (Estado == EstadoPacote.Fechando)
        {
            await FecharAsync(ct).ConfigureAwait(false);
        }
    }

    // Foto sem arquivo pronto (só em gravação): não gera relatório vazio; backoff exponencial.
    private void EncerrarFotoVazia(DateTimeOffset agora)
    {
        if (_registros.Count > 0)
        {
            // Pacote retomado do diário: fecha com o que já foi tratado.
            Estado = EstadoPacote.Fechando;
            return;
        }

        _fotosVazias++;
        var espera = Math.Min(
            Math.Max(1, _opcoes.JanelaColetaSegundos) * Math.Pow(2, Math.Min(_fotosVazias, 20)),
            Math.Max(1, _opcoes.BackoffMaximoSegundos));
        _proximaFoto = agora.AddSeconds(espera);
        _inicio = null;
        _periodo = null;
        Estado = EstadoPacote.Ocioso;
        _logger.LogInformation(
            "Foto sem arquivos prontos (ainda em gravação); nova tentativa após {Espera:0}s.", espera);
    }

    private async Task FecharAsync(CancellationToken ct)
    {
        Estado = EstadoPacote.Fechando;
        var inicio = _inicio ?? _clock.UtcNow;
        bool ok;
        try
        {
            var registros = ConsolidadorRegistros.Consolidar(_registros);
            ok = await _fechador.FecharAsync(registros, inicio, _clock.UtcNow, ct, _periodo).ConfigureAwait(false);
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
        _inicio = null;
        _periodo = null;
        Estado = EstadoPacote.Ocioso;
        _logger.LogInformation("Pacote fechado.");
    }

    // Retomada após queda: a fonte de verdade é o diário. Arquivo ainda na entrada que já tem registro
    // e não foi regravado depois dele só precisa ser movido (sem reenviar ao WMS).
    private void Retomar(DateTimeOffset agora)
    {
        var lidos = _diario.Ler();
        if (lidos.Count == 0)
        {
            return;
        }

        var registros = ConsolidadorRegistros.Consolidar(lidos);
        foreach (var registro in registros)
        {
            if (!File.Exists(registro.Arquivo))
            {
                continue;
            }

            if (File.GetLastWriteTimeUtc(registro.Arquivo) <= registro.Horario.UtcDateTime)
            {
                _processador.ReconciliarMove(registro);
            }
        }

        _registros.AddRange(registros);
        _inicio = _registros.Min(r => r.Horario);
        var restantes = ListarXml();
        if (restantes.Count > 0)
        {
            _inicio = agora;
            Estado = EstadoPacote.Coletando;
        }
        else
        {
            Estado = EstadoPacote.Fechando;
        }

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
