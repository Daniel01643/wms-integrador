using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Cobre o requisito da Etapa 7c: cada pacote tem sua PRÓPRIA subpasta em Processados/Erro, com
/// o MESMO identificador do relatório, e as 3 modalidades de <see cref="OrganizacaoErro"/> ficam
/// aninhadas sob a subpasta do pacote.
/// </summary>
public sealed class SubpastaPorPacoteTests : IDisposable
{
    private readonly AmbientePacote _amb = new();
    private readonly OrquestradorFalso _orq = new();

    public void Dispose() => _amb.Dispose();

    // (i) processados vão para Processados\<id>\ (uma única subpasta de pacote).
    [Fact]
    public async Task Processados_VaoParaSubpastaUnicaDoPacote()
    {
        _amb.EscreverXml("a.xml", "100", "70001");
        _amb.EscreverXml("b.xml", "200", "70001");

        var g = _amb.CriarGerenciador(_orq);
        await _amb.RodarAteOciosoAsync(g);

        var subs = Directory.GetDirectories(Path.Combine(_amb.Entrada, "Processados"));
        var sub = Assert.Single(subs);
        Assert.StartsWith("pacote_", Path.GetFileName(sub));
        Assert.Equal(
            new[] { "a.xml", "b.xml" },
            Directory.GetFiles(sub, "*.xml").Select(Path.GetFileName).OrderBy(n => n).ToArray());
        Assert.Empty(Directory.GetFiles(_amb.Entrada, "*.xml", SearchOption.TopDirectoryOnly));
    }

    // (ii) erro nas 3 modalidades, SEMPRE aninhado sob a subpasta do pacote.
    [Theory]
    [InlineData(OrganizacaoErro.PorCategoria, "ProdutoNaoCadastrado")]
    [InlineData(OrganizacaoErro.PorArquivo, "a")]
    [InlineData(OrganizacaoErro.Plana, "")]
    public void Erro_FicaAninhadoSobASubpastaDoPacote_Nas3Modalidades(OrganizacaoErro org, string sufixo)
    {
        var raiz = _amb.Entrada;
        var arquivo = Path.Combine(raiz, "a.xml");
        File.WriteAllText(arquivo, "<x/>");
        const string id = "pacote_20250110_120000";

        var organizador = new OrganizadorArquivos(
            _amb.Clock,
            Options.Create(new OpcoesPasta { Entrada = raiz, OrganizacaoErro = org }),
            NullLogger<OrganizadorArquivos>.Instance);

        var r = organizador.Mover(arquivo, DestinoArquivo.Erro, CategoriaErro.ProdutoNaoCadastrado, "motivo", id);

        var esperado = string.IsNullOrEmpty(sufixo)
            ? Path.Combine(raiz, "Erro", id, "a.xml")
            : Path.Combine(raiz, "Erro", id, sufixo, "a.xml");
        Assert.True(r.Sucesso);
        Assert.Equal(esperado, r.Caminho);
    }

    // (iii) o nome da subpasta de pacote == nome-base do relatório do mesmo pacote.
    [Fact]
    public async Task NomeDaSubpasta_IgualAoNomeBaseDoRelatorio()
    {
        _amb.EscreverXml("a.xml", "100", "70001");

        var g = _amb.CriarGerenciador(_orq);
        await _amb.RodarAteOciosoAsync(g);

        var sub = Assert.Single(Directory.GetDirectories(Path.Combine(_amb.Entrada, "Processados")));
        var relatorio = Assert.Single(_amb.RelatoriosGerados(".html"));
        Assert.Equal(Path.GetFileNameWithoutExtension(relatorio), Path.GetFileName(sub));
    }

    // (iv) dois pacotes consecutivos geram subpastas distintas, cada uma com o nome do seu relatório.
    [Fact]
    public async Task DoisPacotes_GeramSubpastasDistintas()
    {
        _amb.EscreverXml("a.xml", "100", "70001");
        var g = _amb.CriarGerenciador(_orq);
        await _amb.RodarAteOciosoAsync(g);

        // Avança o relógio para garantir um carimbo diferente e abre o segundo pacote.
        _amb.Clock.Avancar(TimeSpan.FromSeconds(30));
        _amb.EscreverXml("b.xml", "200", "70001");
        await _amb.RodarAteOciosoAsync(g);

        var subs = Directory.GetDirectories(Path.Combine(_amb.Entrada, "Processados"))
            .Select(Path.GetFileName)
            .OrderBy(n => n)
            .ToArray();
        Assert.Equal(2, subs.Length);
        Assert.NotEqual(subs[0], subs[1]);

        var relatorios = _amb.RelatoriosGerados(".html")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(n => n)
            .ToArray();
        Assert.Equal(relatorios, subs);
    }
}
