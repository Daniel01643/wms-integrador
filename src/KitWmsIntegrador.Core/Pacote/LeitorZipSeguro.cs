using System.IO.Compression;
using System.Security.Cryptography;
using KitWmsIntegrador.Core.Configuracao;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Pacote;

/// <summary>O ZIP inteiro é inválido (corrompido, protegido, estourou limite ou sem XML).</summary>
public sealed class ZipInvalidoException : Exception
{
    /// <summary>Cria a exceção com o motivo (texto que vai para o relatório).</summary>
    public ZipInvalidoException(string motivo, Exception? interna = null)
        : base(motivo, interna)
    {
    }
}

/// <summary>Um XML extraído do ZIP para a pasta temporária.</summary>
/// <param name="Caminho">Caminho do XML extraído (nome original, dentro de uma subpasta numerada).</param>
/// <param name="NomeNoZip">Nome da entrada dentro do ZIP.</param>
public sealed record XmlExtraido(string Caminho, string NomeNoZip);

/// <summary>Entrada do ZIP que não foi extraída (não é XML, ZIP aninhado ou caminho inseguro).</summary>
/// <param name="Nome">Nome da entrada dentro do ZIP.</param>
/// <param name="Motivo">Por que não foi usada.</param>
/// <param name="Insegura">Verdadeiro se o caminho era perigoso (zip-slip); falso para extensão inválida.</param>
public sealed record EntradaZipIgnorada(string Nome, string Motivo, bool Insegura);

/// <summary>Resultado da leitura de um ZIP.</summary>
/// <param name="PastaTemporaria">Pasta com os XMLs extraídos (apagar com <see cref="LeitorZipSeguro.Limpar"/>).</param>
/// <param name="Xmls">XMLs extraídos, na ordem do ZIP.</param>
/// <param name="Ignoradas">Entradas não extraídas, com o motivo.</param>
/// <param name="Hash">SHA-256 (hex minúsculo) do conteúdo do ZIP.</param>
public sealed record ResultadoZip(
    string PastaTemporaria,
    IReadOnlyList<XmlExtraido> Xmls,
    IReadOnlyList<EntradaZipIgnorada> Ignoradas,
    string Hash);

/// <summary>
/// Extrai com segurança os XMLs de um ZIP para <c>Estado\tmp\&lt;guid&gt;</c>. Só extrai
/// entradas <c>.xml</c> pelo NOME do arquivo (nunca usa o caminho interno, o que impede zip-slip),
/// recusa ZIP aninhado, respeita <see cref="OpcoesZip.MaxArquivos"/> e
/// <see cref="OpcoesZip.MaxTamanhoMb"/> (conferindo o tamanho declarado e os bytes realmente
/// copiados, contra ZIP bomba que mente no cabeçalho). Sem NuGet: System.IO.Compression.
/// </summary>
public sealed class LeitorZipSeguro
{
    /// <summary>Subpasta do Estado com as extrações temporárias.</summary>
    public const string SubpastaTemporaria = "tmp";

    private readonly OpcoesZip _opcoes;
    private readonly OpcoesPasta _pastas;
    private readonly ILogger<LeitorZipSeguro> _logger;

