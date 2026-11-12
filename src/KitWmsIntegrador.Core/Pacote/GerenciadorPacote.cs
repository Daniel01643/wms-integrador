using System.Globalization;
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
    private string? _idPacote;
    private PeriodoColeta? _periodo;
    private DateTimeOffset _proximaFoto = DateTimeOffset.MinValue;
    private int _fotosVazias;
    private bool _retomado;

    // Etapas medidas do pacote em andamento (null = desconhecido). Pacote retomado de queda não as tem.
    private long? _esperaJanelaMs;
    private long? _leituraMs;
    private bool _pacoteRetomado;

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

        var classificacao = ClassificarEntrada();
        var naoXmlDefinitivos = new HashSet<string>(classificacao.NaoXml.Select(e => e.Caminho), StringComparer.OrdinalIgnoreCase);
        var entradaDe = classificacao.Aceitos.Concat(classificacao.NaoXml)
            .GroupBy(e => e.Caminho, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var arquivos = entradaDe.Keys.ToList();
        var deteccao = _detector.Avaliar(arquivos);
        var pendentes = new HashSet<string>(_processador.MovePendentes, StringComparer.OrdinalIgnoreCase);

        if (Estado == EstadoPacote.Ocioso)
        {
            // Instáveis nunca abrem pacote sozinhos; moves pendentes já foram enviados ao WMS.
            // O backoff vale só para arquivos instáveis: um arquivo pronto novo abre pacote sem esperar.
            var candidatos = deteccao.Prontos.Concat(deteccao.Aguardando).Count(a => !pendentes.Contains(a));
            var prontosNovos = deteccao.Prontos.Count(a => !pendentes.Contains(a));
            if (candidatos == 0 || (prontosNovos == 0 && agora < _proximaFoto))
            {
                return;
            }

            _inicio = agora;
            _idPacote = IdDe(agora);
            _periodo = null;
            Estado = EstadoPacote.Coletando;
            _logger.LogInformation(
                "Pacote aberto ({Id}): {Arquivos} arquivo(s) na entrada; janela de coleta de {Janela}s.",
                _idPacote, candidatos, _opcoes.JanelaColetaSegundos);
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
                if (!_pacoteRetomado)
                {
                    // Espera da janela: abertura do pacote até a foto (medida com o IClock).
                    _esperaJanelaMs = Math.Max(0L, (long)(agora - _inicio!.Value).TotalMilliseconds);
                }

                Estado = EstadoPacote.Processando;
                _logger.LogInformation(
                    "Foto da pasta: {Arquivos} arquivo(s); primeira gravação {Primeira:dd/MM/yyyy HH:mm:ss}, última {Ultima:dd/MM/yyyy HH:mm:ss}.",
                    foto.Count, _periodo?.PrimeiraGravacao, _periodo?.UltimaGravacao);

                _idPacote ??= IdDe(_inicio ?? agora);

                // A foto pode conter XML/ZIP (fluxo de integração) e não-XML definitivos (ExtensaoInvalida).
                var fotoXml = foto.Where(a => !naoXmlDefinitivos.Contains(a)).Select(a => entradaDe[a]).ToList();
                var fotoNaoXml = foto.Where(a => naoXmlDefinitivos.Contains(a)).Select(a => entradaDe[a]).ToList();

                if (fotoNaoXml.Count > 0)
                {
                    var ignorados = _processador.ProcessarNaoXml(fotoNaoXml, _idPacote);
                    _registros.AddRange(ignorados);
                }

                var novos = await _processador.ProcessarAsync(fotoXml, _idPacote, ct).ConfigureAwait(false);
                _registros.AddRange(novos);
                if (!_pacoteRetomado)
                {
                    _leituraMs = _processador.UltimaLeituraMs;
                }

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
        _esperaJanelaMs = null;
        _leituraMs = null;
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
            var id = _idPacote ?? IdDe(inicio);
            ok = await _fechador.FecharAsync(
                registros, inicio, _clock.UtcNow, ct, _periodo, id,
                new EtapasPacote(_esperaJanelaMs, _leituraMs)).ConfigureAwait(false);
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
        _idPacote = null;
        _periodo = null;
        _esperaJanelaMs = null;
        _leituraMs = null;
        _pacoteRetomado = false;
        Estado = EstadoPacote.Ocioso;
        _logger.LogInformation("Pacote fechado.");
    }

    // Identificador único do pacote (horário LOCAL): mesmo carimbo das subpastas e do relatório.
    private static string IdDe(DateTimeOffset instante) =>
        "pacote_" + instante.ToLocalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

    // Retomada após queda: a fonte de verdade é o diário. Arquivo ainda na entrada que já tem registro
    // e não foi regravado depois dele só precisa ser movido (sem reenviar ao WMS).
    private void Retomar(DateTimeOffset agora)
    {
        var lidos = _diario.Ler();
        if (lidos.Count == 0)
        {
            return;
        }

        // Pacote retomado: espera da janela e leitura não são conhecidas (as linhas somem do relatório).
        _pacoteRetomado = true;
        _esperaJanelaMs = null;
        _leituraMs = null;
        var registros = ConsolidadorRegistros.Consolidar(lidos);

        // Restaura o id do diário (primeiro registro que o traga) para reconciliar os moves na
        // MESMA subpasta; se o diário for antigo (sem o campo), recalcula de _inicio como fallback.
        var inicio = registros.Count > 0 ? registros.Min(r => r.Horario) : agora;
        _idPacote = registros
            .Select(r => r.IdentificadorPacote)
            .FirstOrDefault(id => !string.IsNullOrEmpty(id)) ?? IdDe(inicio);

        var mantidos = new List<RegistroDiario>();
        foreach (var registro in registros)
        {
            if (File.Exists(registro.Arquivo))
            {
                if (File.GetLastWriteTimeUtc(registro.Arquivo) > registro.Horario.UtcDateTime)
                {
                    // Regravado depois do registro: o arquivo será reprocessado como novo e o
                    // registro antigo sai do pacote para não esconder o resultado novo.
                    continue;
                }

                _processador.ReconciliarMove(registro, _idPacote);
            }

            mantidos.Add(registro);
        }

        _registros.AddRange(mantidos);
        _inicio = _registros.Count > 0 ? _registros.Min(r => r.Horario) : agora;
        var restantes = ListarEntrada();
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

    // Candidatos da raiz da entrada e das pastas Puxada/PuxadaTSP, já com tipo e origem: XML/ZIP
    // (fluxo de integração) e não-XML definitivos (viram ExtensaoInvalida). Temporários/em-cópia
    // são ignorados e não aparecem aqui.
    private ClassificacaoPastas ClassificarEntrada()
    {
        var entrada = _pastas.CaminhoEntrada;
        if (!Directory.Exists(entrada))
        {
            _logger.LogWarning("Pasta de entrada não encontrada: {Pasta}", entrada);
            return new ClassificacaoPastas(Array.Empty<EntradaArquivo>(), Array.Empty<EntradaArquivo>());
        }

        return ClassificadorEntrada.ClassificarPastas(_pastas);
    }

    // Lista os candidatos (XML/ZIP + não-XML definitivos) para a detecção/estabilização.
    private IReadOnlyList<string> ListarEntrada() =>
        ClassificarEntrada() is var c ? c.Aceitos.Concat(c.NaoXml).Select(e => e.Caminho).ToList() : Array.Empty<string>();
}
