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
    /// Constrói o DTO tipado da I40 para a nota.
    /// <paramref name="enviarPlaca"/> = false: <c>tipoOperacaoNota</c> = <c>Devolucao:TipoOperacaoNota</c>
    /// (IMP), placa e motorista em branco, nada é exigido.
    /// <paramref name="enviarPlaca"/> = true: <c>tipoOperacaoNota</c> = <c>Devolucao:TipoOperacaoNotaComPlaca</c>
    /// (ICD), exige <paramref name="placa"/> (<see cref="ArgumentException"/>) e
    /// <c>Devolucao:NomeMotorista</c> (<see cref="InvalidOperationException"/>).
    /// </summary>
    PayloadI40 Construir(NotaFiscal nota, bool enviarPlaca, string? placa = null);

    /// <summary>Constrói o DTO e serializa para o JSON exato esperado pela API.</summary>
    string ConstruirJson(NotaFiscal nota, bool enviarPlaca, string? placa = null);
}
