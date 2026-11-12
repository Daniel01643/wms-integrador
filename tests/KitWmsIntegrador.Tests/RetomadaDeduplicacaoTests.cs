using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public sealed class RetomadaDeduplicacaoTests : IDisposable
{
    private readonly AmbientePacote _amb = new();
    private readonly OrquestradorFalso _orq = new();

    public void Dispose() => _amb.Dispose();

    /// <summary>Gerador que registra as linhas de cada relatório gerado.</summary>
    private sealed class GeradorEspiao : IGeradorRelatorio
    {
        private readonly GeradorRelatorio _real;

        public GeradorEspiao(string pasta) =>
            _real = new GeradorRelatorio(Options.Create(new OpcoesRelatorio { PastaSaida = pasta }));

        public List<IReadOnlyList<LinhaRelatorioErro>> Pacotes { get; } = new();

        public string GerarHtml(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm, PeriodoColeta? periodo = null, DadosPacote? pacote = null)
        {
            Pacotes.Add(linhas);
            return _real.GerarHtml(linhas, geradoEm, periodo, pacote);
        }

        public byte[] GerarPdf(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm, PeriodoColeta? periodo = null, DadosPacote? pacote = null) =>
            _real.GerarPdf(linhas, geradoEm, periodo, pacote);

        public (string CaminhoHtml, string CaminhoPdf) GerarArquivos(IReadOnlyList<LinhaRelatorioErro> linhas, DateTimeOffset geradoEm) =>
            _real.GerarArquivos(linhas, geradoEm);
    }

    private static RegistroDiario Reg(
        string arquivo, bool integrada, string mensagem, DateTimeOffset horario, DestinoArquivo destino = DestinoArquivo.Processados) =>
        new(arquivo, false, "100", "70001", horario, mensagem, integrada, 1,
            integrada ? StatusLinhaRelatorio.Integrada : StatusLinhaRelatorio.NaoIntegrada, "", "",
            Array.Empty<string>(), Array.Empty<string>(), null, destino);

    private DiarioPacote NovoDiario() =>
        new(Options.Create(_amb.Pastas), NullLogger<DiarioPacote>.Instance);

    // ---- ConsolidadorRegistros ----

    [Fact]
    public void Consolidar_MantemOIntegradoQuandoHaIntegradaEJaExistia()
    {
        var t = DateTimeOffset.UtcNow;
        var registros = new[]
        {
            Reg(@"C:\e\a.xml", false, "ja existe", t),          // 'já existia' veio primeiro no arquivo
            Reg(@"C:\E\A.XML", true, "Registrado com sucesso.", t.AddSeconds(1)),
            Reg(@"C:\e\b.xml", true, "ok", t),
        };

        var r = ConsolidadorRegistros.Consolidar(registros);

        Assert.Equal(2, r.Count);
        Assert.True(r[0].Integrada);
        Assert.Equal("Registrado com sucesso.", r[0].Mensagem);
        Assert.Equal(@"C:\e\b.xml", r[1].Arquivo);
    }

    [Fact]
    public void Consolidar_SemIntegrado_MantemOPrimeiroRegistrado()
    {
        var t = DateTimeOffset.UtcNow;
        var r = ConsolidadorRegistros.Consolidar(new[]
        {
            Reg(@"C:\e\a.xml", false, "primeiro", t),
            Reg(@"C:\e\a.xml", false, "segundo", t.AddSeconds(1)),
        });

        Assert.Equal("primeiro", Assert.Single(r).Mensagem);
    }

    [Fact]
    public void Consolidar_PrimeiroIntegradoVence()
    {
        var t = DateTimeOffset.UtcNow;
        var r = ConsolidadorRegistros.Consolidar(new[]
        {
            Reg(@"C:\e\a.xml", true, "primeiro integrado", t),
            Reg(@"C:\e\a.xml", true, "segundo integrado", t.AddSeconds(1)),
        });

        Assert.Equal("primeiro integrado", Assert.Single(r).Mensagem);
    }

    // ---- Retomada ----

    [Fact]
    public async Task Retomada_ArquivoJaRegistradoAindaNaEntrada_EMovidoSemChamarOWms()
    {
        var espiao = new GeradorEspiao(_amb.Relatorios);
        _amb.GeradorSubstituto = espiao;
        var caminho = _amb.EscreverXml("a.xml", "100", "70001");
        File.SetLastWriteTimeUtc(caminho, DateTime.UtcNow.AddHours(-1)); // gravado ANTES do registro
        var diario = NovoDiario();
        var horario = DateTimeOffset.UtcNow;
        diario.Registrar(Reg(caminho, true, "Registrado com sucesso.", horario));
        diario.Registrar(Reg(caminho, false, "Já existia no WMS (reprocesso): ja existe", horario.AddSeconds(1)));

        var g = _amb.CriarGerenciador(_orq);
        await _amb.RodarAteOciosoAsync(g);

        Assert.Empty(_orq.Chamadas);
        Assert.False(File.Exists(caminho));
        Assert.Single(_amb.Arquivos("Processados"));
        var linhas = Assert.Single(espiao.Pacotes);
        var linha = Assert.Single(linhas);
        Assert.True(linha.EstaIntegrada);
        Assert.Single(_amb.RelatoriosGerados(".pdf"));
    }

    [Fact]
    public async Task Retomada_ArquivoRegravadoDepoisDoRegistro_ETratadoComoNovo()
    {
        var espiao = new GeradorEspiao(_amb.Relatorios);
        _amb.GeradorSubstituto = espiao;
        _orq.Comportamento = (n, t) =>
            Task.FromResult(OrquestradorFalso.Falha(n.Numero, t, "WMS recusou a nota regravada"));
        var caminho = _amb.EscreverXml("a.xml", "100", "70001");
        File.SetLastWriteTimeUtc(caminho, DateTime.UtcNow); // mais novo que o registro
        NovoDiario().Registrar(Reg(caminho, true, "antigo", DateTimeOffset.UtcNow.AddHours(-1)));

        var g = _amb.CriarGerenciador(_orq);
        await _amb.RodarAteOciosoAsync(g);

        Assert.Equal(("100", "70001"), Assert.Single(_orq.Chamadas));
        Assert.False(File.Exists(caminho));
        // O resultado novo (falha) aparece no relatório; o registro antigo "integrada" não o esconde.
        var linhas = Assert.Single(espiao.Pacotes);
        var linha = Assert.Single(linhas);
        Assert.False(linha.EstaIntegrada);
    }

    [Fact]
    public async Task Retomada_DiarioDuplicadoNaoGeraLinhaDuplicadaNoRelatorio()
    {
        var espiao = new GeradorEspiao(_amb.Relatorios);
        _amb.GeradorSubstituto = espiao;
        var t = DateTimeOffset.UtcNow.AddMinutes(-5);
        var diario = NovoDiario();
        diario.Registrar(Reg(@"C:\sumiu\a.xml", false, "ja existe", t));
        diario.Registrar(Reg(@"C:\sumiu\a.xml", true, "Registrado com sucesso.", t.AddSeconds(1)));
        diario.Registrar(Reg(@"C:\sumiu\b.xml", true, "ok", t));

        var g = _amb.CriarGerenciador(_orq);
        await _amb.RodarAteOciosoAsync(g);

        var linhas = Assert.Single(espiao.Pacotes);
        Assert.Equal(2, linhas.Count);
        Assert.All(linhas, l => Assert.True(l.EstaIntegrada));
    }

    // ---- Moves pendentes ----

    [Fact]
    public async Task RepetirMovesPendentes_MoveQuandoLiberadoSemReenviarAoWms()
    {
        var processador = _amb.CriarProcessador(_orq, out _);
        var caminho = _amb.EscreverXml("a.xml", "100", "70001");

        // Leitura compartilhada sem FileShare.Delete: o parse funciona, o move falha.
        var trava = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var registros = await processador.ProcessarAsync(new[] { caminho }, "pacote_20250110_120000", CancellationToken.None);

            Assert.Single(registros);
            Assert.Single(_orq.Chamadas);
            Assert.Single(processador.MovePendentes);
            Assert.True(File.Exists(caminho));
        }
        finally
        {
            trava.Dispose();
        }

        processador.RepetirMovesPendentes();

        Assert.Empty(processador.MovePendentes);
        Assert.False(File.Exists(caminho));
        Assert.Single(_amb.Arquivos("Processados"));
        Assert.Single(_orq.Chamadas); // não reenviou
    }

    [Fact]
    public async Task MovePendente_NoGerenciador_NaoAbrePacoteNemReenvia()
    {
        var g = _amb.CriarGerenciador(_orq);
        var caminho = _amb.EscreverXml("a.xml", "100", "70001");
        var trava = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            // Com leitura compartilhada o detector não consegue abrir exclusivo: fica aguardando.
            // Libera durante o pacote para a foto incluí-lo, e prende de novo só no move.
            trava.Dispose();
            _orq.Comportamento = (n, t) =>
            {
                trava = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Task.FromResult(OrquestradorFalso.Sucesso(n.Numero, t));
            };

            await _amb.RodarAteOciosoAsync(g);
            Assert.Single(_orq.Chamadas);
            Assert.True(File.Exists(caminho));
        }
        finally
        {
            trava.Dispose();
        }

        for (var i = 0; i < 6; i++)
        {
            _amb.Clock.Avancar(TimeSpan.FromSeconds(10));
            await g.CicloAsync(CancellationToken.None);
        }

        Assert.Single(_orq.Chamadas); // nunca reenviado
        Assert.False(File.Exists(caminho)); // move repetido pelo ciclo Ocioso
        Assert.Single(_amb.Arquivos("Processados"));
        Assert.Equal(EstadoPacote.Ocioso, g.Estado);
    }
}
