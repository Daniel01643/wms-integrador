using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Tests.Infra;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Janela de silêncio da coleta do pacote: fecha quando passa Pacote:SilencioSegundos sem arquivo novo
/// (nem alteração de um existente), com teto em Pacote:JanelaMaximaSegundos. Relógio falso, ciclos de 5 s.
/// </summary>
public sealed class JanelaSilencioTests : IDisposable
{
    private const int Passo = 5;

    private readonly AmbientePacote _amb = new();
    private readonly OrquestradorFalso _orq = new();

    public JanelaSilencioTests()
    {
        _amb.Pacote.ModoJanela = "Silencio";
        _amb.Pacote.SilencioSegundos = 15;
        _amb.Pacote.JanelaMaximaSegundos = 180;
        _amb.Pacote.EstabilizacaoSegundos = 3;
    }

    public void Dispose() => _amb.Dispose();

    private async Task Ciclo(GerenciadorPacote g)
    {
        await g.CicloAsync(CancellationToken.None);
    }

    private void Avancar(int segundos = Passo) => _amb.Clock.Avancar(TimeSpan.FromSeconds(segundos));

    private string XmlEm(string subpasta, int numero, string transporte = "1000000001")
    {
        var pasta = string.IsNullOrEmpty(subpasta) ? _amb.Entrada : _amb.Pasta(subpasta);
        var caminho = Path.Combine(pasta, AmbientePacote.NomeXmlBrf(numero));
        File.WriteAllText(caminho, AmbientePacote.XmlBrf(numero, transporte));
        return caminho;
    }

    /// <summary>Roda ciclos de 5 s até Ocioso; devolve os segundos decorridos desde <paramref name="inicio"/>.</summary>
    private async Task<double> AteOciosoAsync(GerenciadorPacote g, DateTimeOffset inicio, int maxCiclos = 100)
    {
        for (var i = 0; i < maxCiclos; i++)
        {
            await Ciclo(g);
            if (g.Estado == EstadoPacote.Ocioso)
            {
                return (_amb.Clock.UtcNow - inicio).TotalSeconds;
            }

            Avancar();
        }

        throw new InvalidOperationException("O pacote não voltou a Ocioso.");
    }

    [Fact]
    public async Task ChegadaContinuaPor25s_GeraUmPacote()
    {
        var g = _amb.CriarGerenciador(_orq);
        var inicio = _amb.Clock.UtcNow;

        // Um arquivo novo a cada 5 s, de 0 a 25 s (6 arquivos): nunca há 15 s de silêncio antes do fim.
        for (var n = 0; n <= 5; n++)
        {
            XmlEm("Puxada", 100 + n);
            await Ciclo(g);
            Assert.Equal(EstadoPacote.Coletando, g.Estado);
            Avancar();
        }

        var duracao = await AteOciosoAsync(g, inicio);

        Assert.Equal(6, _orq.Chamadas.Count);
        Assert.Single(_amb.RelatoriosGerados(".pdf"));
        Assert.Single(_amb.Email.Enviadas);
        Assert.Equal(40, duracao); // último arquivo em 25 s + 15 s de silêncio
    }

    [Fact]
    public async Task PausaMaiorQueOSilencio_GeraDoisPacotes()
    {
        var g = _amb.CriarGerenciador(_orq);
        var inicio = _amb.Clock.UtcNow;

        XmlEm("Puxada", 200);
        await AteOciosoAsync(g, inicio);
        Assert.Single(_amb.RelatoriosGerados(".pdf"));

        Avancar(20); // pausa maior que o silêncio
        XmlEm("Puxada", 201);
        await AteOciosoAsync(g, _amb.Clock.UtcNow);

        Assert.Equal(2, _amb.RelatoriosGerados(".pdf").Length);
        Assert.Equal(2, _amb.Email.Enviadas.Count);
        Assert.Equal(2, _orq.Chamadas.Count);
    }

