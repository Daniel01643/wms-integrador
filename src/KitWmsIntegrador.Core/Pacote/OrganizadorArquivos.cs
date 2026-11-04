using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Pacote;

/// <summary>Resultado da movimentação de um arquivo.</summary>
/// <param name="Sucesso">Verdadeiro se o arquivo foi movido.</param>
/// <param name="Caminho">Caminho final (vazio em falha).</param>
/// <param name="Erro">Mensagem de erro (null em sucesso).</param>
public sealed record ResultadoMovimento(bool Sucesso, string Caminho, string? Erro);

/// <summary>
/// Move arquivos da entrada para Processados ou Erro (conforme <see cref="OpcoesPasta"/>).
/// Nunca lança por causa de um arquivo: devolve <see cref="ResultadoMovimento"/>.
/// </summary>
public sealed class OrganizadorArquivos
{
    private const int MaxSufixo = 1000;

    private readonly IClock _clock;
    private readonly OpcoesPasta _opcoes;
    private readonly ILogger<OrganizadorArquivos> _logger;

    /// <summary>Cria o organizador.</summary>
    public OrganizadorArquivos(IClock clock, IOptions<OpcoesPasta> opcoes, ILogger<OrganizadorArquivos> logger)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _opcoes = opcoes?.Value ?? throw new ArgumentNullException(nameof(opcoes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Move <paramref name="arquivo"/> para o destino. Em Erro, grava o sidecar
    /// <c>&lt;arquivo&gt;.motivo.txt</c> e usa a organização configurada. Carimba
    /// <c>LastWriteTimeUtc</c> com o relógio (base da limpeza).
    /// </summary>
    public ResultadoMovimento Mover(string arquivo, DestinoArquivo destino, CategoriaErro? categoria, string motivo)
    {
        try
        {
            if (!File.Exists(arquivo))
            {
                return Falha(arquivo, "Arquivo não encontrado.");
            }

            var pasta = PastaDestino(arquivo, destino, categoria);
            Directory.CreateDirectory(pasta);

            var final = CaminhoUnico(pasta, Path.GetFileName(arquivo));
            File.Move(arquivo, final);

            try
            {
                File.SetLastWriteTimeUtc(final, _clock.UtcNow.UtcDateTime);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não foi possível carimbar a data de {Arquivo}.", final);
            }

            if (destino == DestinoArquivo.Erro)
            {
                GravarSidecar(final, motivo);
            }

            _logger.LogInformation("Arquivo movido para {Destino}: {Caminho}", destino, final);
            return new ResultadoMovimento(true, final, null);
        }
        catch (Exception ex)
        {
            return Falha(arquivo, ex.Message, ex);
        }
    }

    private string PastaDestino(string arquivo, DestinoArquivo destino, CategoriaErro? categoria)
    {
        if (destino == DestinoArquivo.Processados)
        {
            return _opcoes.CaminhoProcessados;
        }

        var erro = _opcoes.CaminhoErro;
        return _opcoes.OrganizacaoErro switch
        {
            OrganizacaoErro.PorCategoria => Path.Combine(erro, (categoria ?? CategoriaErro.Outro).ToString()),
            OrganizacaoErro.PorArquivo => Path.Combine(erro, Path.GetFileNameWithoutExtension(arquivo)),
            _ => erro,
        };
    }

    private static string CaminhoUnico(string pasta, string nome)
    {
        var candidato = Path.Combine(pasta, nome);
        if (!File.Exists(candidato))
        {
            return candidato;
        }

        var baseNome = Path.GetFileNameWithoutExtension(nome);
        var extensao = Path.GetExtension(nome);
        for (var i = 1; i <= MaxSufixo; i++)
        {
            candidato = Path.Combine(pasta, $"{baseNome}_{i}{extensao}");
            if (!File.Exists(candidato))
            {
                return candidato;
            }
        }

        throw new IOException($"Não foi possível obter nome único para {nome} em {pasta}.");
    }

    private void GravarSidecar(string arquivoFinal, string motivo)
    {
        try
        {
            var sidecar = arquivoFinal + ".motivo.txt";
            File.WriteAllText(sidecar, motivo ?? string.Empty);
            File.SetLastWriteTimeUtc(sidecar, _clock.UtcNow.UtcDateTime);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível gravar o motivo de {Arquivo}.", arquivoFinal);
        }
    }

    private ResultadoMovimento Falha(string arquivo, string erro, Exception? ex = null)
    {
        _logger.LogError(ex, "Falha ao mover {Arquivo}: {Erro}", arquivo, erro);
        return new ResultadoMovimento(false, string.Empty, erro);
    }
}
