using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;
using KitWmsIntegrador.Tests.Infra;

namespace KitWmsIntegrador.Tests;

public sealed class ProcessadorLoteTests : IDisposable
{
    private readonly AmbientePacote _amb = new();
    private readonly OrquestradorFalso _orq = new();

    public void Dispose() => _amb.Dispose();

    private const string IdPacote = "pacote_20250101_100000";

    private Task<IReadOnlyList<RegistroDiario>> Processar(ProcessadorLote p, params string[] arquivos) =>
        p.ProcessarAsync(arquivos, IdPacote, CancellationToken.None);

    [Fact]
    public async Task Sucesso_VaiParaProcessados()
    {
        var p = _amb.CriarProcessador(_orq, out _);
        var a = _amb.EscreverXml("a.xml", "100", "70001");

        var regs = await Processar(p, a);

        Assert.Single(regs);
        Assert.Single(_amb.Arquivos("Processados"));
        Assert.False(File.Exists(a));
        Assert.Equal(StatusLinhaRelatorio.Integrada, regs[0].Status);
    }

    [Fact]
    public async Task Pendente_VaiParaProcessados()
    {
        _orq.Comportamento = (n, t) => Task.FromResult(new KitWmsIntegrador.Core.Orquestrador.ResultadoIntegracaoNota(
            n.Numero, t, false, 1, KitWmsIntegrador.Core.Orquestrador.EstadoIntegracao.Pendente,
            "Foi importado e esta pendente na interface.", new[] { "x" }));
        var p = _amb.CriarProcessador(_orq, out _);

        var regs = await Processar(p, _amb.EscreverXml("a.xml", "100", "70001"));

        Assert.Single(_amb.Arquivos("Processados"));
        Assert.Equal(StatusLinhaRelatorio.Pendente, regs[0].Status);
    }

    [Fact]
    public async Task ProdutoNaoCadastrado_VaiParaErroCategoria()
    {
        _orq.Comportamento = (n, t) => Task.FromResult(OrquestradorFalso.Falha(
            n.Numero, t, "Produto Cod\\EAN: \"1\" da empresa \"2\" não ativo ou faltando ou não cadastrado!", 3));
        var p = _amb.CriarProcessador(_orq, out _);

        await Processar(p, _amb.EscreverXml("a.xml", "100", "70001"));

        var movido = Assert.Single(_amb.ArquivosErroCategoria("ProdutoNaoCadastrado"));
        Assert.EndsWith(Path.Combine("ProdutoNaoCadastrado", "a.xml"), movido);
        Assert.True(File.Exists(movido + ".motivo.txt"));
    }

    [Fact]
    public async Task XmlInvalido_VaiParaErroXmlInvalido_ESegueOsDemais()
    {
        var p = _amb.CriarProcessador(_orq, out _);
        File.WriteAllText(_amb.Caminho("ruim.xml"), "isto nao e xml");
        var ok = _amb.EscreverXml("ok.xml", "100", "70001");

        var regs = await Processar(p, _amb.Caminho("ruim.xml"), ok);

        Assert.Equal(2, regs.Count);
        Assert.Single(regs, r => r.XmlInvalido);
        Assert.Single(_amb.ArquivosErroCategoria("XmlInvalido"));
        Assert.Single(_amb.Arquivos("Processados"));
        Assert.Single(_orq.Chamadas);
    }

    [Fact]
    public async Task JaExiste_VaiParaProcessados_ComMensagemPrefixada()
    {
        _orq.Comportamento = (n, t) => Task.FromResult(OrquestradorFalso.Falha(n.Numero, t, "Registro já existe no cadastro."));
        var p = _amb.CriarProcessador(_orq, out _);

        var regs = await Processar(p, _amb.EscreverXml("a.xml", "100", "70001"));

        Assert.Single(_amb.Arquivos("Processados"));
        Assert.Empty(_amb.Arquivos("Erro"));
        Assert.StartsWith("Já existia no WMS (reprocesso)", regs[0].Mensagem);
    }

    [Fact]
    public async Task NotaSemTransporte_PassaPeloOrquestradorComTransporteVazio()
    {
        _orq.Comportamento = (n, t) => Task.FromResult(OrquestradorFalso.Falha(
            n.Numero, t, "Nota sem número de transporte (nroagrupseparacao) — não é possível montar a I50.", 0));
        var p = _amb.CriarProcessador(_orq, out _);

        await Processar(p, _amb.EscreverXml("a.xml", "100", null));

        Assert.Equal(("100", ""), _orq.Chamadas.Single());
        Assert.Single(_amb.ArquivosErroCategoria("SemTransporte"));
    }

