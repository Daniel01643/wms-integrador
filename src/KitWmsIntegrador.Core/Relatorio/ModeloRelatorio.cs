using KitWmsIntegrador.Core.Pacote;

namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>Totais exibidos no resumo do topo (HTML e PDF usam o mesmo objeto).</summary>
/// <param name="Total">Notas no relatório.</param>
/// <param name="Integradas">Notas aceitas pelo WMS (inclui as "revisar produto").</param>
/// <param name="IntegradasRevisarProduto">Subconjunto de integradas com produto provisório a revisar.</param>
/// <param name="Pendentes">Notas pendentes na interface.</param>
/// <param name="NaoIntegradas">Notas não integradas.</param>
/// <param name="ArquivosIgnorados">Arquivos não-XML ignorados pela integração (não são notas reais).</param>
/// <param name="Transportes">Quantidade de transportes distintos.</param>
/// <param name="NotasPorTransporte">Notas por transporte (PUXADA), na ordem do relatório.</param>
/// <param name="NotasPuxada">Notas de tipo PUXADA (inclui as da raiz).</param>
/// <param name="NotasTsp">Notas de tipo TSP (pasta PuxadaTSP).</param>
/// <param name="NotasRaiz">Notas que vieram soltas na raiz da entrada (contadas também em PUXADA).</param>
/// <param name="OndasTsp">Quantidade de ondas da TSP (uma por ZIP).</param>
/// <param name="NotasPorOnda">Por onda TSP: notas e transportes distintos, na ordem do relatório.</param>
public sealed record ResumoRelatorio(
    int Total,
    int Integradas,
    int IntegradasRevisarProduto,
    int Pendentes,
    int NaoIntegradas,
    int ArquivosIgnorados,
    int Transportes,
    IReadOnlyList<(string Transporte, int Notas)> NotasPorTransporte,
    int NotasPuxada = 0,
    int NotasTsp = 0,
    int NotasRaiz = 0,
    int OndasTsp = 0,
    IReadOnlyList<(string Onda, int Notas, int Transportes)>? NotasPorOnda = null);

/// <summary>Grupo de linhas de um mesmo transporte (PUXADA) ou de uma mesma onda (TSP).</summary>
/// <param name="Transporte">Número do transporte; no grupo TSP é o nome da onda.</param>
/// <param name="Linhas">Linhas ordenadas por nota.</param>
/// <param name="Tipo">PUXADA ou TSP.</param>
public sealed record GrupoTransporte(string Transporte, IReadOnlyList<LinhaRelatorioErro> Linhas, string Tipo = "PUXADA")
{
    /// <summary>Grupo de Puxada TSP (agrupado por onda).</summary>
    public bool EhTsp => string.Equals(Tipo, "TSP", StringComparison.OrdinalIgnoreCase);

    /// <summary>Nome da onda (só no grupo TSP; vazio na PUXADA).</summary>
    public string Onda => EhTsp ? Transporte : string.Empty;

    /// <summary>Transportes distintos das notas do grupo (ignora nota sem transporte).</summary>
    public int Transportes => Linhas
        .Select(l => l.NumeroTransporte ?? string.Empty)
        .Where(t => !string.IsNullOrWhiteSpace(t) && t != RegistroDiario.RotuloSemTransporte && t != RegistroDiario.RotuloSemTransporteTsp)
        .Distinct(StringComparer.Ordinal)
        .Count();

    /// <summary>Quantidade de notas do grupo.</summary>
    public int Notas => Linhas.Count;

    /// <summary>Notas integradas (inclui "revisar produto").</summary>
    public int Integradas => Linhas.Count(l => l.EstaIntegrada);

