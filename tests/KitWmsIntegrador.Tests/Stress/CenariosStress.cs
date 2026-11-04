using System.Diagnostics;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.Options;
using PdfSharpCore.Pdf.IO;
using Xunit.Abstractions;

namespace KitWmsIntegrador.Tests.Stress;

/// <summary>
/// Cenários de carga 100% locais (fake de WMS, diretório temporário). Só rodam com <c>STRESS=1</c>.
/// Rode UM cenário por invocação para isolar o pico de memória do processo:
/// <c>$env:STRESS='1'; dotnet test tests\KitWmsIntegrador.Tests -c Release --filter "FullyQualifiedName~CenariosStress.Cenario1"</c>.
/// Variáveis: STRESS_LATENCIA_MS (padrão 80), STRESS_NOTAS (cenários 1/2), STRESS_SAIDA (arquivo JSONL).
/// </summary>
[Collection("Stress")]
public sealed class CenariosStress
{
    private static readonly TimeSpan Limite = TimeSpan.FromMinutes(25);
    private readonly ITestOutputHelper _saida;

    public CenariosStress(ITestOutputHelper saida)
    {
        _saida = saida;
    }

    private static int Latencia => int.TryParse(Environment.GetEnvironmentVariable("STRESS_LATENCIA_MS"), out var v) ? v : 80;

    private static int Notas(int padrao) => int.TryParse(Environment.GetEnvironmentVariable("STRESS_NOTAS"), out var v) ? v : padrao;

    // ---- Cenário 1 / 2 / sem latência / comparativo: todos os XMLs antes do 1º ciclo -> 1 pacote ----

    [StressFact]
    public async Task Cenario1_500Xmls_25Transportes_1Pacote()
    {
        var r = await ExecutarEmUmPacoteAsync("Cenario1_500xmls_25transp", Notas(500), 25, 1, Latencia);
        Assert.Equal(1, r.Pacotes);
    }

    [StressFact]
    public async Task Cenario2_2000Xmls()
    {
        var r = await ExecutarEmUmPacoteAsync("Cenario2_2000xmls", Notas(2000), 25, 1, Latencia);
        Assert.Equal(1, r.Pacotes);
    }

    [StressFact]
    public async Task Cenario_SemLatencia_2000Notas()
    {
        var r = await ExecutarEmUmPacoteAsync("SemLatencia_2000notas", Notas(2000), 25, 1, 0);
        Assert.Equal(1, r.Pacotes);
    }

    [StressFact]
    public async Task Comparativo_Paralelismo()
    {
        foreach (var variante in new[] { "limpo500", "erros300" })
        {
            double? basePar1 = null;
            foreach (var par in new[] { 1, 2, 4 })
            {
                ResultadoStress r;
                if (variante == "limpo500")
                {
                    r = await ExecutarEmUmPacoteAsync($"Comparativo_{variante}_p{par}", 500, 25, par, Latencia);
                }
                else
                {
                    r = await ExecutarMisturaAsync($"Comparativo_{variante}_p{par}", 300, 25, par, Latencia);
                }

                basePar1 ??= r.TempoTotalS;
                _saida.WriteLine($"{variante} p={par}: {r.TempoTotalS}s ganho x{basePar1 / r.TempoTotalS:0.00}");
                Assert.True(r.Integridade == "OK", r.Integridade);
            }
        }
    }

    // ---- Cenário 3: chegada contínua durante o processamento ----

