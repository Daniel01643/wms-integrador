using System.Globalization;
using System.Text.Json;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Pacote;

/// <summary>
/// Guarda o sequencial do dia e as ondas já criadas (por hash do conteúdo do ZIP) em
/// <c>Estado\tsp\ondas.json</c>. O sequencial reinicia no dia seguinte. Reprocessar o mesmo ZIP
/// (mesmo conteúdo) devolve a mesma onda, sem consumir sequencial. Nunca lança por falha de disco.
/// </summary>
public sealed class RegistroOndasTsp
{
    /// <summary>Subpasta do Estado.</summary>
    public const string Subpasta = "tsp";

    /// <summary>Nome do arquivo de estado.</summary>
    public const string NomeArquivo = "ondas.json";

    private const int DiasRetencaoHash = 90;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly IClock _clock;
    private readonly OpcoesPasta _pastas;
    private readonly ILogger<RegistroOndasTsp> _logger;
    private readonly object _lock = new();
    private EstadoOndas? _cache;

    /// <summary>Cria o registro.</summary>
    public RegistroOndasTsp(IClock clock, IOptions<OpcoesPasta> pastas, ILogger<RegistroOndasTsp> logger)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _pastas = pastas?.Value ?? throw new ArgumentNullException(nameof(pastas));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Caminho completo do arquivo de estado.</summary>
    public string Caminho => Path.Combine(_pastas.CaminhoEstado, Subpasta, NomeArquivo);

    /// <summary>
    /// Devolve a onda do ZIP: a já existente para o mesmo <paramref name="hashConteudo"/> ou uma
    /// nova (próximo sequencial do dia).
    /// </summary>
    public string ObterOuCriar(string nomeArquivoZip, string hashConteudo)
    {
        lock (_lock)
        {
            var estado = _cache ??= Carregar();
            if (!string.IsNullOrEmpty(hashConteudo) && estado.Ondas.TryGetValue(hashConteudo, out var existente))
            {
                return existente.Onda;
            }

            var agora = _clock.UtcNow.ToLocalTime();
            var hoje = agora.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            if (!string.Equals(estado.Data, hoje, StringComparison.Ordinal))
            {
                estado.Data = hoje;
                estado.Ultimo = 0;
            }

            estado.Ultimo++;
            var onda = NomeadorOndaTsp.Montar(agora.Date, estado.Ultimo, nomeArquivoZip);
            if (!string.IsNullOrEmpty(hashConteudo))
            {
                estado.Ondas[hashConteudo] = new EntradaOnda { Onda = onda, Data = hoje };
            }

            Podar(estado, agora.Date);
            Salvar(estado);
            return onda;
        }
    }

    private EstadoOndas Carregar()
    {
        try
        {
            if (!File.Exists(Caminho))
            {
                return new EstadoOndas();
            }

            var texto = File.ReadAllText(Caminho);
            var estado = JsonSerializer.Deserialize<EstadoOndas>(texto, Json);
            if (estado is null)
            {
                throw new JsonException("Conteúdo vazio.");
            }

            estado.Ondas = new Dictionary<string, EntradaOnda>(estado.Ondas ?? new(), StringComparer.OrdinalIgnoreCase);
            return estado;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning(
                ex, "Arquivo de ondas TSP ilegível ({Caminho}); o sequencial recomeça do 01 e o arquivo antigo foi preservado.", Caminho);
            Preservar();
            return new EstadoOndas();
        }
    }

    private void Preservar()
    {
        try
        {
            var destino = Caminho + ".corrompido-" + _clock.UtcNow.ToLocalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            if (File.Exists(Caminho) && !File.Exists(destino))
            {
                File.Move(Caminho, destino);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Não foi possível preservar o arquivo de ondas TSP ilegível.");
        }
    }

    private void Salvar(EstadoOndas estado)
    {
        try
        {
            var pasta = Path.GetDirectoryName(Caminho)!;
            Directory.CreateDirectory(pasta);
            var temporario = Caminho + ".tmp";
            File.WriteAllText(temporario, JsonSerializer.Serialize(estado, Json));
            File.Move(temporario, Caminho, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Falha ao gravar o estado das ondas TSP ({Caminho}); segue só em memória.", Caminho);
        }
    }

    private static void Podar(EstadoOndas estado, DateTime hoje)
    {
        var velhos = estado.Ondas
            .Where(p => DateTime.TryParseExact(p.Value.Data, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                        && (hoje - d).TotalDays > DiasRetencaoHash)
            .Select(p => p.Key)
            .ToList();
        foreach (var chave in velhos)
        {
            estado.Ondas.Remove(chave);
        }
    }

    private sealed class EstadoOndas
    {
        public string Data { get; set; } = string.Empty;

        public int Ultimo { get; set; }

        public Dictionary<string, EntradaOnda> Ondas { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class EntradaOnda
    {
        public string Onda { get; set; } = string.Empty;

        public string Data { get; set; } = string.Empty;
    }
}
