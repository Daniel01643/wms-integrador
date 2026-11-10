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

    private static ConstrutorI60 Criar(OpcoesOperacionais? op = null, OpcoesI60? i60 = null) =>
        new(Options.Create(op ?? OperacionalPadrao()), Options.Create(i60 ?? new OpcoesI60()));

    private static NotaFiscal NotaComQuantidades(string? qVolXml, params decimal[] quantidades) =>
        LerXml1() with
        {
            Volumes = new VolumeTransporte
            {
                QuantidadeVolumes = qVolXml,
                PesoBruto = 10m,
                PesoLiquido = 9m,
            },
            Itens = quantidades
                .Select((q, i) => new ItemNota { CodigoProduto = $"P{i + 1}", Quantidade = q })
                .ToList(),
        };
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
        Assert.Equal(36m, nv.QtdeVolume); // soma dos itens (12 + 24), igual ao qVol deste XML
        Assert.Equal(12644.40m, nv.ValorTotal);
    }

    [Fact]
    public void QtdeVolume_VariosItens_ESomaDasQuantidades()
    {
        var nota = NotaComQuantidades("5", 12m, 24m, 3m, 1m);

        var nv = Assert.Single(Criar().Construir(nota).NotasVenda);

        Assert.Equal(40m, nv.QtdeVolume);
        Assert.Equal(40m, nv.QtdeTotalNota);
    }

    [Fact]
    public void QtdeVolume_QVolDoXmlDifere_PrevaleceASoma()
    {
        // qVol do XML = 7, mas a soma das caixas dos itens = 36: deve prevalecer a soma.
        var nota = NotaComQuantidades("7", 12m, 24m);

        var nv = Assert.Single(Criar().Construir(nota).NotasVenda);

        Assert.Equal(36m, nv.QtdeVolume);
    }

    [Fact]
    public void QtdeVolume_SemVolumesNoXml_UsaSomaDosItens()
    {
        var nota = NotaComQuantidades(null, 12m, 24m) with { Volumes = null };

        var nv = Assert.Single(Criar().Construir(nota).NotasVenda);

        Assert.Equal(36m, nv.QtdeVolume);
    }

    [Fact]
    public void QtdeVolume_BateComSomaEnviadaNaI50()
    {
        // Por construção: soma da QuantidadeProduto por item da I50 == qtdeVolume da I60.
        var nota = NotaComQuantidades("99", 12m, 24m, 5m);

        var i50 = new ConstrutorI50(Options.Create(OperacionalPadrao()), Options.Create(new OpcoesI50()));
        var somaI50 = Assert.Single(i50.Construir(nota, "123").Pedido)
            .ItemPedido.Sum(i => i.QuantidadeProduto);
        var nv = Assert.Single(Criar().Construir(nota).NotasVenda);

        Assert.Equal(somaI50, nv.QtdeVolume);
    }

    [Theory]
    [InlineData("36.4", 36)]   // fração < 0,5 -> para baixo
    [InlineData("36.5", 37)]   // meio -> para longe de zero
    [InlineData("36.6", 37)]   // fração > 0,5 -> para cima
    public void QtdeVolume_SomaComFracao_ArredondaParaInteiro(string soma, decimal esperado)
    {
        var total = decimal.Parse(soma, System.Globalization.CultureInfo.InvariantCulture);
        var nota = NotaComQuantidades("1", total - 10m, 10m);

        var nv = Assert.Single(Criar().Construir(nota).NotasVenda);

        Assert.Equal(esperado, nv.QtdeVolume);
        Assert.Equal(total, nv.QtdeTotalNota); // qtdeTotalNota segue sem arredondar
    }

    [Fact]
    public void TipoOperacaoNota_PadraoEIne_NosDoisCampos()
    {
        var nv = Assert.Single(Criar().Construir(LerXml1()).NotasVenda);

        Assert.Equal("INE", nv.TipoOperacaoNota);
        Assert.Equal("INE", nv.Tpsit);
    }

    [Fact]
    public void TipoOperacaoNota_Configurado_AplicaEmTpsitETipoOperacaoNota()
    {
        var construtor = Criar(i60: new OpcoesI60 { TipoOperacaoNota = " INV " });

        var nv = Assert.Single(construtor.Construir(LerXml1()).NotasVenda);

        Assert.Equal("INV", nv.TipoOperacaoNota);
        Assert.Equal("INV", nv.Tpsit);
        Assert.Contains("\"Tpsit\":\"INV\"", construtor.ConstruirJson(LerXml1()));
        Assert.Contains("\"tipoOperacaoNota\":\"INV\"", construtor.ConstruirJson(LerXml1()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TipoOperacaoNota_Vazio_VoltaAoPadrao(string valor)
    {
        var construtor = Criar(i60: new OpcoesI60 { TipoOperacaoNota = valor });

        var nv = Assert.Single(construtor.Construir(LerXml1()).NotasVenda);

        Assert.Equal("INE", nv.TipoOperacaoNota);
        Assert.Equal("INE", nv.Tpsit);
    }

    [Fact]
    public void ConstrutorI60_SemOpcoesI60_UsaPadrao()
    {
        var construtor = new ConstrutorI60(Options.Create(OperacionalPadrao()));

        var nv = Assert.Single(construtor.Construir(LerXml1()).NotasVenda);

        Assert.Equal("INE", nv.Tpsit);
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
