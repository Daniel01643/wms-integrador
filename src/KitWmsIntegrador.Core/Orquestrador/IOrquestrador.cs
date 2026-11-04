using KitWmsIntegrador.Core.Agrupamento;
using KitWmsIntegrador.Core.Modelos;

namespace KitWmsIntegrador.Core.Orquestrador;

/// <summary>
/// Orquestrador reativo (orientado pelo erro) do fluxo de integração de notas ao WMS.
///
/// Para cada nota envia a I50 e, conforme o texto do retorno, dispara as pré-condições
/// (I10 para empresa não localizada, I30 para produto não cadastrado) e reenvia a I50,
/// com limite de 3 tentativas por nota. A I60 só é enviada após sucesso da I50 quando
/// <c>OpcoesI60.Habilitada</c> for verdadeiro (hoje desligada).
///
/// O objetivo é agilidade: pendência e não-integrada são resultados aceitáveis e bem
/// registrados; o orquestrador NUNCA trava o lote nem entra em loop.
/// </summary>
public interface IOrquestrador
{
    /// <summary>
    /// Integra todas as notas de uma <see cref="Onda"/>, usando o
    /// <see cref="Onda.NumeroTransporte"/> como agrupador (nroagrupseparacao).
    /// Devolve um <see cref="ResultadoIntegracaoNota"/> por nota.
    /// </summary>
    Task<IReadOnlyList<ResultadoIntegracaoNota>> IntegrarAsync(
        Onda onda, CancellationToken cancellationToken = default);

    /// <summary>
    /// Integra uma sequência de notas associadas a um mesmo
    /// <paramref name="numeroTransporte"/>. Devolve um
    /// <see cref="ResultadoIntegracaoNota"/> por nota.
    /// </summary>
    Task<IReadOnlyList<ResultadoIntegracaoNota>> IntegrarAsync(
        IEnumerable<NotaFiscal> notas, string numeroTransporte,
        CancellationToken cancellationToken = default);
}
