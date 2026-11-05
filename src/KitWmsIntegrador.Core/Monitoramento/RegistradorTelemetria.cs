using System.Text.Json;
using System.Text.Json.Serialization;
using KitWmsIntegrador.Core.Configuracao;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Persistência da telemetria em JSONL (append + flush), seguindo o padrão do diário do pacote:
/// 1 linha por registro, leitura tolerante a linha truncada. Falhas de I/O são logadas, nunca
/// lançadas. O diretório base é resolvido de <see cref="OpcoesMonitoramento.CaminhoTelemetria"/>
/// (vazio = <c>&lt;Estado&gt;/telemetria</c>) e criado sob demanda.
/// </summary>
public sealed class RegistradorTelemetria
{
    /// <summary>Nome do arquivo de telemetria de pacotes.</summary>
    public const string NomeArquivoTelemetria = "telemetria.jsonl";

    /// <summary>Nome do arquivo de eventos de saúde do serviço.</summary>
    public const string NomeArquivoServico = "servico.jsonl";

    /// <summary>Subpasta padrão da telemetria dentro do Estado.</summary>
    public const string SubpastaPadrao = "telemetria";

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly OpcoesPasta _pastas;
    private readonly OpcoesMonitoramento _monitoramento;
    private readonly ILogger<RegistradorTelemetria> _logger;
    private readonly object _lock = new();

    /// <summary>Cria o registrador.</summary>
    public RegistradorTelemetria(
        IOptions<OpcoesPasta> pastas,
        IOptions<OpcoesMonitoramento> monitoramento,
        ILogger<RegistradorTelemetria> logger)
    {
        _pastas = pastas?.Value ?? throw new ArgumentNullException(nameof(pastas));
        _monitoramento = monitoramento?.Value ?? throw new ArgumentNullException(nameof(monitoramento));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Pasta base da telemetria (resolvida ao lado do Estado quando não configurada).</summary>
    public string PastaBase =>
        string.IsNullOrWhiteSpace(_monitoramento.CaminhoTelemetria)
            ? Path.Combine(_pastas.CaminhoEstado, SubpastaPadrao)
            : _monitoramento.CaminhoTelemetria;

    /// <summary>Caminho completo do arquivo de telemetria de pacotes.</summary>
    public string CaminhoTelemetria => Path.Combine(PastaBase, NomeArquivoTelemetria);

    /// <summary>Caminho completo do arquivo de eventos de saúde do serviço.</summary>
    public string CaminhoServico => Path.Combine(PastaBase, NomeArquivoServico);

    /// <summary>Acrescenta a telemetria de um pacote. Falhas são logadas, nunca lançadas.</summary>
    public void RegistrarPacote(RegistroTelemetria registro)
    {
        ArgumentNullException.ThrowIfNull(registro);
        Append(CaminhoTelemetria, registro, "a telemetria do pacote");
    }

    /// <summary>Acrescenta um evento de saúde do serviço. Falhas são logadas, nunca lançadas.</summary>
    public void RegistrarEventoServico(EventoServico evento)
    {
        ArgumentNullException.ThrowIfNull(evento);
        Append(CaminhoServico, evento, "o evento de serviço");
    }

    /// <summary>Lê a telemetria gravada; linhas inválidas são ignoradas.</summary>
    public IReadOnlyList<RegistroTelemetria> LerPacotes() =>
        Ler<RegistroTelemetria>(CaminhoTelemetria);

    /// <summary>Lê os eventos de serviço gravados; linhas inválidas são ignoradas.</summary>
    public IReadOnlyList<EventoServico> LerEventosServico() =>
        Ler<EventoServico>(CaminhoServico);

    private void Append<T>(string caminho, T registro, string descricao)
    {
        try
        {
            var linha = JsonSerializer.Serialize(registro, Json);
            lock (_lock)
            {
                Directory.CreateDirectory(PastaBase);
                using var fs = new FileStream(caminho, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var sw = new StreamWriter(fs);
                sw.WriteLine(linha);
                sw.Flush();
                fs.Flush(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao gravar {Descricao} ({Caminho}).", descricao, caminho);
        }
    }

    private IReadOnlyList<T> Ler<T>(string caminho)
    {
        var lista = new List<T>();
        try
        {
            lock (_lock)
            {
                if (!File.Exists(caminho))
                {
                    return lista;
                }

                foreach (var linha in File.ReadLines(caminho))
                {
                    if (string.IsNullOrWhiteSpace(linha))
                    {
                        continue;
                    }

                    try
                    {
                        var registro = JsonSerializer.Deserialize<T>(linha, Json);
                        if (registro is not null)
                        {
                            lista.Add(registro);
                        }
                    }
                    catch (JsonException)
                    {
                        _logger.LogWarning("Linha inválida ignorada na telemetria ({Caminho}).", caminho);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao ler a telemetria ({Caminho}).", caminho);
        }

        return lista;
    }
}
