using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Pacote;

/// <summary>Resultado de uma avaliação de arquivos.</summary>
/// <param name="Prontos">Arquivos completos (estáveis e liberados), prontos para processar.</param>
/// <param name="Instaveis">Arquivos que não ficaram prontos dentro do timeout (não bloqueiam o fechamento).</param>
/// <param name="Aguardando">Arquivos ainda estabilizando.</param>
public sealed record ResultadoDeteccao(
    IReadOnlyList<string> Prontos,
    IReadOnlyList<string> Instaveis,
    IReadOnlyList<string> Aguardando);

/// <summary>
/// Decide se um arquivo foi completamente gravado: mesmo tamanho e data em ao menos 2 ciclos,
/// estável há <see cref="OpcoesPacote.EstabilizacaoSegundos"/> (pelo <see cref="IClock"/>) e
/// abertura exclusiva (FileShare.None) bem-sucedida.
/// </summary>
public sealed class DetectorArquivoPronto
{
    private sealed class Estado
    {
        public DateTimeOffset PrimeiroVisto;
        public DateTimeOffset EstavelDesde;
        public long Tamanho;
        public DateTime Mtime;
        public int Observacoes;
        public bool TimeoutLogado;
    }

    private readonly IClock _clock;
    private readonly OpcoesPacote _opcoes;
    private readonly ILogger<DetectorArquivoPronto> _logger;
    private readonly Dictionary<string, Estado> _estados = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cria o detector.</summary>
    public DetectorArquivoPronto(IClock clock, IOptions<OpcoesPacote> opcoes, ILogger<DetectorArquivoPronto> logger)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _opcoes = opcoes?.Value ?? throw new ArgumentNullException(nameof(opcoes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Avalia os arquivos (um ciclo). Esquece arquivos que não estão mais na lista.</summary>
    public ResultadoDeteccao Avaliar(IReadOnlyCollection<string> caminhos)
    {
        ArgumentNullException.ThrowIfNull(caminhos);

        var agora = _clock.UtcNow;
        var prontos = new List<string>();
        var instaveis = new List<string>();
        var aguardando = new List<string>();
        var presentes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var caminho in caminhos)
        {
            long tamanho;
            DateTime mtime;
            try
            {
                var info = new FileInfo(caminho);
                if (!info.Exists)
                {
                    continue;
                }

                tamanho = info.Length;
                mtime = info.LastWriteTimeUtc;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            presentes.Add(caminho);

            if (!_estados.TryGetValue(caminho, out var estado))
            {
                estado = new Estado
                {
                    PrimeiroVisto = agora,
                    EstavelDesde = agora,
                    Tamanho = tamanho,
                    Mtime = mtime,
                    Observacoes = 1,
                };
                _estados[caminho] = estado;
            }
            else
            {
                estado.Observacoes++;
                if (estado.Tamanho != tamanho || estado.Mtime != mtime)
                {
                    estado.Tamanho = tamanho;
                    estado.Mtime = mtime;
                    estado.EstavelDesde = agora;
                }
            }

            var estavel = estado.Observacoes >= 2
                          && (agora - estado.EstavelDesde).TotalSeconds >= _opcoes.EstabilizacaoSegundos;

            if (estavel && PodeAbrirExclusivo(caminho))
            {
                prontos.Add(caminho);
            }
            else if ((agora - estado.PrimeiroVisto).TotalSeconds >= _opcoes.TimeoutArquivoSegundos)
            {
                instaveis.Add(caminho);
                if (!estado.TimeoutLogado)
                {
                    estado.TimeoutLogado = true;
                    _logger.LogWarning(
                        "Arquivo {Arquivo} não ficou pronto em {Timeout}s; não bloqueia o fechamento do pacote.",
                        caminho, _opcoes.TimeoutArquivoSegundos);
                }
            }
            else
            {
                aguardando.Add(caminho);
            }
        }

        foreach (var chave in _estados.Keys.Where(k => !presentes.Contains(k)).ToList())
        {
            _estados.Remove(chave);
        }

        return new ResultadoDeteccao(prontos, instaveis, aguardando);
    }

    private static bool PodeAbrirExclusivo(string caminho)
    {
        try
        {
            using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
