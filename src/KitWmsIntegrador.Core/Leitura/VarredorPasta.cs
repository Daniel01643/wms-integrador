using KitWmsIntegrador.Core.Modelos;

namespace KitWmsIntegrador.Core.Leitura;

/// <summary>
/// Erro de parse de um arquivo específico durante a varredura. A varredura não
/// aborta no primeiro erro: coleta o problema e segue para o próximo arquivo.
/// </summary>
public sealed record ErroArquivo(string Arquivo, string Mensagem);

/// <summary>
/// Varre uma pasta de XMLs de NF-e em streaming, um arquivo por vez
/// (<c>yield return</c>), sem carregar todos em memória.
/// </summary>
public sealed class VarredorPasta
{
    private readonly LeitorNotaFiscalXml _leitor;

    public VarredorPasta(LeitorNotaFiscalXml leitor)
    {
        _leitor = leitor;
    }

    /// <summary>Quantidade de arquivos .XML encontrados na última varredura.</summary>
    public int ArquivosEncontrados { get; private set; }

    /// <summary>Erros de parse coletados na última varredura (um por arquivo).</summary>
    public IReadOnlyList<ErroArquivo> Erros => _erros;

    private readonly List<ErroArquivo> _erros = new();

    /// <summary>
    /// Enumera as notas fiscais lidas de <paramref name="pasta"/>. Arquivos com
    /// erro de parse são registrados em <see cref="Erros"/> e pulados, sem
    /// interromper a varredura. Somente leitura — nenhum arquivo é alterado.
    /// </summary>
    public IEnumerable<NotaFiscal> Varrer(string pasta)
    {
        _erros.Clear();
        ArquivosEncontrados = 0;

        if (!Directory.Exists(pasta))
        {
            throw new DirectoryNotFoundException($"Pasta de entrada não encontrada: {pasta}");
        }

        var arquivos = Directory.EnumerateFiles(pasta, "*.xml", SearchOption.TopDirectoryOnly);

        foreach (var arquivo in arquivos)
        {
            ArquivosEncontrados++;

            NotaFiscal? nota = null;
            try
            {
                nota = _leitor.ParseArquivo(arquivo);
            }
            catch (Exception ex)
            {
                _erros.Add(new ErroArquivo(arquivo, ex.Message));
            }

            if (nota is not null)
            {
                yield return nota;
            }
        }
    }
}
