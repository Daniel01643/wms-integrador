using System.Text.Json;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores.Dtos;
using KitWmsIntegrador.Core.Modelos;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Construtores;

/// <summary>
/// Implementação do <see cref="IConstrutorI60"/>. Monta 1 NOTAS_VENDA por nota, com a
/// amarração ao DocCarga da I50 em <c>pedidos[]</c> (mesmo <c>numeroPedido</c> derivado
/// do nNF). CNPJs operacionais/depósito vêm da config; valores fixos seguem o template.
/// </summary>
public sealed class ConstrutorI60 : IConstrutorI60
{
    private readonly OpcoesOperacionais _operacional;

    public ConstrutorI60(IOptions<OpcoesOperacionais> operacional)
    {
        _operacional = operacional.Value;
    }

    /// <inheritdoc />
    public PayloadI60 Construir(NotaFiscal nota)
    {
        ArgumentNullException.ThrowIfNull(nota);

        var cfopPrimeiroItem = nota.Itens.Count > 0 ? nota.Itens[0].Cfop : string.Empty;

        // Transportadora do XML; se ausente, usa o CNPJ do armazém (config), conforme template.
        var cnpjTransportadora = string.IsNullOrWhiteSpace(nota.TransportadoraCnpj)
            ? _operacional.CnpjArmazem
            : nota.TransportadoraCnpj!;

        var volumes = nota.Volumes;
        var qtdeVolume = ParseDecimalOuZero(volumes?.QuantidadeVolumes);
        var qtdeTotal = nota.Itens.Sum(i => i.Quantidade);

        var itens = new List<ItemNotaVendaI60>(nota.Itens.Count);
        for (var i = 0; i < nota.Itens.Count; i++)
        {
            var item = nota.Itens[i];
            itens.Add(new ItemNotaVendaI60
            {
                NumeroSequencialItem = i + 1,
                CodProduto = item.CodigoProduto,
                QtdeProduto = item.Quantidade,
                QtdeProdutoConfirmada = item.Quantidade,
                // A CONFIRMAR: sem fator de conversão, qtdeEmbalagem replica qCom.
                QtdeEmbalagem = item.Quantidade,
                // Valor unitário = vUnCom do XML (confirmado).
                ValorOriginalItem = item.ValorUnitario,
                ValorAtualItem = item.ValorUnitario,
                NumeroLote = item.Lote ?? string.Empty,
                DataValidade = FormatoWms.Data(item.Validade) ?? string.Empty,
            });
        }

        var notaVenda = new NotaVendaI60
        {
            CnpjEmitente = FormatoWms.Documento(nota.EmitenteCnpj),
            CnpjRemetente = FormatoWms.Documento(nota.EmitenteCnpj),
            CnpjDestinatario = FormatoWms.Documento(nota.DestinatarioCnpj),
            CnpjLocalEntrega = FormatoWms.Documento(nota.DestinatarioCnpj),
            CnpjTransportadora = FormatoWms.Documento(cnpjTransportadora),
            CodNatOperacaoFiscal = cfopPrimeiroItem,
            NumeroNota = nota.Numero,
            SerieNota = nota.Serie,
            Chavenfe = nota.ChaveNFe,
            Protocolo = nota.Protocolo ?? string.Empty,
            CnpjNota = FormatoWms.Documento(nota.EmitenteCnpj),
            DataEmissao = FormatoWms.Data(nota.DataEmissao),
            QtdeTotalNota = qtdeTotal,
            PesoBrutoKg = volumes?.PesoBruto ?? 0m,
            PesoLiquidoKg = volumes?.PesoLiquido ?? 0m,
            QtdeVolume = qtdeVolume,
            ValorTotal = nota.ValorTotal ?? 0m,

            // CONFIG operacional (não hardcoded), com máscara.
            CnpjProprietariaProduto = FormatoWms.Documento(_operacional.CnpjDepositante),
            Deposito = _operacional.Deposito,
            CnpjArmazem = FormatoWms.Documento(_operacional.CnpjArmazem),

            // Amarração ao DocCarga da I50: mesmo numeroPedido (= nNF). A CONFIRMAR (nNF vs xPed).
            Pedidos = new[] { nota.Numero },

            ItemNotaVenda = itens,
            // Campos fixos (tipoNota="AR", formaTransporte="PR", tpacao="I",
            // Tpsit="INE", Tpnota="NV", tipoOperacaoNota="INE") vêm dos defaults do DTO.
        };

        return new PayloadI60 { NotasVenda = new[] { notaVenda } };
    }

    /// <inheritdoc />
    public string ConstruirJson(NotaFiscal nota) =>
        JsonSerializer.Serialize(Construir(nota), JsonWms.Opcoes);

    private static decimal ParseDecimalOuZero(string? raw) =>
        decimal.TryParse(
            raw,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0m;
}
