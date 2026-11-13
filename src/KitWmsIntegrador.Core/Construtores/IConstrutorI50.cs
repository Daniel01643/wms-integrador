using KitWmsIntegrador.Core.Construtores.Dtos;
using KitWmsIntegrador.Core.Modelos;

namespace KitWmsIntegrador.Core.Construtores;

/// <summary>
/// Monta o payload da I50 (PEDIDO/DocCarga) a partir de uma <see cref="NotaFiscal"/>
/// já parseada. 1 nota = 1 DocCarga = 1 I50; o <c>nroagrupseparacao</c> recebe o número
/// do transporte (agrupador da onda).
///
/// NÃO faz chamadas HTTP — apenas constrói o objeto/JSON. O envio é do cliente WMS.
/// </summary>
public interface IConstrutorI50
{
    /// <summary>
    /// Constrói o DTO tipado da I50 para a nota. O <paramref name="numeroTransporte"/>
    /// vira o <c>nroagrupseparacao</c> (default: <see cref="NotaFiscal.NumeroTransporte"/>).
    /// Lança <see cref="ArgumentException"/> se o transporte for vazio — o caso B
    /// (nota sem transporte) é tratado pelo Orquestrador (Etapa 5), não aqui.
    /// Com <paramref name="ehTsp"/> (Puxada TSP) a placa do veículo é governada por
    /// <c>I50:EnviarPlacaVeiculoTsp</c> (padrão false); sem ele, por <c>I50:EnviarPlacaVeiculo</c>.
    /// </summary>
    PayloadI50 Construir(NotaFiscal nota, string? numeroTransporte = null, bool ehTsp = false);

    /// <summary>Constrói o DTO e serializa para o JSON exato esperado pela API.</summary>
    string ConstruirJson(NotaFiscal nota, string? numeroTransporte = null, bool ehTsp = false);
}