    /// <summary>
    /// Notas com problema: pendentes, não integradas ou integradas com produto a revisar.
    /// Arquivos ignorados (não-XML) não são problema de integração e ficam de fora.
    /// </summary>
    public int ComProblema => Linhas.Count(l => !l.ArquivoIgnorado && (!l.EstaIntegrada || l.IntegradaRevisarProduto));
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
    /// <summary>Rótulo do grupo TSP de linhas sem onda (ZIP inválido, arquivo que não é ZIP).</summary>
    public const string SemOnda = "(sem onda)";

    private static string ChaveOnda(LinhaRelatorioErro l) =>
        string.IsNullOrWhiteSpace(l.Onda) ? SemOnda : l.Onda;

    /// <summary>Monta o modelo com ordenação estável por transporte e nota (ordinal).</summary>
    public static ModeloRelatorio Montar(IReadOnlyList<LinhaRelatorioErro> linhas)
    {
        ArgumentNullException.ThrowIfNull(linhas);

        // PUXADA primeiro (por transporte, como sempre), TSP depois (por onda).
        var ordenadas = linhas
            .OrderBy(l => l.EhTsp ? 1 : 0)
            .ThenBy(l => l.EhTsp ? ChaveOnda(l) : l.NumeroTransporte ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(l => l.NumeroNota ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        var gruposPuxada = ordenadas
            .Where(l => !l.EhTsp)
            .GroupBy(l => l.NumeroTransporte ?? string.Empty, StringComparer.Ordinal)
            .Select(g => new GrupoTransporte(g.Key, g.ToList()))
            .ToList();
        var gruposTsp = ordenadas
            .Where(l => l.EhTsp)
            .GroupBy(ChaveOnda, StringComparer.Ordinal)
            .Select(g => new GrupoTransporte(g.Key, g.ToList(), "TSP"))
            .ToList();
        var grupos = gruposPuxada.Concat(gruposTsp).ToList();

        var resumo = new ResumoRelatorio(
            // Total reflete apenas notas reais (nota fiscal); arquivos ignorados têm contador próprio.
            Total: ordenadas.Count(l => !l.ArquivoIgnorado),
            Integradas: ordenadas.Count(l => l.EstaIntegrada),
            IntegradasRevisarProduto: ordenadas.Count(l => l.IntegradaRevisarProduto),
            Pendentes: ordenadas.Count(l => !l.EstaIntegrada && l.StatusEfetivo == StatusLinhaRelatorio.Pendente),
            NaoIntegradas: ordenadas.Count(l => !l.ArquivoIgnorado && !l.EstaIntegrada && l.StatusEfetivo != StatusLinhaRelatorio.Pendente),
            ArquivosIgnorados: ordenadas.Count(l => l.ArquivoIgnorado),
            // Transportes distintos de todas as notas (PUXADA, TSP e raiz), qualquer status.
            Transportes: ordenadas
                .Select(l => l.NumeroTransporte ?? string.Empty)
                .Where(t => !string.IsNullOrWhiteSpace(t) && t != RegistroDiario.RotuloSemTransporte && t != RegistroDiario.RotuloSemTransporteTsp)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            NotasPorTransporte: gruposPuxada.Select(g => (g.Transporte, g.Notas)).ToList(),
            NotasPuxada: ordenadas.Count(l => !l.ArquivoIgnorado && !l.EhTsp),
            NotasTsp: ordenadas.Count(l => !l.ArquivoIgnorado && l.EhTsp),
            NotasRaiz: ordenadas.Count(l => !l.ArquivoIgnorado && l.EhRaiz),
            OndasTsp: gruposTsp.Count(g => g.Transporte != SemOnda),
            NotasPorOnda: gruposTsp.Where(g => g.Transporte != SemOnda).Select(g => (g.Transporte, g.Notas, g.Transportes)).ToList());

        var produtos = ordenadas
            .Where(l => l.RevisaoProdutoPendente)
            .Select(l => new ProdutosRevisao(l.NumeroNota, l.NumeroTransporte, l.ProdutosProvisorios!))
            .ToList();

        return new ModeloRelatorio(resumo, grupos, produtos);
    }
}