    [StressFact]
    public async Task Cenario3_ChegadaContinua()
    {
        const int Transportes = 25;
        const int PorSegundo = 20;
        const int Segundos = 30;
        const int Total = PorSegundo * Segundos;

        using var amb = new AmbienteStress(1, Latencia);
        var esperados = Enumerable.Range(0, Total).Select(AmbienteStress.NomeArquivo).ToList();
        var produtorTerminou = false;

        using var medidor = new MedidorRecursos();
        var sw = Stopwatch.StartNew();

        var produtor = Task.Run(async () =>
        {
            var i = 0;
            for (var s = 0; s < Segundos; s++)
            {
                var inicioSegundo = sw.Elapsed;
                var lentos = new List<Task>();
                for (var k = 0; k < PorSegundo; k++, i++)
                {
                    if (i % 10 == 9)
                    {
                        // 10%: arquivo gravado em duas etapas (aberto sem compartilhamento por ~700 ms).
                        var n = i;
                        lentos.Add(Task.Run(async () =>
                        {
                            var caminho = Path.Combine(amb.Entrada, AmbienteStress.NomeArquivo(n));
                            var texto = KitWmsIntegrador.Tests.Infra.AmbientePacote.Xml(
                                AmbienteStress.NumeroNota(n), AmbienteStress.Transporte(n, Transportes));
                            using var fs = new FileStream(caminho, FileMode.Create, FileAccess.Write, FileShare.None);
                            using var w = new StreamWriter(fs);
                            await w.WriteAsync(texto[..(texto.Length / 2)]);
                            await w.FlushAsync();
                            await Task.Delay(700);
                            await w.WriteAsync(texto[(texto.Length / 2)..]);
                        }));
                    }
                    else
                    {
                        // Escrita em staging + File.Move atômico para a entrada.
                        var staging = AmbienteStress.GravarXml(amb.Staging, i, Transportes, false);
                        File.Move(staging, Path.Combine(amb.Entrada, AmbienteStress.NomeArquivo(i)));
                    }
                }

                await Task.WhenAll(lentos);
                var resto = TimeSpan.FromSeconds(1) - (sw.Elapsed - inicioSegundo);
                if (resto > TimeSpan.Zero)
                {
                    await Task.Delay(resto);
                }
            }

            produtorTerminou = true;
        });

        await amb.RodarAsync(() => produtorTerminou && amb.Terminou(), Limite);
        await produtor;
        sw.Stop();
        medidor.Parar();

        var integ = amb.VerificarIntegridade(esperados);
        var tamanhos = amb.Gerador.LinhasPorPacote.Select(p => p.Count).ToList();
        var r = amb.Resultado("Cenario3_chegada_continua", Total, Transportes, sw.Elapsed, medidor, integ,
            $"pacotes tamanho min/med/max={tamanhos.Min()}/{tamanhos.Average():0}/{tamanhos.Max()}; producao={PorSegundo}/s por {Segundos}s");
        ColetorResultados.Gravar(r, _saida);

        Assert.True(integ.Ok, integ.Detalhe);
        Assert.True(r.Pacotes >= 2, "esperado >= 2 pacotes, obtido " + r.Pacotes);
        Assert.Equal(r.Pacotes, r.Emails);
    }

    // ---- Cenário 4: 10% de erros de várias categorias ----

    [StressFact]
    public async Task Cenario4_Mistura10PorCentoErros()
    {
        var r = await ExecutarMisturaAsync("Cenario4_1000xmls_10pct_erros", 1000, 25, 1, Latencia);
        Assert.Equal(1, r.Pacotes);
        Assert.Equal("OK", r.Integridade);
    }

    // ---- Cenário 5: relatório com milhares de linhas ----

    [StressFact]
    public void Cenario5_Relatorio_1000Linhas() => MedirRelatorio(1000);

    [StressFact]
    public void Cenario5_Relatorio_5000Linhas() => MedirRelatorio(5000);

    [StressFact]
    public void Cenario5_Relatorio_10000Linhas() => MedirRelatorio(10000);

