using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Monitoramento;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class RegistradorTelemetriaTests
{
    private static RegistroTelemetria RegistroExemplo(string id = "pacote_1") => new(
        IdPacote: id,
        AberturaLocal: new DateTimeOffset(2025, 1, 10, 12, 0, 0, TimeSpan.FromHours(-3)),
        FechamentoLocal: new DateTimeOffset(2025, 1, 10, 12, 1, 0, TimeSpan.FromHours(-3)),
        TotalNotas: 3,
        Integradas: 2,
        IntegradasRevisarProduto: 0,
        Pendentes: 0,
        NaoIntegradas: 1,
        XmlInvalido: 0,
        Transportes: 2,
        TempoTotalMs: 60_000,
        TempoMedioPorNotaMs: 20_000,
        Tentativas: new Dictionary<string, int> { ["1"] = 2, ["2"] = 1, ["3+"] = 0 },
        ChamadasWms: new Dictionary<string, int> { ["I50"] = 3 },
        ChamadasWmsAproximado: true,
        ErrosPorCategoria: new Dictionary<string, int> { ["TentativasEsgotadas"] = 1 });

    private static (RegistradorTelemetria Reg, string Entrada, string Estado) Criar()
    {
        var entrada = Path.Combine(Path.GetTempPath(), "wms-tel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(entrada);
        var pastas = new OpcoesPasta { Entrada = entrada, Estado = "Estado" };
        var reg = new RegistradorTelemetria(
            Options.Create(pastas),
            Options.Create(new OpcoesMonitoramento()),
            NullLogger<RegistradorTelemetria>.Instance);
        return (reg, entrada, pastas.CaminhoEstado);
    }

    [Fact]
    public void RegistrarPacote_CriaArquivoEReleituraDevolveRegistros()
    {
        var (reg, entrada, _) = Criar();
        try
        {
            reg.RegistrarPacote(RegistroExemplo("a"));
            reg.RegistrarPacote(RegistroExemplo("b"));

            Assert.True(File.Exists(reg.CaminhoTelemetria));
            var lidos = reg.LerPacotes();
            Assert.Equal(2, lidos.Count);
            Assert.Equal("a", lidos[0].IdPacote);
            Assert.Equal("b", lidos[1].IdPacote);
            Assert.True(lidos[0].ChamadasWmsAproximado);
            Assert.Equal(3, lidos[0].ChamadasWms["I50"]);
        }
        finally
        {
            if (Directory.Exists(entrada)) Directory.Delete(entrada, recursive: true);
        }
    }

    [Fact]
    public void CaminhoBase_ResolvidoAoLadoDoEstado()
    {
        var (reg, entrada, estado) = Criar();
        try
        {
            Assert.Equal(Path.Combine(estado, RegistradorTelemetria.SubpastaPadrao), reg.PastaBase);
            Assert.Equal(
                Path.Combine(estado, RegistradorTelemetria.SubpastaPadrao, RegistradorTelemetria.NomeArquivoTelemetria),
                reg.CaminhoTelemetria);
        }
        finally
        {
            if (Directory.Exists(entrada)) Directory.Delete(entrada, recursive: true);
        }
    }

    [Fact]
    public void LerPacotes_IgnoraLinhaFinalTruncada()
    {
        var (reg, entrada, _) = Criar();
        try
        {
            reg.RegistrarPacote(RegistroExemplo("ok"));
            // Acrescenta uma linha JSON truncada manualmente.
            File.AppendAllText(reg.CaminhoTelemetria, "{\"IdPacote\":\"trunc");

            var lidos = reg.LerPacotes();
            Assert.Single(lidos);
            Assert.Equal("ok", lidos[0].IdPacote);
        }
        finally
        {
            if (Directory.Exists(entrada)) Directory.Delete(entrada, recursive: true);
        }
    }
}
