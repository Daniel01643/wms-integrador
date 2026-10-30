using KitWmsIntegrador.Core.Construtores.Dtos;
using KitWmsIntegrador.Core.Modelos;

namespace KitWmsIntegrador.Core.Construtores;

/// <summary>
/// Monta o payload da I10 (EMPRESA) a partir do destinatário de uma
/// <see cref="NotaFiscal"/> já parseada. Usado apenas no caso D (empresa não localizada
/// no WMS) — o gatilho é do Orquestrador (Etapa 5); aqui só a montagem.
///
/// NÃO faz chamadas HTTP — apenas constrói o objeto/JSON.
/// </summary>
public interface IConstrutorI10
{
    /// <summary>Constrói o DTO tipado da I10 a partir do destinatário da nota.</summary>
    PayloadI10 Construir(NotaFiscal nota);

    /// <summary>Constrói o DTO e serializa para o JSON exato esperado pela API.</summary>
    string ConstruirJson(NotaFiscal nota);
}
