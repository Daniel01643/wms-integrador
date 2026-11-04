using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Pacote;

/// <summary>Resultado de uma execução da limpeza.</summary>
/// <param name="ArquivosRemovidos">Arquivos apagados.</param>
/// <param name="PastasRemovidas">Subpastas vazias removidas.</param>
/// <param name="Erros">Problemas encontrados (configuração recusada, falhas por item).</param>
public sealed record ResultadoLimpeza(int ArquivosRemovidos, int PastasRemovidas, IReadOnlyList<string> Erros);

/// <summary>
/// Remove arquivos antigos (por <c>LastWriteTimeUtc</c>) de Processados e Erro. Só atua dentro
/// dessas duas pastas, recusa configuração perigosa e não segue reparse points.
/// </summary>
public sealed class LimpadorPastas
{
    private readonly IClock _clock;
    private readonly OpcoesPasta _pastas;
    private readonly OpcoesLimpeza _limpeza;
    private readonly ILogger<LimpadorPastas> _logger;

    /// <summary>Cria o limpador.</summary>
    public LimpadorPastas(
        IClock clock,
        IOptions<OpcoesPasta> pastas,
        IOptions<OpcoesLimpeza> limpeza,
        ILogger<LimpadorPastas> logger)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _pastas = pastas?.Value ?? throw new ArgumentNullException(nameof(pastas));
        _limpeza = limpeza?.Value ?? throw new ArgumentNullException(nameof(limpeza));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Executa a limpeza. Nunca lança.</summary>
    public ResultadoLimpeza Limpar()
    {
        var erros = new List<string>();
        var arquivos = 0;
        var pastas = 0;

        try
        {
            if (!_limpeza.Habilitada)
            {
                return new ResultadoLimpeza(0, 0, erros);
            }

            if (_limpeza.DiasRetencao < 0)
            {
                return Recusar(erros, "Limpeza:DiasRetencao não pode ser negativo.");
            }

            var problemas = _pastas.Validar();
            if (problemas.Count > 0)
            {
                return Recusar(erros, "Configuração de pastas insegura para limpeza: " + string.Join(" ", problemas));
            }

            var limite = _clock.UtcNow.UtcDateTime - TimeSpan.FromDays(_limpeza.DiasRetencao);

            foreach (var raiz in new[] { _pastas.CaminhoProcessados, _pastas.CaminhoErro })
            {
                LimparRaiz(raiz, limite, erros, ref arquivos, ref pastas);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha inesperada na limpeza.");
            erros.Add(ex.Message);
        }

        _logger.LogInformation(
            "Limpeza concluída: {Arquivos} arquivo(s) e {Pastas} pasta(s) removidos, {Erros} erro(s).",
            arquivos, pastas, erros.Count);
        return new ResultadoLimpeza(arquivos, pastas, erros);
    }

    private ResultadoLimpeza Recusar(List<string> erros, string motivo)
    {
        _logger.LogError("Limpeza recusada: {Motivo}", motivo);
        erros.Add(motivo);
        return new ResultadoLimpeza(0, 0, erros);
    }

    private void LimparRaiz(string raiz, DateTime limite, List<string> erros, ref int arquivos, ref int pastas)
    {
        if (!Directory.Exists(raiz) || EhReparsePoint(raiz))
        {
            return;
        }

        // Subpastas imediatas da raiz = subpastas de pacote: removidas por INTEIRO quando vencidas.
        foreach (var sub in Directory.EnumerateDirectories(raiz))
        {
            LimparSubpastaDePacote(sub, raiz, limite, erros, ref arquivos, ref pastas);
        }

        // Retrocompatibilidade: arquivos SOLTOS direto na raiz (layout antigo) tratados por arquivo.
        foreach (var arquivo in Directory.EnumerateFiles(raiz))
        {
            RemoverArquivoSeVencido(arquivo, raiz, limite, erros, ref arquivos);
        }
    }

    private void LimparSubpastaDePacote(string sub, string raiz, DateTime limite, List<string> erros, ref int arquivos, ref int pastas)
    {
        try
        {
            if (EhReparsePoint(sub) || !EstaDentro(sub, raiz))
            {
                return;
            }

            // Idade pela MAIOR LastWriteTimeUtc do conteúdo: evita apagar um pacote com qualquer
            // arquivo ainda dentro do prazo. Subpasta sem arquivos é considerada vazia (removível).
            var arquivosDaSub = Directory.EnumerateFiles(sub, "*", SearchOption.AllDirectories)
                .Where(a => !EhReparsePoint(a))
                .ToList();

            if (arquivosDaSub.Count > 0)
            {
                var idadeMax = arquivosDaSub.Max(File.GetLastWriteTimeUtc);
                if (idadeMax >= limite)
                {
                    return;
                }

                Directory.Delete(sub, recursive: true);
                arquivos += arquivosDaSub.Count;
                pastas++;
                _logger.LogInformation(
                    "Limpeza removeu a subpasta de pacote {Pasta} ({Arquivos} arquivo(s), idade {Dias:F1} dia(s)).",
                    sub, arquivosDaSub.Count, (_clock.UtcNow.UtcDateTime - idadeMax).TotalDays);
            }
            else
            {
                Directory.Delete(sub, recursive: true);
                pastas++;
                _logger.LogInformation("Limpeza removeu a subpasta de pacote vazia {Pasta}.", sub);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Limpeza não conseguiu remover a subpasta {Pasta}.", sub);
            erros.Add($"{sub}: {ex.Message}");
        }
    }

    private void RemoverArquivoSeVencido(string arquivo, string raiz, DateTime limite, List<string> erros, ref int arquivos)
    {
        try
        {
            if (!EstaDentro(arquivo, raiz) || EhReparsePoint(arquivo))
            {
                return;
            }

            var mtime = File.GetLastWriteTimeUtc(arquivo);
            if (mtime >= limite)
            {
                return;
            }

            File.Delete(arquivo);
            arquivos++;
            _logger.LogInformation(
                "Limpeza removeu {Arquivo} (idade {Dias:F1} dia(s)).",
                arquivo, (_clock.UtcNow.UtcDateTime - mtime).TotalDays);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Limpeza não conseguiu remover {Arquivo}.", arquivo);
            erros.Add($"{arquivo}: {ex.Message}");
        }
    }

    private static bool EstaDentro(string caminho, string raiz) =>
        Path.GetFullPath(caminho).StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool EhReparsePoint(string caminho) =>
        (File.GetAttributes(caminho) & FileAttributes.ReparsePoint) != 0;
}
