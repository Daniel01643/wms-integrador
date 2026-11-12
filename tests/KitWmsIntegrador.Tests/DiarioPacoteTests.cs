using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public sealed class DiarioPacoteTests : IDisposable
{
    private readonly AmbientePacote _amb = new();

    public void Dispose() => _amb.Dispose();

    private DiarioPacote Criar() => new(Options.Create(_amb.Pastas), NullLogger<DiarioPacote>.Instance);

    private static RegistroDiario Registro(string arquivo, string nota) => new(
        arquivo, false, nota, "T1", new DateTimeOffset(2025, 1, 10, 12, 0, 0, TimeSpan.Zero),
        "msg ção", true, 2, StatusLinhaRelatorio.Pendente, "tx", "doc",
        new[] { "P1" }, new[] { "h1", "h2" }, null, DestinoArquivo.Processados);

    [Fact]
    public void RegistrarELer_RoundTrip()
    {
        var diario = Criar();
        diario.Registrar(Registro("a.xml", "1"));
        diario.Registrar(Registro("b.xml", "2") with { Categoria = CategoriaErro.Outro, Destino = DestinoArquivo.Erro });

        var lidos = Criar().Ler();

        Assert.Equal(2, lidos.Count);
        Assert.Equal(Registro("a.xml", "1").Mensagem, lidos[0].Mensagem);
        Assert.Equal(StatusLinhaRelatorio.Pendente, lidos[0].Status);
        Assert.Equal(new[] { "h1", "h2" }, lidos[0].Historico);
        Assert.Equal(CategoriaErro.Outro, lidos[1].Categoria);
        Assert.Equal(DestinoArquivo.Erro, lidos[1].Destino);
    }

    [Fact]
    public void LinhaTruncadaOuCorrompida_EhIgnorada()
    {
        var diario = Criar();
        diario.Registrar(Registro("a.xml", "1"));
        File.AppendAllText(diario.Caminho, "{\"Arquivo\":\"b.xml\",\"XmlInva");

        var lidos = diario.Ler();

        Assert.Single(lidos);
        Assert.Equal("a.xml", lidos[0].Arquivo);
    }

    [Fact]
    public void Apagar_RemoveArquivo_ELeituraVazia()
    {
        var diario = Criar();
        diario.Registrar(Registro("a.xml", "1"));
        Assert.True(File.Exists(diario.Caminho));

        Assert.True(diario.Apagar());

        Assert.False(File.Exists(diario.Caminho));
        Assert.Empty(diario.Ler());
    }

    [Fact]
    public void ParaLinha_SemTransporte_UsaRotulo()
    {
        var linha = (Registro("a.xml", "1") with { NumeroTransporte = "" }).ParaLinha();

        Assert.Equal(RegistroDiario.RotuloSemTransporte, linha.NumeroTransporte);
    }

    [Fact]
    public void CamposNovos_TipoOrigemOndaZip_IdaEVolta()
    {
        var diario = Criar();
        diario.Registrar(Registro("a.xml", "1") with { Tipo = "TSP", Origem = "PuxadaTSP", Onda = "20261112-01-SJC", Zip = "XMLS SJC.zip" });

        var lido = Criar().Ler().Single();

        Assert.Equal("TSP", lido.Tipo);
        Assert.Equal("PuxadaTSP", lido.Origem);
        Assert.Equal("20261112-01-SJC", lido.Onda);
        Assert.Equal("XMLS SJC.zip", lido.Zip);
        var linha = lido.ParaLinha();
        Assert.True(linha.EhTsp);
        Assert.False(linha.EhRaiz);
        Assert.Equal("20261112-01-SJC", linha.Onda);
    }

    [Fact]
    public void LinhaAntigaSemCamposNovos_LeComPadroes()
    {
        var diario = Criar();
        Directory.CreateDirectory(Path.GetDirectoryName(diario.Caminho)!);
        File.WriteAllText(diario.Caminho,
            "{\"Arquivo\":\"a.xml\",\"XmlInvalido\":false,\"NumeroNota\":\"7\",\"NumeroTransporte\":\"T9\"," +
            "\"Horario\":\"2025-01-10T12:00:00+00:00\",\"Mensagem\":\"ok\",\"Integrada\":true,\"Tentativas\":1," +
            "\"Status\":\"Integrada\",\"IdTransacao\":\"\",\"IdDocumento\":\"\",\"ProdutosProvisorios\":[]," +
            "\"Historico\":[],\"Categoria\":null,\"Destino\":\"Processados\",\"IdentificadorPacote\":\"pacote_1\"}\n");

        var lido = diario.Ler().Single();

        Assert.Equal("PUXADA", lido.Tipo);
        Assert.Equal(string.Empty, lido.Origem);
        Assert.Equal(string.Empty, lido.Onda);
        Assert.False(lido.ParaLinha().EhTsp);
        Assert.Equal("T9", lido.ParaLinha().NumeroTransporte);
    }}
