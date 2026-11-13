using KitWmsIntegrador.Core.Devolucao;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;
using KitWmsIntegrador.Tests.Infra;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Puxada TSP (uma onda por ZIP) x PUXADA (onda = transporte) no ProcessadorLote. Todos os ZIPs/XMLs
/// são SINTÉTICOS, gerados em pasta temporária (emitente BRF, série 6, "Transporte: NNNNNNNNNN").
/// </summary>
public sealed class ProcessadorLoteTspTests : IDisposable
{
    private const string PacoteId = "pacote_20261112_100000";

    private readonly AmbientePacote _amb = new();
    private readonly OrquestradorFalso _orq = new();

    public void Dispose() => _amb.Dispose();

    private string Data => _amb.Clock.UtcNow.ToLocalTime().ToString("yyyyMMdd");

    private string SjcZip(string pasta) =>
        _amb.EscreverZipBrf(pasta, "XML'S SJC.zip", 1001, "1000000001", "1000000002");

    // 5 notas, 4 transportes (o 1000000001 se repete).
    private string SorocabaZip(string pasta) =>
        _amb.EscreverZipBrf(pasta, "XML'S SOROCABA.zip", 2001,
            "1000000001", "1000000002", "1000000003", "1000000004", "1000000001");

    private static EntradaArquivo Tsp(string caminho) => new(caminho, TipoNota.Tsp, "PuxadaTSP");

    private static EntradaArquivo Puxada(string caminho) => new(caminho, TipoNota.Puxada, "Puxada");

    private string[] Todos(string subpasta) =>
        Directory.Exists(Path.Combine(_amb.Entrada, subpasta))
            ? Directory.GetFiles(Path.Combine(_amb.Entrada, subpasta), "*", SearchOption.AllDirectories)
            : Array.Empty<string>();

    private async Task<IReadOnlyList<RegistroDiario>> Processar(params EntradaArquivo[] entradas)
    {
        var proc = _amb.CriarProcessador(_orq, out _);
        return await proc.ProcessarAsync(entradas, PacoteId, CancellationToken.None);
    }

    [Fact]
    public async Task Sjc_DuasNotasDoisTransportes_UmaOnda_UmDocCargaPorNota()
    {
        var zip = SjcZip("PuxadaTSP");

        var regs = await Processar(Tsp(zip));

        var onda = $"TSP-{Data}-01-SJC";
        Assert.Equal(2, _orq.Chamadas.Count);
        Assert.All(_orq.Chamadas, c => Assert.Equal(onda, c.Transporte));
        Assert.Equal(new[] { "1001", "1002" }, _orq.Chamadas.Select(c => c.Nota).OrderBy(n => n));
        Assert.Equal(2, regs.Count);
        Assert.All(regs, r =>
        {
            Assert.Equal("TSP", r.Tipo);
            Assert.Equal("PuxadaTSP", r.Origem);
            Assert.Equal(onda, r.Onda);
            Assert.Equal("XML'S SJC.zip", r.Zip);
            Assert.True(r.Integrada);
        });
        Assert.Equal(new[] { "1000000001", "1000000002" }, regs.Select(r => r.NumeroTransporte).OrderBy(t => t));
    }

    [Fact]
    public async Task Tsp_ZipEXmlSolto_PropagamEhTspAoOrquestrador()
    {
        var zip = SjcZip("PuxadaTSP");
        var solto = Path.Combine(_amb.Pasta("PuxadaTSP"), "solto.xml");
        File.WriteAllText(solto, AmbientePacote.XmlBrf(3001, "1000000009"));

        await Processar(Tsp(zip), Tsp(solto));

        Assert.Equal(3, _orq.EhTspPorNota.Count);
        Assert.All(_orq.EhTspPorNota, kv => Assert.True(kv.Value));
    }

    [Fact]
    public async Task Puxada_NaoMarcaEhTspNoOrquestrador()
    {
        var zip = _amb.EscreverZipBrf("Puxada", "normal.zip", 4001, "1000000001");

        await Processar(Puxada(zip));

        Assert.Single(_orq.EhTspPorNota);
        Assert.All(_orq.EhTspPorNota, kv => Assert.False(kv.Value));
    }

    [Fact]
    public async Task Sorocaba_CincoNotasQuatroTransportes_UmaOnda()
    {
        var zip = SorocabaZip("PuxadaTSP");

        var regs = await Processar(Tsp(zip));

        var onda = $"TSP-{Data}-01-SOROCABA";
        Assert.Equal(5, _orq.Chamadas.Count);
        Assert.All(_orq.Chamadas, c => Assert.Equal(onda, c.Transporte));
        Assert.Equal(5, regs.Count);
        Assert.Equal(4, regs.Select(r => r.NumeroTransporte).Distinct().Count());
        Assert.All(regs, r => Assert.Equal(onda, r.Onda));
    }

