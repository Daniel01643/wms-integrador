using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;

namespace KitWmsIntegrador.Tests;

public class MapeadorTelemetriaTests
{
    private static readonly DateTimeOffset Inicio = new(2025, 1, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Fim = new(2025, 1, 10, 12, 1, 0, TimeSpan.Zero);

    private static RegistroDiario Registro(
        string nota,
        string transporte,
        bool integrada,
        int tentativas,
        StatusLinhaRelatorio status,
        bool xmlInvalido = false,
        CategoriaErro? categoria = null,
        IReadOnlyList<string>? historico = null) =>
        new(
            Arquivo: nota + ".xml",
            XmlInvalido: xmlInvalido,
            NumeroNota: nota,
            NumeroTransporte: transporte,
            Horario: Inicio,
            Mensagem: "msg",
            Integrada: integrada,
            Tentativas: tentativas,
            Status: status,
            IdTransacao: "t",
            IdDocumento: "d",
            ProdutosProvisorios: Array.Empty<string>(),
            Historico: historico ?? Array.Empty<string>(),
            Categoria: categoria,
            Destino: integrada ? DestinoArquivo.Processados : DestinoArquivo.Erro);

    [Fact]
    public void De_ContaStatusTransportesETentativas()
    {
        var registros = new[]
        {
            Registro("1", "T1", integrada: true, tentativas: 1, StatusLinhaRelatorio.Integrada),
            Registro("2", "T1", integrada: false, tentativas: 2, StatusLinhaRelatorio.Pendente),
            Registro("3", "T2", integrada: false, tentativas: 3, StatusLinhaRelatorio.NaoIntegrada,
                categoria: CategoriaErro.TentativasEsgotadas),
            Registro("4", "T2", integrada: false, tentativas: 5, StatusLinhaRelatorio.NaoIntegrada,
                categoria: CategoriaErro.TentativasEsgotadas),
        };

        var t = MapeadorTelemetria.De(registros, Inicio, Fim, "pacote_x");

        Assert.Equal("pacote_x", t.IdPacote);
        Assert.Equal(4, t.TotalNotas);
        Assert.Equal(1, t.Integradas);
        Assert.Equal(1, t.Pendentes);
        Assert.Equal(2, t.NaoIntegradas);
        Assert.Equal(2, t.Transportes);

        Assert.Equal(1, t.Tentativas["1"]);
        Assert.Equal(1, t.Tentativas["2"]);
        Assert.Equal(2, t.Tentativas["3+"]);

        Assert.Equal(2, t.ErrosPorCategoria["TentativasEsgotadas"]);
    }

    [Fact]
    public void De_ContaXmlInvalidoETempos()
    {
        var registros = new[]
        {
            Registro("1", "T1", integrada: true, tentativas: 1, StatusLinhaRelatorio.Integrada),
            Registro("2", "T1", integrada: false, tentativas: 1, StatusLinhaRelatorio.NaoIntegrada, xmlInvalido: true),
        };

        var t = MapeadorTelemetria.De(registros, Inicio, Fim, "p");

        Assert.Equal(1, t.XmlInvalido);
        Assert.Equal(60_000, t.TempoTotalMs);
        Assert.Equal(30_000, t.TempoMedioPorNotaMs);
    }

    [Fact]
    public void De_ChamadasWms_Heuristico_ContaI50I10I30_IgnoraI60EOutros()
    {
        var historico = new[]
        {
            "I50 enviada",
            "  I10 (empresa) criada",
            "I30 produto criado",
            "I60 (ignorada)",
            "texto qualquer",
        };
        var registros = new[]
        {
            Registro("1", "T1", integrada: true, tentativas: 1, StatusLinhaRelatorio.Integrada, historico: historico),
        };

        var t = MapeadorTelemetria.De(registros, Inicio, Fim, "p");

        Assert.True(t.ChamadasWmsAproximado);
        Assert.Equal(1, t.ChamadasWms["I50"]);
        Assert.Equal(1, t.ChamadasWms["I10"]);
        Assert.Equal(1, t.ChamadasWms["I30"]);
        Assert.False(t.ChamadasWms.ContainsKey("I60"));
    }

    [Fact]
    public void De_HorariosEmLocal()
    {
        var registros = new[]
        {
            Registro("1", "T1", integrada: true, tentativas: 1, StatusLinhaRelatorio.Integrada),
        };

        var t = MapeadorTelemetria.De(registros, Inicio, Fim, "p");

        Assert.Equal(Inicio.ToLocalTime(), t.AberturaLocal);
        Assert.Equal(Fim.ToLocalTime(), t.FechamentoLocal);
    }
}
