using System.Text.Json;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Core.Modelos;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class ConstrutorI40Tests
{
    private static readonly string PastaRecursos =
        Path.Combine(AppContext.BaseDirectory, "Recursos");

    private const string Xml1 = "NFE35251201838723009850550060156658071604491612.XML";
    private const string Placa = "abc1d23";

    private static NotaFiscal LerXml1() =>
        new LeitorNotaFiscalXml().ParseArquivo(Path.Combine(PastaRecursos, Xml1));

    private static OpcoesOperacionais OperacionalPadrao() => new()
    {
        Deposito = "JD",
        CnpjArmazem = "14.605.065/0001-75",
        CnpjDepositante = "01.838.723/0098-50",
    };

    private static OpcoesDevolucao DevolucaoPadrao() => new() { NomeMotorista = "MOTORISTA DEVOLUCAO" };

    private static ConstrutorI40 Criar(OpcoesDevolucao? dev = null) =>
        new(Options.Create(OperacionalPadrao()), Options.Create(dev ?? DevolucaoPadrao()));

    [Fact]
    public void Construir_MapeiaCamposDaNota()
    {
        var nota = LerXml1();

        var nf = Assert.Single(Criar().Construir(nota, Placa).NotaFiscal);

        Assert.Equal(nota.ChaveNFe, nf.Chavenfe);
        Assert.Equal(44, nf.Chavenfe.Length);
        Assert.Equal(nota.Numero, nf.NumeroNota);
        Assert.Equal(nota.Serie, nf.SerieNota);
        Assert.Equal(nota.Protocolo ?? string.Empty, nf.Protocolo);
        Assert.Equal(nota.Itens[0].Cfop, nf.CodNatOperacaoFiscal);
        Assert.Equal(nota.ValorTotal ?? 0m, nf.ValorTotal);
        Assert.Equal(nota.Itens.Sum(i => i.Quantidade), nf.QtdeTotalNota);
        Assert.Equal(FormatoWms.Data(nota.DataEmissao), nf.DataEmissao);
        Assert.Equal("01.838.723/0098-50", nf.CnpjProprietariaProduto);
        Assert.Equal("JD", nf.Deposito);
        Assert.Equal("14.605.065/0001-75", nf.CnpjArmazem);
        Assert.Equal("PR", nf.FormaTransporte);
    }

    [Fact]
    public void Construir_TipoNotaDE_E_TipoOperacaoICD_PorPadrao()
    {
        var nf = Assert.Single(Criar().Construir(LerXml1(), Placa).NotaFiscal);

        Assert.Equal("DE", nf.TipoNota);
        Assert.Equal("ICD", nf.TipoOperacaoNota);
        Assert.All(nf.ItemNotaFiscal, i => Assert.Equal("040", i.CodSituacaoTributaria));
    }

    [Fact]
    public void Construir_TipoOperacaoConfiguravel()
    {
        var dev = DevolucaoPadrao();
        dev.TipoOperacaoNota = "IMC";

        var nf = Assert.Single(Criar(dev).Construir(LerXml1(), Placa).NotaFiscal);

        Assert.Equal("IMC", nf.TipoOperacaoNota);
    }

    [Fact]
    public void Construir_PlacaEMotorista_VaoNoPayload()
    {
        var nf = Assert.Single(Criar().Construir(LerXml1(), Placa).NotaFiscal);

        Assert.Equal("ABC1D23", nf.PlacaVeiculoPrevistoNota);
        Assert.Equal("MOTORISTA DEVOLUCAO", nf.NomeMotorista);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Construir_PlacaVazia_Lanca(string? placa)
    {
        var ex = Assert.Throws<ArgumentException>(() => Criar().Construir(LerXml1(), placa!));

        Assert.Contains("placa", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construir_MotoristaNaoConfigurado_Lanca(string motorista)
    {
        var dev = new OpcoesDevolucao { NomeMotorista = motorista };

        var ex = Assert.Throws<InvalidOperationException>(() => Criar(dev).Construir(LerXml1(), Placa));

        Assert.Contains("Devolucao:NomeMotorista", ex.Message);
    }

    [Fact]
    public void Construir_SemInverter_MantemPartesDaVenda()
    {
        var nota = LerXml1();

        var nf = Assert.Single(Criar().Construir(nota, Placa).NotaFiscal);

        Assert.Equal(FormatoWms.Documento(nota.EmitenteCnpj), nf.CnpjEmitente);
        Assert.Equal(FormatoWms.Documento(nota.EmitenteCnpj), nf.CnpjRemetente);
        Assert.Equal(FormatoWms.Documento(nota.EmitenteCnpj), nf.CnpjNota);
        Assert.Equal(FormatoWms.Documento(nota.DestinatarioCnpj), nf.CnpjDestinatario);
        Assert.Equal(FormatoWms.Documento(nota.DestinatarioCnpj), nf.CnpjLocalEntrega);
    }

    [Fact]
    public void Construir_InverterPartes_TrocaEmitenteComDestinatario()
    {
        var nota = LerXml1();
        var dev = DevolucaoPadrao();
        dev.InverterPartes = true;

        var nf = Assert.Single(Criar(dev).Construir(nota, Placa).NotaFiscal);

        Assert.NotEqual(nota.EmitenteCnpj, nota.DestinatarioCnpj);
        Assert.Equal(FormatoWms.Documento(nota.DestinatarioCnpj), nf.CnpjEmitente);
        Assert.Equal(FormatoWms.Documento(nota.DestinatarioCnpj), nf.CnpjRemetente);
        // cnpjNota segue o emitente do envio.
        Assert.Equal(nf.CnpjEmitente, nf.CnpjNota);
        Assert.Equal(FormatoWms.Documento(nota.EmitenteCnpj), nf.CnpjDestinatario);
        Assert.Equal(FormatoWms.Documento(nota.EmitenteCnpj), nf.CnpjLocalEntrega);
    }

    [Fact]
    public void Construir_TransportadoraAusente_UsaCnpjArmazem()
    {
        var nota = LerXml1() with { TransportadoraCnpj = null };

        var nf = Assert.Single(Criar().Construir(nota, Placa).NotaFiscal);

        Assert.Equal("14.605.065/0001-75", nf.CnpjTransportadora);
    }

    [Fact]
    public void Construir_Atributo1_TemChaveEProtocoloETruncaEm200()
    {
        var nota = LerXml1();
        var nf = Assert.Single(Criar().Construir(nota, Placa).NotaFiscal);
        Assert.Equal($"CH:{nota.ChaveNFe} PROT:{nota.Protocolo}", nf.Atributo1);

        var longa = nota with { Protocolo = new string('9', 400) };
        var nfLonga = Assert.Single(Criar().Construir(longa, Placa).NotaFiscal);
        Assert.Equal(200, nfLonga.Atributo1.Length);
    }

    [Fact]
    public void Construir_Itens_MapeiamEQtdeEmbalagemIgualCaixas()
    {
        var nota = LerXml1();

        var nf = Assert.Single(Criar().Construir(nota, Placa).NotaFiscal);

        Assert.Equal(nota.Itens.Count, nf.ItemNotaFiscal.Count);
        for (var i = 0; i < nota.Itens.Count; i++)
        {
            var item = nf.ItemNotaFiscal[i];
            Assert.Equal(i + 1, item.NumeroSequencialItem);
            Assert.Equal(nota.Itens[i].CodigoProduto, item.CodProduto);
            Assert.Equal(nota.Itens[i].Quantidade, item.QtdeProduto);
            Assert.Equal(nota.Itens[i].Quantidade, item.QtdeProdutoConfirmada);
            Assert.Equal(nota.Itens[i].Quantidade, item.QtdeEmbalagem);
            Assert.Equal(nota.Itens[i].Ean, item.Ean);
            Assert.Equal(nota.Itens[i].Descricao, item.Descricao);
            Assert.Equal(nota.Itens[i].Ncm, item.Ncm);
            Assert.Equal(nota.Itens[i].Unidade, item.CodUnidade);
        }
    }

    [Fact]
    public void ConstruirJson_RaizNotaFiscal_ItensComoArray_NomesExatos()
    {
        var json = Criar().ConstruirJson(LerXml1(), Placa);

        using var doc = JsonDocument.Parse(json);
        var notas = doc.RootElement.GetProperty("NOTA_FISCAL");
        Assert.Equal(JsonValueKind.Array, notas.ValueKind);
        var nota = notas[0];
        Assert.Equal(JsonValueKind.Array, nota.GetProperty("ITEM_NOTA_FISCAL").ValueKind);
        foreach (var campo in new[]
        {
            "cnpjEmitente", "cnpjRemetente", "cnpjDestinatario", "cnpjLocalEntrega", "cnpjTransportadora",
            "codNatOperacaoFiscal", "numeroNota", "serieNota", "chavenfe", "protocolo", "cnpjNota", "tipoNota",
            "dataEmissao", "qtdeTotalNota", "pesoBrutoKg", "pesoLiquidoKg", "qtdeVolume", "valorTotal",
            "formaTransporte", "cnpjProprietariaProduto", "deposito", "cnpjArmazem", "nomeMotorista",
            "tipoOperacaoNota", "placaVeiculoPrevistoNota", "atributo1", "atributo2", "atributo3",
        })
        {
            Assert.True(nota.TryGetProperty(campo, out _), $"Campo ausente: {campo}");
        }

        var item = nota.GetProperty("ITEM_NOTA_FISCAL")[0];
        foreach (var campo in new[]
        {
            "numeroSequencialItem", "codProduto", "qtdeProduto", "qtdeProdutoConfirmada", "qtdeEmbalagem",
            "valorOriginalItem", "valorAtualItem", "aliquotaICMS", "aliquotaIPI", "codSituacaoTributaria",
            "numeroLote", "valorDetalhe1", "valorDetalhe4", "dataFabricacao", "dataValidade", "ean",
            "descricao", "ncm", "codunidade",
        })
        {
            Assert.True(item.TryGetProperty(campo, out _), $"Campo de item ausente: {campo}");
        }
    }

    [Fact]
    public void Construir_QtdeVolume_EInteiroSomaDasCaixasArredondada()
    {
        var nota = LerXml1() with
        {
            Itens = new[]
            {
                new ItemNota { CodigoProduto = "A", Quantidade = 10.4m },
                new ItemNota { CodigoProduto = "B", Quantidade = 2.2m },
            },
        };

        var nf = Assert.Single(Criar().Construir(nota, Placa).NotaFiscal);
        Assert.Equal(13L, nf.QtdeVolume); // 12,6 -> 13

        var json = Criar().ConstruirJson(nota, Placa);
        using var doc = JsonDocument.Parse(json);
        var volume = doc.RootElement.GetProperty("NOTA_FISCAL")[0].GetProperty("qtdeVolume");
        Assert.Equal(JsonValueKind.Number, volume.ValueKind);
        Assert.Equal("13", volume.GetRawText());
    }

    [Fact]
    public void InterfaceWms_I40_TemIdentificador()
    {
        Assert.Equal("WEBAPI-I40-Json", InterfaceWms.I40.ParaIdentificador());
    }
}
