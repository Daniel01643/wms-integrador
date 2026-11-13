using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KitWmsIntegrador.Core.Configuracao;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Separacao;

/// <summary>Pastas de <c>Estado\i51\</c> e utilitários de escrita atômica (nunca lançam por disco).</summary>
public sealed class CaminhosI51
{
    /// <summary>Opções JSON do estado (enums como texto).</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly OpcoesPasta _pastas;

    /// <summary>Cria a resolução de caminhos.</summary>
    public CaminhosI51(IOptions<OpcoesPasta> pastas)
    {
        _pastas = pastas?.Value ?? throw new ArgumentNullException(nameof(pastas));
    }

    /// <summary><c>Estado\i51</c>.</summary>
    public string Base => Path.Combine(_pastas.CaminhoEstado, "i51");

    /// <summary>Um JSON por nota.</summary>
    public string Notas => Path.Combine(Base, "notas");

    /// <summary>Cópias do XML da NF-e.</summary>
    public string Xml => Path.Combine(Base, "xml");

    /// <summary>Documentos sem nota correspondente.</summary>
    public string Orfaos => Path.Combine(Base, "orfaos");

    /// <summary>Respostas brutas ainda não processadas.</summary>
    public string BrutosPendentes => Path.Combine(Base, "brutos", "pendentes");

    /// <summary>Respostas brutas processadas (por data).</summary>
    public string BrutosProcessados => Path.Combine(Base, "brutos", "processados");

    /// <summary>Respostas brutas que esgotaram as tentativas.</summary>
    public string BrutosFalhos => Path.Combine(Base, "brutos", "falhos");

    /// <summary>Pedidos de reenfileiramento vindos de outro processo.</summary>
    public string Comandos => Path.Combine(Base, "comandos");

    /// <summary>Comandos ilegíveis.</summary>
    public string ComandosFalhos => Path.Combine(Base, "comandos", "falhos");

    /// <summary>Logs diários dos alertas imediatos da I60 (arquivo de calibração).</summary>
    public string Alertas => Path.Combine(Base, "alertas");

    /// <summary>Arquivo de saúde da consulta.</summary>
    public string ArquivoConsulta => Path.Combine(Base, "consulta.json");

    /// <summary>Nome seguro para arquivo: só <c>[0-9A-Za-z_-]</c>.</summary>
    public static string NomeSeguro(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return "_";
        }

        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto)
        {
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_');
        }

        return sb.ToString();
    }

    /// <summary>Grava o texto de forma atômica (.tmp + Move). Devolve false (e loga) se falhar.</summary>
    public static bool GravarTexto(string caminho, string texto, ILogger logger)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
            var tmp = caminho + ".tmp";
            File.WriteAllText(tmp, texto, new UTF8Encoding(false));
            File.Move(tmp, caminho, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            logger.LogError(ex, "Falha ao gravar {Arquivo} (estado I51).", Path.GetFileName(caminho));
            return false;
        }
    }

    /// <summary>Grava o objeto como JSON de forma atômica.</summary>
    public static bool GravarJson<T>(string caminho, T valor, ILogger logger) =>
        GravarTexto(caminho, JsonSerializer.Serialize(valor, Json), logger);
}
