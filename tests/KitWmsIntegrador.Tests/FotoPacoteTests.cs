using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Tests.Infra;

namespace KitWmsIntegrador.Tests;

/// <summary>Foto da pasta: o pacote é o conjunto de arquivos prontos no fim da janela fixa.</summary>
public sealed class FotoPacoteTests : IDisposable
{
    private readonly AmbientePacote _amb = new();
    private readonly OrquestradorFalso _orq = new();

    public void Dispose() => _amb.Dispose();

    private async Task<EstadoPacote> Ciclo(GerenciadorPacote g, int avancarAntesSegundos = 0)
    {
        _amb.Clock.Avancar(TimeSpan.FromSeconds(avancarAntesSegundos));
        await g.CicloAsync(CancellationToken.None);
        return g.Estado;
    }

    [Fact]
    public async Task ArquivoQueChegaDuranteProcessamento_FicaParaOProximoPacote()
    {
        var gravou = false;
        _orq.Comportamento = (nota, transporte) =>
        {
            if (!gravou)
            {
                gravou = true;
                _amb.EscreverXml("novo.xml", "300", "70009"); // chega no meio do pacote 1
            }

            return Task.FromResult(OrquestradorFalso.Sucesso(nota.Numero, transporte));
        };
        var g = _amb.CriarGerenciador(_orq);
        _amb.EscreverXml("a.xml", "100", "70001");
        _amb.EscreverXml("b.xml", "101", "70001");

        await _amb.RodarAteOciosoAsync(g);

        // Pacote 1: só a foto original; o novo arquivo continua na entrada.
        Assert.Equal(new[] { "100", "101" }, _orq.Chamadas.Select(c => c.Nota).OrderBy(n => n));
        Assert.True(File.Exists(_amb.Caminho("novo.xml")));
        Assert.Single(_amb.RelatoriosGerados(".pdf"));
        Assert.Single(_amb.Email.Enviadas);

        // Pacote 2: nova foto, relatório próprio.
        await _amb.RodarAteOciosoAsync(g);

        Assert.Equal(3, _orq.Chamadas.Count);
        Assert.Contains(("300", "70009"), _orq.Chamadas);
        Assert.Equal(2, _amb.RelatoriosGerados(".pdf").Length);
        Assert.Equal(2, _amb.Email.Enviadas.Count);
        Assert.Single(_amb.Arquivos("Processados"), a => Path.GetFileName(a) == "novo.xml");
    }

    [Fact]
    public async Task UmEmailPorPacote_DoisPacotesDoisEmails()
    {
        var g = _amb.CriarGerenciador(_orq);
        _amb.EscreverXml("a.xml", "100", "70001");
        await _amb.RodarAteOciosoAsync(g);
        _amb.EscreverXml("b.xml", "101", "70002");
        await Ciclo(g, 1);
        await _amb.RodarAteOciosoAsync(g);

        Assert.Equal(2, _amb.RelatoriosGerados(".pdf").Length);
        Assert.Equal(2, _amb.RelatoriosGerados(".html").Length);
        Assert.Equal(2, _amb.Email.Enviadas.Count);
        foreach (var email in _amb.Email.Enviadas)
        {
            Assert.Single(email.Anexos);
        }
    }

    [Fact]
    public async Task NotasSaoProcessadasSequencialmentePorTransporte()
    {
        _amb.Pacote.MaxTransportesConcorrentes = 1;
        var g = _amb.CriarGerenciador(_orq);
        // Chegada intercalada A, B, A, B (70001=A, 70002=B).
        foreach (var (arq, nota, transp) in new[] { ("1.xml", "1", "70001"), ("2.xml", "2", "70002"), ("3.xml", "3", "70001"), ("4.xml", "4", "70002") })
        {
            var caminho = _amb.EscreverXml(arq, nota, transp);
            File.SetCreationTimeUtc(caminho, DateTime.UtcNow.AddMinutes(-10 + int.Parse(nota)));
        }

        await _amb.RodarAteOciosoAsync(g);

        Assert.Equal(new[] { "70001", "70001", "70002", "70002" }, _orq.Chamadas.Select(c => c.Transporte));
        Assert.Equal(1, _orq.MaximoSimultaneo);
        Assert.Single(_amb.Email.Enviadas);
    }

    [Fact]
    public async Task JanelaFixa_ArquivoNoMeioDaJanelaEntraNaFotoENaoAdiaOFechamento()
    {
        var g = _amb.CriarGerenciador(_orq);
        _amb.EscreverXml("a.xml", "100", "70001");
        Assert.Equal(EstadoPacote.Coletando, await Ciclo(g));        // t=0: janela abre

        _amb.EscreverXml("b.xml", "101", "70001");
        Assert.Equal(EstadoPacote.Coletando, await Ciclo(g, 5));     // t=5: b chega (janela NÃO reinicia)

        // t=10: fim da janela fixa (numa janela deslizante ainda faltariam 5 s).
        Assert.Equal(EstadoPacote.Ocioso, await Ciclo(g, 5));

        Assert.Equal(2, _orq.Chamadas.Count);
        Assert.Single(_amb.RelatoriosGerados(".pdf"));
    }

    [Fact]
    public async Task ChegadaContinua_NaoPerdeNemDuplicaNotas()
    {
        var g = _amb.CriarGerenciador(_orq);
        var proximo = 0;
        for (var ciclo = 0; ciclo < 40; ciclo++)
        {
            if (ciclo < 20)
            {
                proximo++;
                _amb.EscreverXml($"n{proximo}.xml", proximo.ToString(), "70001");
            }

            await Ciclo(g, 4);
        }

        await _amb.RodarAteOciosoAsync(g);

        Assert.Equal(20, _orq.Chamadas.Count);
        Assert.Equal(20, _orq.Chamadas.Select(c => c.Nota).Distinct().Count());
        Assert.Equal(20, _amb.Arquivos("Processados").Length);
        Assert.Empty(Directory.GetFiles(_amb.Entrada, "*.xml"));
        Assert.True(_amb.Email.Enviadas.Count >= 2);
        Assert.Equal(_amb.RelatoriosGerados(".pdf").Length, _amb.Email.Enviadas.Count);
    }
}
