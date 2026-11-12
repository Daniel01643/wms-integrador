using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>Leitor seguro de ZIP: tudo com ZIPs/XMLs SINTÉTICOS gerados em pasta temporária.</summary>
public sealed class LeitorZipSeguroTests : IDisposable
{
    private readonly AmbientePacote _amb = new();

    public void Dispose() => _amb.Dispose();

    private LeitorZipSeguro Criar(int maxArquivos = 1000, int maxMb = 500) => new(
        Options.Create(new OpcoesZip { MaxArquivos = maxArquivos, MaxTamanhoMb = maxMb }),
        Options.Create(_amb.Pastas),
        NullLogger<LeitorZipSeguro>.Instance);

    private string PastaTmp => Path.Combine(_amb.Pastas.CaminhoEstado, "tmp");

    private void AssertTmpVazio() =>
        Assert.True(!Directory.Exists(PastaTmp) || Directory.GetFileSystemEntries(PastaTmp).Length == 0);

    [Fact]
    public void Extrai_OsXml_ComONomeOriginal_EmEstadoTmp()
    {
        var zip = _amb.EscreverZipBrf("PuxadaTSP", "XML'S SJC.zip", 1001, "1000000001", "1000000002");

        var leitor = Criar();
        var r = leitor.Extrair(zip);

        Assert.Equal(2, r.Xmls.Count);
        Assert.Empty(r.Ignoradas);
        Assert.Equal(64, r.Hash.Length);
        Assert.All(r.Xmls, x => Assert.True(File.Exists(x.Caminho)));
        Assert.Equal(AmbientePacote.NomeXmlBrf(1001), Path.GetFileName(r.Xmls[0].Caminho));
        Assert.StartsWith(PastaTmp, r.PastaTemporaria);
        Assert.True(File.Exists(zip));

        leitor.Limpar(r.PastaTemporaria);
        AssertTmpVazio();
    }

    [Fact]
    public void MesmoConteudo_MesmoHash()
    {
        var zip = _amb.EscreverZipBrf("PuxadaTSP", "a.zip", 1, "1");
        var copia = Path.Combine(_amb.Pasta("PuxadaTSP"), "b.zip");
        File.Copy(zip, copia);
        var leitor = Criar();

        var a = leitor.Extrair(zip);
        var b = leitor.Extrair(copia);

        Assert.Equal(a.Hash, b.Hash);
        leitor.Limpar(a.PastaTemporaria);
        leitor.Limpar(b.PastaTemporaria);
    }

    [Fact]
    public void ZipCorrompido_ZipInvalido_SemSobras()
    {
        var caminho = Path.Combine(_amb.Pasta("PuxadaTSP"), "quebrado.zip");
        File.WriteAllBytes(caminho, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });

