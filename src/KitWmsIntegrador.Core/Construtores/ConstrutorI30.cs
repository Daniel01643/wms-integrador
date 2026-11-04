using System.Text.Json;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores.Dtos;
using KitWmsIntegrador.Core.Modelos;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Construtores;

/// <summary>
/// Implementação do <see cref="IConstrutorI30"/>. Mapeia cada item da nota em um
/// <see cref="ProdutoI30"/> (um por item), com <c>codigo</c> SEM zeros à esquerda e
/// <c>codcliente</c> COM zeros. <c>codempresa</c>/CNPJs/depósito vêm da config operacional;
/// valores fixos (codclassificacao="2", codtipologia="SEC", etc.) seguem o template e os
/// achados ao vivo. O produto é provisório e entra em revisão do colaborador.
/// </summary>
public sealed class ConstrutorI30 : IConstrutorI30
{
    private readonly OpcoesOperacionais _operacional;

    public ConstrutorI30(IOptions<OpcoesOperacionais> operacional)
    {
        _operacional = operacional.Value;
    }

    /// <inheritdoc />
    public PayloadI30 Construir(NotaFiscal nota)
    {
        ArgumentNullException.ThrowIfNull(nota);

        var cnpjArmazem = FormatoWms.Documento(_operacional.CnpjArmazem);
        var cnpjProprietaria = FormatoWms.Documento(_operacional.CnpjDepositante);

        var produtos = new List<ProdutoI30>(nota.Itens.Count);
        foreach (var item in nota.Itens)
        {
            produtos.Add(new ProdutoI30
            {
                // codigo sem zeros à esquerda; codcliente preserva o código original.
                Codigo = item.CodigoProduto.TrimStart('0'),
                Codcliente = item.CodigoProduto,
                Descricao = item.Descricao,
                Aliasproduto = item.Descricao,
                Codempresa = _operacional.CodigoDepositante,
                Codunidade = item.Unidade,
                Eancliente = item.Ean,
                Classificacaoncm = item.Ncm,

                // CONFIG operacional (não hardcoded), com máscara nos CNPJs.
                CnpjArmazem = cnpjArmazem,
                CnpjProprietaria = cnpjProprietaria,
                Deposito = _operacional.Deposito,

                // Demais campos (codtpprod1="2", codclassificacao="2", codtipologia="SEC",
                // tpacao="I", codigotributario="040", etc.) vêm dos defaults do DTO.
            });
        }

        return new PayloadI30 { Produto = produtos };
    }

    /// <inheritdoc />
    public string ConstruirJson(NotaFiscal nota) =>
        JsonSerializer.Serialize(Construir(nota), JsonWms.Opcoes);
}