    [Fact]
    public async Task ExcecaoEmUmaNota_NaoDerrubaAsDemais_EVaiParaErroOutro()
    {
        _orq.Comportamento = (n, t) => n.Numero == "101"
            ? throw new InvalidOperationException("boom")
            : Task.FromResult(OrquestradorFalso.Sucesso(n.Numero, t));
        var p = _amb.CriarProcessador(_orq, out _);

        var regs = await Processar(p,
            _amb.EscreverXml("a.xml", "100", "70001"),
            _amb.EscreverXml("b.xml", "101", "70001"),
            _amb.EscreverXml("c.xml", "102", "80002"));

        Assert.Equal(3, regs.Count);
        Assert.Equal(2, _amb.Arquivos("Processados").Length);
        Assert.Single(_amb.ArquivosErroCategoria("Outro"));
    }

    [Fact]
    public async Task OrdemDeChegada_CriacaoDepoisNome_EOndasContiguas()
    {
        var p = _amb.CriarProcessador(_orq, out _);
        var baseTempo = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        // Chegada: x(T2), y(T1), z(T2), w(T1). Ondas na ordem do primeiro arquivo: T2 antes de T1.
        var arquivos = new[]
        {
            (_amb.EscreverXml("x.xml", "1", "80002"), 0),
            (_amb.EscreverXml("y.xml", "2", "70001"), 1),
            (_amb.EscreverXml("z.xml", "3", "80002"), 2),
            (_amb.EscreverXml("w.xml", "4", "70001"), 3),
        };
        foreach (var (caminho, i) in arquivos)
        {
            File.SetCreationTimeUtc(caminho, baseTempo.AddMinutes(i));
        }

        await Processar(p, arquivos.Select(a => a.Item1).Reverse().ToArray());

        Assert.Equal(
            new[] { ("1", "80002"), ("3", "80002"), ("2", "70001"), ("4", "70001") },
            _orq.Chamadas.ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Concorrencia_LimitaTransportesSimultaneos(int maximo)
    {
        _amb.Pacote.MaxTransportesConcorrentes = maximo;
        _orq.Atraso = TimeSpan.FromMilliseconds(80);
        var p = _amb.CriarProcessador(_orq, out _);

        await Processar(p,
            _amb.EscreverXml("a.xml", "1", "70001"),
            _amb.EscreverXml("b.xml", "2", "80002"),
            _amb.EscreverXml("c.xml", "3", "90003"));

        Assert.Equal(maximo, _orq.MaximoSimultaneo);
        Assert.Equal(3, _amb.Arquivos("Processados").Length);
    }

    [Fact]
    public async Task FalhaAoMover_NaoReenviaAoWms_ERepeteSoOMove()
    {
        var p = _amb.CriarProcessador(_orq, out _);
        var a = _amb.EscreverXml("a.xml", "100", "70001");
        FileStream? trava = null;
        _orq.Comportamento = (n, t) =>
        {
            // Trava o arquivo depois do parse e antes do move.
            trava = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.None);
            return Task.FromResult(OrquestradorFalso.Sucesso(n.Numero, t));
        };

        await Processar(p, a);

        Assert.True(File.Exists(a));
        Assert.Equal(new[] { a }, p.MovePendentes);

        trava!.Dispose();
        var regs = await Processar(p, a);

        Assert.Empty(regs);
        Assert.Single(_orq.Chamadas);
        Assert.False(File.Exists(a));
        Assert.Single(_amb.Arquivos("Processados"));
        Assert.Empty(p.MovePendentes);
    }

    [Fact]
    public async Task Cancelamento_Propaga()
    {
        var p = _amb.CriarProcessador(_orq, out _);
        var a = _amb.EscreverXml("a.xml", "100", "70001");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => p.ProcessarAsync(new[] { a }, IdPacote, cts.Token));
        Assert.True(File.Exists(a));
    }

    [Fact]
    public async Task RegistraNoDiario_ParaCadaArquivo()
    {
        var p = _amb.CriarProcessador(_orq, out var diario);

        await Processar(p,
            _amb.EscreverXml("a.xml", "1", "70001"),
            _amb.EscreverXml("b.xml", "2", "70001"));

        Assert.Equal(2, diario.Ler().Count);
    }
}
