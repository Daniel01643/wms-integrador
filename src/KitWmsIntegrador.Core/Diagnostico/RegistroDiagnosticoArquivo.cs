using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Diagnostico;

/// <summary>
/// Diário de diagnóstico em JSONL, um arquivo por dia local (<c>&lt;Estado&gt;\diagnostico\yyyyMMdd.jsonl</c>), no
/// padrão do <c>RegistradorTelemetria</c>: lock, append e flush. Falhas de I/O são logadas (no máximo uma vez por
/// minuto), nunca lançadas. <c>Mensagem</c> e <c>Backend</c> passam pelo <see cref="SanitizadorDiagnostico"/>
/// antes de gravar (ponto único de defesa contra segredos).
/// </summary>
public sealed class RegistroDiagnosticoArquivo : IRegistroDiagnostico
{
    /// <summary>Subpasta do diário dentro do Estado.</summary>
    public const string SubpastaPadrao = "diagnostico";

    private const string FormatoArquivo = "yyyyMMdd";
    private const string Extensao = ".jsonl";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IClock _clock;
    private readonly OpcoesPasta _pastas;
    private readonly OpcoesLimpeza _limpeza;
    private readonly ILogger<RegistroDiagnosticoArquivo> _logger;
    private readonly object _lock = new();
    private DateTimeOffset _ultimoAvisoUtc = DateTimeOffset.MinValue;

    /// <summary>Cria o registro.</summary>
    public RegistroDiagnosticoArquivo(
        IClock clock,
        IOptions<OpcoesPasta> pastas,
        IOptions<OpcoesLimpeza> limpeza,
        ILogger<RegistroDiagnosticoArquivo> logger)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _pastas = pastas?.Value ?? throw new ArgumentNullException(nameof(pastas));
        _limpeza = limpeza?.Value ?? throw new ArgumentNullException(nameof(limpeza));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Pasta do diário (<c>&lt;CaminhoEstado&gt;\diagnostico</c>).</summary>
    public string PastaBase => Path.Combine(_pastas.CaminhoEstado, SubpastaPadrao);

    /// <summary>Dias de retenção configurados em <c>Limpeza:DiasRetencao</c>.</summary>
    public int DiasRetencao => _limpeza.DiasRetencao;

    /// <summary>Espelha <c>Limpeza:Habilitada</c>: com a limpeza desligada o diário também não é apagado.</summary>
    public bool LimpezaHabilitada => _limpeza.Habilitada;

    /// <inheritdoc />
    public void Registrar(EventoDiagnostico evento)
    {
        try
        {
            if (evento is null || string.IsNullOrWhiteSpace(evento.Tipo))
            {
                return;
            }

            var quando = evento.QuandoLocal == default ? _clock.UtcNow.ToLocalTime() : evento.QuandoLocal;
            var gravar = evento with
            {
                QuandoLocal = quando,
                Mensagem = SanitizadorDiagnostico.Texto(evento.Mensagem),
                Backend = SanitizadorDiagnostico.Texto(evento.Backend, 120),
            };
            var linha = JsonSerializer.Serialize(gravar, Json);
            var caminho = CaminhoDoDia(quando);
            lock (_lock)
            {
                Directory.CreateDirectory(PastaBase);
                using var fs = new FileStream(caminho, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var sw = new StreamWriter(fs);
                sw.WriteLine(linha);
                sw.Flush();
                fs.Flush(true);
            }
        }
        catch (Exception ex)
        {
            AvisarComLimite(ex, "gravar");
        }
    }

    /// <summary>
    /// Lê os eventos com <c>QuandoLocal</c> em [<paramref name="inicio"/>, <paramref name="fim"/>]. Só abre os arquivos
    /// dos dias que tocam a janela; linha inválida é ignorada. Nunca lança.
    /// </summary>
    public IReadOnlyList<EventoDiagnostico> Ler(DateTimeOffset inicio, DateTimeOffset fim)
    {
        var lista = new List<EventoDiagnostico>();
        try
        {
            var primeiro = inicio.ToLocalTime().Date;
            var ultimo = fim.ToLocalTime().Date;
            lock (_lock)
            {
                for (var dia = primeiro; dia <= ultimo; dia = dia.AddDays(1))
                {
                    var caminho = Path.Combine(PastaBase, dia.ToString(FormatoArquivo, CultureInfo.InvariantCulture) + Extensao);
                    if (!File.Exists(caminho))
                    {
                        continue;
                    }

                    using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sr = new StreamReader(fs);
                    string? linha;
                    while ((linha = sr.ReadLine()) is not null)
                    {
                        if (string.IsNullOrWhiteSpace(linha))
                        {
                            continue;
                        }

                        try
                        {
                            var e = JsonSerializer.Deserialize<EventoDiagnostico>(linha, Json);
                            if (e is not null && !string.IsNullOrEmpty(e.Tipo) && e.QuandoLocal >= inicio && e.QuandoLocal <= fim)
                            {
                                lista.Add(e);
                            }
                        }
                        catch (JsonException)
                        {
                            // linha truncada/inválida: ignorada
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AvisarComLimite(ex, "ler");
        }

        return lista;
    }

    /// <summary>Apaga os <c>yyyyMMdd.jsonl</c> mais antigos que <paramref name="diasRetencao"/> dias. Devolve quantos apagou.</summary>
    public int Limpar(int diasRetencao)
    {
        var apagados = 0;
        try
        {
            if (!Directory.Exists(PastaBase))
            {
                return 0;
            }

            var corte = _clock.UtcNow.ToLocalTime().Date.AddDays(-Math.Max(1, diasRetencao));
            lock (_lock)
            {
                foreach (var caminho in Directory.EnumerateFiles(PastaBase, "*" + Extensao))
                {
                    var nome = Path.GetFileNameWithoutExtension(caminho);
                    if (!DateTime.TryParseExact(nome, FormatoArquivo, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia)
                        || dia >= corte)
                    {
                        continue;
                    }

                    try
                    {
                        File.Delete(caminho);
                        apagados++;
                    }
                    catch (Exception ex)
                    {
                        AvisarComLimite(ex, "apagar");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AvisarComLimite(ex, "limpar");
        }

        return apagados;
    }

    private string CaminhoDoDia(DateTimeOffset quando) =>
        Path.Combine(PastaBase, quando.ToLocalTime().Date.ToString(FormatoArquivo, CultureInfo.InvariantCulture) + Extensao);

    private void AvisarComLimite(Exception ex, string acao)
    {
        try
        {
            var agora = _clock.UtcNow;
            lock (_lock)
            {
                if (agora - _ultimoAvisoUtc < TimeSpan.FromMinutes(1))
                {
                    return;
                }

                _ultimoAvisoUtc = agora;
            }

            _logger.LogError(ex, "Falha ao {Acao} o diário de diagnóstico ({Pasta}).", acao, PastaBase);
        }
        catch
        {
            // o diário nunca lança
        }
    }
}