    private void MedirRelatorio(int quantidade)
    {
        var linhas = GerarLinhas(quantidade);
        var pasta = Path.Combine(Path.GetTempPath(), "kitwms-stress-rel-" + Guid.NewGuid().ToString("N"));
        try
        {
            var gerador = new GeradorRelatorio(Options.Create(new OpcoesRelatorio { PastaSaida = pasta }));
            var geradoEm = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
            using var medidor = new MedidorRecursos();

            var sw = Stopwatch.StartNew();
            var html = gerador.GerarHtml(linhas, geradoEm);
            var msHtml = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var pdf = gerador.GerarPdf(linhas, geradoEm);
            var msPdf = sw.Elapsed.TotalMilliseconds;
            medidor.Parar();

            var paginas = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import).PageCount;
            var texto = PdfTextoHelper.Texto(pdf);
            var paginacao = texto.Contains($"de {paginas}") || texto.Contains($"Página 1 de {paginas}");
            Assert.True(paginas > 1);
            Assert.True(paginacao, "rodapé 'Página X de Y' não encontrado");

            var r = new ResultadoStress(
                $"Cenario5_relatorio_{quantidade}linhas", quantidade, linhas.Select(l => l.NumeroTransporte).Distinct().Count(),
                0, 0, 0, 0, 1, 0, Math.Round((msHtml + msPdf) / 1000, 2), 0,
                Math.Round(medidor.PicoWorkingSetMb, 1), Math.Round(medidor.PicoGerenciadoMb, 1),
                Math.Round(medidor.AlocadoTotalMb, 1), $"{medidor.Gen0}/{medidor.Gen1}/{medidor.Gen2}",
                Math.Round(msHtml, 1), Math.Round(msPdf, 1), Math.Round(pdf.Length / 1024.0, 1), 0, 0, "OK",
                $"paginas={paginas}; htmlKB={html.Length / 1024}; baseGerenciadaMB={medidor.BaseGerenciadaMb:0.0}; rodapePaginaXdeY={paginacao}");
            ColetorResultados.Gravar(r, _saida);
        }
        finally
        {
            if (Directory.Exists(pasta))
            {
                Directory.Delete(pasta, true);
            }
        }
    }

    private static List<LinhaRelatorioErro> GerarLinhas(int n)
    {
        var rnd = new Random(42);
        var agora = new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero);
        var hist = new[]
        {
            "I50 (tentativa 1): sucesso=N Destinataria - Empresa não localizada, sigla não informada.",
            "I10 (empresa): sucesso=S Registrado com sucesso.",
            "I50 (tentativa 2): sucesso=N Produto Cod\\EAN: \"175886\" não ativo ou faltando ou não cadastrado!",
            "I30 (produto provisório — requer revisão): sucesso=S Registrado com sucesso.",
            "I50 (tentativa 3): sucesso=S Registrado com sucesso.",
        };
        var lista = new List<LinhaRelatorioErro>(n);
        for (var i = 0; i < n; i++)
        {
            var transporte = (70000 + rnd.Next(25, 201)).ToString();
            var erro = rnd.NextDouble() < 0.30;
            lista.Add(erro
                ? new LinhaRelatorioErro(
                    (200000 + i).ToString(), transporte, agora.AddSeconds(i), ExemplosRelatorio.MensagemLonga,
                    Integrada: false, Tentativas: 3, IdTransacao: "T" + i, IdDocumento: "D" + i,
                    Status: StatusLinhaRelatorio.NaoIntegrada, ProdutosProvisorios: new[] { "175886" }, Historico: hist)
                : new LinhaRelatorioErro(
                    (200000 + i).ToString(), transporte, agora.AddSeconds(i), "Registrado com sucesso.",
                    Integrada: true, Tentativas: 1, IdTransacao: "T" + i, IdDocumento: "D" + i,
                    Status: StatusLinhaRelatorio.Integrada, ProdutosProvisorios: null, Historico: hist.Take(1).ToArray()));
        }

        return lista;
    }

    // ---- Execuções reutilizáveis ----

    private async Task<ResultadoStress> ExecutarEmUmPacoteAsync(string nome, int notas, int transportes, int paralelismo, int latenciaMs)
    {
        using var amb = new AmbienteStress(paralelismo, latenciaMs);
        var esperados = new List<string>(notas);
        for (var i = 0; i < notas; i++)
        {
            AmbienteStress.GravarXml(amb.Entrada, i, transportes, false);
            esperados.Add(AmbienteStress.NomeArquivo(i));
        }

        using var medidor = new MedidorRecursos();
        var sw = Stopwatch.StartNew();
        await amb.RodarAsync(amb.Terminou, Limite);
        sw.Stop();
        medidor.Parar();

        var integ = amb.VerificarIntegridade(esperados);
        var r = amb.Resultado(nome, notas, transportes, sw.Elapsed, medidor, integ,
            $"concorrenciaMax={amb.Wms.ConcorrenciaMaxima}; janela=1s; estab=0s; ciclo=200ms");
        ColetorResultados.Gravar(r, _saida);

        Assert.True(integ.Ok, integ.Detalhe);
        Assert.Equal(notas, integ.Processados);
        Assert.Equal(r.Pacotes, r.Emails);
        Assert.True(amb.Wms.ConcorrenciaMaxima <= paralelismo, "concorrência observada " + amb.Wms.ConcorrenciaMaxima);
        return r;
    }

    private async Task<ResultadoStress> ExecutarMisturaAsync(string nome, int notas, int transportes, int paralelismo, int latenciaMs)
    {
        using var amb = new AmbienteStress(paralelismo, latenciaMs);

        // 10% de erros (4 categorias em partes iguais) + ~12% de casos que terminam em Processados
        // (pendência, já existe, produto resolvido, empresa resolvida).
        var erros = notas / 10;
        var porCategoria = erros / 4;
        var outros = Math.Max(1, notas / 33);
        var sequencia = new List<(PlanoFalha? Plano, bool Invalido)>();
        void Adicionar(int qtd, PlanoFalha? plano, bool invalido = false)
        {
            for (var k = 0; k < qtd; k++)
            {
                sequencia.Add((plano, invalido));
            }
        }

        Adicionar(porCategoria, PlanoFalha.ProdutoEsgotado);
        Adicionar(porCategoria, PlanoFalha.EmpresaEsgotada);
        Adicionar(porCategoria, PlanoFalha.RejeicaoGenerica);
        Adicionar(porCategoria, null, true);
        Adicionar(outros, PlanoFalha.Pendencia);
        Adicionar(outros, PlanoFalha.JaExiste);
        Adicionar(outros, PlanoFalha.ProdutoResolvido);
        Adicionar(outros, PlanoFalha.EmpresaResolvida);
        Adicionar(Math.Max(0, notas - sequencia.Count), PlanoFalha.Sucesso);

        // Embaralha de forma determinística para espalhar os casos pelos transportes.
        var rnd = new Random(7);
        var ordem = Enumerable.Range(0, sequencia.Count).OrderBy(_ => rnd.Next()).ToList();
        var esperados = new List<string>();
        for (var i = 0; i < ordem.Count; i++)
        {
            var (plano, invalido) = sequencia[ordem[i]];
            AmbienteStress.GravarXml(amb.Entrada, i, transportes, invalido);
            esperados.Add(AmbienteStress.NomeArquivo(i));
            if (plano is not null)
            {
                amb.Wms.Programar(AmbienteStress.NumeroNota(i), plano.Value);
            }
        }

        var categorias = new Dictionary<string, int>
        {
            [nameof(CategoriaErro.ProdutoNaoCadastrado)] = porCategoria,
            [nameof(CategoriaErro.EmpresaNaoLocalizada)] = porCategoria,
            [nameof(CategoriaErro.Outro)] = porCategoria,
            [nameof(CategoriaErro.XmlInvalido)] = porCategoria,
        };

        using var medidor = new MedidorRecursos();
        var sw = Stopwatch.StartNew();
        await amb.RodarAsync(amb.Terminou, Limite);
        sw.Stop();
        medidor.Parar();

        var integ = amb.VerificarIntegridade(esperados, categorias);
        var linhas = amb.Gerador.LinhasPorPacote.SelectMany(p => p).ToList();
        var r = amb.Resultado(nome, esperados.Count, transportes, sw.Elapsed, medidor, integ,
            $"erros={integ.Erro} (4 categorias x {porCategoria}); processados={integ.Processados}; " +
            $"linhasNaoIntegradas={linhas.Count(l => !l.EstaIntegrada && l.StatusEfetivo == StatusLinhaRelatorio.NaoIntegrada)}; " +
            $"pendentes={linhas.Count(l => l.StatusEfetivo == StatusLinhaRelatorio.Pendente)}; " +
            $"I50/I10/I30={amb.Wms.ChamadasI50}/{amb.Wms.ChamadasI10}/{amb.Wms.ChamadasI30}; concorrenciaMax={amb.Wms.ConcorrenciaMaxima}");
        ColetorResultados.Gravar(r, _saida);

        Assert.True(integ.Ok, integ.Detalhe);
        Assert.Equal(r.Pacotes, r.Emails);
        Assert.True(amb.Wms.ConcorrenciaMaxima <= paralelismo);
        return r;
    }
}
