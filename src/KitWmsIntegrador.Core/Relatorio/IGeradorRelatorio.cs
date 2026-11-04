namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>
/// Gera o relatório de erros/pendências de integração em HTML e PDF a partir de uma
/// lista de <see cref="LinhaRelatorioErro"/>. Sem comunicação de rede.
/// </summary>
public interface IGeradorRelatorio
{
    /// <summary>
    /// Monta o relatório como HTML autocontido (CSS inline, sem recursos externos), com resumo,
    /// linhas agrupadas por transporte, status coloridos, detalhe/histórico em &lt;details&gt; e
    /// filtro opcional em JavaScript puro.
    /// </summary>
    string GerarHtml(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm);

    /// <summary>
    /// Monta o relatório como PDF A4 paisagem (bytes), com resumo, agrupamento por transporte,
    /// quebra de linha nas células, cabeçalho da tabela repetido e rodapé "Página X de Y".
    /// </summary>
    byte[] GerarPdf(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm);

    /// <summary>
    /// Gera os dois arquivos (HTML e PDF) na pasta de saída configurada
    /// (<c>OpcoesRelatorio.PastaSaida</c>), com nome <c>erros_&lt;yyyy-MM-dd_HHmm&gt;.&lt;ext&gt;</c>
    /// derivado de <paramref name="geradoEm"/>, e retorna os caminhos gravados.
    /// </summary>
    (string CaminhoHtml, string CaminhoPdf) GerarArquivos(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm);
}
