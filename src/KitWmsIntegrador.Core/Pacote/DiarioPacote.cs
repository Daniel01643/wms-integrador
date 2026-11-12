using System.Text.Json;
using System.Text.Json.Serialization;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Pacote;

/// <summary>
/// Resultado de um arquivo no pacote (uma linha do diário). Contém o necessário para remontar
/// a linha do relatório após uma queda/reinício.
/// </summary>
public sealed record RegistroDiario(
    string Arquivo,
    bool XmlInvalido,
    string NumeroNota,
    string NumeroTransporte,
    DateTimeOffset Horario,
    string Mensagem,
    bool Integrada,
    int Tentativas,
    StatusLinhaRelatorio Status,
    string IdTransacao,
    string IdDocumento,
    IReadOnlyList<string> ProdutosProvisorios,
    IReadOnlyList<string> Historico,
    CategoriaErro? Categoria,
    DestinoArquivo Destino,
    string? IdentificadorPacote = null,
    string Tipo = "PUXADA",
    string Origem = "",
    string Onda = "",
    string? Zip = null)
{
    /// <summary>Rótulo usado no relatório quando a nota/arquivo não tem transporte.</summary>
    public const string RotuloSemTransporte = "(sem transporte)";

    /// <summary>Rótulo da coluna Transporte na TSP: nota sem transporte sobe normalmente (a onda é o ZIP).</summary>
    public const string RotuloSemTransporteTsp = "(sem)";

    /// <summary>Converte para a linha do relatório.</summary>
    public LinhaRelatorioErro ParaLinha() => new(
        NumeroNota,
        // Arquivo ignorado (não-XML) não é nota: não recebe o rótulo "(sem transporte)", fica vazio.
        Status == StatusLinhaRelatorio.ArquivoIgnorado
            ? string.Empty
            : string.IsNullOrWhiteSpace(NumeroTransporte)
                ? (string.Equals(Tipo, TipoNotaExtensoes.RotuloTsp, StringComparison.OrdinalIgnoreCase) ? RotuloSemTransporteTsp : RotuloSemTransporte)
                : NumeroTransporte,
        Horario,
        Mensagem,
        Integrada,
        Tentativas,
        IdTransacao ?? string.Empty,
        IdDocumento ?? string.Empty,
        Status,
        ProdutosProvisorios ?? Array.Empty<string>(),
        Historico ?? Array.Empty<string>(),
        string.IsNullOrWhiteSpace(Tipo) ? TipoNotaExtensoes.RotuloPuxada : Tipo,
        Origem ?? string.Empty,
        Onda ?? string.Empty,
        Zip ?? string.Empty);
}

/// <summary>
/// Diário do pacote em JSONL (<c>pacote-atual.jsonl</c> na pasta de estado): 1 linha por
/// resultado, com append + flush. Permite retomar o pacote após reinício. Tolera linha final
/// truncada/corrompida.
/// </summary>
public sealed class DiarioPacote
{
    /// <summary>Nome do arquivo do diário.</summary>
    public const string NomeArquivo = "pacote-atual.jsonl";

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly OpcoesPasta _pastas;
    private readonly ILogger<DiarioPacote> _logger;
    private readonly object _lock = new();

    /// <summary>Cria o diário.</summary>
    public DiarioPacote(IOptions<OpcoesPasta> pastas, ILogger<DiarioPacote> logger)
    {
        _pastas = pastas?.Value ?? throw new ArgumentNullException(nameof(pastas));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Caminho completo do arquivo do diário.</summary>
    public string Caminho => Path.Combine(_pastas.CaminhoEstado, NomeArquivo);

    /// <summary>Acrescenta um registro (append + flush). Falhas são logadas, nunca lançadas.</summary>
    public void Registrar(RegistroDiario registro)
    {
        ArgumentNullException.ThrowIfNull(registro);
        try
        {
            var linha = JsonSerializer.Serialize(registro, Json);
            lock (_lock)
            {
                Directory.CreateDirectory(_pastas.CaminhoEstado);
                using var fs = new FileStream(Caminho, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var sw = new StreamWriter(fs);
                sw.WriteLine(linha);
                sw.Flush();
                fs.Flush(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao gravar o diário do pacote ({Caminho}).", Caminho);
        }
    }

    /// <summary>Lê os registros gravados; linhas inválidas (ex.: última truncada) são ignoradas.</summary>
    public IReadOnlyList<RegistroDiario> Ler()
    {
        var lista = new List<RegistroDiario>();
        try
        {
            lock (_lock)
            {
                if (!File.Exists(Caminho))
                {
                    return lista;
                }

                foreach (var linha in File.ReadLines(Caminho))
                {
                    if (string.IsNullOrWhiteSpace(linha))
                    {
                        continue;
                    }

                    try
                    {
                        var registro = JsonSerializer.Deserialize<RegistroDiario>(linha, Json);
                        if (registro is not null)
                        {
                            lista.Add(registro);
                        }
                    }
                    catch (JsonException)
                    {
                        _logger.LogWarning("Linha inválida ignorada no diário do pacote.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao ler o diário do pacote ({Caminho}).", Caminho);
        }

        return lista;
    }

    /// <summary>Apaga o diário. Devolve falso (e loga) se não conseguiu.</summary>
    public bool Apagar()
    {
        try
        {
            lock (_lock)
            {
                if (File.Exists(Caminho))
                {
                    File.Delete(Caminho);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao apagar o diário do pacote ({Caminho}).", Caminho);
            return false;
        }
    }
}
