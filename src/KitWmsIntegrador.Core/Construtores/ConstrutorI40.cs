using System.Text.Json;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores.Dtos;
using KitWmsIntegrador.Core.Modelos;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Construtores;

/// <summary>
/// Implementação do <see cref="IConstrutorI40"/> (devolução, Plano B). Reaproveita o
/// mapeamento do <see cref="ConstrutorI60"/>: CNPJs operacionais da config, transportadora
/// com fallback no armazém, qtdeVolume inteiro = soma das caixas, qtdeEmbalagem = caixas.
/// A nota original é a da VENDA; com <see cref="OpcoesDevolucao.InverterPartes"/> as partes
/// são trocadas (cliente remete, nós recebemos).
/// </summary>
public sealed class ConstrutorI40 : IConstrutorI40
{
    /// <summary>Limite de caracteres dos atributos da I40.</summary>
    public const int LimiteAtributo = 200;

    private readonly OpcoesOperacionais _operacional;
    private readonly OpcoesDevolucao _devolucao;

    public ConstrutorI40(IOptions<OpcoesOperacionais> operacional, IOptions<OpcoesDevolucao>? devolucao = null)
    {
        _operacional = operacional.Value;
        _devolucao = devolucao?.Value ?? new OpcoesDevolucao();
    }

    /// <inheritdoc />
    public PayloadI40 Construir(NotaFiscal nota)
    {
        ArgumentNullException.ThrowIfNull(nota);

        var tipoOperacao = ValorOuPadrao(_devolucao.TipoOperacaoNota, OpcoesDevolucao.TipoOperacaoNotaPadrao);

        var cfopPrimeiroItem = nota.Itens.Count > 0 ? nota.Itens[0].Cfop : string.Empty;

        var cnpjTransportadora = string.IsNullOrWhiteSpace(nota.TransportadoraCnpj)
            ? _operacional.CnpjArmazem
            : nota.TransportadoraCnpj!;

        var volumes = nota.Volumes;
        var qtdeTotal = nota.Itens.Sum(i => i.Quantidade);
        var qtdeVolume = (long)ConstrutorI60.CalcularQtdeVolume(nota);

        var itens = new List<ItemNotaFiscalI40>(nota.Itens.Count);
        for (var i = 0; i < nota.Itens.Count; i++)
        {
            var item = nota.Itens[i];
            itens.Add(new ItemNotaFiscalI40
            {
                NumeroSequencialItem = i + 1,
                CodProduto = item.CodigoProduto,
                QtdeProduto = item.Quantidade,
                QtdeProdutoConfirmada = item.Quantidade,
                QtdeEmbalagem = item.Quantidade,
                ValorOriginalItem = item.ValorUnitario,
                ValorAtualItem = item.ValorUnitario,
                CodSituacaoTributaria = ValorOuPadrao(_devolucao.CodSituacaoTributaria, OpcoesDevolucao.CodSituacaoTributariaPadrao),
                NumeroLote = item.Lote ?? string.Empty,
                DataValidade = FormatoWms.Data(item.Validade) ?? string.Empty,
                Ean = item.Ean,
                Descricao = item.Descricao,
                Ncm = item.Ncm,
                CodUnidade = item.Unidade,
            });
        }

        // Partes: padrão = como na venda (emitente = nós, destinatário = cliente).
        // InverterPartes: emitente/remetente = cliente; destinatário/local de entrega = nós.
        // cnpjNota segue o emitente do envio.
        var emitente = _devolucao.InverterPartes ? nota.DestinatarioCnpj : nota.EmitenteCnpj;
        var destinatario = _devolucao.InverterPartes ? nota.EmitenteCnpj : nota.DestinatarioCnpj;

        var atributo1 = FormatoWms.Truncar(
            $"CH:{nota.ChaveNFe} PROT:{nota.Protocolo}", LimiteAtributo);

        var notaI40 = new NotaFiscalI40
        {
            CnpjEmitente = FormatoWms.Documento(emitente),
            CnpjRemetente = FormatoWms.Documento(emitente),
            CnpjDestinatario = FormatoWms.Documento(destinatario),
            CnpjLocalEntrega = FormatoWms.Documento(destinatario),
            CnpjTransportadora = FormatoWms.Documento(cnpjTransportadora),
            CodNatOperacaoFiscal = cfopPrimeiroItem,
            NumeroNota = nota.Numero,
            SerieNota = nota.Serie,
            Chavenfe = nota.ChaveNFe,
            Protocolo = nota.Protocolo ?? string.Empty,
            CnpjNota = FormatoWms.Documento(emitente),
            TipoNota = ValorOuPadrao(_devolucao.TipoNota, OpcoesDevolucao.TipoNotaPadrao),
            DataEmissao = FormatoWms.Data(nota.DataEmissao),
            QtdeTotalNota = qtdeTotal,
            PesoBrutoKg = volumes?.PesoBruto ?? 0m,
            PesoLiquidoKg = volumes?.PesoLiquido ?? 0m,
            QtdeVolume = qtdeVolume,
            ValorTotal = nota.ValorTotal ?? 0m,
            FormaTransporte = ValorOuPadrao(_devolucao.FormaTransporte, OpcoesDevolucao.FormaTransportePadrao),
            CnpjProprietariaProduto = FormatoWms.Documento(_operacional.CnpjDepositante),
            Deposito = _operacional.Deposito,
            CnpjArmazem = FormatoWms.Documento(_operacional.CnpjArmazem),
            TipoOperacaoNota = tipoOperacao,
            Atributo1 = atributo1,
            ItemNotaFiscal = itens,
        };

        return new PayloadI40 { NotaFiscal = new[] { notaI40 } };
    }

    /// <inheritdoc />
    public string ConstruirJson(NotaFiscal nota) =>
        JsonSerializer.Serialize(Construir(nota), JsonWms.Opcoes);

    private static string ValorOuPadrao(string? valor, string padrao) =>
        string.IsNullOrWhiteSpace(valor) ? padrao : valor.Trim();
}
