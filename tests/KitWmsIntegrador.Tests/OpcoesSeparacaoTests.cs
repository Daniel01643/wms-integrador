using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Separacao;

namespace KitWmsIntegrador.Tests;

public class OpcoesI51Tests
{
    [Fact]
    public void Padroes_SaoSeguros()
    {
        var o = new OpcoesI51();
        Assert.False(o.Habilitada);
        Assert.Equal("WEBAPI-I51", o.Identificador);
        Assert.Equal(5, o.IntervaloMinutos);
        Assert.Equal(24, o.HorasParaAtraso);
        Assert.Equal("Numero", o.CampoUnico);
        Assert.Equal(14, o.DiasMaxTodos);
        Assert.Empty(o.Validar());
        Assert.False(new OpcoesI60().Habilitada);
    }

    [Theory]
    [InlineData("Identificador", "")]
    [InlineData("Identificador", "WEBAPI I51")]
    [InlineData("IntervaloMinutos", "0")]
    [InlineData("IntervaloMinutos", "1441")]
    [InlineData("AtrasoInicialSegundos", "4")]
    [InlineData("HorasParaAtraso", "0")]
    [InlineData("IntervaloUnicoMinutos", "4")]
    [InlineData("MaxUnicoPorCiclo", "201")]
    [InlineData("DiasRetencaoEstado", "6")]
    [InlineData("MaxTentativasProcessarBruto", "0")]
    [InlineData("DiasMaxTodos", "366")]
    [InlineData("CampoUnico", "Serie")]
    [InlineData("ItemSemRetorno", "Apagar")]
    [InlineData("CnpjConsulta", "123")]
    public void Validar_RejeitaValoresForaDaFaixa(string campo, string valor)
    {
        var o = new OpcoesI51();
        var prop = typeof(OpcoesI51).GetProperty(campo)!;
        prop.SetValue(o, prop.PropertyType == typeof(int) ? int.Parse(valor) : valor);
        Assert.NotEmpty(o.Validar());
    }

    [Fact]
    public void Validar_ChaveNeutraDeveTerVazioOu44Digitos()
    {
        var o = new OpcoesI51();
        o.ValoresNeutros.ChaveNFe = "123";
        Assert.NotEmpty(o.Validar());
        o.ValoresNeutros.ChaveNFe = new string('1', 44);
        Assert.Empty(o.Validar());
    }
}

public class OpcoesI60ValidarTests
{
    [Fact]
    public void Padroes_PreservamOComportamentoAtual()
    {
        var o = new OpcoesI60();
        Assert.Equal("INE", o.TipoOperacaoNota);
        Assert.Equal("IMP", o.TipoOperacaoEncerrado);
        Assert.Equal("UPD", o.TpAcaoReenvioImp);
        Assert.Equal("SomaCaixas", o.QtdeVolumeOrigem);
        Assert.Empty(o.Validar());
    }

    [Theory]
    [InlineData("TpAcaoReenvioImp", "XYZ")]
    [InlineData("QtdeVolumeOrigem", "Foo")]
    [InlineData("TipoOperacaoEncerrado", "")]
    [InlineData("TipoOperacaoNota", "IMBX")]
    [InlineData("PadroesDocCargaEncerrado", "")]
    public void Validar_RejeitaValoresInvalidos(string campo, string valor)
    {
        var o = new OpcoesI60();
        typeof(OpcoesI60).GetProperty(campo)!.SetValue(o, valor);
        Assert.NotEmpty(o.Validar());
    }

    [Theory]
    [InlineData("BackoffMaxMinutos", 0)]
    [InlineData("MaxTentativasOutro", 0)]
    [InlineData("MaxFalhasComunicacaoAlerta", 0)]
    public void Validar_RejeitaInteirosForaDaFaixa(string campo, int valor)
    {
        var o = new OpcoesI60();
        typeof(OpcoesI60).GetProperty(campo)!.SetValue(o, valor);
        Assert.NotEmpty(o.Validar());
    }

    [Fact]
    public void Validar_ListaVaziaDeEncerradoSoImportaComImpLigado()
    {
        var o = new OpcoesI60 { PadroesDocCargaEncerrado = string.Empty, ReenviarEncerradoComoImp = false };
        Assert.Empty(o.Validar());
    }
}

public class OpcoesRelatorioI60Tests
{
    [Fact]
    public void Padroes()
    {
        var o = new OpcoesRelatorioI60();
        Assert.Equal(new TimeOnly(12, 0), o.HoraDoDia);
        Assert.Empty(o.Validar());
    }

    [Fact]
    public void HoraInvalida_CaiPara12MasEReportada()
    {
        var o = new OpcoesRelatorioI60 { Hora = "25:99" };
        Assert.Equal(new TimeOnly(12, 0), o.HoraDoDia);
        Assert.NotEmpty(o.Validar());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(5001)]
    public void MaxLinhasForaDaFaixa(int max)
    {
        Assert.NotEmpty(new OpcoesRelatorioI60 { MaxLinhasPorSecao = max }.Validar());
    }

    [Fact]
    public void AssuntoComQuebraDeLinha_Reprovado()
    {
        Assert.NotEmpty(new OpcoesRelatorioI60 { AssuntoPrefixo = "a\nb" }.Validar());
    }
}

public class TextoNormalizadoTests
{
    [Theory]
    [InlineData("Doccarga ENCERRADO no WMS", "encerrad")]
    [InlineData("Doccarga Encerrádo", "encerrad")]
    [InlineData("Quantidade não confere com a quantidade da nota", "nao confere com a quantidade")]
    [InlineData("Quantidade nao confere com a quantidade", "não confere com a quantidade")]
    [InlineData("Nota [1] já   existe,   TpAcao precisa ser UPD!", "tpacao precisa ser upd")]
    public void Casa_IgnoraAcentoCaixaEEspacos(string texto, string padrao)
    {
        Assert.True(TextoNormalizado.Casa(texto, new[] { padrao }));
    }

    [Fact]
    public void PadraoVazio_NuncaCasa()
    {
        Assert.False(TextoNormalizado.Casa("qualquer coisa", new[] { string.Empty, "  " }));
        Assert.False(TextoNormalizado.Casa(string.Empty, new[] { "x" }));
    }

    [Fact]
    public void Lista_SeparaPorPontoEVirgula()
    {
        Assert.Equal(new[] { "a", "b c" }, TextoNormalizado.Lista(" a ; b c ;; "));
        Assert.Empty(TextoNormalizado.Lista(null));
    }
}

public class ParserRessalvasTests
{
    [Fact]
    public void Extrai_VariosBlocos()
    {
        var r = ParserRessalvas.Extrair("Foi importado e esta pendente na interface. Ressalvas : {Nota [x] já existe, TpAcao precisa ser UPD!}{Doccarga encerrado}");
        Assert.Equal(2, r.Count);
        Assert.Contains("TpAcao precisa ser UPD", r[0]);
        Assert.Equal("Doccarga encerrado", r[1]);
    }

    [Fact]
    public void SemMarcador_ListaVazia()
    {
        Assert.Empty(ParserRessalvas.Extrair("Registrado com sucesso."));
        Assert.Empty(ParserRessalvas.Extrair(null));
    }

    [Fact]
    public void SemChaves_UsaOTextoRestante()
    {
        var r = ParserRessalvas.Extrair("pendente. Ressalvas : doccarga encerrado");
        Assert.Equal("doccarga encerrado", Assert.Single(r));
    }
}
