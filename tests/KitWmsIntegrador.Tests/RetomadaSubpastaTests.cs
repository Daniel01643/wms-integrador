using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Retomada após queda (Etapa 7c): o move reconciliado usa o identificador do pacote gravado no
/// diário, caindo na MESMA subpasta, sem reenviar nada ao WMS.
/// </summary>
public sealed class RetomadaSubpastaTests : IDisposable
{
    private readonly AmbientePacote _amb = new();
    private readonly OrquestradorFalso _orq = new();

    public void Dispose() => _amb.Dispose();

    private DiarioPacote NovoDiario() =>
        new(Options.Create(_amb.Pastas), NullLogger<DiarioPacote>.Instance);

    private static RegistroDiario Reg(string arquivo, DateTimeOffset horario, string? idPacote) =>
        new(arquivo, false, "100", "70001", horario, "Registrado com sucesso.", true, 1,
            StatusLinhaRelatorio.Integrada, "", "", Array.Empty<string>(), Array.Empty<string>(),
            null, DestinoArquivo.Processados, idPacote);

    // (v) retomada reconcilia o move para Processados\<idDoRegistro>\ sem chamar o WMS.
    [Fact]
    public async Task Retomada_ReconciliaNaSubpastaDoDiario_SemReenviarAoWms()
    {
        const string idPacote = "pacote_20250110_115900";
        var caminho = _amb.EscreverXml("a.xml", "100", "70001");
        File.SetLastWriteTimeUtc(caminho, DateTime.UtcNow.AddHours(-1)); // gravado ANTES do registro
        NovoDiario().Registrar(Reg(caminho, DateTimeOffset.UtcNow, idPacote));

        var g = _amb.CriarGerenciador(_orq);
        await _amb.RodarAteOciosoAsync(g);

        Assert.Empty(_orq.Chamadas);
        Assert.False(File.Exists(caminho));

        var destino = Path.Combine(_amb.Entrada, "Processados", idPacote, "a.xml");
        Assert.True(File.Exists(destino), $"esperado em {destino}");
    }

    // Arquivos movidos antes da queda + reconciliados após a queda compartilham a MESMA subpasta.
    [Fact]
    public async Task Retomada_MoveAntesEDepoisDaQueda_CompartilhamASubpasta()
    {
        const string idPacote = "pacote_20250110_115900";

        // Simula o arquivo já movido antes da queda: dentro de Processados\<id>\.
        var jaMovido = Path.Combine(_amb.Entrada, "Processados", idPacote, "a.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(jaMovido)!);
        File.WriteAllText(jaMovido, "<x/>");

        // E um arquivo ainda na entrada com registro no diário (a reconciliar).
        var pendente = _amb.EscreverXml("b.xml", "200", "70001");
        File.SetLastWriteTimeUtc(pendente, DateTime.UtcNow.AddHours(-1));
        NovoDiario().Registrar(Reg(pendente, DateTimeOffset.UtcNow, idPacote));

        var g = _amb.CriarGerenciador(_orq);
        await _amb.RodarAteOciosoAsync(g);

        Assert.Empty(_orq.Chamadas);
        var subpastas = Directory.GetDirectories(Path.Combine(_amb.Entrada, "Processados"));
        var sub = Assert.Single(subpastas);
        Assert.Equal(idPacote, Path.GetFileName(sub));
        Assert.Equal(
            new[] { "a.xml", "b.xml" },
            Directory.GetFiles(sub, "*.xml").Select(Path.GetFileName).OrderBy(n => n).ToArray());
    }

    // Diário antigo (sem IdentificadorPacote) → fallback recalculado de _inicio, ainda sem reenviar.
    [Fact]
    public async Task Retomada_DiarioSemId_UsaFallbackSemReenviar()
    {
        var caminho = _amb.EscreverXml("a.xml", "100", "70001");
        File.SetLastWriteTimeUtc(caminho, DateTime.UtcNow.AddHours(-1));
        NovoDiario().Registrar(Reg(caminho, DateTimeOffset.UtcNow, idPacote: null));

        var g = _amb.CriarGerenciador(_orq);
        await _amb.RodarAteOciosoAsync(g);

        Assert.Empty(_orq.Chamadas);
        Assert.False(File.Exists(caminho));

        var subpastas = Directory.GetDirectories(Path.Combine(_amb.Entrada, "Processados"));
        var sub = Assert.Single(subpastas);
        Assert.StartsWith("pacote_", Path.GetFileName(sub));
        Assert.True(File.Exists(Path.Combine(sub, "a.xml")));
    }
}
