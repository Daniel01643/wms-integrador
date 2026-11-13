using System.Text;
using System.Text.Json;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Core.Modelos;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class ConstrutorI60ParametrosTests
{
    private static NotaFiscal Nota() =>
        new LeitorNotaFiscalXml().Parse(
            new MemoryStream(Encoding.UTF8.GetBytes(AmbienteI51.Xml(321, "11111111000111", ("A", 10m), ("B", 5.5m)))), "x.xml");

    private static ConstrutorI60 Construtor(OpcoesI60? i60 = null) =>
        new(Options.Create(new OpcoesOperacionais { CnpjDepositante = "01.838.723/0098-50", CnpjArmazem = "14.605.065/0001-75", Deposito = "JD" }),
            Options.Create(i60 ?? new OpcoesI60()));

    [Fact]
    public void SemParametros_EIdenticoAoMetodoAnterior()
    {
        var c = Construtor();
        var nota = Nota();
        var antigo = JsonSerializer.Serialize(c.Construir(nota), JsonWms.Opcoes);
        var novo = JsonSerializer.Serialize(c.Construir(nota, new ParametrosI60()), JsonWms.Opcoes);
        Assert.Equal(antigo, novo);
    }

    [Fact]
    public void QuantidadesAjustadas_AlteramOsTresCampos_ETotalEVolume()
    {
        var p = Construtor().Construir(Nota(), new ParametrosI60
        {
            Quantidades = new[] { new QuantidadeItemI60(1, 8m), new QuantidadeItemI60(2, 5m) },
        });
        var nv = p.NotasVenda[0];
        Assert.Equal(8m, nv.ItemNotaVenda[0].QtdeProduto);
        Assert.Equal(8m, nv.ItemNotaVenda[0].QtdeProdutoConfirmada);
        Assert.Equal(8m, nv.ItemNotaVenda[0].QtdeEmbalagem);
        Assert.Equal(13m, nv.QtdeTotalNota);
        Assert.Equal(13m, nv.QtdeVolume);
    }

    [Fact]
    public void ItemSemEntrada_EOmitido_MantendoANumeracaoDosDemais()
    {
        var p = Construtor().Construir(Nota(), new ParametrosI60 { Quantidades = new[] { new QuantidadeItemI60(2, 4m) } });
        var itens = p.NotasVenda[0].ItemNotaVenda;
        Assert.Equal(2, itens.Single().NumeroSequencialItem);
    }

    [Fact]
    public void OperacaoETpacao_DoParametro()
    {
        var nv = Construtor().Construir(Nota(), new ParametrosI60 { TipoOperacaoNota = "IMP", Tpacao = "UPD" }).NotasVenda[0];
        Assert.Equal("IMP", nv.TipoOperacaoNota);
        Assert.Equal("IMP", nv.Tpsit);
        Assert.Equal("UPD", nv.Tpacao);
    }

    [Fact]
    public void Volume_NuloArredondaASomaAjustada_NaoNuloUsa_ZeroTemPrecedencia()
    {
        var qs = new[] { new QuantidadeItemI60(1, 2.5m), new QuantidadeItemI60(2, 1m) };
        Assert.Equal(4m, Construtor().Construir(Nota(), new ParametrosI60 { Quantidades = qs }).NotasVenda[0].QtdeVolume); // 3,5 -> 4
        Assert.Equal(7m, Construtor().Construir(Nota(), new ParametrosI60 { Quantidades = qs, QtdeVolume = 7m }).NotasVenda[0].QtdeVolume);
        Assert.Equal(0m, Construtor(new OpcoesI60 { QtdeVolumeZero = true })
            .Construir(Nota(), new ParametrosI60 { Quantidades = qs, QtdeVolume = 7m }).NotasVenda[0].QtdeVolume);
    }

    [Fact]
    public void ValorTotalEPesos_SeguemOXml()
    {
        var antigo = Construtor().Construir(Nota()).NotasVenda[0];
        var novo = Construtor().Construir(Nota(), new ParametrosI60 { Quantidades = new[] { new QuantidadeItemI60(1, 1m), new QuantidadeItemI60(2, 1m) } }).NotasVenda[0];
        Assert.Equal(antigo.ValorTotal, novo.ValorTotal);
        Assert.Equal(antigo.PesoBrutoKg, novo.PesoBrutoKg);
        Assert.Equal(antigo.Chavenfe, novo.Chavenfe);
    }
}
