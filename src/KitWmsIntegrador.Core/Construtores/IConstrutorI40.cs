using KitWmsIntegrador.Core.Construtores.Dtos;
using KitWmsIntegrador.Core.Modelos;

namespace KitWmsIntegrador.Core.Construtores;

/// <summary>
/// Monta o payload da I40 (NOTA_FISCAL, devolução) a partir de uma <see cref="NotaFiscal"/>
/// já parseada. NÃO faz chamadas HTTP — apenas constrói o objeto/JSON.
/// </summary>
public interface IConstrutorI40
{
    /// <summary>
    /// Constrói o DTO tipado da I40 para a nota. Exige <paramref name="placa"/> não vazia e
    /// <c>Devolucao:NomeMotorista</c> configurado (<see cref="InvalidOperationException"/>
    /// / <see cref="ArgumentException"/> com mensagem clara).
    /// </summary>
    PayloadI40 Construir(NotaFiscal nota, string placa);

    /// <summary>Constrói o DTO e serializa para o JSON exato esperado pela API.</summary>
    string ConstruirJson(NotaFiscal nota, string placa);
}
