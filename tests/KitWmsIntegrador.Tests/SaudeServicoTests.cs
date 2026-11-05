using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Monitoramento;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class SaudeServicoTests
{
    private static (RegistradorTelemetria Reg, string Entrada) Criar()
    {
        var entrada = Path.Combine(Path.GetTempPath(), "wms-saude-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(entrada);
        var pastas = new OpcoesPasta { Entrada = entrada, Estado = "Estado" };
        var reg = new RegistradorTelemetria(
            Options.Create(pastas),
            Options.Create(new OpcoesMonitoramento()),
            NullLogger<RegistradorTelemetria>.Instance);
        return (reg, entrada);
    }

    [Fact]
    public void RegistrarEventoServico_GravaTipoVersao_ELeituraDevolve()
    {
        var (reg, entrada) = Criar();
        try
        {
            var quando = new DateTimeOffset(2025, 1, 10, 12, 0, 0, TimeSpan.FromHours(-3));
            reg.RegistrarEventoServico(new EventoServico("servico-iniciado", quando, "1.2.3"));

            Assert.True(File.Exists(reg.CaminhoServico));
            var eventos = reg.LerEventosServico();
            var evt = Assert.Single(eventos);
            Assert.Equal("servico-iniciado", evt.Tipo);
            Assert.Equal("1.2.3", evt.Versao);
            Assert.Equal(quando, evt.QuandoLocal);
        }
        finally
        {
            if (Directory.Exists(entrada)) Directory.Delete(entrada, recursive: true);
        }
    }
}
