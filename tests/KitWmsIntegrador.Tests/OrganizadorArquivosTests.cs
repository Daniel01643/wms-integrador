using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public sealed class OrganizadorArquivosTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "kitwms_" + Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new(new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero));

    public OrganizadorArquivosTests() => Directory.CreateDirectory(_raiz);

    public void Dispose()
    {
        try { Directory.Delete(_raiz, true); } catch { /* melhor esforço */ }
    }

    private OrganizadorArquivos Criar(OrganizacaoErro org = OrganizacaoErro.PorCategoria) =>
        new(_clock, Options.Create(new OpcoesPasta { Entrada = _raiz, OrganizacaoErro = org }),
            NullLogger<OrganizadorArquivos>.Instance);

    private string NovoArquivo(string nome = "a.xml")
    {
        var p = Path.Combine(_raiz, nome);
        File.WriteAllText(p, "<x/>");
        File.SetLastWriteTimeUtc(p, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        return p;
    }

    [Fact]
    public void MoveParaProcessadosECarimbaData()
    {
        var arq = NovoArquivo();
        var r = Criar().Mover(arq, DestinoArquivo.Processados, null, "ok");

        Assert.True(r.Sucesso);
        Assert.Equal(Path.Combine(_raiz, "Processados", "a.xml"), r.Caminho);
        Assert.False(File.Exists(arq));
        Assert.Equal(_clock.UtcNow.UtcDateTime, File.GetLastWriteTimeUtc(r.Caminho));
    }

    [Fact]
    public void MoveParaErroPorCategoriaComSidecar()
    {
        var r = Criar().Mover(NovoArquivo(), DestinoArquivo.Erro, CategoriaErro.ProdutoNaoCadastrado, "motivo X");

        Assert.True(r.Sucesso);
        Assert.Equal(Path.Combine(_raiz, "Erro", "ProdutoNaoCadastrado", "a.xml"), r.Caminho);
        Assert.Equal("motivo X", File.ReadAllText(r.Caminho + ".motivo.txt"));
    }

    [Fact]
    public void MoveParaErroPorArquivo()
    {
        var r = Criar(OrganizacaoErro.PorArquivo).Mover(NovoArquivo(), DestinoArquivo.Erro, CategoriaErro.Outro, "m");
        Assert.Equal(Path.Combine(_raiz, "Erro", "a", "a.xml"), r.Caminho);
    }

    [Fact]
    public void MoveParaErroPlana()
    {
        var r = Criar(OrganizacaoErro.Plana).Mover(NovoArquivo(), DestinoArquivo.Erro, CategoriaErro.Outro, "m");
        Assert.Equal(Path.Combine(_raiz, "Erro", "a.xml"), r.Caminho);
    }

    [Fact]
    public void ColisaoDeNomeGeraSufixo()
    {
        var sut = Criar();
        var r1 = sut.Mover(NovoArquivo(), DestinoArquivo.Processados, null, "");
        var r2 = sut.Mover(NovoArquivo(), DestinoArquivo.Processados, null, "");
        var r3 = sut.Mover(NovoArquivo(), DestinoArquivo.Processados, null, "");

        Assert.True(r1.Sucesso && r2.Sucesso && r3.Sucesso);
        Assert.EndsWith("a.xml", r1.Caminho);
        Assert.EndsWith("a_1.xml", r2.Caminho);
        Assert.EndsWith("a_2.xml", r3.Caminho);
    }

    [Fact]
    public void ArquivoTravadoNaoLancaEDevolveFalha()
    {
        var arq = NovoArquivo();
        using var trava = new FileStream(arq, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var r = Criar().Mover(arq, DestinoArquivo.Processados, null, "");

        Assert.False(r.Sucesso);
        Assert.NotNull(r.Erro);
        Assert.True(File.Exists(arq));
    }

    [Fact]
    public void ArquivoInexistenteDevolveFalha()
    {
        var r = Criar().Mover(Path.Combine(_raiz, "nao.xml"), DestinoArquivo.Processados, null, "");
        Assert.False(r.Sucesso);
    }
}