    /// <summary>Cria o leitor.</summary>
    public LeitorZipSeguro(IOptions<OpcoesZip> opcoes, IOptions<OpcoesPasta> pastas, ILogger<LeitorZipSeguro> logger)
    {
        _opcoes = opcoes?.Value ?? throw new ArgumentNullException(nameof(opcoes));
        _pastas = pastas?.Value ?? throw new ArgumentNullException(nameof(pastas));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Extrai os XMLs do ZIP. Lança <see cref="ZipInvalidoException"/> se o ZIP for ilegível,
    /// protegido por senha, estourar limite ou não tiver nenhum XML; nesse caso a pasta temporária
    /// já foi apagada.
    /// </summary>
    public ResultadoZip Extrair(string caminhoZip)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminhoZip);
        var temp = Path.Combine(_pastas.CaminhoEstado, SubpastaTemporaria, Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            var resultado = ExtrairPara(caminhoZip, temp);
            return resultado;
        }
        catch
        {
            Limpar(temp);
            throw;
        }
    }

    /// <summary>Apaga a pasta temporária (melhor esforço; nunca lança).</summary>
    public void Limpar(string pastaTemporaria)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(pastaTemporaria) && Directory.Exists(pastaTemporaria))
            {
                Directory.Delete(pastaTemporaria, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Não foi possível apagar a pasta temporária {Pasta}.", pastaTemporaria);
        }
    }

    private ResultadoZip ExtrairPara(string caminhoZip, string temp)
    {
        var limiteBytes = (long)_opcoes.MaxTamanhoMb * 1024 * 1024;
        var tempCompleto = Path.GetFullPath(temp);
        string hash;
        var xmls = new List<XmlExtraido>();
        var ignoradas = new List<EntradaZipIgnorada>();

        try
        {
            hash = CalcularHash(caminhoZip);
            using var arquivo = ZipFile.OpenRead(caminhoZip);
            var entradas = arquivo.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
            if (entradas.Count > _opcoes.MaxArquivos)
            {
                throw new ZipInvalidoException(
                    $"ZIP com {entradas.Count} arquivos; o máximo aceito é {_opcoes.MaxArquivos} (Zip:MaxArquivos).");
            }

            if (entradas.Sum(e => e.Length) > limiteBytes)
            {
                throw new ZipInvalidoException(
                    $"ZIP descompactado passa de {_opcoes.MaxTamanhoMb} MB (Zip:MaxTamanhoMb).");
            }

            long copiados = 0;
            var indice = 0;
            foreach (var entrada in entradas)
            {
                var nomeNoZip = entrada.FullName;
                var inseguro = MotivoInseguro(nomeNoZip);
                if (inseguro is not null)
                {
                    ignoradas.Add(new EntradaZipIgnorada(nomeNoZip, inseguro, true));
                    continue;
                }

                // Só o NOME do arquivo é usado; o caminho interno do ZIP é descartado (anti zip-slip).
                var normalizado = nomeNoZip.Replace('\\', '/');
                var nome = normalizado[(normalizado.LastIndexOf('/') + 1)..];
                if (nome.Length == 0)
                {
                    continue;
                }

                var extensao = Path.GetExtension(nome);
                if (string.Equals(extensao, ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ignoradas.Add(new EntradaZipIgnorada(nomeNoZip, "ZIP dentro de ZIP não é aberto. Salve os XMLs direto no ZIP.", false));
                    continue;
                }

                if (!string.Equals(extensao, ".xml", StringComparison.OrdinalIgnoreCase))
                {
                    ignoradas.Add(new EntradaZipIgnorada(
                        nomeNoZip, $"Arquivo ignorado: extensão {(extensao.Length == 0 ? "(nenhuma)" : extensao)} não é XML.", false));
                    continue;
                }

                indice++;
                var pasta = Path.Combine(tempCompleto, indice.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
                var destino = Path.GetFullPath(Path.Combine(pasta, nome));
                if (!destino.StartsWith(tempCompleto + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    ignoradas.Add(new EntradaZipIgnorada(nomeNoZip, "Caminho inseguro dentro do ZIP (ignorado).", true));
                    continue;
                }

                Directory.CreateDirectory(pasta);
                using (var origem = entrada.Open())
                using (var saida = new FileStream(destino, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    int lidos;
                    while ((lidos = origem.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        copiados += lidos;
                        if (copiados > limiteBytes)
                        {
                            throw new ZipInvalidoException(
                                $"ZIP descompactado passa de {_opcoes.MaxTamanhoMb} MB (Zip:MaxTamanhoMb).");
                        }

                        saida.Write(buffer, 0, lidos);
                    }
                }

                xmls.Add(new XmlExtraido(destino, nomeNoZip));
            }
        }
        catch (ZipInvalidoException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException or
                                       UnauthorizedAccessException or CryptographicException)
        {
            // Inclui ZIP corrompido, truncado e protegido por senha (a leitura da entrada falha).
            _logger.LogWarning(ex, "ZIP ilegível: {Zip}", Path.GetFileName(caminhoZip));
            throw new ZipInvalidoException(
                "ZIP ilegível, corrompido ou protegido por senha. Gere o ZIP de novo e salve outra vez.", ex);
        }

        if (xmls.Count == 0)
        {
            throw new ZipInvalidoException("ZIP sem nenhum arquivo XML.");
        }

        return new ResultadoZip(temp, xmls, ignoradas, hash);
    }

    // Perigoso: caminho absoluto, unidade (C:), segmento "..", ou nome com caractere inválido.
    private static string? MotivoInseguro(string nomeNoZip)
    {
        var normalizado = nomeNoZip.Replace('\\', '/');
        if (normalizado.StartsWith('/') || normalizado.Contains(':') || Path.IsPathRooted(nomeNoZip))
        {
            return "Caminho absoluto dentro do ZIP (ignorado por segurança).";
        }

        if (normalizado.Split('/').Any(s => s == ".."))
        {
            return "Caminho com \"..\" dentro do ZIP (ignorado por segurança).";
        }

        var nome = normalizado[(normalizado.LastIndexOf('/') + 1)..];
        if (nome.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "Nome de arquivo inválido dentro do ZIP (ignorado).";
        }

        return null;
    }

    private static string CalcularHash(string caminho)
    {
        using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }
}