    [Fact]
    public async Task DoisZipsNoMesmoPacote_UmaOndaCadaUm_SequencialNaOrdemDeChegada()
    {
        var sjc = SjcZip("PuxadaTSP");
        var soro = SorocabaZip("PuxadaTSP");

        var regs = await Processar(Tsp(sjc), Tsp(soro));

        Assert.Equal(new[] { $"TSP-{Data}-01-SJC", $"TSP-{Data}-02-SOROCABA" }, regs.Select(r => r.Onda).Distinct().OrderBy(o => o));
        Assert.Equal(7, _orq.Chamadas.Count);
        Assert.Equal(2, _orq.Chamadas.Select(c => c.Transporte).Distinct().Count());
    }

    [Fact]
    public async Task NotaTspSemTransporte_SobeNormalmente_ENaoViraPendencia()
    {
        var zip = _amb.EscreverZipBrf("PuxadaTSP", "XML'S SJC.zip", 3001, "1000000001", null);

        var regs = await Processar(Tsp(zip));

        var onda = $"TSP-{Data}-01-SJC";
        Assert.Equal(2, _orq.Chamadas.Count);
        Assert.All(_orq.Chamadas, c => Assert.Equal(onda, c.Transporte));
        var semTransporte = regs.Single(r => r.NumeroNota == "3002");
        Assert.True(semTransporte.Integrada);
        Assert.Equal(StatusLinhaRelatorio.Integrada, semTransporte.Status);
        Assert.Equal(string.Empty, semTransporte.NumeroTransporte);
        Assert.Equal("(sem)", semTransporte.ParaLinha().NumeroTransporte);
        Assert.Equal(onda, semTransporte.Onda);
    }

    [Fact]
    public async Task MesmosZipsNaPuxada_SobemComoPuxada_OndasPorTransporte()
    {
        var sjc = SjcZip("Puxada");
        var soro = SorocabaZip("Puxada");

        var regs = await Processar(Puxada(sjc), Puxada(soro));

        // Transportes distintos: 1000000001..1000000004 (SJC reaproveita 01 e 02) = 4 ondas.
        Assert.Equal(7, _orq.Chamadas.Count);
        Assert.Equal(
            new[] { "1000000001", "1000000002", "1000000003", "1000000004" },
            _orq.Chamadas.Select(c => c.Transporte).Distinct().OrderBy(t => t));
        Assert.All(regs, r =>
        {
            Assert.Equal("PUXADA", r.Tipo);
            Assert.Equal("Puxada", r.Origem);
            Assert.Equal(string.Empty, r.Onda);
            Assert.Contains(r.NumeroTransporte, new[] { "1000000001", "1000000002", "1000000003", "1000000004" });
        });
        Assert.False(File.Exists(Path.Combine(_amb.Pastas.CaminhoEstado, "tsp", "ondas.json")));
    }

    [Fact]
    public async Task PuxadaNormal_Inalterada_OndaEhOTransporte_ESemTransporteContinuaPendencia()
    {
        var comT = _amb.EscreverXml("a.xml", "1", "1000000100");
        var semT = _amb.EscreverXml("b.xml", "2", null);
        var proc = _amb.CriarProcessador(_orq, out _);

        var regs = await proc.ProcessarAsync(new[] { comT, semT }, PacoteId, CancellationToken.None);

        Assert.Contains(("1", "1000000100"), _orq.Chamadas);
        Assert.Contains(("2", string.Empty), _orq.Chamadas);
        var r1 = regs.Single(r => r.NumeroNota == "1");
        Assert.Equal("1000000100", r1.NumeroTransporte);
        Assert.Equal("PUXADA", r1.Tipo);
        Assert.Equal("raiz", r1.Origem);
        Assert.Equal(string.Empty, r1.Onda);
    }

