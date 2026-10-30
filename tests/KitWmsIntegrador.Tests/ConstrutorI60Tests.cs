using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Core.Modelos;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class ConstrutorI60Tests
{
    private static readonly string PastaRecursos =
        Path.Combine(AppContext.BaseDirectory, "Recursos");

    private const string Xml1 = "NFE35251201838723009850550060156658071604491612.XML";

    private static NotaFiscal LerXml1() =>
        new LeitorNotaFiscalXml().ParseArquivo(Path.Combine(PastaRecursos, Xml1));

    private static OpcoesOperacionais OperacionalPadrao() => new()
    {
        Deposito = "JD",
        CnpjArmazem = "14.605.065/0001-75",
        CnpjDepositante = "01.838.723/0098-50",
    };

    private static ConstrutorI60 Criar(OpcoesOperacionais? op = null) =>
        new(Options.Create(op ?? OperacionalPadrao()));

    [Fact]
    public void Construir_UmaNota_GeraUmaUnicaNotaVenda()
    {
        var nota = LerXml1();

        var payload = Criar().Construir(nota);

        Assert.Single(payload.NotasVenda);
    }

    [Fact]
    public void Construir_ChaveNumeroESerie_VemDaNota()
    {
        var nota = LerXml1();

        var nv = Assert.Single(Criar().Construir(nota).NotasVenda);

        Assert.Equal("35251201838723009850550060156658071604491612", nv.Chavenfe);
        Assert.Equal(44, nv.Chavenfe.Length);
        Assert.Equal("15665807", nv.NumeroNota);
        Assert.Equal("6", nv.SerieNota);
    }

    [Fact]
    public void Construir_Pedidos_AmarramAoNumeroDaNota_IgualAoI50()
    {
        var nota = LerXml1();

        var nv = Assert.Single(Criar().Construir(nota).NotasVenda);
        var numeroPedido = Assert.Single(nv.Pedidos);

        Assert.Equal("15665807", numeroPedido);

        // A amarração bate com o numeroPedido da I50 para a mesma nota.
        var i50 = new ConstrutorI50(
            Options.Create(OperacionalPadrao()),
            Options.Create(new OpcoesI50()));
        var pedidoI50 = Assert.Single(i50.Construir(nota).Pedido);
        Assert.Equal(pedidoI50.NumeroPedido, numeroPedido);
    }

    [Fact]
    public void Construir_TipoSituacao_EIneFixoDoTemplate()
    {
        var nota = LerXml1();

        var nv = Assert.Single(Criar().Construir(nota).NotasVenda);

        Assert.Equal("INE", nv.Tpsit);
        Assert.Equal("INE", nv.TipoOperacaoNota);
        Assert.Equal("NV", nv.Tpnota);
        Assert.Equal("AR", nv.TipoNota);
    }

    [Fact]
    public void Construir_CnpjsOperacionais_VemDaConfig()
    {
        var nota = LerXml1();

        var nv = Assert.Single(Criar().Construir(nota).NotasVenda);

        Assert.Equal("01.838.723/0098-50", nv.CnpjProprietariaProduto);
        Assert.Equal("14.605.065/0001-75", nv.CnpjArmazem);
        Assert.Equal("JD", nv.Deposito);
    }

    [Fact]
    public void Construir_Cfop_VemDoPrimeiroItem()
    {
        var nota = LerXml1();

        var nv = Assert.Single(Criar().Construir(nota).NotasVenda);

        Assert.Equal("5102", nv.CodNatOperacaoFiscal);
    }

    [Fact]
    public void Construir_PesosEVolume_VemDaNota()
    {
        var nota = LerXml1();

        var nv = Assert.Single(Criar().Construir(nota).NotasVenda);

        Assert.Equal(275.640m, nv.PesoLiquidoKg);
        Assert.Equal(302.675m, nv.PesoBrutoKg);
        Assert.Equal(36m, nv.QtdeVolume);
        Assert.Equal(12644.40m, nv.ValorTotal);
    }

    [Fact]
    public void Construir_Itens_MapeiamQCom()
    {
        var nota = LerXml1();

        var nv = Assert.Single(Criar().Construir(nota).NotasVenda);

        Assert.Equal(2, nv.ItemNotaVenda.Count);
        Assert.Equal("175894", nv.ItemNotaVenda[0].CodProduto);
        Assert.Equal(12.0000m, nv.ItemNotaVenda[0].QtdeProduto);
        Assert.Equal(36m, nv.QtdeTotalNota); // 12 + 24
    }

    [Fact]
    public void ConstruirJson_GeraNomesExatosDoContrato()
    {
        var nota = LerXml1();

        var json = Criar().ConstruirJson(nota);

        Assert.Contains("\"NOTAS_VENDA\"", json);
        Assert.Contains("\"chavenfe\":\"35251201838723009850550060156658071604491612\"", json);
        Assert.Contains("\"pedidos\":[\"15665807\"]", json);
        Assert.Contains("\"Tpsit\":\"INE\"", json);
        Assert.Contains("\"Tpnota\":\"NV\"", json);
        Assert.Contains("\"NroEmb\"", json);
        Assert.Contains("\"ITEM_NOTA_VENDA\"", json);
    }

    [Fact]
    public void ConstruirJson_Decimais_UsamPontoDecimal()
    {
        var nota = LerXml1();

        var json = Criar().ConstruirJson(nota);

        Assert.Contains("\"pesoLiquidoKg\":275.64", json);
        Assert.DoesNotContain("275,64", json);
    }
}
