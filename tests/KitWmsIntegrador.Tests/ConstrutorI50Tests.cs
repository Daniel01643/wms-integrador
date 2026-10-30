using System.Text.Json;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Core.Modelos;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class ConstrutorI50Tests
{
    private static readonly string PastaRecursos =
        Path.Combine(AppContext.BaseDirectory, "Recursos");

    // XML real: nNF 15665807, transporte 0130693549, 2 itens, emitente BRF, dest FREE MAN.
    private const string Xml1 = "NFE35251201838723009850550060156658071604491612.XML";

    private static NotaFiscal LerXml1() =>
        new LeitorNotaFiscalXml().ParseArquivo(Path.Combine(PastaRecursos, Xml1));

    private static OpcoesOperacionais OperacionalPadrao() => new()
    {
        Deposito = "JD",
        IdDeposito = 8,
        CnpjArmazem = "14.605.065/0001-75",
        CnpjDepositante = "01.838.723/0098-50",
        CodigoDepositante = "3509308",
    };

    private static OpcoesI50 I50Padrao() => new()
    {
        TipoCarga = "CN",
        TipoSeparacao = "RES;FEF;PLI",
        TipoSeparacao2 = "AGL;FEF;PLL",
        TipoAcondicionamento = "PC",
        QtdMandante = "U",
    };

    private static ConstrutorI50 Criar(OpcoesOperacionais? op = null, OpcoesI50? i50 = null) =>
        new(Options.Create(op ?? OperacionalPadrao()), Options.Create(i50 ?? I50Padrao()));

    [Fact]
    public void Construir_UmaNota_GeraUmUnicoDocCarga()
    {
        var nota = LerXml1();

        var payload = Criar().Construir(nota);

        Assert.Single(payload.Pedido);
    }

    [Fact]
    public void Construir_Nroagrupseparacao_RecebeNumeroDoTransporteComZerosPreservados()
    {
        var nota = LerXml1();

        var pedido = Assert.Single(Criar().Construir(nota).Pedido);

        Assert.Equal("0130693549", pedido.Nroagrupseparacao);
    }

    [Fact]
    public void Construir_TransporteExplicito_SobrescreveODaNota()
    {
        var nota = LerXml1();

        var pedido = Assert.Single(Criar().Construir(nota, "0000012345").Pedido);

        Assert.Equal("0000012345", pedido.Nroagrupseparacao);
    }

    [Fact]
    public void Construir_NumeroPedido_DerivaDoNumeroDaNota()
    {
        var nota = LerXml1();

        var pedido = Assert.Single(Criar().Construir(nota).Pedido);

        Assert.Equal("15665807", pedido.NumeroPedido);
    }

    [Fact]
    public void Construir_ChaveEDestinatario_VemDaNota()
    {
        var nota = LerXml1();

        var pedido = Assert.Single(Criar().Construir(nota).Pedido);

        Assert.Equal(44, nota.ChaveNFe.Length);
        // CNPJ do destinatário sai com máscara (regra confirmada).
        Assert.Equal("28.288.914/0001-19", pedido.CnpjDestinatario);
    }

    [Fact]
    public void Construir_CnpjsOperacionais_VemDaConfig()
    {
        var nota = LerXml1();

        var pedido = Assert.Single(Criar().Construir(nota).Pedido);

        Assert.Equal("01.838.723/0098-50", pedido.CnpjProprietario);
        Assert.Equal("14.605.065/0001-75", pedido.CnpjArmazem);
        Assert.Equal("JD", pedido.Deposito);
    }

    [Fact]
    public void Construir_TipoCargaESeparacao_VemDaConfig_NoCabecalhoENosItens()
    {
        var nota = LerXml1();
        var i50 = new OpcoesI50
        {
            TipoCarga = "TR",
            TipoSeparacao = "AAA;BBB",
            TipoSeparacao2 = "CCC;DDD",
            TipoAcondicionamento = "PP",
            QtdMandante = "E",
        };

        var pedido = Assert.Single(Criar(i50: i50).Construir(nota).Pedido);

        Assert.Equal("TR", pedido.TipoCarga);
        Assert.Equal("AAA;BBB", pedido.TipoSeparacao);
        Assert.Equal("CCC;DDD", pedido.TipoSeparacao2);
        Assert.Equal("PP", pedido.TipoAcondicionamento);

        // Os mesmos valores de separação/qtdMandante devem ser aplicados a cada item.
        Assert.NotEmpty(pedido.ItemPedido);
        Assert.All(pedido.ItemPedido, item =>
        {
            Assert.Equal("AAA;BBB", item.TipoSeparacao);
            Assert.Equal("CCC;DDD", item.TipoSeparacao2);
            Assert.Equal("E", item.QtdMandante);
        });
    }

    [Fact]
    public void Construir_Itens_MapeiamCodigoEQuantidadeDoQCom()
    {
        var nota = LerXml1();

        var pedido = Assert.Single(Criar().Construir(nota).Pedido);

        Assert.Equal(2, pedido.ItemPedido.Count);
        Assert.Equal(1, pedido.ItemPedido[0].NumeroSequencialItem);
        Assert.Equal("175894", pedido.ItemPedido[0].CodigoProduto);
        Assert.Equal(12.0000m, pedido.ItemPedido[0].QuantidadeProduto);
        Assert.Equal(2, pedido.ItemPedido[1].NumeroSequencialItem);
        Assert.Equal(24.0000m, pedido.ItemPedido[1].QuantidadeProduto);
    }

    [Fact]
    public void Construir_Data_FormatadaDiaMesAnoHoraMinutoSegundo()
    {
        var nota = LerXml1();

        var pedido = Assert.Single(Criar().Construir(nota).Pedido);

        // dhEmi = 2025-12-15T08:29:34-03:00 → emissão "15/12/2025 08:29:34".
        Assert.Equal("15/12/2025 08:29:34", pedido.DataEmissao);
        // Previsão confirmada BRF = emissão + 2 dias → "17/12/2025 08:29:34".
        Assert.Equal("17/12/2025 08:29:34", pedido.DhprevEmbarque);
        Assert.Equal("17/12/2025 08:29:34", pedido.Dataprev);
    }

    [Fact]
    public void Construir_TransporteVazio_Lanca()
    {
        var nota = LerXml1() with { NumeroTransporte = null };

        Assert.Throws<ArgumentException>(() => Criar().Construir(nota));
        Assert.Throws<ArgumentException>(() => Criar().Construir(nota, "   "));
    }

    [Fact]
    public void ConstruirJson_GeraNomesExatosDoContrato()
    {
        var nota = LerXml1();

        var json = Criar().ConstruirJson(nota);

        Assert.Contains("\"PEDIDO\"", json);
        Assert.Contains("\"nroagrupseparacao\":\"0130693549\"", json);
        Assert.Contains("\"ITEM_PEDIDO\"", json);
        Assert.Contains("\"DhprevEmbarque\"", json);
        Assert.Contains("\"tipoSeparacao\"", json);
    }

    [Fact]
    public void ConstruirJson_Decimais_UsamPontoDecimal()
    {
        var nota = LerXml1();

        var json = Criar().ConstruirJson(nota);

        // Quantidade 12 e 24 serializadas com ponto (cultura invariante), nunca vírgula.
        Assert.Contains("\"quantidadeProduto\":12", json);
        Assert.DoesNotContain("12,", json);
    }

    [Fact]
    public void ConstruirJson_NumeroEmbarqueNulo_EOmitido()
    {
        var nota = LerXml1();

        var json = Criar().ConstruirJson(nota);

        Assert.DoesNotContain("numeroEmbarque", json);
    }
}
