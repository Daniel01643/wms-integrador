using KitWmsIntegrador.Core.Construtores.Dtos;
using KitWmsIntegrador.Core.Modelos;

namespace KitWmsIntegrador.Core.Construtores;

/// <summary>
/// Monta o payload da I60 (NOTAS_VENDA) a partir de uma <see cref="NotaFiscal"/> já
/// parseada. 1 I60 por nota, associando a nota ao DocCarga da I50 via <c>pedidos[]</c>.
///
/// NÃO faz chamadas HTTP — apenas constrói o objeto/JSON.
/// </summary>
public interface IConstrutorI60
{
    /// <summary>Constrói o DTO tipado da I60 para a nota.</summary>
    PayloadI60 Construir(NotaFiscal nota);

    /// <summary>Constrói o DTO e serializa para o JSON exato esperado pela API.</summary>
    string ConstruirJson(NotaFiscal nota);
}
