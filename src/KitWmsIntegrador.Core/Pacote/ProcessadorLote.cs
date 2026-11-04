using System.Collections.Concurrent;
using KitWmsIntegrador.Core.Agrupamento;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Core.Modelos;
using KitWmsIntegrador.Core.Orquestrador;
using KitWmsIntegrador.Core.Relatorio;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Pacote;

/// <summary>
/// Processa um lote de arquivos prontos: parse → agrupa por transporte → integra cada nota pelo
/// Orquestrador (ondas em paralelo limitado, notas em sequência) → classifica → move logo após
/// cada nota → registra no diário. Exceção em um arquivo/onda não derruba os demais.
/// </summary>
public sealed class ProcessadorLote
{
    private sealed record MovePendente(DestinoArquivo Destino, CategoriaErro? Categoria, string Motivo);

    private readonly IOrquestrador _orquestrador;
    private readonly LeitorNotaFiscalXml _leitor;
    private readonly AgrupadorTransporte _agrupador;
    private readonly ClassificadorCategoriaErro _classificador;
    private readonly OrganizadorArquivos _organizador;
    private readonly DiarioPacote _diario;
    private readonly IClock _clock;
    private readonly OpcoesPacote _opcoes;
    private readonly ILogger<ProcessadorLote> _logger;

