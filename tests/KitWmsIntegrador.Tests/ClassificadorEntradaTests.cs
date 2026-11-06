using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Pacote;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Classificação da entrada em 3 grupos: XML (integra), temporário/em-cópia (ignorado, fica na
/// entrada) e não-XML definitivo (ExtensaoInvalida, quando habilitado).
/// </summary>
public sealed class ClassificadorEntradaTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "kitwms-class-" + Guid.NewGuid().ToString("N"));

    public ClassificadorEntradaTests() => Directory.CreateDirectory(_raiz);

    public void Dispose()
    {
        try { Directory.Delete(_raiz, true); } catch { /* melhor esforço */ }
    }

    private string Criar(string nome, FileAttributes? atributos = null)
    {
        var caminho = Path.Combine(_raiz, nome);
        File.WriteAllText(caminho, "x");
        if (atributos is not null)
        {
            File.SetAttributes(caminho, atributos.Value);
        }

        return caminho;
    }

    private OpcoesPasta Opcoes(bool tratarNaoXml = true, IList<string>? temporarias = null)
    {
        var o = new OpcoesPasta { Entrada = _raiz, TratarNaoXmlComoErro = tratarNaoXml };
        if (temporarias is not null)
        {
            o.ExtensoesTemporarias = temporarias;
        }

        return o;
    }

    [Theory]
    [InlineData("nota.xml")]
    [InlineData("NOTA.XML")]
    public void Xml_VaiParaGrupoXml(string nome)
    {
        var a = Criar(nome);

        var r = ClassificadorEntrada.Classificar(new[] { a }, Opcoes());

        Assert.Equal(new[] { a }, r.XmlOuAguardando);
        Assert.Empty(r.NaoXmlDefinitivo);
    }

    [Theory]
    [InlineData("arquivo.tmp")]
    [InlineData("arquivo.part")]
    [InlineData("arquivo.crdownload")]
    public void Temporario_EIgnorado(string nome)
    {
        var a = Criar(nome);

        var r = ClassificadorEntrada.Classificar(new[] { a }, Opcoes());

        Assert.Empty(r.XmlOuAguardando);
        Assert.Empty(r.NaoXmlDefinitivo);
    }

    [Theory]
    [InlineData("doc.pdf")]
    [InlineData("nota.txt")]
    [InlineData("planilha.xlsx")]
    public void NaoXml_ViraDefinitivo_QuandoTratarHabilitado(string nome)
    {
        var a = Criar(nome);

        var r = ClassificadorEntrada.Classificar(new[] { a }, Opcoes());

        Assert.Empty(r.XmlOuAguardando);
        Assert.Equal(new[] { a }, r.NaoXmlDefinitivo);
    }

    [Fact]
    public void NaoXml_EIgnorado_QuandoTratarDesligado()
    {
        var a = Criar("doc.pdf");

        var r = ClassificadorEntrada.Classificar(new[] { a }, Opcoes(tratarNaoXml: false));

        Assert.Empty(r.XmlOuAguardando);
        Assert.Empty(r.NaoXmlDefinitivo);
    }

    [Fact]
    public void ExtensaoTemporariaCustom_NaConfig_EIgnorada()
    {
        var a = Criar("dado.xyz");

        var r = ClassificadorEntrada.Classificar(new[] { a }, Opcoes(temporarias: new List<string> { ".xyz" }));

        Assert.Empty(r.XmlOuAguardando);
        Assert.Empty(r.NaoXmlDefinitivo);
    }

    [Fact]
    public void ArquivoOculto_EIgnorado()
    {
        var a = Criar("oculto.pdf", FileAttributes.Hidden);

        var r = ClassificadorEntrada.Classificar(new[] { a }, Opcoes());

        Assert.Empty(r.XmlOuAguardando);
        Assert.Empty(r.NaoXmlDefinitivo);
    }
}
