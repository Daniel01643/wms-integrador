using KitWmsIntegrador.Core.Leitura;

namespace KitWmsIntegrador.Tests;

public class LeitorNotaFiscalXmlTests
{
    private static readonly string PastaRecursos =
        Path.Combine(AppContext.BaseDirectory, "Recursos");

    private const string Xml1 = "NFE35251201838723009850550060156658071604491612.XML";
    private const string Xml2 = "NFE35251201838723009850550060156663211093034056.XML";

    private static string Caminho(string nome) => Path.Combine(PastaRecursos, nome);

    [Fact]
    public void ParseArquivo_Xml1_ExtraiCamposPrincipais()
    {
        var leitor = new LeitorNotaFiscalXml();

        var nota = leitor.ParseArquivo(Caminho(Xml1));

        Assert.Equal("15665807", nota.Numero);
        Assert.Equal("35251201838723009850550060156658071604491612", nota.ChaveNFe);
        Assert.Equal("0130693549", nota.NumeroTransporte);
        Assert.Equal("6", nota.Serie);
        Assert.Equal("55", nota.Modelo);
        Assert.Equal("01838723009850", nota.EmitenteCnpj);
        Assert.Equal("BRF S.A.", nota.EmitenteNome);
        Assert.Equal("28288914000119", nota.DestinatarioCnpj);
        Assert.Equal(12644.40m, nota.ValorTotal);
    }

    [Fact]
    public void ParseArquivo_Xml1_ExtraiItens()
    {
        var leitor = new LeitorNotaFiscalXml();

        var nota = leitor.ParseArquivo(Caminho(Xml1));

        Assert.Equal(2, nota.Itens.Count);

        var primeiro = nota.Itens[0];
        Assert.Equal("175894", primeiro.CodigoProduto);
        Assert.Equal(12.0000m, primeiro.Quantidade);
        Assert.Equal("CX", primeiro.Unidade);
        Assert.Equal("5102", primeiro.Cfop);
        Assert.Equal(5710.80m, primeiro.ValorProduto);
        Assert.Equal("3755261", primeiro.Lote);
        Assert.Equal(new DateOnly(2026, 1, 16), primeiro.Validade);

        var segundo = nota.Itens[1];
        Assert.Equal("175886", segundo.CodigoProduto);
        Assert.Equal(24.0000m, segundo.Quantidade);
    }

    [Fact]
    public void Parse_Item_ExtraiNcmEEan()
    {
        var leitor = new LeitorNotaFiscalXml();

        var nota = leitor.ParseArquivo(Caminho(Xml1));

        var primeiro = nota.Itens[0];
        Assert.Equal("19059090", primeiro.Ncm);
        Assert.Equal("17891515645813", primeiro.Ean);
    }

    [Fact]
    public void ParseArquivo_Xml1_ExtraiVolumesETransportadora()
    {
        var leitor = new LeitorNotaFiscalXml();

        var nota = leitor.ParseArquivo(Caminho(Xml1));

        Assert.NotNull(nota.Volumes);
        Assert.Equal("36", nota.Volumes!.QuantidadeVolumes);
        Assert.Equal(275.640m, nota.Volumes.PesoLiquido);
        Assert.Equal(302.675m, nota.Volumes.PesoBruto);
        Assert.Equal("FRIOZEM LOGISTICA LTDA", nota.TransportadoraNome);
        Assert.Equal("03639682000175", nota.TransportadoraCnpj);
    }

    [Fact]
    public void ParseArquivo_Xml1_ExpoeInfoComplementar()
    {
        var leitor = new LeitorNotaFiscalXml();

        var nota = leitor.ParseArquivo(Caminho(Xml1));

        Assert.NotNull(nota.InfoComplementar);
        Assert.Contains("Transporte: 0130693549", nota.InfoComplementar!);
    }

    [Fact]
    public void ParseArquivo_Xml2_ExtraiNumeroETransporte()
    {
        var leitor = new LeitorNotaFiscalXml();

        var nota = leitor.ParseArquivo(Caminho(Xml2));

        Assert.Equal("15666321", nota.Numero);
        Assert.Equal("0130693967", nota.NumeroTransporte);
    }

    [Fact]
    public void ParseArquivo_Xml1_ExtraiPlacaVeiculo()
    {
        var leitor = new LeitorNotaFiscalXml();

        var nota = leitor.ParseArquivo(Caminho(Xml1));

        Assert.Equal("AYC8G72", nota.PlacaVeiculo);
    }

    [Fact]
    public void ParseArquivo_Xml2_ExtraiPlacaVeiculo()
    {
        var leitor = new LeitorNotaFiscalXml();

        var nota = leitor.ParseArquivo(Caminho(Xml2));

        Assert.Equal("QPJ1H28", nota.PlacaVeiculo);
    }

    [Fact]
    public void Parse_ComReboque_UsaPlacaDoVeiculoEIgnoraReboque()
    {
        // NFe com veicTransp/placa E reboque/placa: a placa do reboque NÃO deve
        // sobrescrever a placa do veículo (requisito "só veicTransp").
        const string xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe">
              <NFe>
                <infNFe Id="NFe35251201838723009850550060156658071604491612">
                  <transp>
                    <modFrete>0</modFrete>
                    <veicTransp>
                      <placa>ABC1234</placa>
                      <UF>SP</UF>
                    </veicTransp>
                    <reboque>
                      <placa>REB9999</placa>
                      <UF>SP</UF>
                    </reboque>
                  </transp>
                </infNFe>
              </NFe>
            </nfeProc>
            """;

        var leitor = new LeitorNotaFiscalXml();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));

        var nota = leitor.Parse(stream, "memoria.xml");

        Assert.Equal("ABC1234", nota.PlacaVeiculo);
    }
}
