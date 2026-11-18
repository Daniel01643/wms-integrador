using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Limpeza dos logs rotacionados do NSSM (<c>worker.out-*.log</c>) com <c>Limpeza:DiasRetencaoLogs</c> e
/// <c>Limpeza:PastaLogs</c>, e confirmação de que os brutos processados da I51 seguem a retenção do estado.
/// </summary>
public sealed class LimpezaLogsRotacionadosTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "kitwms-limpeza-logs-" + Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly string _logs;
    private readonly OpcoesPasta _pastas;

    public LimpezaLogsRotacionadosTests()
    {
        var entrada = Path.Combine(_raiz, "entrada");
        _logs = Path.Combine(_raiz, "logs");
        Directory.CreateDirectory(entrada);
        Directory.CreateDirectory(_logs);
        _pastas = new OpcoesPasta { Entrada = entrada };
    }

    public void Dispose()
    {
        try { Directory.Delete(_raiz, true); } catch { /* melhor esforço */ }
    }

    private LimpadorPastas Criar(OpcoesLimpeza limpeza) =>
        new(_clock, Options.Create(_pastas), Options.Create(limpeza), NullLogger<LimpadorPastas>.Instance);

    private string Log(string nome, double diasAtras, string? pasta = null)
    {
        var caminho = Path.Combine(pasta ?? _logs, nome);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, "log");
        File.SetLastWriteTimeUtc(caminho, _clock.UtcNow.UtcDateTime.AddDays(-diasAtras));
        return caminho;
    }

    [Fact]
    public void ApagaRotacionadosMaisAntigosQueARetencao_MantemRecentes()
    {
        var velho = Log("worker.out-20261001T081500.123.log", 48);
        var limite = Log("worker.out-20261018T081500.123.log", 29);
        var recente = Log("worker.out-20261117T081500.123.log", 1);

        var r = Criar(new OpcoesLimpeza { DiasRetencaoLogs = 30, PastaLogs = _logs }).Limpar();

        Assert.Empty(r.Erros);
        Assert.False(File.Exists(velho));
        Assert.True(File.Exists(limite));
        Assert.True(File.Exists(recente));
        Assert.Equal(1, r.ArquivosRemovidos);
    }

    [Fact]
    public void NuncaApagaOArquivoAtual_MesmoVelho()
    {
        var atual = Log("worker.out.log", 400);
        var atualErr = Log("worker.err.log", 400);
        var velho = Log("worker.out-20250101T000000.000.log", 400);

        Criar(new OpcoesLimpeza { DiasRetencaoLogs = 30, PastaLogs = _logs }).Limpar();

        Assert.True(File.Exists(atual));
        Assert.True(File.Exists(atualErr));
        Assert.False(File.Exists(velho));
    }

    [Fact]
    public void SoApagaOsPadroesDoNssm_OutrosArquivosFicam()
    {
        var outro = Log("outro-sistema.log", 400);
        var txt = Log("worker.out-20250101T000000.000.txt", 400);
        var rotErr = Log("worker.err-20250101T000000.000.log", 400);

        Criar(new OpcoesLimpeza { DiasRetencaoLogs = 30, PastaLogs = _logs }).Limpar();

        Assert.True(File.Exists(outro));
        Assert.True(File.Exists(txt));
        Assert.False(File.Exists(rotErr));
    }

    [Fact]
    public void RetencaoRespeitaOValorConfigurado()
    {
        var com10Dias = Log("worker.out-20261105T000000.000.log", 13);
        var com3Dias = Log("worker.out-20261115T000000.000.log", 3);

        Criar(new OpcoesLimpeza { DiasRetencaoLogs = 7, PastaLogs = _logs }).Limpar();

        Assert.False(File.Exists(com10Dias));
        Assert.True(File.Exists(com3Dias));
    }

    [Fact]
    public void PastaInexistente_EIgnoradaSemErro()
    {
        var r = Criar(new OpcoesLimpeza { PastaLogs = Path.Combine(_raiz, "nao-existe") }).Limpar();

        Assert.Empty(r.Erros);
        Assert.Equal(0, r.ArquivosRemovidos);
    }

    [Fact]
    public void PastaPadrao_RelativaAoApp_NaoExistindoEIgnorada()
    {
        // PastaLogs vazio = <pasta do app>\..\..\logs; no ambiente de teste essa pasta não existe e nada é tocado.
        var r = Criar(new OpcoesLimpeza { PastaLogs = string.Empty }).Limpar();

        Assert.Empty(r.Erros);
    }

    [Fact]
    public void RaizDeDrive_ERecusadaPorSeguranca()
    {
        var raiz = Path.GetPathRoot(Path.GetTempPath())!;

        var r = Criar(new OpcoesLimpeza { PastaLogs = raiz }).Limpar();

        Assert.Empty(r.Erros);
        Assert.Equal(0, r.ArquivosRemovidos);
    }

    [Fact]
    public void RetencaoDeLogsInvalida_NaoApagaENoticiaErro()
    {
        var velho = Log("worker.out-20250101T000000.000.log", 400);

        var r = Criar(new OpcoesLimpeza { DiasRetencaoLogs = 0, PastaLogs = _logs }).Limpar();

        Assert.True(File.Exists(velho));
        Assert.Contains(r.Erros, e => e.Contains("DiasRetencaoLogs"));
    }

    [Fact]
    public void LimpezaDesabilitada_NaoApagaLogs()
    {
        var velho = Log("worker.out-20250101T000000.000.log", 400);

        Criar(new OpcoesLimpeza { Habilitada = false, PastaLogs = _logs }).Limpar();

        Assert.True(File.Exists(velho));
    }

    [Fact]
    public void LimpezaDeLogs_NaoDependeDeOutrasPastasDoFluxo_ESomaNoResultado()
    {
        var processados = Path.Combine(_pastas.CaminhoProcessados, "pacote_velho", "a.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(processados)!);
        File.WriteAllText(processados, "<x/>");
        File.SetLastWriteTimeUtc(processados, _clock.UtcNow.UtcDateTime.AddDays(-90));
        Log("worker.out-20250101T000000.000.log", 400);

        var r = Criar(new OpcoesLimpeza { PastaLogs = _logs }).Limpar();

        Assert.Equal(2, r.ArquivosRemovidos);
    }

    [Fact]
    public async Task BrutosProcessadosDaI51_SaoLimpadosPelaRetencaoDoEstadoNoCicloDiario()
    {
        using var amb = new AmbienteI51();
        var pendente = amb.Brutos.GravarPendente("TODOS", null, "{}");
        amb.Brutos.MoverProcessado(pendente!);
        Assert.Single(Directory.GetFiles(amb.Caminhos.BrutosProcessados, "*.json", SearchOption.AllDirectories));

        // Dentro da retenção (30 dias): permanece.
        amb.Clock.Avancar(TimeSpan.FromDays(29));
        await amb.Servico.CicloAsync(CancellationToken.None);
        Assert.Single(Directory.GetFiles(amb.Caminhos.BrutosProcessados, "*.json", SearchOption.AllDirectories));

        // Passou da retenção: o ciclo diário da I51 apaga a pasta do dia.
        amb.Clock.Avancar(TimeSpan.FromDays(3));
        await amb.Servico.CicloAsync(CancellationToken.None);
        Assert.Empty(Directory.GetFiles(amb.Caminhos.BrutosProcessados, "*.json", SearchOption.AllDirectories));
    }
}
