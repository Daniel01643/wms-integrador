using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public sealed class GerenciadorPacoteTests : IDisposable
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

    private sealed class GeradorQuebravel : IGeradorRelatorio
    {
        private readonly GeradorRelatorio _real;

        public GeradorQuebravel(string pasta) =>
            _real = new GeradorRelatorio(Options.Create(new OpcoesRelatorio { PastaSaida = pasta }));

        public bool Quebrar { get; set; }

        public string GerarHtml(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm) =>
            Quebrar ? throw new IOException("sem espaço") : _real.GerarHtml(linhas, geradoEm);

        public byte[] GerarPdf(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm) =>
            _real.GerarPdf(linhas, geradoEm);

        public (string CaminhoHtml, string CaminhoPdf) GerarArquivos(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm) =>
            _real.GerarArquivos(linhas, geradoEm);
    }

    [Fact]
    public async Task Estados_OciosoColetandoProcessandoFechandoOcioso()
    {
        var g = _amb.CriarGerenciador(_orq);
        Assert.Equal(EstadoPacote.Ocioso, await Ciclo(g));
        Assert.Null(g.PacoteAtual);

        _amb.EscreverXml("a.xml", "100", "70001");

        Assert.Equal(EstadoPacote.Coletando, await Ciclo(g));
        Assert.NotNull(g.PacoteAtual);
        Assert.Equal(EstadoPacote.Coletando, await Ciclo(g, 5)); // janela (10s) ainda aberta
        Assert.Equal(EstadoPacote.Processando, await Ciclo(g, 10));
        Assert.Equal(1, g.PacoteAtual!.Notas);
        Assert.Empty(Directory.GetFiles(_amb.Entrada, "*.xml", SearchOption.TopDirectoryOnly));
        Assert.Single(_amb.Arquivos("Processados"));
        Assert.Equal(EstadoPacote.Processando, await Ciclo(g, 10)); // quietude 10s < 30s
        Assert.Equal(EstadoPacote.Processando, await Ciclo(g, 10));
        Assert.Equal(EstadoPacote.Ocioso, await Ciclo(g, 10));      // 30s de quietude
        Assert.Null(g.PacoteAtual);
        Assert.Single(_amb.RelatoriosGerados(".pdf"));
        Assert.Single(_amb.RelatoriosGerados(".html"));
    }

    [Fact]
    public async Task Debounce_ArquivoNovoDuranteQuietude_MantemPacoteAberto()
    {
        var g = _amb.CriarGerenciador(_orq);
        _amb.EscreverXml("a.xml", "100", "70001");
        await Ciclo(g);
        await Ciclo(g, 10); // processa a.xml
        await Ciclo(g, 10);
        await Ciclo(g, 10); // t=30: quietude em 20s

        _amb.EscreverXml("b.xml", "101", "70001");
        Assert.Equal(EstadoPacote.Processando, await Ciclo(g, 10)); // t=40, b novo (atividade reinicia)
        Assert.Equal(EstadoPacote.Processando, await Ciclo(g, 10)); // processa b
        Assert.Empty(_amb.RelatoriosGerados(".pdf"));

        await _amb.RodarAteOciosoAsync(g);

        Assert.Single(_amb.RelatoriosGerados(".pdf"));
        Assert.Equal(2, _orq.Chamadas.Count);
        Assert.Equal(2, _amb.Arquivos("Processados").Length);
        var html = File.ReadAllText(_amb.RelatoriosGerados(".html").Single());
        Assert.Contains("100", html);
        Assert.Contains("101", html);
    }

    [Fact]
    public async Task ArquivoAposFechar_AbreNovoPacote()
    {
        var g = _amb.CriarGerenciador(_orq);
        _amb.EscreverXml("a.xml", "100", "70001");
        await _amb.RodarAteOciosoAsync(g);

        _amb.EscreverXml("b.xml", "101", "70001");
        Assert.Equal(EstadoPacote.Coletando, await Ciclo(g, 1));
        await _amb.RodarAteOciosoAsync(g);

        Assert.Equal(2, _amb.RelatoriosGerados(".pdf").Length);
        Assert.Equal(2, _orq.Chamadas.Count);
    }

    [Fact]
    public async Task Retomada_DiarioPreExistente_NaoReenviaERelatorioTemAmbos()
    {
        var diario = new DiarioPacote(Options.Create(_amb.Pastas), Microsoft.Extensions.Logging.Abstractions.NullLogger<DiarioPacote>.Instance);
        diario.Registrar(new RegistroDiario(
            "antigo.xml", false, "999", "T9", _amb.Clock.UtcNow.AddMinutes(-5), "Registrado com sucesso.", true, 1,
            StatusLinhaRelatorio.Integrada, "", "", Array.Empty<string>(), Array.Empty<string>(), null,
            DestinoArquivo.Processados));
        _amb.EscreverXml("restante.xml", "100", "70001");

        var g = _amb.CriarGerenciador(_orq);
        await _amb.RodarAteOciosoAsync(g);

        Assert.Equal(("100", "70001"), _orq.Chamadas.Single());
        var html = File.ReadAllText(_amb.RelatoriosGerados(".html").Single());
        Assert.Contains("999", html);
        Assert.Contains("100", html);
        Assert.False(File.Exists(diario.Caminho));
    }

    [Fact]
    public async Task RetomadaSemArquivos_FechaOPacoteDoDiario()
    {
        var diario = new DiarioPacote(Options.Create(_amb.Pastas), Microsoft.Extensions.Logging.Abstractions.NullLogger<DiarioPacote>.Instance);
        diario.Registrar(new RegistroDiario(
            "antigo.xml", false, "999", "T9", _amb.Clock.UtcNow, "x", false, 1,
            StatusLinhaRelatorio.NaoIntegrada, "", "", Array.Empty<string>(), Array.Empty<string>(),
            CategoriaErro.Outro, DestinoArquivo.Erro));

        var g = _amb.CriarGerenciador(_orq);
        await _amb.RodarAteOciosoAsync(g);

        Assert.Single(_amb.RelatoriosGerados(".pdf"));
        Assert.Single(_amb.Email.Enviadas); // não integrada => ComProblema
        Assert.Empty(_orq.Chamadas);
    }

    [Fact]
    public async Task TudoIntegrado_GeraPdfMasNaoEnviaEmailNoPadrao()
    {
        var g = _amb.CriarGerenciador(_orq);
        _amb.EscreverXml("a.xml", "100", "70001");

        await _amb.RodarAteOciosoAsync(g);

        Assert.Single(_amb.RelatoriosGerados(".pdf"));
        Assert.Empty(_amb.Email.Enviadas);
    }

    [Theory]
    [InlineData(StatusEnvioEmail.NaoConfigurado)]
    [InlineData(StatusEnvioEmail.Falhou)]
    public async Task EmailNaoConfiguradoOuFalhando_NaoImpedeFechamento(StatusEnvioEmail status)
    {
        _amb.Email.Resultado = new ResultadoEnvioEmail(status, "x");
        _orq.Comportamento = (n, t) => Task.FromResult(OrquestradorFalso.Falha(n.Numero, t, "erro qualquer"));
        var g = _amb.CriarGerenciador(_orq);
        _amb.EscreverXml("a.xml", "100", "70001");

        await _amb.RodarAteOciosoAsync(g);

        Assert.Equal(EstadoPacote.Ocioso, g.Estado);
        Assert.Single(_amb.Email.Enviadas);
        Assert.Single(_amb.RelatoriosGerados(".pdf"));
    }

    [Fact]
    public async Task FalhaNoRelatorio_MantemFechandoEDiario_ETentaDeNovo()
    {
        var gerador = new GeradorQuebravel(_amb.Relatorios) { Quebrar = true };
        _amb.GeradorSubstituto = gerador;
        var g = _amb.CriarGerenciador(_orq);
        _amb.EscreverXml("a.xml", "100", "70001");

        for (var i = 0; i < 8; i++)
        {
            await Ciclo(g, i == 0 ? 0 : 10);
        }

        Assert.Equal(EstadoPacote.Fechando, g.Estado);
        Assert.True(File.Exists(Path.Combine(_amb.Pastas.CaminhoEstado, DiarioPacote.NomeArquivo)));
        Assert.Empty(_amb.RelatoriosGerados(".pdf"));

        gerador.Quebrar = false;
        Assert.Equal(EstadoPacote.Ocioso, await Ciclo(g, 10));
        Assert.Single(_amb.RelatoriosGerados(".pdf"));
        Assert.False(File.Exists(Path.Combine(_amb.Pastas.CaminhoEstado, DiarioPacote.NomeArquivo)));
    }

    [Fact]
    public async Task ArquivoTravadoAcimaDoTimeout_NaoBloqueiaFechamento()
    {
        _amb.Pacote.TimeoutArquivoSegundos = 40;
        var g = _amb.CriarGerenciador(_orq);
        _amb.EscreverXml("a.xml", "100", "70001");
        var travado = _amb.EscreverXml("travado.xml", "101", "70001");
        using var trava = new FileStream(travado, FileMode.Open, FileAccess.Read, FileShare.None);

        await _amb.RodarAteOciosoAsync(g);

        Assert.True(File.Exists(travado)); // continua na entrada para o próximo pacote
        Assert.Single(_orq.Chamadas);
        Assert.Single(_amb.RelatoriosGerados(".pdf"));
    }

    [Fact]
    public async Task CancelamentoNoCiclo_Propaga()
    {
        var g = _amb.CriarGerenciador(_orq);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => g.CicloAsync(cts.Token));
    }
}
