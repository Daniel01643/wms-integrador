using System.Globalization;
using System.Text.Json;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Separacao;

/// <summary>Repositório do estado das notas acompanhadas (I51/I60).</summary>
public interface IRepositorioSeparacao
{
    /// <summary>Registra a nota; idempotente por chave (false se já existe ou falhou).</summary>
    bool Registrar(NotaSeparacao nota);

    /// <summary>
    /// Aplica a alteração, valida a transição e grava. Devolve false se a nota não existe, a
    /// transição é inválida ou a gravação falhou (o índice permanece como estava).
    /// </summary>
    bool Atualizar(string chave, Func<NotaSeparacao, NotaSeparacao> alteracao);

    /// <summary>Obtém uma nota (ou null).</summary>
    NotaSeparacao? Obter(string chave);

    /// <summary>Foto de todas as notas.</summary>
    IReadOnlyList<NotaSeparacao> Listar();

    /// <summary>Foto das notas nas situações indicadas, da mais antiga para a mais nova.</summary>
    IReadOnlyList<NotaSeparacao> ListarPorSituacao(params SituacaoNota[] situacoes);

    /// <summary>
    /// Reenfileiramento manual (única aresta <c>Rejeitada → Separada</c>): por chave OU por motivo.
    /// Devolve quantas notas voltaram a <c>Separada</c>.
    /// </summary>
    int Reenfileirar(string? chave, MotivoI60? motivo, DateTimeOffset agora);

    /// <summary>Apaga notas terminais vencidas (e o XML copiado). Devolve quantas.</summary>
    int Limpar(DateTimeOffset agora);
}

/// <summary>
/// Um arquivo JSON por nota em <c>Estado\i51\notas</c>, com índice em memória. Carga preguiçosa
/// (construir não toca o disco). Escrita atômica; o índice só muda depois do <c>Move</c>. Nunca
/// lança por falha de disco.
/// </summary>
public sealed class RepositorioSeparacaoArquivo : IRepositorioSeparacao
{
    private readonly CaminhosI51 _caminhos;
    private readonly OpcoesI51 _opcoes;
    private readonly IClock _clock;
    private readonly ILogger<RepositorioSeparacaoArquivo> _logger;
    private readonly object _lock = new();
    private Dictionary<string, NotaSeparacao>? _indice;