    [Fact]
    public async Task ReenvioDoMesmoZip_EmOutroPacote_ReaproveitaAOnda()
    {
        var zip = SjcZip("PuxadaTSP");
        var backup = Path.Combine(_amb.Raiz, "backup-sjc.zip");
        File.Copy(zip, backup);
        var proc = _amb.CriarProcessador(_orq, out _);

        var primeiro = await proc.ProcessarAsync(new[] { Tsp(zip) }, PacoteId, CancellationToken.None);
        File.Copy(backup, zip);
        var segundo = await proc.ProcessarAsync(new[] { Tsp(zip) }, "pacote_20261112_110000", CancellationToken.None);

        var onda = $"TSP-{Data}-01-SJC";
        Assert.All(primeiro.Concat(segundo), r => Assert.Equal(onda, r.Onda));
        Assert.Equal(4, _orq.Chamadas.Count);
        Assert.Equal(2, Todos("Processados").Count(a => a.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task ZipOutroConteudoMesmoNome_GanhaNovoSequencial()
    {
        var proc = _amb.CriarProcessador(_orq, out _);
        var a = _amb.EscreverZipBrf("PuxadaTSP", "XML'S SJC.zip", 1001, "1000000001");
        var r1 = await proc.ProcessarAsync(new[] { Tsp(a) }, PacoteId, CancellationToken.None);
        var b = _amb.EscreverZipBrf("PuxadaTSP", "XML'S SJC.zip", 1501, "1000000009");
        var r2 = await proc.ProcessarAsync(new[] { Tsp(b) }, "pacote_20261112_110000", CancellationToken.None);

        Assert.Equal($"TSP-{Data}-01-SJC", r1.Single().Onda);
        Assert.Equal($"TSP-{Data}-02-SJC", r2.Single().Onda);
    }

    [Fact]
    public async Task Processados_MantemLayoutPacote_ComXmlsEZip_ENadaFicaNaEntradaNemNoTmp()
    {
        var zip = SjcZip("PuxadaTSP");

        await Processar(Tsp(zip));

        var arquivos = Todos("Processados");
        Assert.Equal(3, arquivos.Length);
        Assert.All(arquivos, a => Assert.Equal(PacoteId, Path.GetFileName(Path.GetDirectoryName(a))));
        Assert.Contains(arquivos, a => a.EndsWith(AmbientePacote.NomeXmlBrf(1001)));
        Assert.Contains(arquivos, a => a.EndsWith("XML'S SJC.zip"));
        Assert.False(File.Exists(zip));
        var tmp = Path.Combine(_amb.Pastas.CaminhoEstado, "tmp");
        Assert.True(!Directory.Exists(tmp) || Directory.GetFileSystemEntries(tmp).Length == 0);
    }

    [Fact]
    public async Task DevolucaoI40_AindaAchaOXmlDepoisDeProcessarUmZip()
    {
        var zip = SjcZip("PuxadaTSP");
        await Processar(Tsp(zip));

        var localizador = new LocalizadorXmlDevolucao(_amb.Pastas.CaminhoProcessados);
        var achado = localizador.Localizar(AmbientePacote.ChaveBrf(1001));

        Assert.NotNull(achado);
        Assert.Equal(AmbientePacote.NomeXmlBrf(1001), Path.GetFileName(achado), ignoreCase: true);
        Assert.Equal(PacoteId, Path.GetFileName(Path.GetDirectoryName(achado)));
        Assert.NotEmpty(localizador.BuscarPorNumeroSerie(1001, 6, AmbientePacote.CnpjBrf));
    }

    [Fact]
    public async Task ZipCorrompido_ViraZipInvalido_VaiParaErro_SemChamarOWms()
    {
        var caminho = Path.Combine(_amb.Pasta("PuxadaTSP"), "quebrado.zip");
        File.WriteAllBytes(caminho, new byte[] { 9, 9, 9, 9, 9, 9 });

        var regs = await Processar(Tsp(caminho));

        var r = Assert.Single(regs);
        Assert.Equal(CategoriaErro.ZipInvalido, r.Categoria);
        Assert.Equal(DestinoArquivo.Erro, r.Destino);
        Assert.Equal(StatusLinhaRelatorio.NaoIntegrada, r.Status);
        Assert.Equal("quebrado.zip", r.NumeroNota);
        Assert.Equal("TSP", r.Tipo);
        Assert.Empty(_orq.Chamadas);
        var movido = Todos("Erro").Single(a => a.EndsWith("quebrado.zip"));
        Assert.Equal("ZipInvalido", Path.GetFileName(Path.GetDirectoryName(movido)));
        Assert.True(File.Exists(movido + ".motivo.txt"));
        Assert.False(File.Exists(caminho));
    }

    [Fact]
    public async Task ZipComEntradasInvalidas_ProcessaOXmlBom_EReportaAsOutras()
    {
        var zip = _amb.EscreverZip("PuxadaTSP", "XML'S SJC.zip",
            (AmbientePacote.NomeXmlBrf(1001), AmbientePacote.XmlBrf(1001, "1000000001")),
            ("leia-me.txt", "oi"),
            ("interno.zip", "PK"),
            ("..\\fora.xml", "<x/>"));

        var regs = await Processar(Tsp(zip));

        var ok = regs.Single(r => r.NumeroNota == "1001");
        Assert.True(ok.Integrada);
        var txt = regs.Single(r => r.NumeroNota == "leia-me.txt");
        Assert.Equal(CategoriaErro.ExtensaoInvalida, txt.Categoria);
        Assert.Equal(StatusLinhaRelatorio.ArquivoIgnorado, txt.Status);
        var aninhado = regs.Single(r => r.NumeroNota == "interno.zip");
        Assert.Equal(CategoriaErro.ExtensaoInvalida, aninhado.Categoria);
        var perigoso = regs.Single(r => r.NumeroNota == "..\\fora.xml");
        Assert.Equal(CategoriaErro.ZipInvalido, perigoso.Categoria);
        Assert.Equal(StatusLinhaRelatorio.NaoIntegrada, perigoso.Status);
        Assert.Single(_orq.Chamadas);
        Assert.False(File.Exists(Path.Combine(_amb.Raiz, "fora.xml")));
        Assert.Contains(Todos("Processados"), a => a.EndsWith("XML'S SJC.zip"));
    }

    [Fact]
    public async Task XmlInvalidoDentroDoZip_VaiParaErroXmlInvalido_OsDemaisSobem()
    {
        var zip = _amb.EscreverZip("PuxadaTSP", "XML'S SJC.zip",
            (AmbientePacote.NomeXmlBrf(1001), AmbientePacote.XmlBrf(1001, "1000000001")),
            ("estragado.xml", "isto nao e xml"));

        var regs = await Processar(Tsp(zip));

        Assert.Single(_orq.Chamadas);
        var ruim = regs.Single(r => r.XmlInvalido);
        Assert.Equal(CategoriaErro.XmlInvalido, ruim.Categoria);
        Assert.Equal("TSP", ruim.Tipo);
        Assert.Contains(Todos("Erro"), a => a.EndsWith("estragado.xml"));
    }

    [Fact]
    public async Task MesmaNotaEmDoisZips_ProcessaUmaVez_ORepetidoFicaComoIgnorado()
    {
        var a = _amb.EscreverZip("PuxadaTSP", "A.zip", (AmbientePacote.NomeXmlBrf(1001), AmbientePacote.XmlBrf(1001, "1000000001")));
        var b = _amb.EscreverZip("PuxadaTSP", "B.zip", (AmbientePacote.NomeXmlBrf(1001), AmbientePacote.XmlBrf(1001, "1000000001")));

        var regs = await Processar(Tsp(a), Tsp(b));

        Assert.Single(_orq.Chamadas);
        var repetido = regs.Single(r => r.Status == StatusLinhaRelatorio.ArquivoIgnorado);
        Assert.StartsWith(LinhaRelatorioErro.PrefixoXmlRepetido, repetido.Mensagem);
        Assert.Equal(DestinoArquivo.Processados, repetido.Destino);
        Assert.Null(repetido.Categoria);
        Assert.Equal("Arquivo ignorado (XML repetido)", repetido.ParaLinha().StatusDescricao);
        Assert.Empty(Todos("Erro"));
    }

    [Fact]
    public async Task ExcecaoNoOrquestrador_ComTsp_RegistraNaoIntegradaComOTransporteDaNota()
    {
        _orq.Comportamento = (_, _) => throw new InvalidOperationException("falhou");
        var zip = SjcZip("PuxadaTSP");

        var regs = await Processar(Tsp(zip));

        Assert.All(regs, r =>
        {
            Assert.Equal(CategoriaErro.Outro, r.Categoria);
            Assert.Equal($"TSP-{Data}-01-SJC", r.Onda);
        });
        Assert.Equal(new[] { "1000000001", "1000000002" }, regs.Select(r => r.NumeroTransporte).OrderBy(t => t));
    }

    [Fact]
    public void NaoXmlNaPuxadaTsp_DizQueAPastaAceitaApenasZip()
    {
        var pdf = Path.Combine(_amb.Pasta("PuxadaTSP"), "nota.xml.pdf");
        File.WriteAllText(pdf, "x");
        var proc = _amb.CriarProcessador(_orq, out _);

        var regs = proc.ProcessarNaoXml(new[] { Tsp(pdf) }, PacoteId);

        var r = Assert.Single(regs);
        Assert.Contains("aceita apenas ZIP", r.Mensagem);
        Assert.Equal(CategoriaErro.ExtensaoInvalida, r.Categoria);
        Assert.Equal("TSP", r.Tipo);
        Assert.Equal("PuxadaTSP", r.Origem);
        Assert.Contains(Todos("Erro"), a => a.EndsWith("nota.xml.pdf"));
    }
}