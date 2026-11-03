namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>
/// Gera o relatório de erros/pendências de integração em HTML e PDF a partir de uma
/// lista de <see cref="LinhaRelatorioErro"/>. Sem comunicação de rede.
/// </summary>
public interface IGeradorRelatorio
{
    /// <summary>
    /// Monta o relatório como HTML (string), com título, data de geração e uma tabela
    /// com as colunas da linha.
    /// </summary>
    string GerarHtml(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm);

    /// <summary>
    /// Monta o relatório como PDF (bytes), com título, data de geração e as colunas
    /// desenhadas em tabela simples.
    /// </summary>
    byte[] GerarPdf(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm);

    /// <summary>
    /// Gera os dois arquivos (HTML e PDF) na pasta de saída configurada
    /// (<c>OpcoesRelatorio.PastaSaida</c>), com nome <c>erros_&lt;yyyy-MM-dd_HHmm&gt;.&lt;ext&gt;</c>
    /// derivado de <paramref name="geradoEm"/>, e retorna os caminhos gravados.
    /// </summary>
    (string CaminhoHtml, string CaminhoPdf) GerarArquivos(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm);
}
