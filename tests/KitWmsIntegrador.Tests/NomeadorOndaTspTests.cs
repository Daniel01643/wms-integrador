using System.Text.RegularExpressions;
using KitWmsIntegrador.Core.Pacote;

namespace KitWmsIntegrador.Tests;

public class NomeadorOndaTspTests
{
    private static readonly DateTime Data = new(2026, 11, 12);

    [Theory]
    [InlineData("XML'S SOROCABA.zip", "SOROCABA")]
    [InlineData("XML'S SJC.zip", "SJC")]
    [InlineData("XML\u2019S SJC.zip", "SJC")]
    [InlineData("xml\u00B4s sjc.ZIP", "SJC")]
    [InlineData("XMLS.zip", "")]
    [InlineData("XML'S.zip", "")]
    [InlineData("XML SANTOS XML.zip", "SANTOS")]
    [InlineData("XMLSANTOS.zip", "XMLSANTOS")]
    [InlineData("S\u00E3o Jos\u00E9 dos Campos.zip", "SAO-JOSE-DOS-CAMPOS")]
    [InlineData("  a  b__c.zip", "A-B-C")]
    [InlineData("REL (final) 2.zip", "REL-FINAL-2")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void LimparNome(string? zip, string esperado) =>
        Assert.Equal(esperado, NomeadorOndaTsp.LimparNome(zip));

    [Fact]
    public void Montar_ComPrefixoDataSequencialENome()
    {
        Assert.Equal("TSP-20261112-01-SOROCABA", NomeadorOndaTsp.Montar(Data, 1, "XML'S SOROCABA.zip"));
        Assert.Equal("TSP-20261112-02-SJC", NomeadorOndaTsp.Montar(Data, 2, "XML'S SJC.zip"));
        Assert.StartsWith("TSP-", NomeadorOndaTsp.Prefixo);
    }

    [Theory]
    [InlineData("XML'S.zip")]
    [InlineData("")]
    [InlineData(null)]
    public void Montar_SemNome_FicaSoComDataESequencial(string? zip)
    {
        var onda = NomeadorOndaTsp.Montar(Data, 3, zip);
        Assert.Equal("TSP-20261112-03", onda);
        Assert.Equal(15, onda.Length);
    }

    [Fact]
    public void Montar_CortaONomeEm14_SemHifenFinal()
    {
        var longo = NomeadorOndaTsp.Montar(Data, 1, "S\u00E3o Jos\u00E9 dos Campos.zip");
        Assert.Equal("TSP-20261112-01-SAO-JOSE-DOS-C", longo);
        Assert.Equal(30, longo.Length);

        var hifen = NomeadorOndaTsp.Montar(Data, 1, "AAAAAAAAAAAAA BBBB.zip");
        Assert.Equal("TSP-20261112-01-AAAAAAAAAAAAA", hifen);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(250)]
    public void Montar_NuncaPassaDe30_FormatoSeguro(int sequencial)
    {
        var nomes = new[]
        {
            "XML'S SOROCABA.zip", "RELA\u00C7\u00C3O MUITO MAIOR DO QUE O LIMITE PERMITIDO.zip", "A.zip", "----.zip",
            "0123456789012345678901234567890123456789.zip", "Jundia\u00ED - Entregas (2).zip", "XML'S.zip",
        };
        foreach (var nome in nomes)
        {
            var onda = NomeadorOndaTsp.Montar(Data, sequencial, nome);
            Assert.True(onda.Length <= 30, $"{onda} ({onda.Length})");
            Assert.Matches(new Regex(@"^TSP-20261112-\d{2,3}(-[A-Z0-9]+)*$"), onda);
        }
    }
}