    [Fact]
    public async Task ChegadaContinuaAlemDoTeto_FechaNoTeto()
    {
        _amb.Pacote.JanelaMaximaSegundos = 60;
        var g = _amb.CriarGerenciador(_orq);
        var inicio = _amb.Clock.UtcNow;

        // Um arquivo novo a cada 5 s por 100 s: o silêncio nunca é atingido, só o teto de 60 s.
        var fechouEm = -1.0;
        for (var n = 0; n <= 20; n++)
        {
            XmlEm("Puxada", 300 + n);
            await Ciclo(g);
            if (g.Estado == EstadoPacote.Ocioso)
            {
                fechouEm = (_amb.Clock.UtcNow - inicio).TotalSeconds;
                break;
            }

            Avancar();
        }

        Assert.Equal(60, fechouEm);
        Assert.Equal(12, _orq.Chamadas.Count); // arquivos de 0 a 55 s; o de 60 s (recém-chegado) fica para o próximo
        Assert.Single(Directory.GetFiles(Path.Combine(_amb.Entrada, "Puxada"), "*.XML"));
    }

    [Fact]
    public async Task ArquivoCrescendo_AdiaOFechamento()
    {
        var g = _amb.CriarGerenciador(_orq);
        var inicio = _amb.Clock.UtcNow;
        var caminho = XmlEm("Puxada", 400);
        var conteudoFinal = File.ReadAllText(caminho);
        File.WriteAllText(caminho, conteudoFinal[..(conteudoFinal.Length / 2)]);

        // Cópia lenta: o tamanho muda a cada 5 s até 30 s. Nada de arquivo novo, só crescimento.
        for (var t = 0; t <= 30; t += Passo)
        {
            await Ciclo(g);
            Assert.Equal(EstadoPacote.Coletando, g.Estado);
            File.WriteAllText(caminho, conteudoFinal[..Math.Min(conteudoFinal.Length, conteudoFinal.Length / 2 + t + 1)]);
            Avancar();
        }

        File.WriteAllText(caminho, conteudoFinal); // última alteração (em 35 s)
        await Ciclo(g);
        Avancar(10);
        await Ciclo(g);
        Assert.Equal(EstadoPacote.Coletando, g.Estado); // só 10 s de silêncio

        Avancar(5);
        var duracao = await AteOciosoAsync(g, inicio);

        Assert.True(duracao >= 50, "fechou só depois do silêncio contado da última alteração");
        Assert.Single(_orq.Chamadas);
    }

    [Fact]
    public async Task LoteUnicoPequeno_FechaEmAproximadamenteOSilencio()
    {
        var g = _amb.CriarGerenciador(_orq);
        var inicio = _amb.Clock.UtcNow;
        XmlEm("", 500);
        XmlEm("", 501);
        XmlEm("", 502, "1000000002");

        var duracao = await AteOciosoAsync(g, inicio);

        Assert.Equal(15, duracao); // nem 10 s fixos, nem o teto de 180 s
        Assert.Equal(3, _orq.Chamadas.Count);
        Assert.Single(_amb.RelatoriosGerados(".pdf"));
    }

    [Fact]
    public async Task ModoFixa_ReproduzOComportamentoAntigo()
    {
        _amb.Pacote.ModoJanela = "Fixa";
        _amb.Pacote.JanelaColetaSegundos = 10;
        var g = _amb.CriarGerenciador(_orq);
        var inicio = _amb.Clock.UtcNow;

        // Um arquivo a cada 5 s por 25 s: a janela fixa de 10 s corta o lote em vários pacotes.
        var fechamentos = 0;
        var primeiroFechamento = -1.0;
        for (var n = 0; n <= 5; n++)
        {
            XmlEm("Puxada", 600 + n);
            await Ciclo(g);
            if (g.Estado == EstadoPacote.Ocioso && _amb.RelatoriosGerados(".pdf").Length > fechamentos)
            {
                fechamentos++;
                if (primeiroFechamento < 0)
                {
                    primeiroFechamento = (_amb.Clock.UtcNow - inicio).TotalSeconds;
                }
            }

            Avancar();
        }

        Assert.Equal(10, primeiroFechamento);
        Assert.True(fechamentos >= 2, "janela fixa divide a chegada contínua em mais de um pacote");
    }