    /// <summary>Cria o repositório.</summary>
    public RepositorioSeparacaoArquivo(
        CaminhosI51 caminhos, IOptions<OpcoesI51> opcoes, IClock clock, ILogger<RepositorioSeparacaoArquivo> logger)
    {
        _caminhos = caminhos ?? throw new ArgumentNullException(nameof(caminhos));
        _opcoes = opcoes?.Value ?? throw new ArgumentNullException(nameof(opcoes));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Tabela de transições permitidas pelo <see cref="Atualizar"/> (mesma situação é sempre permitida).</summary>
    public static bool TransicaoValida(SituacaoNota de, SituacaoNota para) =>
        de == para || (de, para) switch
        {
            (SituacaoNota.AguardandoSeparacao, SituacaoNota.Atrasada) => true,
            (SituacaoNota.AguardandoSeparacao, SituacaoNota.Separada) => true,
            (SituacaoNota.Atrasada, SituacaoNota.Separada) => true,
            (SituacaoNota.Separada, SituacaoNota.I60Enviada) => true,
            (SituacaoNota.Separada, SituacaoNota.Rejeitada) => true,
            (SituacaoNota.I60Enviada, SituacaoNota.Associada) => true,
            _ => false,
        };

    /// <inheritdoc />
    public bool Registrar(NotaSeparacao nota)
    {
        ArgumentNullException.ThrowIfNull(nota);
        lock (_lock)
        {
            var indice = Indice();
            if (string.IsNullOrEmpty(nota.Chave))
            {
                _logger.LogError("Nota sem chave não pode ser registrada no acompanhamento da separação.");
                return false;
            }

            if (indice.ContainsKey(nota.Chave))
            {
                _logger.LogInformation("Nota {Numero} já acompanhada (registro idempotente).", nota.Numero);
                return false;
            }

            if (!Gravar(nota))
            {
                return false;
            }

            indice[nota.Chave] = nota;
            return true;
        }
    }

    /// <inheritdoc />
    public bool Atualizar(string chave, Func<NotaSeparacao, NotaSeparacao> alteracao)
    {
        ArgumentNullException.ThrowIfNull(alteracao);
        lock (_lock)
        {
            var indice = Indice();
            if (!indice.TryGetValue(chave, out var atual))
            {
                return false;
            }

            var novo = alteracao(atual) with { Chave = atual.Chave, AtualizadoEm = _clock.UtcNow };
            if (!TransicaoValida(atual.Situacao, novo.Situacao))
            {
                throw new InvalidOperationException(
                    $"Transição inválida {atual.Situacao} → {novo.Situacao} (nota {atual.Numero}).");
            }

            if (!Gravar(novo))
            {
                return false;
            }

            indice[chave] = novo;
            return true;
        }
    }

    /// <inheritdoc />
    public NotaSeparacao? Obter(string chave)
    {
        lock (_lock)
        {
            return Indice().TryGetValue(chave, out var n) ? n : null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<NotaSeparacao> Listar()
    {
        lock (_lock)
        {
            return Indice().Values.OrderBy(n => n.RegistradaEm).ThenBy(n => n.Chave, StringComparer.Ordinal).ToList();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<NotaSeparacao> ListarPorSituacao(params SituacaoNota[] situacoes)
    {
        lock (_lock)
        {
            return Indice().Values
                .Where(n => situacoes.Contains(n.Situacao))
                .OrderBy(n => n.RegistradaEm).ThenBy(n => n.Chave, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <inheritdoc />
    public int Reenfileirar(string? chave, MotivoI60? motivo, DateTimeOffset agora)
    {
        lock (_lock)
        {
            var indice = Indice();
            var alvos = indice.Values
                .Where(n => n.Situacao == SituacaoNota.Rejeitada
                            && (chave is null || string.Equals(n.Chave, chave, StringComparison.Ordinal))
                            && (motivo is null || n.Motivo == motivo))
                .ToList();
            var total = 0;
            foreach (var n in alvos)
            {
                var novo = n with
                {
                    Situacao = SituacaoNota.Separada,
                    TentativasI60 = 0,
                    TentativasOutro = 0,
                    FalhasComunicacao = 0,
                    ProximaTentativaI60Em = null,
                    FinalizadaEm = null,
                    Motivo = MotivoI60.Nenhum,
                    Mensagem = string.Empty,
                    Ressalvas = Array.Empty<string>(),
                    OperacaoI60 = string.Empty,
                    TpAcaoI60 = string.Empty,
                    AlertaI60Em = null,
                    AtualizadoEm = agora,
                    Observacao = Juntar(n.Observacao, "reenfileirada em " + agora.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)),
                };
                if (Gravar(novo))
                {
                    indice[n.Chave] = novo;
                    total++;
                }
            }

            return total;
        }
    }

    /// <inheritdoc />
    public int Limpar(DateTimeOffset agora)
    {
        lock (_lock)
        {
            var indice = Indice();
            var limite = agora - TimeSpan.FromDays(_opcoes.DiasRetencaoEstado);
            var vencidas = indice.Values
                .Where(n => n.Situacao is SituacaoNota.Associada or SituacaoNota.Rejeitada or SituacaoNota.I60Enviada
                            && (n.FinalizadaEm ?? n.AtualizadoEm) < limite)
                .ToList();
            var total = 0;
            foreach (var n in vencidas)
            {
                try
                {
                    File.Delete(CaminhoNota(n.Chave));
                    if (n.XmlCopiado && n.CaminhoXml.Length > 0)
                    {
                        File.Delete(Path.Combine(_caminhos.Base, n.CaminhoXml));
                    }

                    indice.Remove(n.Chave);
                    total++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Não foi possível apagar a nota vencida {Numero} do estado I51.", n.Numero);
                }
            }

            return total;
        }
    }

    private static string Juntar(string a, string b) => string.IsNullOrEmpty(a) ? b : a + "; " + b;

    private string CaminhoNota(string chave) => Path.Combine(_caminhos.Notas, CaminhosI51.NomeSeguro(chave) + ".json");

    private bool Gravar(NotaSeparacao nota) => CaminhosI51.GravarJson(CaminhoNota(nota.Chave), nota, _logger);

    // Carga preguiçosa: só aqui o disco é tocado. Arquivo corrompido é preservado como .corrompido-<carimbo>.
    private Dictionary<string, NotaSeparacao> Indice()
    {
        if (_indice is not null)
        {
            return _indice;
        }

        var indice = new Dictionary<string, NotaSeparacao>(StringComparer.Ordinal);
        try
        {
            Directory.CreateDirectory(_caminhos.Notas);
            foreach (var arquivo in Directory.EnumerateFiles(_caminhos.Notas, "*.json"))
            {
                try
                {
                    var nota = JsonSerializer.Deserialize<NotaSeparacao>(File.ReadAllText(arquivo), CaminhosI51.Json);
                    if (nota is null || string.IsNullOrEmpty(nota.Chave))
                    {
                        throw new JsonException("Conteúdo vazio.");
                    }

                    indice[nota.Chave] = nota;
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    _logger.LogWarning(ex, "Estado de nota ilegível ({Arquivo}); preservado como .corrompido.", Path.GetFileName(arquivo));
                    Preservar(arquivo);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Falha ao carregar o estado das notas I51; segue só em memória.");
        }

        _indice = indice;
        return indice;
    }

    private void Preservar(string arquivo)
    {
        try
        {
            var destino = arquivo + ".corrompido-" + _clock.UtcNow.ToLocalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            if (!File.Exists(destino))
            {
                File.Move(arquivo, destino);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Não foi possível preservar o estado ilegível.");
        }
    }
}
