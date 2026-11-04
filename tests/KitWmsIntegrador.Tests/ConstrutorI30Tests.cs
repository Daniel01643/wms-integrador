using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Core.Modelos;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class ConstrutorI30Tests
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
        CodigoDepositante = "3509308",
    };

    private static ConstrutorI30 Criar(OpcoesOperacionais? op = null) =>
        new(Options.Create(op ?? OperacionalPadrao()));

    [Fact]
    public void Construir_UmItemPorProdutoDaNota()
    {
        var nota = LerXml1();

        var payload = Criar().Construir(nota);

        Assert.Equal(nota.Itens.Count, payload.Produto.Count);
        Assert.Equal(2, payload.Produto.Count);
    }

    [Fact]
    public void Construir_Codigo_SemZerosAEsquerda_Codcliente_ComZeros()
    {
        // Item com zeros à esquerda para provar o trim de codigo vs. codcliente preservado.
        var nota = new NotaFiscal
        {
            Numero = "15665807",
            Itens = new[]
            {
                new ItemNota { CodigoProduto = "00175894", Descricao = "PROD", Unidade = "CX" },
            },
        };

        var produto = Assert.Single(Criar().Construir(nota).Produto);

        Assert.Equal("175894", produto.Codigo);
        Assert.Equal("00175894", produto.Codcliente);
    }

    [Fact]
    public void Construir_CodempresaEClassificacao_ConfirmadosAoVivo()
    {
        var nota = LerXml1();

        var produto = Criar().Construir(nota).Produto[0];

        Assert.Equal("3509308", produto.Codempresa);
        Assert.Equal("2", produto.Codclassificacao);
    }

    [Fact]
    public void Construir_NcmEEan_VemDoItem()
    {
        var nota = LerXml1();

        var produto = Criar().Construir(nota).Produto[0];

        Assert.Equal("19059090", produto.Classificacaoncm);
        Assert.Equal("17891515645813", produto.Eancliente);
    }

    [Fact]
    public void Construir_CodtipologiaEDefaults_SeguemTemplate()
    {
        var nota = LerXml1();

        var produto = Criar().Construir(nota).Produto[0];

        Assert.Equal("SEC", produto.Codtipologia);
        Assert.Equal("I", produto.Tpacao);
        Assert.Equal("2", produto.Codtpprod1);
    }

    [Fact]
    public void Construir_CnpjsOperacionais_VemDaConfigComMascara()
    {
        var nota = LerXml1();

        var produto = Criar().Construir(nota).Produto[0];

        Assert.Equal("14.605.065/0001-75", produto.CnpjArmazem);
        Assert.Equal("01.838.723/0098-50", produto.CnpjProprietaria);
        Assert.Equal("JD", produto.Deposito);
    }

    [Fact]
    public void ConstruirJson_GeraNomesExatosDoContrato()
    {
        var nota = LerXml1();

        var json = Criar().ConstruirJson(nota);

        Assert.Contains("\"PRODUTO\"", json);
        Assert.Contains("\"codigo\":\"175894\"", json);
        Assert.Contains("\"classificacaoncm\":\"19059090\"", json);
        Assert.Contains("\"codclassificacao\":\"2\"", json);
        Assert.Contains("\"codempresa\":\"3509308\"", json);
    }
}
