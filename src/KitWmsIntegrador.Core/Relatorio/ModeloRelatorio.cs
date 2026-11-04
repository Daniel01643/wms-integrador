namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>Totais exibidos no resumo do topo (HTML e PDF usam o mesmo objeto).</summary>
/// <param name="Total">Notas no relatório.</param>
/// <param name="Integradas">Notas aceitas pelo WMS (inclui as "revisar produto").</param>
/// <param name="IntegradasRevisarProduto">Subconjunto de integradas com produto provisório a revisar.</param>
/// <param name="Pendentes">Notas pendentes na interface.</param>
/// <param name="NaoIntegradas">Notas não integradas.</param>
/// <param name="Transportes">Quantidade de transportes distintos.</param>
/// <param name="NotasPorTransporte">Notas por transporte, na ordem do relatório.</param>
public sealed record ResumoRelatorio(
    int Total,
    int Integradas,
    int IntegradasRevisarProduto,
    int Pendentes,
    int NaoIntegradas,
    int Transportes,
    IReadOnlyList<(string Transporte, int Notas)> NotasPorTransporte);

/// <summary>Grupo de linhas de um mesmo transporte.</summary>
/// <param name="Transporte">Número do transporte.</param>
/// <param name="Linhas">Linhas ordenadas por nota.</param>
public sealed record GrupoTransporte(string Transporte, IReadOnlyList<LinhaRelatorioErro> Linhas)
{
    /// <summary>Quantidade de notas do grupo.</summary>
    public int Notas => Linhas.Count;

    /// <summary>Notas integradas (inclui "revisar produto").</summary>
    public int Integradas => Linhas.Count(l => l.EstaIntegrada);

    /// <summary>Notas com problema: pendentes, não integradas ou integradas com produto a revisar.</summary>
    public int ComProblema => Linhas.Count(l => !l.EstaIntegrada || l.IntegradaRevisarProduto);
}

/// <summary>Produtos provisórios de uma nota, para a seção "revisar cadastro".</summary>
public sealed record ProdutosRevisao(string Nota, string Transporte, IReadOnlyList<string> Produtos);

/// <summary>
/// Modelo montado uma única vez e consumido pelos renderizadores HTML e PDF, garantindo
/// números idênticos nos dois formatos.
/// </summary>
public sealed record ModeloRelatorio(
    ResumoRelatorio Resumo,
    IReadOnlyList<GrupoTransporte> Grupos,
    IReadOnlyList<ProdutosRevisao> ProdutosRevisao)
{
    /// <summary>Monta o modelo com ordenação estável por transporte e nota (ordinal).</summary>
    public static ModeloRelatorio Montar(IReadOnlyList<LinhaRelatorioErro> linhas)
    {
        ArgumentNullException.ThrowIfNull(linhas);

        var ordenadas = linhas
            .OrderBy(l => l.NumeroTransporte ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(l => l.NumeroNota ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        var grupos = ordenadas
            .GroupBy(l => l.NumeroTransporte ?? string.Empty, StringComparer.Ordinal)
            .Select(g => new GrupoTransporte(g.Key, g.ToList()))
            .ToList();

        var resumo = new ResumoRelatorio(
            Total: ordenadas.Count,
            Integradas: ordenadas.Count(l => l.EstaIntegrada),
            IntegradasRevisarProduto: ordenadas.Count(l => l.IntegradaRevisarProduto),
            Pendentes: ordenadas.Count(l => !l.EstaIntegrada && l.StatusEfetivo == StatusLinhaRelatorio.Pendente),
            NaoIntegradas: ordenadas.Count(l => !l.EstaIntegrada && l.StatusEfetivo != StatusLinhaRelatorio.Pendente),
            Transportes: grupos.Count,
            NotasPorTransporte: grupos.Select(g => (g.Transporte, g.Notas)).ToList());

        var produtos = ordenadas
            .Where(l => l.RevisaoProdutoPendente)
            .Select(l => new ProdutosRevisao(l.NumeroNota, l.NumeroTransporte, l.ProdutosProvisorios!))
            .ToList();

        return new ModeloRelatorio(resumo, grupos, produtos);
    }
}
