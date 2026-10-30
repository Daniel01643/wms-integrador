using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Core.Modelos;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class ConstrutorI10Tests
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

    private static ConstrutorI10 Criar(OpcoesOperacionais? op = null) =>
        new(Options.Create(op ?? OperacionalPadrao()));

    [Fact]
    public void Construir_GeraUmaUnicaEmpresa()
    {
        var nota = LerXml1();

        var payload = Criar().Construir(nota);

        Assert.Single(payload.Empresa);
    }

    [Fact]
    public void Construir_CnpjENome_VemDoDestinatario()
    {
        var nota = LerXml1();

        var empresa = Assert.Single(Criar().Construir(nota).Empresa);

        // CNPJ do destinatário sai com máscara (regra confirmada).
        Assert.Equal("28.288.914/0001-19", empresa.Cnpj);
        Assert.Equal(nota.DestinatarioNome, empresa.Razaosocial);
    }

    [Fact]
    public void Construir_Endereco_VemDoDestinatario()
    {
        var nota = LerXml1();

        var empresa = Assert.Single(Criar().Construir(nota).Empresa);

        Assert.Equal(nota.Endereco!.Logradouro ?? string.Empty, empresa.Endereco);
        Assert.Equal(nota.Endereco!.Numero ?? string.Empty, empresa.Numero);
        Assert.Equal(nota.Endereco!.Bairro ?? string.Empty, empresa.Bairro);
        Assert.Equal(nota.Endereco!.Municipio ?? string.Empty, empresa.Cidade);
        Assert.Equal(nota.Endereco!.Uf ?? string.Empty, empresa.Uf);
        // CEP sai com máscara (regra confirmada): 05705901 → 05705-901.
        Assert.Equal("05705-901", empresa.Cep);
    }

    [Fact]
    public void Construir_CnpjsOperacionais_VemDaConfig()
    {
        var nota = LerXml1();

        var empresa = Assert.Single(Criar().Construir(nota).Empresa);

        Assert.Equal("14.605.065/0001-75", empresa.CnpjArmazem);
        Assert.Equal("01.838.723/0098-50", empresa.CnpjProprietaria);
        Assert.Equal("JD", empresa.Deposito);
    }

    [Fact]
    public void Construir_ValoresFixos_SeguemOTemplate()
    {
        var nota = LerXml1();

        var empresa = Assert.Single(Criar().Construir(nota).Empresa);

        // indireto confirmado BRF = "IN" (INDIRETO), não "CL".
        Assert.Equal("IN", empresa.Indireto);
        Assert.Equal("I", empresa.Tpacao);
        Assert.Equal("ISENTO", empresa.Ie);
        Assert.Equal("B", empresa.Classe);
    }

    [Fact]
    public void ConstruirJson_GeraNomesExatosDoContrato()
    {
        var nota = LerXml1();

        var json = Criar().ConstruirJson(nota);

        Assert.Contains("\"EMPRESA\"", json);
        Assert.Contains("\"cnpj\":\"28.288.914/0001-19\"", json);
        Assert.Contains("\"cnpjArmazem\":\"14.605.065/0001-75\"", json);
        Assert.Contains("\"deposito\":\"JD\"", json);
    }
}
