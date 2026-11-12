using System.Globalization;
using System.Text.Json;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;

namespace KitWmsIntegrador.Core.Separacao;

/// <summary>Pedido de reenfileiramento gravado por outro processo (CLI) para o Worker aplicar.</summary>
public sealed record ComandoReenfileirar
{
    /// <summary>Chave da nota (ou nulo para usar o motivo).</summary>
    public string? Chave { get; init; }

    /// <summary>Motivo (<see cref="MotivoI60"/> como texto) das notas rejeitadas a reenfileirar.</summary>
    public string? Motivo { get; init; }

    /// <summary>Quando o pedido foi gravado.</summary>
    public DateTimeOffset CriadoEm { get; init; }
}

/// <summary>
/// Comandos entre processos: a CLI <c>--reenfileirar-i60</c> NÃO edita as notas (o Worker em execução é
/// a única escritora do índice); grava um arquivo em <c>Estado\i51\comandos</c> que o Worker consome
/// no início do ciclo seguinte.
/// </summary>
public sealed class ComandosI51
{
    private const string Prefixo = "reenfileirar_";

    private readonly CaminhosI51 _caminhos;
    private readonly IClock _clock;
    private readonly ILogger<ComandosI51> _logger;

    /// <summary>Cria o gerenciador de comandos.</summary>
    public ComandosI51(CaminhosI51 caminhos, IClock clock, ILogger<ComandosI51> logger)
    {
        _caminhos = caminhos ?? throw new ArgumentNullException(nameof(caminhos));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Valida o motivo informado na CLI; devolve o texto de erro ou nulo.</summary>
    public static string? ValidarMotivo(string? motivo)
    {
        if (!Enum.TryParse<MotivoI60>(motivo, ignoreCase: true, out var m) || m == MotivoI60.Nenhum
            || !Enum.IsDefined(m))
        {
            return "Motivo inválido: use um valor de MotivoI60 diferente de Nenhum (ex.: Outro, QuantidadeDiferente, JaExiste).";
        }

        return null;
    }

    /// <summary>Grava o pedido de reenfileiramento. Devolve false se o disco falhar.</summary>
    public bool GravarReenfileirar(string? chave, string? motivo)
    {
        var carimbo = _clock.UtcNow.ToLocalTime().ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture);
        var caminho = Path.Combine(_caminhos.Comandos, $"{Prefixo}{carimbo}_{Guid.NewGuid():N}.json");
        var comando = new ComandoReenfileirar
        {
            Chave = string.IsNullOrWhiteSpace(chave) ? null : chave.Trim(),
            Motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim(),
            CriadoEm = _clock.UtcNow,
        };
        return CaminhosI51.GravarJson(caminho, comando, _logger);
    }

    /// <summary>
    /// Aplica e apaga os pedidos pendentes (no processo do Worker). Devolve (notas reenfileiradas, comandos lidos).
    /// Comando ilegível vai para <c>comandos\falhos</c>.
    /// </summary>
    public (int Notas, int Comandos) Consumir(IRepositorioSeparacao repositorio)
    {
        ArgumentNullException.ThrowIfNull(repositorio);
        var notas = 0;
        var comandos = 0;
        try
        {
            if (!Directory.Exists(_caminhos.Comandos))
            {
                return (0, 0);
            }

            foreach (var arquivo in Directory.EnumerateFiles(_caminhos.Comandos, Prefixo + "*.json").OrderBy(a => a, StringComparer.Ordinal))
            {
                ComandoReenfileirar? cmd = null;
                try
                {
                    cmd = JsonSerializer.Deserialize<ComandoReenfileirar>(File.ReadAllText(arquivo), CaminhosI51.Json);
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    _logger.LogWarning(ex, "Comando ilegível ({Arquivo}); movido para falhos.", Path.GetFileName(arquivo));
                }

                MotivoI60? motivo = null;
                var valido = cmd is not null && (!string.IsNullOrEmpty(cmd.Chave) || ValidarMotivo(cmd.Motivo) is null);
                if (valido && string.IsNullOrEmpty(cmd!.Chave))
                {
                    motivo = Enum.Parse<MotivoI60>(cmd.Motivo!, ignoreCase: true);
                }

                if (!valido)
                {
                    MoverFalho(arquivo);
                    continue;
                }

                comandos++;
                notas += repositorio.Reenfileirar(cmd!.Chave, motivo, _clock.UtcNow);
                try
                {
                    File.Delete(arquivo);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Não foi possível apagar o comando {Arquivo}.", Path.GetFileName(arquivo));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Falha ao ler os comandos de reenfileiramento.");
        }

        if (comandos > 0)
        {
            _logger.LogInformation("Reenfileiramento manual: {Comandos} pedido(s), {Notas} nota(s) voltaram a Separada.", comandos, notas);
        }

        return (notas, comandos);
    }

    private void MoverFalho(string arquivo)
    {
        try
        {
            Directory.CreateDirectory(_caminhos.ComandosFalhos);
            File.Move(arquivo, Path.Combine(_caminhos.ComandosFalhos, Path.GetFileName(arquivo)), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Não foi possível mover o comando ilegível {Arquivo}.", Path.GetFileName(arquivo));
        }
    }
}
