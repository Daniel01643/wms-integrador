using KitWmsIntegrador.Core.Devolucao;

namespace KitWmsIntegrador.Tests;

public sealed class LocalizadorXmlDevolucaoTests : IDisposable
{
    // Chave real dos XMLs de teste (DV válido).
    private const string Chave = "35251201838723009850550060156658071604491612";

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "loc-dev-" + Guid.NewGuid().ToString("N"));
    private readonly string _processados;

    public LocalizadorXmlDevolucaoTests()
    {
        _processados = Path.Combine(_raiz, "Processados");
        Directory.CreateDirectory(_processados);
    }

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    private string Criar(string subpasta, string nome, DateTime escritaUtc)
    {
        var pasta = Path.Combine(_raiz, subpasta);
        Directory.CreateDirectory(pasta);
        var caminho = Path.Combine(pasta, nome);
        File.WriteAllText(caminho, "<x/>");
        File.SetLastWriteTimeUtc(caminho, escritaUtc);
        return caminho;
    }

    [Fact]
    public void ChaveDeTeste_TemDigitoValido()
    {
        Assert.Null(LocalizadorXmlDevolucao.ValidarChave(Chave));
    }

    [Fact]
    public void Localizar_AchaEmPacote_SemDiferenciarMaiusculas()
    {
        var esperado = Criar(@"Processados\pacote_20251201_100000", $"nfe{Chave}.xml", DateTime.UtcNow);

        var achado = new LocalizadorXmlDevolucao(_processados).Localizar(Chave);

        Assert.Equal(esperado, achado);
    }

    [Fact]
    public void Localizar_AchaDiretamenteEmProcessados()
    {
        var esperado = Criar("Processados", $"NFE{Chave}.XML", DateTime.UtcNow);

        Assert.Equal(esperado, new LocalizadorXmlDevolucao(_processados).Localizar(Chave));
    }

    [Fact]
    public void Localizar_PrefereOMaisRecentePorLastWriteTimeUtc()
    {
        var agora = DateTime.UtcNow;
        Criar(@"Processados\pacote_20251201_100000", $"NFE{Chave}.XML", agora.AddDays(-10));
        var recente = Criar(@"Processados\pacote_20251205_100000", $"NFE{Chave}.XML", agora.AddDays(-1));
        Criar(@"Processados\pacote_20251202_100000", $"NFE{Chave}.XML", agora.AddDays(-5));

        Assert.Equal(recente, new LocalizadorXmlDevolucao(_processados).Localizar(Chave));
    }

    [Fact]
    public void Localizar_IgnoraPastaErro()
    {
        Criar(@"Erro\ErroWms", $"NFE{Chave}.XML", DateTime.UtcNow);

        Assert.Null(new LocalizadorXmlDevolucao(_processados).Localizar(Chave));
    }

    [Fact]
    public void Localizar_IgnoraOutrasChavesEPastasNaoPacote()
    {
        Criar(@"Processados\pacote_20251201_100000", "NFE35251201838723009850550060156663211093034056.XML", DateTime.UtcNow);
        Criar(@"Processados\outra", $"NFE{Chave}.XML", DateTime.UtcNow);

        Assert.Null(new LocalizadorXmlDevolucao(_processados).Localizar(Chave));
    }

    [Fact]
    public void Localizar_PastaInexistente_RetornaNull()
    {
        var loc = new LocalizadorXmlDevolucao(Path.Combine(_raiz, "naoexiste"));

        Assert.Null(loc.Localizar(Chave));
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("3525120183872300985055006015665807160449161X")]
    [InlineData("352512018387230098505500601566580716044916123")]
    public void Localizar_ChaveInvalida_Lanca(string chave)
    {
        Assert.Throws<ArgumentException>(() => new LocalizadorXmlDevolucao(_processados).Localizar(chave));
        Assert.NotNull(LocalizadorXmlDevolucao.ValidarChave(chave));
    }

    [Fact]
    public void Localizar_DigitoVerificadorErrado_Lanca()
    {
        var errada = Chave[..43] + "0"; // DV correto é 2

        var ex = Assert.Throws<ArgumentException>(() => new LocalizadorXmlDevolucao(_processados).Localizar(errada));

        Assert.Contains("verificador", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
