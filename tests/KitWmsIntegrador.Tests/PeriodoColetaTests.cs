using System.Globalization;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Tests.Infra;

namespace KitWmsIntegrador.Tests;

public sealed class PeriodoColetaTests : IDisposable
{
    private readonly AmbientePacote _amb = new();

    public void Dispose() => _amb.Dispose();

    private static readonly DateTime Primeira = new(2025, 3, 4, 8, 15, 20, DateTimeKind.Utc);
    private static readonly DateTime Ultima = new(2025, 3, 4, 9, 40, 5, DateTimeKind.Utc);

    private string[] TresArquivos()
    {
        var a = _amb.EscreverXml("a.xml", "100", "70001");
        var b = _amb.EscreverXml("b.xml", "101", "70001");
        var c = _amb.EscreverXml("c.xml", "102", "70001");
        File.SetLastWriteTimeUtc(b, Primeira.AddMinutes(30));
        File.SetLastWriteTimeUtc(a, Primeira);
        File.SetLastWriteTimeUtc(c, Ultima);
        return new[] { a, b, c };
    }

    [Fact]
    public void Calcular_DevolvePrimeiraEUltimaGravacao()
    {
        var periodo = PeriodoColeta.Calcular(TresArquivos());

        Assert.NotNull(periodo);
        Assert.Equal(Primeira, periodo!.PrimeiraGravacao.UtcDateTime);
        Assert.Equal(Ultima, periodo.UltimaGravacao.UtcDateTime);
    }

    [Fact]
    public void Calcular_IgnoraInexistentes_ENuloQuandoNenhumExiste()
    {
        Assert.Null(PeriodoColeta.Calcular(new[] { _amb.Caminho("nao-existe.xml") }));
        Assert.Null(PeriodoColeta.Calcular(Array.Empty<string>()));

        var a = _amb.EscreverXml("a.xml", "100", "70001");
        File.SetLastWriteTimeUtc(a, Primeira);
        var periodo = PeriodoColeta.Calcular(new[] { a, _amb.Caminho("fantasma.xml") });
        Assert.Equal(periodo!.PrimeiraGravacao, periodo.UltimaGravacao);
    }

    [Fact]
    public async Task PeriodoApareceNoEmailNoHtmlENoPacoteAtual()
    {
        var arquivos = TresArquivos();
        var esperado = PeriodoColeta.Calcular(arquivos)!;
        var orq = new OrquestradorFalso();
        var g = _amb.CriarGerenciador(orq);
        var ci = CultureInfo.InvariantCulture;

        await g.CicloAsync(CancellationToken.None);
        Assert.Null(g.PacoteAtual!.Periodo); // a foto ainda não foi tirada
        await _amb.RodarAteOciosoAsync(g);

        var corpo = _amb.Email.Enviadas.Single().CorpoHtml;
        Assert.Contains(esperado.PrimeiraGravacao.ToString("dd/MM/yyyy HH:mm:ss", ci), corpo);
        Assert.Contains(esperado.UltimaGravacao.ToString("dd/MM/yyyy HH:mm:ss", ci), corpo);
        Assert.Contains("Coleta", corpo);

        var html = File.ReadAllText(_amb.RelatoriosGerados(".html").Single());
        Assert.Contains("Coleta: ", html);
        Assert.Contains(esperado.PrimeiraGravacao.ToString("dd/MM/yyyy HH:mm:ss", ci), html);
        Assert.Contains(esperado.UltimaGravacao.ToString("dd/MM/yyyy HH:mm:ss", ci), html);
    }
}