    // Arquivos já enviados ao WMS cujo move falhou: NUNCA reenviar, só repetir o move.
    private readonly ConcurrentDictionary<string, MovePendente> _movePendentes =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cria o processador.</summary>
    public ProcessadorLote(
        IOrquestrador orquestrador,
        LeitorNotaFiscalXml leitor,
        AgrupadorTransporte agrupador,
        ClassificadorCategoriaErro classificador,
        OrganizadorArquivos organizador,
        DiarioPacote diario,
        IClock clock,
        IOptions<OpcoesPacote> opcoes,
        ILogger<ProcessadorLote> logger)
    {
        _orquestrador = orquestrador ?? throw new ArgumentNullException(nameof(orquestrador));
        _leitor = leitor ?? throw new ArgumentNullException(nameof(leitor));
        _agrupador = agrupador ?? throw new ArgumentNullException(nameof(agrupador));
        _classificador = classificador ?? throw new ArgumentNullException(nameof(classificador));
        _organizador = organizador ?? throw new ArgumentNullException(nameof(organizador));
        _diario = diario ?? throw new ArgumentNullException(nameof(diario));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _opcoes = opcoes?.Value ?? throw new ArgumentNullException(nameof(opcoes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Arquivos enviados ao WMS cujo move ainda está pendente (não bloqueiam o fechamento).</summary>
    public IReadOnlyCollection<string> MovePendentes => _movePendentes.Keys.ToList();

    /// <summary>
    /// Repete os moves que falharam (arquivo já enviado ao WMS). Nunca chama o WMS. A fonte de
    /// verdade após reinício é o diário (ver <see cref="ReconciliarMove"/>).
    /// </summary>
    public void RepetirMovesPendentes()
    {
        foreach (var par in _movePendentes.ToArray())
        {
            RepetirMove(par.Key, par.Value);
        }
    }

    /// <summary>
    /// Move um arquivo já tratado (registro do diário) sem reenviar ao WMS. Usado na retomada
    /// após queda entre registrar e mover. Se o move falhar, fica como pendente.
    /// </summary>
    public bool ReconciliarMove(RegistroDiario registro)
    {
        ArgumentNullException.ThrowIfNull(registro);
        var arquivo = registro.Arquivo;
        if (!File.Exists(arquivo))
        {
            return true;
        }

        var motivo = MotivoSidecar(registro);
        var mov = _organizador.Mover(arquivo, registro.Destino, registro.Categoria, motivo);
        if (mov.Sucesso || !File.Exists(arquivo))
        {
            return true;
        }

        _logger.LogWarning(
            "Arquivo {Arquivo} já tratado antes da queda, mas o move falhou ({Erro}); será repetido sem reenviar ao WMS.",
            arquivo, mov.Erro);
        _movePendentes[arquivo] = new MovePendente(registro.Destino, registro.Categoria, motivo);
        return false;
    }

    /// <summary>
    /// Processa os arquivos e devolve os registros novos. Respeita a ordem de chegada
    /// (criação, depois nome). Propaga apenas o cancelamento.
    /// </summary>
    public async Task<IReadOnlyList<RegistroDiario>> ProcessarAsync(
        IReadOnlyCollection<string> arquivos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(arquivos);
        var registros = new ConcurrentQueue<RegistroDiario>();

        var ordenados = arquivos
            .OrderBy(ObterCriacao)
            .ThenBy(a => Path.GetFileName(a), StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 1) Parse (e repetição de moves pendentes).
        var lidos = new List<(string Arquivo, NotaFiscal Nota)>();
        foreach (var arquivo in ordenados)
        {
            ct.ThrowIfCancellationRequested();

            if (_movePendentes.TryGetValue(arquivo, out var pendente))
            {
                RepetirMove(arquivo, pendente);
                continue;
            }

            try
            {
                lidos.Add((arquivo, _leitor.ParseArquivo(arquivo)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var motivo = "XML inválido: " + ex.Message;
                _logger.LogWarning("Arquivo {Arquivo} não pôde ser lido: {Motivo}", arquivo, ex.Message);
                Finalizar(registros, Registro(arquivo, true, Path.GetFileName(arquivo), string.Empty, motivo, false, 0,
                        StatusLinhaRelatorio.NaoIntegrada, string.Empty, string.Empty, null,
                        new[] { motivo }, CategoriaErro.XmlInvalido, DestinoArquivo.Erro),
                    arquivo);
            }
        }

        if (lidos.Count == 0)
        {
            return registros.ToList();
        }

        // 2) Agrupa por transporte, ondas na ordem do primeiro arquivo.
        var arquivoDe = new Dictionary<NotaFiscal, (string Arquivo, int Ordem)>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < lidos.Count; i++)
        {
            arquivoDe[lidos[i].Nota] = (lidos[i].Arquivo, i);
        }

        var agrupamento = _agrupador.Agrupar(lidos.Select(l => l.Nota));
        var ondas = agrupamento.Ondas
            .OrderBy(o => o.Notas.Min(n => arquivoDe[n].Ordem))
            .Select(o => (o.NumeroTransporte, Notas: (IReadOnlyList<NotaFiscal>)o.Notas))
            .ToList();
        if (agrupamento.SemTransporte.Count > 0)
        {
            ondas.Add((string.Empty, agrupamento.SemTransporte));
        }

        // 3) Ondas em paralelo limitado; notas de cada onda em sequência.
        using var semaforo = new SemaphoreSlim(Math.Max(1, _opcoes.MaxTransportesConcorrentes));
        var tarefas = ondas.Select(o => ProcessarOndaAsync(o.NumeroTransporte, o.Notas, arquivoDe, semaforo, registros, ct))
            .ToList();
        await Task.WhenAll(tarefas).ConfigureAwait(false);

        return registros.ToList();
    }

    private async Task ProcessarOndaAsync(
        string transporte,
        IReadOnlyList<NotaFiscal> notas,
        Dictionary<NotaFiscal, (string Arquivo, int Ordem)> arquivoDe,
        SemaphoreSlim semaforo,
        ConcurrentQueue<RegistroDiario> registros,
        CancellationToken ct)
    {
        await semaforo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var nota in notas)
            {
                ct.ThrowIfCancellationRequested();
                var arquivo = arquivoDe[nota].Arquivo;
                try
                {
                    var resultados = await _orquestrador.IntegrarAsync(new[] { nota }, transporte, ct)
                        .ConfigureAwait(false);
                    var resultado = resultados.Count > 0
                        ? resultados[0]
                        : throw new InvalidOperationException("O Orquestrador não devolveu resultado para a nota.");
                    Concluir(registros, arquivo, resultado);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Exceção ao integrar a nota {Nota} ({Arquivo}).", nota.Numero, arquivo);
                    var motivo = "Exceção ao integrar: " + ex.Message;
                    Finalizar(registros, Registro(arquivo, false, nota.Numero, transporte, motivo, false, 0,
                            StatusLinhaRelatorio.NaoIntegrada, string.Empty, string.Empty, null,
                            new[] { motivo }, CategoriaErro.Outro, DestinoArquivo.Erro),
                        arquivo);
                }
            }
        }
        finally
        {
            semaforo.Release();
        }
    }

    private void Concluir(ConcurrentQueue<RegistroDiario> registros, string arquivo, ResultadoIntegracaoNota r)
    {
        var destino = _classificador.DestinoDe(r);
        var categoria = destino == DestinoArquivo.Erro ? _classificador.Classificar(r) : (CategoriaErro?)null;

        var mensagem = r.UltimaMensagem ?? string.Empty;
        if (destino == DestinoArquivo.Processados && !r.Integrada && r.Estado != EstadoIntegracao.Pendente)
        {
            mensagem = "Já existia no WMS (reprocesso): " + mensagem;
        }

        var status = r.Estado switch
        {
            EstadoIntegracao.Integrada => StatusLinhaRelatorio.Integrada,
            EstadoIntegracao.Pendente => StatusLinhaRelatorio.Pendente,
            _ => StatusLinhaRelatorio.NaoIntegrada,
        };

        Finalizar(registros, Registro(arquivo, false, r.NumeroNota, r.NumeroTransporte, mensagem, r.Integrada,
                r.Tentativas, status, r.IdTransacao, r.IdDocumento, r.ProdutosProvisorios, r.Historico,
                categoria, destino),
            arquivo);
    }

    // Registra no diário ANTES de mover (uma queda no meio reenvia no máximo 1 nota; o WMS barra a duplicata).
    private void Finalizar(ConcurrentQueue<RegistroDiario> registros, RegistroDiario registro, string arquivo)
    {
        _diario.Registrar(registro);
        registros.Enqueue(registro);

        var motivo = MotivoSidecar(registro);
        var mov = _organizador.Mover(arquivo, registro.Destino, registro.Categoria, motivo);
        if (!mov.Sucesso && File.Exists(arquivo))
        {
            _logger.LogWarning(
                "Arquivo {Arquivo} já tratado, mas o move falhou ({Erro}); será repetido sem reenviar ao WMS.",
                arquivo, mov.Erro);
            _movePendentes[arquivo] = new MovePendente(registro.Destino, registro.Categoria, motivo);
        }
    }

    private void RepetirMove(string arquivo, MovePendente p)
    {
        var mov = _organizador.Mover(arquivo, p.Destino, p.Categoria, p.Motivo);
        if (mov.Sucesso || !File.Exists(arquivo))
        {
            _movePendentes.TryRemove(arquivo, out _);
        }
    }

    private static string MotivoSidecar(RegistroDiario r) =>
        $"Nota: {r.NumeroNota}{Environment.NewLine}Transporte: {r.NumeroTransporte}{Environment.NewLine}" +
        $"Tentativas: {r.Tentativas}{Environment.NewLine}Data: {r.Horario:yyyy-MM-dd HH:mm:ss zzz}{Environment.NewLine}" +
        $"Mensagem: {r.Mensagem}";

    private RegistroDiario Registro(
        string arquivo, bool xmlInvalido, string nota, string transporte, string mensagem, bool integrada,
        int tentativas, StatusLinhaRelatorio status, string idTransacao, string idDocumento,
        IReadOnlyList<string>? produtos, IReadOnlyList<string> historico, CategoriaErro? categoria,
        DestinoArquivo destino) =>
        new(arquivo, xmlInvalido, nota ?? string.Empty, transporte ?? string.Empty, _clock.UtcNow, mensagem,
            integrada, tentativas, status, idTransacao ?? string.Empty, idDocumento ?? string.Empty,
            produtos ?? Array.Empty<string>(), historico ?? Array.Empty<string>(), categoria, destino);

    private static DateTime ObterCriacao(string caminho)
    {
        try
        {
            return File.GetCreationTimeUtc(caminho);
        }
        catch (Exception)
        {
            return DateTime.MaxValue;
        }
    }
}