    [Fact]
    public async Task Puxada_ZipsChegandoComIntervalo_UmPacote()
    {
        var g = _amb.CriarGerenciador(_orq);
        var inicio = _amb.Clock.UtcNow;

        _amb.EscreverZipBrf("Puxada", "Lote1.zip", 700, "1000000010", "1000000011");
        await Ciclo(g);
        Avancar(10);
        _amb.EscreverZipBrf("Puxada", "Lote2.zip", 710, "1000000012");
        await AteOciosoAsync(g, inicio);

        Assert.Single(_amb.RelatoriosGerados(".pdf"));
        Assert.Equal(3, _orq.Chamadas.Count);
    }

    [Fact]
    public async Task PuxadaTsp_XmlSolto_UmaOndaMesmoComChegadaEspacada()
    {
        var g = _amb.CriarGerenciador(_orq);
        var inicio = _amb.Clock.UtcNow;

        XmlEm("PuxadaTSP", 800, "1000000020");
        await Ciclo(g);
        Avancar(10);
        XmlEm("PuxadaTSP", 801, "1000000021");
        await AteOciosoAsync(g, inicio);

        Assert.Single(_amb.RelatoriosGerados(".pdf"));
        Assert.Equal(2, _orq.Chamadas.Count);
        Assert.Single(_orq.Chamadas.Select(c => c.Transporte).Distinct()); // uma onda TSP só
        Assert.All(_orq.EhTspPorNota.Values, ehTsp => Assert.True(ehTsp));
    }

    [Fact]
    public async Task Raiz_XmlsComIntervalo_UmPacote()
    {
        var g = _amb.CriarGerenciador(_orq);
        var inicio = _amb.Clock.UtcNow;

        XmlEm("", 900);
        await Ciclo(g);
        Avancar(10);
        XmlEm("", 901);
        await AteOciosoAsync(g, inicio);

        Assert.Single(_amb.RelatoriosGerados(".pdf"));
        Assert.Equal(2, _orq.Chamadas.Count);
    }

    [Fact]
    public async Task EsperaDaJanela_MedeAberturaAteAFoto()
    {
        var g = _amb.CriarGerenciador(_orq);
        var inicio = _amb.Clock.UtcNow;
        XmlEm("", 950);

        await AteOciosoAsync(g, inicio);

        // A telemetria guarda a espera (abertura até a foto) = silêncio de 15 s.
        var telemetria = Directory.GetFiles(_amb.Raiz, "telemetria*.jsonl", SearchOption.AllDirectories);
        Assert.NotEmpty(telemetria);
        Assert.Contains("\"EsperaJanelaMs\":15000", string.Concat(telemetria.Select(File.ReadAllText)));
    }

    [Fact]
    public void Opcoes_PadroesEValidacao()
    {
        var o = new OpcoesPacote();
        Assert.Equal("Silencio", o.ModoJanela);
        Assert.Equal(15, o.SilencioSegundos);
        Assert.Equal(180, o.JanelaMaximaSegundos);
        Assert.Equal(10, o.JanelaColetaSegundos);
        Assert.Equal(3, o.EstabilizacaoSegundos);
        Assert.True(o.UsaSilencio);
        Assert.Empty(o.Validar());
        Assert.Empty(o.Avisos());

        Assert.NotEmpty(new OpcoesPacote { ModoJanela = "Qualquer" }.Validar());
        Assert.NotEmpty(new OpcoesPacote { SilencioSegundos = 0 }.Validar());
        Assert.NotEmpty(new OpcoesPacote { SilencioSegundos = 30, JanelaMaximaSegundos = 20 }.Validar());
        Assert.Empty(new OpcoesPacote { ModoJanela = "fixa" }.Validar());
        Assert.False(new OpcoesPacote { ModoJanela = "fixa" }.UsaSilencio);
    }

    [Fact]
    public void SilencioEfetivo_NuncaMenorQueEstabilizacao()
    {
        var o = new OpcoesPacote { SilencioSegundos = 2, EstabilizacaoSegundos = 3, JanelaMaximaSegundos = 10 };

        Assert.Equal(3, o.SilencioEfetivoSegundos);
        Assert.Empty(o.Validar());
        Assert.Single(o.Avisos());
        Assert.Equal(15, new OpcoesPacote().SilencioEfetivoSegundos);
    }
}
