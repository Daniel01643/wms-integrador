using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Diagnostico;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Ciclo do Diagnóstico agente integrador: decide (<see cref="AgendadorRelatorioDiario"/>) se o slot diário (14:00) é
/// devido, coleta a janela [slot-24h, slot], grava/envia e SÓ ENTÃO marca o slot. Todos os dias da semana. Falha de
/// gravação em disco não marca (tenta no próximo ciclo); falha de e-mail só loga e o slot é marcado.
/// </summary>
public sealed class ServicoDiagnostico
{
    private static readonly TimeSpan Janela = TimeSpan.FromHours(24);

    private readonly IClock _clock;
    private readonly IColetorDiagnostico _coletor;
    private readonly MarcaUltimoDiagnostico _marca;
    private readonly EnviadorDiagnostico _enviador;
    private readonly OpcoesDiagnostico _opcoes;
    private readonly ILogger<ServicoDiagnostico> _logger;

    /// <summary>Cria o serviço.</summary>
    public ServicoDiagnostico(
        IClock clock, IColetorDiagnostico coletor, MarcaUltimoDiagnostico marca, EnviadorDiagnostico enviador,
        IOptions<OpcoesDiagnostico> opcoes, ILogger<ServicoDiagnostico> logger)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _coletor = coletor ?? throw new ArgumentNullException(nameof(coletor));
        _marca = marca ?? throw new ArgumentNullException(nameof(marca));
        _enviador = enviador ?? throw new ArgumentNullException(nameof(enviador));
        _opcoes = opcoes?.Value ?? throw new ArgumentNullException(nameof(opcoes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Gera sob demanda com a janela [agora-24h, agora] (CLI <c>--diagnostico</c>), sem tocar a marca do slot.</summary>
    public Task<(string Html, bool EmailEnviado)> GerarAgoraAsync(bool enviarEmail, CancellationToken ct)
    {
        var agora = _clock.UtcNow.ToLocalTime();
        return _enviador.EnviarAsync(_coletor.Coletar(agora, agora - Janela, agora), enviarEmail, ct);
    }

    /// <summary>Verifica o agendamento e, quando devido, gera/envia e marca o slot. Nunca derruba o host.</summary>
    public async Task VerificarEEnviarAsync(CancellationToken ct)
    {
        if (!_opcoes.Habilitado)
        {
            return;
        }

        try
        {
            var agora = _clock.UtcNow.ToLocalTime();
            var (deve, slot) = AgendadorRelatorioDiario.DeveEnviar(agora, _marca.Ler(), _opcoes.Horario);
            if (!deve)
            {
                return;
            }

            var modelo = _coletor.Coletar(agora, slot - Janela, slot);
            await _enviador.EnviarAsync(modelo, enviarEmail: true, ct).ConfigureAwait(false);
            _marca.Marcar(slot);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha no Diagnóstico agente integrador (slot não marcado; tenta no próximo ciclo).");
        }
    }
}
