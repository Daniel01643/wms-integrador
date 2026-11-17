using KitWmsIntegrador.Core.Configuracao;

namespace KitWmsIntegrador.Core.Separacao;

/// <summary>Quantidade separada escolhida para um item e se ela veio do último recurso (<c>qtdeProdutoSep</c>).</summary>
/// <param name="Quantidade">Quantidade na unidade da NF-e/I50 (embalagens), ou em unidades no modo <c>Produto</c>.</param>
/// <param name="FallbackProduto">Verdadeiro se faltaram <c>qtdeEmbalagemSep</c> e <c>detalhes[].qtdeCaixaSep</c>.</param>
public readonly record struct QuantidadeSeparadaI51(decimal Quantidade, bool FallbackProduto);

/// <summary>
/// Escolhe a quantidade separada de um item da I51 conforme <c>I51:CampoQuantidadeSeparada</c>.
/// <c>Embalagem</c>: <c>qtdeEmbalagemSep</c>; se ausente/vazia/inválida, a soma de <c>detalhes[].qtdeCaixaSep</c>;
/// se também ausente, <c>qtdeProdutoSep</c> (marcado como fallback). <c>Produto</c>: sempre <c>qtdeProdutoSep</c>.
/// </summary>
public static class SelecionadorQuantidadeI51
{
    /// <summary>Escolhe a quantidade do item.</summary>
    public static QuantidadeSeparadaI51 Escolher(ItemI51 item, OpcoesI51 opcoes)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(opcoes);
        if (opcoes.QuantidadeSeparadaEmProduto)
        {
            return new QuantidadeSeparadaI51(item.QtdeProdutoSep, false);
        }

        if (item.QtdeEmbalagemSep is { } emb)
        {
            return new QuantidadeSeparadaI51(emb, false);
        }

        if (item.QtdeCaixaDetalhes is { } caixa)
        {
            return new QuantidadeSeparadaI51(caixa, false);
        }

        return new QuantidadeSeparadaI51(item.QtdeProdutoSep, true);
    }
}
