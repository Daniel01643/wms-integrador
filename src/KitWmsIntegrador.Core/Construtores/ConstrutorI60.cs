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
    private readonly OpcoesI60 _i60;

    /// <param name="operacional">CNPJs/depósito operacionais.</param>
    /// <param name="i60">
    /// Opções da I60 (tipo de operação). Opcional: sem ela vale o default de
    /// <see cref="OpcoesI60"/>.
    /// </param>
    public ConstrutorI60(IOptions<OpcoesOperacionais> operacional, IOptions<OpcoesI60>? i60 = null)
    {
        _operacional = operacional.Value;
        _i60 = i60?.Value ?? new OpcoesI60();
    }

    /// <inheritdoc />
    public PayloadI60 Construir(NotaFiscal nota) => ConstruirCore(nota, null);

    /// <inheritdoc />
    public PayloadI60 Construir(NotaFiscal nota, ParametrosI60 parametros)
    {
        ArgumentNullException.ThrowIfNull(parametros);
        return ConstruirCore(nota, parametros);
    }

    private PayloadI60 ConstruirCore(NotaFiscal nota, ParametrosI60? parametros)
    {
        ArgumentNullException.ThrowIfNull(nota);

        var cfopPrimeiroItem = nota.Itens.Count > 0 ? nota.Itens[0].Cfop : string.Empty;

        // Transportadora do XML; se ausente, usa o CNPJ do armazém (config), conforme template.
        var cnpjTransportadora = string.IsNullOrWhiteSpace(nota.TransportadoraCnpj)
            ? _operacional.CnpjArmazem
            : nota.TransportadoraCnpj!;

        var volumes = nota.Volumes;
        // qtdeVolume = soma das quantidades em caixas dos itens (a mesma quantidade que a I50
        // envia por item e que o código customizado do WMS soma em QTD_VOLUMES do DocCarga).
        // NÃO usa transp/vol/qVol do XML: assim I50 -> PL/SQL e I60 batem por construção.
        // Diagnóstico (I60:QtdeVolumeZero=true): envia 0 para testar a conferência do WMS.

        var itens = new List<ItemNotaVendaI60>(nota.Itens.Count);
        for (var i = 0; i < nota.Itens.Count; i++)
        {
            var item = nota.Itens[i];
            var quantidade = item.Quantidade;
            if (parametros?.Quantidades is { } ajustadas)
            {
                // Quantidade separada (I51) por sequência; item sem entrada é omitido (I51:ItemSemRetorno=Omitir).
                var achada = ajustadas.FirstOrDefault(q => q.Sequencia == i + 1);
                if (achada is null)
                {
                    continue;
                }

                quantidade = achada.Quantidade;
            }

            itens.Add(new ItemNotaVendaI60
            {
                NumeroSequencialItem = i + 1,
                CodProduto = item.CodigoProduto,
                QtdeProduto = quantidade,
                QtdeProdutoConfirmada = quantidade,
                // A CONFIRMAR: sem fator de conversão, qtdeEmbalagem replica qCom.
                QtdeEmbalagem = quantidade,
                // Valor unitário = vUnCom do XML (confirmado).
                ValorOriginalItem = item.ValorUnitario,
                ValorAtualItem = item.ValorUnitario,
                NumeroLote = item.Lote ?? string.Empty,
                DataValidade = FormatoWms.Data(item.Validade) ?? string.Empty,
            });
        }

        var qtdeTotal = itens.Sum(i => i.QtdeProduto);
        var qtdeVolume = _i60.QtdeVolumeZero
            ? 0m
            : parametros?.QtdeVolume ?? (parametros?.Quantidades is null
                ? CalcularQtdeVolume(nota)
                : Math.Round(qtdeTotal, 0, MidpointRounding.AwayFromZero));

        var tipoOperacao = !string.IsNullOrWhiteSpace(parametros?.TipoOperacaoNota)
            ? parametros!.TipoOperacaoNota!.Trim()
            : string.IsNullOrWhiteSpace(_i60.TipoOperacaoNota)
                ? OpcoesI60.TipoOperacaoNotaPadrao
                : _i60.TipoOperacaoNota.Trim();

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
            QtdeTotalNota = _i60.QtdeTotalNotaZero ? 0m : qtdeTotal,
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
            // Tipo de operação: configurável (I60:TipoOperacaoNota), sem release.
            Tpsit = tipoOperacao,
            TipoOperacaoNota = tipoOperacao,
            Tpacao = string.IsNullOrWhiteSpace(parametros?.Tpacao) ? "I" : parametros!.Tpacao!.Trim(),
            // Campos fixos (tipoNota="AR", formaTransporte="PR", tpacao="I", Tpnota="NV")
            // vêm dos defaults do DTO.
        };

        return new PayloadI60 { NotasVenda = new[] { notaVenda } };
    }

    /// <inheritdoc />
    public string ConstruirJson(NotaFiscal nota) =>
        JsonSerializer.Serialize(Construir(nota), JsonWms.Opcoes);

    /// <summary>
    /// Regra do <c>qtdeVolume</c> (inteiro no WMS: 12 posições, 0 decimais): soma das
    /// quantidades dos itens (caixas). Se a soma tiver fração (incomum, pois a nota está em
    /// caixas), arredonda ao inteiro mais próximo, meio para longe de zero
    /// (<see cref="MidpointRounding.AwayFromZero"/>). Atenção: o PL/SQL grava a soma sem
    /// arredondar; com fração a I60 pode divergir do DocCarga.
    /// </summary>
    internal static decimal CalcularQtdeVolume(NotaFiscal nota) =>
        Math.Round(nota.Itens.Sum(i => i.Quantidade), 0, MidpointRounding.AwayFromZero);
}
