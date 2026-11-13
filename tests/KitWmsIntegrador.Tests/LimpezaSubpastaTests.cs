using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Limpeza de 30 dias da Etapa 7c: a subpasta de pacote vencida é removida por INTEIRO (idade pela
/// MAIOR LastWriteTimeUtc do seu conteúdo); subpastas dentro do prazo e as pastas protegidas
/// (Entrada/Estado/Relatórios) ficam intactas; config perigosa é recusada.
/// </summary>
public sealed class LimpezaSubpastaTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "kitwms-limpeza-" + Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new(new DateTimeOffset(2025, 6, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly string _entrada;
    private readonly OpcoesPasta _pastas;

    public LimpezaSubpastaTests()
    {
        _entrada = Path.Combine(_raiz, "entrada");
        Directory.CreateDirectory(_entrada);
        _pastas = new OpcoesPasta { Entrada = _entrada };
    }

    public void Dispose()
    {
        try { Directory.Delete(_raiz, true); } catch { /* melhor esforço */ }
    }

    private LimpadorPastas Criar(OpcoesLimpeza? limpeza = null, OpcoesPasta? pastas = null) =>
        new(_clock, Options.Create(pastas ?? _pastas), Options.Create(limpeza ?? new OpcoesLimpeza()),
            NullLogger<LimpadorPastas>.Instance);

    // Cria um arquivo com data de modificação relativa a agora (negativo = passado).
    private string Arquivo(string caminhoRelativoAEntrada, double diasAtras)
    {
        var caminho = Path.Combine(_entrada, caminhoRelativoAEntrada);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, "<x/>");
        File.SetLastWriteTimeUtc(caminho, _clock.UtcNow.UtcDateTime.AddDays(-diasAtras));
        return caminho;
    }

    // (a) subpasta de pacote com tudo vencido → removida por inteiro.
    [Fact]
    public void SubpastaDePacoteVencida_ERemovidaPorInteiro()
    {
        Arquivo(Path.Combine("Processados", "pacote_antigo", "a.xml"), diasAtras: 65);
        Arquivo(Path.Combine("Processados", "pacote_antigo", "b.xml"), diasAtras: 70);

        var r = Criar().Limpar();

        Assert.Empty(r.Erros);
        Assert.False(Directory.Exists(Path.Combine(_entrada, "Processados", "pacote_antigo")));
        Assert.Equal(2, r.ArquivosRemovidos);
    }

    // (b) subpasta dentro do prazo → preservada.
    [Fact]
    public void SubpastaDentroDoPrazo_EPreservada()
    {
        Arquivo(Path.Combine("Processados", "pacote_novo", "a.xml"), diasAtras: 2);

        var r = Criar().Limpar();

        Assert.True(Directory.Exists(Path.Combine(_entrada, "Processados", "pacote_novo")));
        Assert.Equal(0, r.ArquivosRemovidos);
    }

    // (c) subpasta com mistura (um velho + um recente) → PRESERVADA (idade pelo máximo).
    [Fact]
    public void SubpastaComArquivoRecente_EPreservadaInteira()
    {
        Arquivo(Path.Combine("Processados", "pacote_misto", "velho.xml"), diasAtras: 90);
        Arquivo(Path.Combine("Processados", "pacote_misto", "recente.xml"), diasAtras: 1);

        var r = Criar().Limpar();

        Assert.True(File.Exists(Path.Combine(_entrada, "Processados", "pacote_misto", "velho.xml")));
        Assert.True(File.Exists(Path.Combine(_entrada, "Processados", "pacote_misto", "recente.xml")));
        Assert.Equal(0, r.ArquivosRemovidos);
    }

    // (d) subpasta de erro vencida → removida com a categoria aninhada.
    [Fact]
    public void SubpastaDeErroVencida_ERemovidaComCategoria()
    {
        Arquivo(Path.Combine("Erro", "pacote_antigo", "ProdutoNaoCadastrado", "a.xml"), diasAtras: 65);
        Arquivo(Path.Combine("Erro", "pacote_antigo", "ProdutoNaoCadastrado", "a.xml.motivo.txt"), diasAtras: 65);

        var r = Criar().Limpar();

        Assert.Empty(r.Erros);
        Assert.False(Directory.Exists(Path.Combine(_entrada, "Erro", "pacote_antigo")));
    }

    // (d2) subpasta de erro ExtensaoInvalida vencida → removida por inteiro (não-XML + sidecar).
    [Fact]
    public void SubpastaDeErroExtensaoInvalidaVencida_ERemovida()
    {
        Arquivo(Path.Combine("Erro", "pacote_antigo", "ExtensaoInvalida", "doc.pdf"), diasAtras: 62);
        Arquivo(Path.Combine("Erro", "pacote_antigo", "ExtensaoInvalida", "doc.pdf.motivo.txt"), diasAtras: 62);

        var r = Criar().Limpar();

        Assert.Empty(r.Erros);
        Assert.False(Directory.Exists(Path.Combine(_entrada, "Erro", "pacote_antigo")));
    }

    // (e) nunca toca Entrada/Estado/Relatórios.
    [Fact]
    public void NaoTocaEntradaEstadoNemRelatorios()
    {
        var entradaXml = Arquivo("na_entrada.xml", diasAtras: 90);
        var estadoArq = Path.Combine(_entrada, "Estado", "pacote-atual.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(estadoArq)!);
        File.WriteAllText(estadoArq, "linha");
        File.SetLastWriteTimeUtc(estadoArq, _clock.UtcNow.UtcDateTime.AddDays(-90));

        var relatorios = Path.Combine(_raiz, "relatorios");
        Directory.CreateDirectory(relatorios);
        var relatorioArq = Path.Combine(relatorios, "pacote_antigo.html");
        File.WriteAllText(relatorioArq, "<html/>");
        File.SetLastWriteTimeUtc(relatorioArq, _clock.UtcNow.UtcDateTime.AddDays(-90));

        // Também cria conteúdo vencido em Processados para comprovar que SÓ ele é limpo.
        Arquivo(Path.Combine("Processados", "pacote_antigo", "a.xml"), diasAtras: 90);

        var r = Criar().Limpar();

        Assert.Empty(r.Erros);
        Assert.True(File.Exists(entradaXml));
        Assert.True(File.Exists(estadoArq));
        Assert.True(File.Exists(relatorioArq));
        Assert.False(Directory.Exists(Path.Combine(_entrada, "Processados", "pacote_antigo")));
    }

    // (f) recusa config perigosa (Processados == Entrada).
    [Fact]
    public void RecusaConfigPerigosa()
    {
        var pastas = new OpcoesPasta { Entrada = _entrada, Processados = _entrada };
        Arquivo(Path.Combine("Processados", "pacote_antigo", "a.xml"), diasAtras: 90);

        var r = Criar(pastas: pastas).Limpar();

        Assert.NotEmpty(r.Erros);
        Assert.Equal(0, r.ArquivosRemovidos);
    }

    // (g) arquivo SOLTO antigo na raiz (layout antigo pré-7c) ainda é removido.
    [Fact]
    public void ArquivoSoltoAntigoNaRaiz_ERemovido()
    {
        Arquivo(Path.Combine("Processados", "solto.xml"), diasAtras: 90);
        Arquivo(Path.Combine("Processados", "recente_solto.xml"), diasAtras: 1);

        var r = Criar().Limpar();

        Assert.Empty(r.Erros);
        Assert.False(File.Exists(Path.Combine(_entrada, "Processados", "solto.xml")));
        Assert.True(File.Exists(Path.Combine(_entrada, "Processados", "recente_solto.xml")));
        Assert.Equal(1, r.ArquivosRemovidos);
    }

    // A raiz Processados/Erro nunca é removida.
    [Fact]
    public void RaizNuncaERemovida()
    {
        Arquivo(Path.Combine("Processados", "pacote_antigo", "a.xml"), diasAtras: 90);

        Criar().Limpar();

        Assert.True(Directory.Exists(Path.Combine(_entrada, "Processados")));
    }
}
