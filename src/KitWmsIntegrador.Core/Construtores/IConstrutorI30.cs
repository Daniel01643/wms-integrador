using KitWmsIntegrador.Core.Construtores.Dtos;
using KitWmsIntegrador.Core.Modelos;

namespace KitWmsIntegrador.Core.Construtores;

/// <summary>
/// Monta o payload da I30 (PRODUTO) a partir de uma <see cref="NotaFiscal"/> já parseada.
/// Gera UM payload com TODOS os itens da nota (o WMS reporta um produto por vez; enviamos
/// todos de uma vez — os que já existirem retornam "já existe" = OK).
///
/// O produto gerado é PROVISÓRIO/genérico (defaults do template) e entra em revisão do
/// colaborador; o objetivo é só desbloquear a nota. NÃO faz chamadas HTTP — apenas constrói
/// o objeto/JSON.
/// </summary>
public interface IConstrutorI30
{
    /// <summary>Constrói o DTO tipado da I30 com um <see cref="ProdutoI30"/> por item da nota.</summary>
    PayloadI30 Construir(NotaFiscal nota);

    /// <summary>Constrói o DTO e serializa para o JSON exato esperado pela API.</summary>
    string ConstruirJson(NotaFiscal nota);
}
