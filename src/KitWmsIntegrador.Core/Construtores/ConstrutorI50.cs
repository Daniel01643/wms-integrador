using System.Text.Json;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores.Dtos;
using KitWmsIntegrador.Core.Modelos;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Construtores;

/// <summary>
/// Implementação do <see cref="IConstrutorI50"/>. Mapeia 1 <see cref="NotaFiscal"/> para
/// 1 DocCarga (PEDIDO), aplicando a config operacional (CNPJs/depósito) e a config da
/// I50 (tipo de carga/separação/acondicionamento/qtdMandante) ao cabeçalho e aos itens.
/// Nenhum valor de negócio é hardcoded aqui — tudo vem das opções.
/// </summary>
public sealed class ConstrutorI50 : IConstrutorI50
{
    private readonly OpcoesOperacionais _operacional;
    private readonly OpcoesI50 _i50;

    public ConstrutorI50(IOptions<OpcoesOperacionais> operacional, IOptions<OpcoesI50> i50)
    {
        _operacional = operacional.Value;
        _i50 = i50.Value;
    }

    /// <inheritdoc />
    public PayloadI50 Construir(NotaFiscal nota, string? numeroTransporte = null)
    {
        ArgumentNullException.ThrowIfNull(nota);

        var transporte = numeroTransporte ?? nota.NumeroTransporte;
        if (string.IsNullOrWhiteSpace(transporte))
        {
            // Caso B: nota sem transporte → vai para o relatório de erros pelo
            // Orquestrador (Etapa 5). Aqui apenas sinalizamos a pré-condição violada.
            throw new ArgumentException(
                "Número do transporte (nroagrupseparacao) é obrigatório para montar a I50.",
                nameof(numeroTransporte));
        }

        // Data de emissão formatada; previsão = emissão + 2 dias (confirmado BRF).
        var emissao = FormatoWms.Data(nota.DataEmissao);
        var previsao = nota.DataEmissao.HasValue
            ? FormatoWms.Data(nota.DataEmissao.Value.AddDays(2))
            : emissao;

        var endereco = nota.Endereco;
        var local = MontarLocalEntrega(endereco);

        var itens = new List<ItemPedidoI50>(nota.Itens.Count);
        for (var i = 0; i < nota.Itens.Count; i++)
        {
            var item = nota.Itens[i];
            itens.Add(new ItemPedidoI50
            {
                NumeroSequencialItem = i + 1,
                CodigoProduto = item.CodigoProduto,
                // A CONFIRMAR: sem fator de conversão no XML, qtdeEmbalagem replica qCom.
                QtdeEmbalagem = item.Quantidade,
                QuantidadeProduto = item.Quantidade,
                // CONFIG: qtdMandante (A CONFIRMAR U vs E) e separação (mesma do cabeçalho).
                QtdMandante = _i50.QtdMandante,
                TipoSeparacao = _i50.TipoSeparacao,
                TipoSeparacao2 = _i50.TipoSeparacao2,
            });
        }

        var pedido = new PedidoI50
        {
            // CONFIG operacional (não hardcoded). CNPJ do proprietário já vem mascarado
            // da config; o do destinatário vem cru do XML → aplicar máscara.
            CnpjProprietario = FormatoWms.Documento(_operacional.CnpjDepositante),
            CnpjDestinatario = FormatoWms.Documento(nota.DestinatarioCnpj),
            Deposito = _operacional.Deposito,
            CnpjArmazem = FormatoWms.Documento(_operacional.CnpjArmazem),

            // Amarração I50↔I60: o numeroPedido da I50 identifica o DocCarga; a I60
            // repete esse valor em pedidos[]. (Confirmado: usa o nNF.)
            NumeroPedido = nota.Numero,

            // CONFIG da I50.
            TipoCarga = _i50.TipoCarga,
            TipoAcondicionamento = _i50.TipoAcondicionamento, // Confirmado BRF: PC (template)
            TipoSeparacao = _i50.TipoSeparacao,
            TipoSeparacao2 = _i50.TipoSeparacao2,

            // Agrupador da onda = número do transporte (string, zeros à esquerda).
            Nroagrupseparacao = transporte,
            // Confirmado BRF: numeroPlanejamento repete o número do transporte.
            NumeroPlanejamento = transporte,

            DataEmissao = emissao,
            Dataprev = previsao,         // Confirmado BRF: emissão + 2 dias.
            DhprevEmbarque = previsao,   // Confirmado BRF: emissão + 2 dias.
            // NumeroEmbarque omitido (null) nesta etapa — onda nasce do nroagrupseparacao.

            IndicaLocalEntregaNome = local.Nome,
            IndicaLocalEntregaBairro = local.Bairro,
            IndicaLocalEntregaCidade = local.Cidade,
            IndicaLocalEntregaCep = FormatoWms.Cep(local.Cep),
            IndicaLocalEntregaUf = local.Uf,

            ItemPedido = itens,
        };

        return new PayloadI50 { Pedido = new[] { pedido } };
    }

    /// <inheritdoc />
    public string ConstruirJson(NotaFiscal nota, string? numeroTransporte = null) =>
        JsonSerializer.Serialize(Construir(nota, numeroTransporte), JsonWms.Opcoes);

    private static (string Nome, string Bairro, string Cidade, string Cep, string Uf) MontarLocalEntrega(
        EnderecoDestinatario? endereco)
    {
        if (endereco is null)
        {
            return (string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }

        // Logradouro + número no mesmo campo (template: "R JOAO FERNANDES,L01").
        var nome = string.Join(
            ",",
            new[] { endereco.Logradouro, endereco.Numero }
                .Where(p => !string.IsNullOrWhiteSpace(p)));

        return (
            nome,
            endereco.Bairro ?? string.Empty,
            endereco.Municipio ?? string.Empty,
            endereco.Cep ?? string.Empty,
            endereco.Uf ?? string.Empty);
    }
}
