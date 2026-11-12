using System.Globalization;
using System.Text;
using System.Text.Json;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;

namespace KitWmsIntegrador.Core.Separacao;

/// <summary>Saúde da consulta periódica (gravada em <c>consulta.json</c> e lida pelo relatório).</summary>
public sealed record SaudeConsultaI51
{
    /// <summary>Última tentativa de consulta.</summary>
    public DateTimeOffset? UltimaTentativaEm { get; init; }

    /// <summary>Última consulta que respondeu sem erro.</summary>
    public DateTimeOffset? UltimaConsultaOkEm { get; init; }

    /// <summary>Falhas de consulta seguidas.</summary>
    public int FalhasConsecutivas { get; init; }

    /// <summary>Última mensagem de "nada a retornar" (truncada).</summary>
    public string UltimaMensagemVazia { get; init; } = string.Empty;
}

/// <summary>
/// Armazena as respostas brutas da I51 em disco ANTES de processar (o TODOS não repete) e a
/// saúde da consulta. Pastas: <c>pendentes</c>, <c>processados\&lt;data&gt;</c>, <c>falhos</c>.
/// Nunca lança por falha de disco.
/// </summary>
public sealed class ArmazenamentoBrutoI51
{
    private readonly CaminhosI51 _caminhos;
    private readonly IClock _clock;
    private readonly ILogger<ArmazenamentoBrutoI51> _logger;

    /// <summary>Cria o armazenamento.</summary>
    public ArmazenamentoBrutoI51(CaminhosI51 caminhos, IClock clock, ILogger<ArmazenamentoBrutoI51> logger)
    {
        _caminhos = caminhos ?? throw new ArgumentNullException(nameof(caminhos));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Grava o corpo bruto em <c>pendentes</c>. Devolve o caminho ou <c>null</c> se falhar.</summary>
    public string? GravarPendente(string tipo, string? numero, string corpo)
    {
        var carimbo = _clock.UtcNow.ToLocalTime().ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture);
        var sufixo = string.IsNullOrEmpty(numero) ? string.Empty : "_" + CaminhosI51.NomeSeguro(numero);
        var baseNome = $"i51_{CaminhosI51.NomeSeguro(tipo)}_{carimbo}{sufixo}";
        var caminho = Path.Combine(_caminhos.BrutosPendentes, baseNome + ".json");
        for (var n = 2; File.Exists(caminho) && n < 1000; n++)
        {
            caminho = Path.Combine(_caminhos.BrutosPendentes, $"{baseNome}_{n}.json");
        }

        return CaminhosI51.GravarTexto(caminho, corpo, _logger) ? caminho : null;
    }

    /// <summary>Brutos pendentes, do mais antigo para o mais novo.</summary>
    public IReadOnlyList<string> ListarPendentes()
    {
        try
        {
            return Directory.Exists(_caminhos.BrutosPendentes)
                ? Directory.EnumerateFiles(_caminhos.BrutosPendentes, "*.json").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToList()
                : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Falha ao listar os brutos pendentes da I51.");
            return Array.Empty<string>();
        }
    }

    /// <summary>Lê o conteúdo de um bruto (null se falhar).</summary>
    public string? Ler(string caminho)
    {
        try
        {
            return File.ReadAllText(caminho, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Falha ao ler o bruto {Arquivo}.", Path.GetFileName(caminho));
            return null;
        }
    }

    /// <summary>Move para <c>processados\&lt;data&gt;</c>.</summary>
    public void MoverProcessado(string caminho) =>
        Mover(caminho, Path.Combine(_caminhos.BrutosProcessados, _clock.UtcNow.ToLocalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture)));

    /// <summary>Move para <c>falhos</c>.</summary>
    public void MoverFalho(string caminho) => Mover(caminho, _caminhos.BrutosFalhos);

    /// <summary>Quantidade de brutos em <c>falhos</c>.</summary>
    public int ContarFalhos()
    {
        try
        {
            return Directory.Exists(_caminhos.BrutosFalhos) ? Directory.EnumerateFiles(_caminhos.BrutosFalhos).Count() : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Apaga pastas de brutos processados mais antigas que a retenção.</summary>
    public int Limpar(int diasRetencao)
    {
        var removidas = 0;
        try
        {
            if (!Directory.Exists(_caminhos.BrutosProcessados))
            {
                return 0;
            }

            var limite = _clock.UtcNow.ToLocalTime().Date.AddDays(-diasRetencao);
            foreach (var pasta in Directory.EnumerateDirectories(_caminhos.BrutosProcessados))
            {
                if (DateTime.TryParseExact(Path.GetFileName(pasta), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    && d < limite)
                {
                    Directory.Delete(pasta, recursive: true);
                    removidas++;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Falha ao limpar os brutos processados da I51.");
        }

        return removidas;
    }

    /// <summary>Lê a saúde da consulta (ausente/ilegível = vazia).</summary>
    public SaudeConsultaI51 LerSaude()
    {
        try
        {
            if (File.Exists(_caminhos.ArquivoConsulta))
            {
                return JsonSerializer.Deserialize<SaudeConsultaI51>(File.ReadAllText(_caminhos.ArquivoConsulta), CaminhosI51.Json)
                       ?? new SaudeConsultaI51();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Saúde da consulta I51 ilegível; recomeçando.");
        }

        return new SaudeConsultaI51();
    }

    /// <summary>Grava a saúde da consulta.</summary>
    public void GravarSaude(SaudeConsultaI51 saude) => CaminhosI51.GravarJson(_caminhos.ArquivoConsulta, saude, _logger);

    private void Mover(string caminho, string pastaDestino)
    {
        try
        {
            Directory.CreateDirectory(pastaDestino);
            var destino = Path.Combine(pastaDestino, Path.GetFileName(caminho));
            File.Move(caminho, destino, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Falha ao mover o bruto {Arquivo}.", Path.GetFileName(caminho));
        }
    }
}