        Assert.Throws<ZipInvalidoException>(() => Criar().Extrair(caminho));
        AssertTmpVazio();
    }

    [Fact]
    public void ZipTruncado_ZipInvalido()
    {
        var zip = _amb.EscreverZipBrf("PuxadaTSP", "a.zip", 1, "1", "2");
        var bytes = File.ReadAllBytes(zip);
        File.WriteAllBytes(zip, bytes.Take(bytes.Length / 2).ToArray());

        Assert.Throws<ZipInvalidoException>(() => Criar().Extrair(zip));
        AssertTmpVazio();
    }

    [Fact]
    public void ZipSemNenhumXml_ZipInvalido()
    {
        var zip = _amb.EscreverZip("PuxadaTSP", "so-txt.zip", ("leia-me.txt", "oi"), ("foto.jpg", "x"));

        var ex = Assert.Throws<ZipInvalidoException>(() => Criar().Extrair(zip));

        Assert.Contains("XML", ex.Message);
        AssertTmpVazio();
    }

    [Fact]
    public void NaoXml_EZipAninhado_SaoIgnoradosComMotivo_ONormalSegue()
    {
        var interno = _amb.EscreverZip("", "interno.zip", ("a.xml", "<x/>"));
        var zip = _amb.EscreverZip("PuxadaTSP", "misto.zip",
            (AmbientePacote.NomeXmlBrf(7), AmbientePacote.XmlBrf(7, "7")),
            ("leia-me.txt", "oi"),
            ("sub/dentro.zip", File.ReadAllText(interno)),
            ("pasta/", string.Empty));

        var leitor = Criar();
        var r = leitor.Extrair(zip);

        Assert.Single(r.Xmls);
        Assert.Equal(2, r.Ignoradas.Count);
        Assert.All(r.Ignoradas, i => Assert.False(i.Insegura));
        Assert.Contains(r.Ignoradas, i => i.Nome == "leia-me.txt" && i.Motivo.Contains(".txt"));
        Assert.Contains(r.Ignoradas, i => i.Nome == "sub/dentro.zip" && i.Motivo.Contains("ZIP dentro de ZIP"));
        leitor.Limpar(r.PastaTemporaria);
    }

    [Theory]
    [InlineData("..\\fora.xml")]
    [InlineData("../fora.xml")]
    [InlineData("a/../../fora.xml")]
    [InlineData("C:\\fora.xml")]
    [InlineData("/etc/fora.xml")]
    [InlineData("\\\\servidor\\share\\fora.xml")]
    public void CaminhoPerigoso_EhIgnorado_ENadaSaiDaPastaTemporaria(string nomePerigoso)
    {
        var zip = _amb.EscreverZip("PuxadaTSP", "zipslip.zip",
            (AmbientePacote.NomeXmlBrf(7), AmbientePacote.XmlBrf(7, "7")),
            (nomePerigoso, "<x/>"));
        var leitor = Criar();

        var r = leitor.Extrair(zip);

        Assert.Single(r.Xmls);
        var ignorada = Assert.Single(r.Ignoradas);
        Assert.True(ignorada.Insegura);
        Assert.False(File.Exists(Path.Combine(_amb.Raiz, "fora.xml")));
        Assert.False(File.Exists(Path.Combine(_amb.Entrada, "fora.xml")));
        Assert.False(File.Exists(Path.Combine(_amb.Pastas.CaminhoEstado, "fora.xml")));
        Assert.False(File.Exists(Path.Combine(PastaTmp, "fora.xml")));
        Assert.All(r.Xmls, x => Assert.StartsWith(r.PastaTemporaria, x.Caminho));
        leitor.Limpar(r.PastaTemporaria);
    }

    [Fact]
    public void LimiteDeArquivos_Estourado_ZipInvalido_ELimpa()
    {
        var zip = _amb.EscreverZipBrf("PuxadaTSP", "muitos.zip", 1, "1", "2", "3");

        var ex = Assert.Throws<ZipInvalidoException>(() => Criar(maxArquivos: 2).Extrair(zip));

        Assert.Contains("Zip:MaxArquivos", ex.Message);
        AssertTmpVazio();
        Assert.Single(Criar(maxArquivos: 3).Extrair(zip).Xmls.Take(1));
    }

    [Fact]
    public void LimiteDeTamanho_TamanhoDeclarado_ZipInvalido()
    {
        var grande = "<x>" + new string('a', 2 * 1024 * 1024) + "</x>";
        var zip = _amb.EscreverZip("PuxadaTSP", "grande.zip", ("a.xml", grande));

        var ex = Assert.Throws<ZipInvalidoException>(() => Criar(maxMb: 1).Extrair(zip));

        Assert.Contains("Zip:MaxTamanhoMb", ex.Message);
        AssertTmpVazio();
    }

    [Fact]
    public void ZipBomba_QueMenteNoCabecalho_NuncaGravaMaisQueOLimite()
    {
        var grande = "<x>" + new string('a', 3 * 1024 * 1024) + "</x>";
        var zip = _amb.EscreverZip("PuxadaTSP", "bomba.zip", ("a.xml", grande));
        // Falsifica o tamanho descompactado declarado no diretório central (offset 24 do cabeçalho 0x02014B50).
        var bytes = File.ReadAllBytes(zip);
        var achou = false;
        for (var i = 0; i < bytes.Length - 28; i++)
        {
            if (bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] == 0x01 && bytes[i + 3] == 0x02)
            {
                BitConverter.GetBytes(100u).CopyTo(bytes, i + 24);
                achou = true;
            }
        }

        Assert.True(achou);
        File.WriteAllBytes(zip, bytes);

        // O .NET já corta a leitura no tamanho declarado; a contagem de bytes copiados é a segunda
        // barreira. Em qualquer dos casos o servidor nunca grava mais que o limite configurado.
        var leitor = Criar(maxMb: 1);
        try
        {
            var r = leitor.Extrair(zip);
            Assert.All(r.Xmls, x => Assert.True(new FileInfo(x.Caminho).Length <= 1024 * 1024));
            leitor.Limpar(r.PastaTemporaria);
        }
        catch (ZipInvalidoException)
        {
            AssertTmpVazio();
        }
    }
}