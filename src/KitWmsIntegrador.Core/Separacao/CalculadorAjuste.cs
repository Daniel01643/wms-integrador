namespace KitWmsIntegrador.Core.Separacao;

/// <summary>Resultado do ajuste: itens original × separada e soma separada.</summary>
public sealed record ResultadoAjuste(IReadOnlyList<ItemSeparacao> Itens, decimal SomaSeparada)
{
    /// <summary>Soma separada igual a zero: a I60 não deve ser enviada.</summary>
    public bool SemQuantidadeSeparada => SomaSeparada <= 0m;
}

/// <summary>
/// Ajusta as quantidades da nota às separadas devolvidas pela I51: casa por
/// <c>numeroSequencialItem</c>; se o <c>codProduto</c> divergir, cai para o primeiro item ainda não
/// usado com o mesmo código. Item sem retorno é zerado ou omitido (<c>I51:ItemSemRetorno</c>).
/// </summary>
public static class CalculadorAjuste
{
    /// <summary>Calcula o ajuste de uma nota.</summary>
    public static ResultadoAjuste Calcular(IReadOnlyList<ItemSeparacao> itensNota, IEnumerable<ItemI51> itensWms, bool omitirSemRetorno)
    {
        ArgumentNullException.ThrowIfNull(itensNota);
        var consolidados = Consolidar(itensWms);
        var usados = new HashSet<int>();
        var resultado = new List<ItemSeparacao>(itensNota.Count);

        foreach (var item in itensNota)
        {
            var idx = Achar(consolidados, usados, item);
            if (idx >= 0)
            {
                usados.Add(idx);
                var sep = consolidados[idx].QtdeProdutoSep;
                resultado.Add(item with { QtdeSeparada = sep, Diferenca = sep - item.QtdeOriginal, SemRetorno = false });
            }
            else
            {
                resultado.Add(item with
                {
                    QtdeSeparada = omitirSemRetorno ? null : 0m,
                    Diferenca = -item.QtdeOriginal,
                    SemRetorno = true,
                });
            }
        }

        return new ResultadoAjuste(resultado, resultado.Sum(i => i.QtdeSeparada ?? 0m));
    }

    // Documentos repetidos para a mesma nota: soma por (sequência, produto).
    private static List<ItemI51> Consolidar(IEnumerable<ItemI51> itens) =>
        itens.GroupBy(i => (i.Sequencia, Cod: i.CodProduto.Trim().ToUpperInvariant()))
            .Select(g => new ItemI51(g.Key.Sequencia, g.First().CodProduto, g.Sum(x => x.QtdeProduto), g.Sum(x => x.QtdeProdutoSep)))
            .ToList();

    private static int Achar(List<ItemI51> wms, HashSet<int> usados, ItemSeparacao item)
    {
        for (var i = 0; i < wms.Count; i++)
        {
            if (!usados.Contains(i) && wms[i].Sequencia == item.Sequencia && MesmoProduto(wms[i], item))
            {
                return i;
            }
        }

        for (var i = 0; i < wms.Count; i++)
        {
            if (!usados.Contains(i) && MesmoProduto(wms[i], item))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool MesmoProduto(ItemI51 w, ItemSeparacao n) =>
        string.Equals(w.CodProduto.Trim(), n.CodProduto.Trim(), StringComparison.OrdinalIgnoreCase);
}
