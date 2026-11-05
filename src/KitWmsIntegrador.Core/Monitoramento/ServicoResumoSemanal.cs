using System.Globalization;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Orquestra um ciclo do resumo semanal: decide (via <see cref="AgendadorResumoSemanal"/>) se o
/// slot da semana já é devido, agrega o período, dispara o envio e marca o slot. Chamado a cada
/// ciclo do Worker. Envolve tudo em try/catch que apenas loga — nunca derruba o Worker.
/// </summary>
public sealed class ServicoResumoSemanal
{
    private readonly IClock _clock;
    private readonly RegistradorTelemetria _telemetria;
    private readonly MarcaUltimoResumo _marca;
    private readonly EnviadorResumoSemanal _enviador;
    private readonly OpcoesMonitoramento _monitoramento;
    private readonly ILogger<ServicoResumoSemanal> _logger;

    /// <summary>Cria o serviço do resumo semanal.</summary>
    public ServicoResumoSemanal(
        IClock clock,
        RegistradorTelemetria telemetria,
        MarcaUltimoResumo marca,
        EnviadorResumoSemanal enviador,
        IOptions<OpcoesMonitoramento> monitoramento,
        ILogger<ServicoResumoSemanal> logger)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _telemetria = telemetria ?? throw new ArgumentNullException(nameof(telemetria));
        _marca = marca ?? throw new ArgumentNullException(nameof(marca));
        _enviador = enviador ?? throw new ArgumentNullException(nameof(enviador));
        _monitoramento = monitoramento?.Value ?? throw new ArgumentNullException(nameof(monitoramento));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Verifica o agendamento e, quando devido, agrega o período e envia o resumo, marcando o
    /// slot ao concluir. Não faz nada quando o monitoramento está desabilitado ou o slot não
    /// chegou. Exceções são apenas logadas.
    /// </summary>
    public async Task VerificarEEnviarAsync(CancellationToken ct)
    {
        if (!_monitoramento.Habilitado)
        {
            return;
        }

        try
        {
            var agora = _clock.UtcNow.ToLocalTime();
            var (deve, slot) = AgendadorResumoSemanal.DeveEnviar(
                agora, _marca.Ler(), _monitoramento.DiaDaSemana, _monitoramento.HoraDoDia);
            if (!deve)
            {
                return;
            }

            var fim = slot;
            var inicio = slot.AddDays(-Math.Max(1, _monitoramento.PeriodoDias));

            var pacotes = _telemetria.LerPacotes();
            var eventos = _telemetria.LerEventosServico();
            var resumo = AgregadorTelemetria.Agregar(pacotes, eventos, inicio, fim);

            var periodoLabel = string.Format(
                CultureInfo.InvariantCulture, "{0:dd/MM/yyyy} a {1:dd/MM/yyyy}",
                inicio.LocalDateTime, fim.LocalDateTime);

            await _enviador.EnviarAsync(resumo, periodoLabel, ct).ConfigureAwait(false);
            _marca.Marcar(slot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha no ciclo do resumo semanal (ignorada; não derruba o Worker).");
        }
    }
}
