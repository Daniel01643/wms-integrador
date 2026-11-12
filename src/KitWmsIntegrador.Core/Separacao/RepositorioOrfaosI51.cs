using System.Text.Json;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;

namespace KitWmsIntegrador.Core.Separacao;

/// <summary>
/// Documentos da I51 sem nota correspondente (a nota pode ainda não ter sido registrada). Um JSON por
/// documento em <c>Estado\i51\orfaos</c>; o nome traz número, CNPJ destinatário e o carimbo do bruto.
/// Nunca lança por falha de disco.
/// </summary>
public sealed class RepositorioOrfaosI51
{
    private readonly CaminhosI51 _caminhos;
    private readonly IClock _clock;
    private readonly ILogger<RepositorioOrfaosI51> _logger;

    /// <summary>Cria o repositório.</summary>
    public RepositorioOrfaosI51(CaminhosI51 caminhos, IClock clock, ILogger<RepositorioOrfaosI51> logger)
    {
        _caminhos = caminhos ?? throw new ArgumentNullException(nameof(caminhos));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Guarda o documento. Devolve false (e loga) se a gravação falhar.</summary>
    public bool Salvar(OrfaoI51 orfao) =>
        CaminhosI51.GravarJson(Caminho(orfao), orfao with { RecebidoEm = orfao.RecebidoEm == default ? _clock.UtcNow : orfao.RecebidoEm }, _logger);

    /// <summary>Lista os órfãos (nome do arquivo e conteúdo).</summary>
    public IReadOnlyList<(string Arquivo, OrfaoI51 Orfao)> Listar()
    {
        var lista = new List<(string, OrfaoI51)>();
        try
        {
            if (!Directory.Exists(_caminhos.Orfaos))
            {
                return lista;
            }

            foreach (var arquivo in Directory.EnumerateFiles(_caminhos.Orfaos, "*.json").OrderBy(a => a, StringComparer.Ordinal))
            {
                try
                {
                    var o = JsonSerializer.Deserialize<OrfaoI51>(File.ReadAllText(arquivo), CaminhosI51.Json);
                    if (o is not null)
                    {
                        lista.Add((arquivo, o));
                    }
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Órfão ilegível ignorado ({Arquivo}).", Path.GetFileName(arquivo));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Falha ao listar os documentos órfãos da I51.");
        }

        return lista;
    }

    /// <summary>Quantidade de órfãos recebidos desde <paramref name="desde"/>.</summary>
    public int ContarDesde(DateTimeOffset desde) => Listar().Count(o => o.Orfao.RecebidoEm >= desde);

    /// <summary>Remove um órfão.</summary>
    public void Remover(string arquivo)
    {
        try
        {
            File.Delete(arquivo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Não foi possível apagar o órfão {Arquivo}.", Path.GetFileName(arquivo));
        }
    }

    /// <summary>Apaga os órfãos mais antigos que a retenção.</summary>
    public int Limpar(DateTimeOffset agora, int diasRetencao)
    {
        var limite = agora - TimeSpan.FromDays(diasRetencao);
        var total = 0;
        foreach (var (arquivo, o) in Listar().Where(x => x.Orfao.RecebidoEm < limite).ToList())
        {
            Remover(arquivo);
            total++;
        }

        return total;
    }

    private string Caminho(OrfaoI51 o) =>
        Path.Combine(
            _caminhos.Orfaos,
            $"{CaminhosI51.NomeSeguro(o.Numero)}_{CaminhosI51.NomeSeguro(o.CnpjDestinatario)}_{CaminhosI51.NomeSeguro(Path.GetFileNameWithoutExtension(o.BrutoOrigem))}.json");
}
