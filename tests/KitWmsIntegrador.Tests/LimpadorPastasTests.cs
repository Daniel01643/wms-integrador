using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public sealed class LimpadorPastasTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "kitwms_" + Guid.NewGuid().ToString("N"));
    private readonly string _entrada;
    private readonly FakeClock _clock = new(new DateTimeOffset(2025, 6, 20, 12, 0, 0, TimeSpan.Zero));

    public LimpadorPastasTests()
    {
        _entrada = Path.Combine(_raiz, "entrada");
        Directory.CreateDirectory(_entrada);
    }

    public void Dispose()
    {
        try { Directory.Delete(_raiz, true); } catch { /* melhor esforço */ }
    }

    private LimpadorPastas Criar(OpcoesPasta? pastas = null, OpcoesLimpeza? limpeza = null) =>
        new(_clock,
            Options.Create(pastas ?? new OpcoesPasta { Entrada = _entrada }),
            Options.Create(limpeza ?? new OpcoesLimpeza()),
            NullLogger<LimpadorPastas>.Instance);

    private string Arq(string relativo, double diasAtras)
    {
        var p = Path.Combine(_entrada, relativo);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "x");
        File.SetLastWriteTimeUtc(p, _clock.UtcNow.UtcDateTime.AddDays(-diasAtras));
        return p;
    }

    // Após a Etapa 7c a limpeza opera por SUBPASTA de pacote: subpasta vencida é removida por
    // inteiro; subpasta com qualquer arquivo recente é preservada (idade pelo máximo).
    [Fact]
    public void ApagaSubpastasVencidasPreservaRecentes()
    {
        var velhoP = Arq("Processados/pacote_velho/a.xml", 31);
        var novoP = Arq("Processados/pacote_novo/b.xml", 29);
        var velhoE = Arq("Erro/pacote_velho/Outro/c.xml", 31);
        var novoE = Arq("Erro/pacote_novo/Outro/d.xml", 1);

        var r = Criar().Limpar();

        Assert.Empty(r.Erros);
        Assert.Equal(2, r.ArquivosRemovidos);
        Assert.False(File.Exists(velhoP));
        Assert.False(File.Exists(velhoE));
        Assert.True(File.Exists(novoP));
        Assert.True(File.Exists(novoE));
    }

    [Fact]
    public void RemoveSubpastaVaziaMasNaoAsRaizes()
    {
        Arq("Erro/Categoria/c.xml", 90);
        Arq("Processados/a.xml", 90);

        var r = Criar().Limpar();

        Assert.Equal(1, r.PastasRemovidas);
        Assert.False(Directory.Exists(Path.Combine(_entrada, "Erro", "Categoria")));
        Assert.True(Directory.Exists(Path.Combine(_entrada, "Erro")));
        Assert.True(Directory.Exists(Path.Combine(_entrada, "Processados")));
    }

    [Fact]
    public void NaoTocaEntradaNemPastasIrmasComPrefixoIgual()
    {
        var naEntrada = Arq("nota.xml", 100);
        var irma = Arq("Erro2/x.xml", 100);
        var irma2 = Arq("ProcessadosAntigos/y.xml", 100);
        Arq("Erro/z.xml", 100);

        var r = Criar().Limpar();

        Assert.Equal(1, r.ArquivosRemovidos);
        Assert.True(File.Exists(naEntrada));
        Assert.True(File.Exists(irma));
        Assert.True(File.Exists(irma2));
    }

    [Fact]
    public void RecusaProcessadosIgualAEntrada()
    {
        var arq = Arq("nota.xml", 100);
        var r = Criar(new OpcoesPasta { Entrada = _entrada, Processados = "." }).Limpar();

        Assert.NotEmpty(r.Erros);
        Assert.Equal(0, r.ArquivosRemovidos);
        Assert.True(File.Exists(arq));
    }

    [Fact]
    public void RecusaProcessadosIgualAErro()
    {
        var arq = Arq("Erro/a.xml", 100);
        var r = Criar(new OpcoesPasta { Entrada = _entrada, Processados = "Erro" }).Limpar();

        Assert.NotEmpty(r.Erros);
        Assert.True(File.Exists(arq));
    }

    [Fact]
    public void DiasRetencao_PadraoE30()
    {
        // Plano B precisa achar o XML da venda original por até 30 dias.
        Assert.Equal(30, new OpcoesLimpeza().DiasRetencao);
    }

    [Fact]
    public void DiasRetencao_PadraoPreservaAte30DiasERemoveDepois()
    {
        var com29 = Arq("Processados/pacote_29/a.xml", 29);
        var com31 = Arq("Processados/pacote_31/a.xml", 31);

        Criar().Limpar();

        Assert.True(File.Exists(com29));
        Assert.False(File.Exists(com31));
    }

    [Fact]
    public void DiasRetencaoConfiguravel()
    {
        var arq = Arq("Processados/a.xml", 3);

        Criar(limpeza: new OpcoesLimpeza { DiasRetencao = 2 }).Limpar();

        Assert.False(File.Exists(arq));
    }

    [Fact]
    public void DesabilitadaNaoApaga()
    {
        var arq = Arq("Processados/a.xml", 100);

        Criar(limpeza: new OpcoesLimpeza { Habilitada = false }).Limpar();

        Assert.True(File.Exists(arq));
    }

    [Fact]
    public void PastasInexistentesNaoFalham()
    {
        var r = Criar().Limpar();
        Assert.Empty(r.Erros);
        Assert.Equal(0, r.ArquivosRemovidos);
    }
}
