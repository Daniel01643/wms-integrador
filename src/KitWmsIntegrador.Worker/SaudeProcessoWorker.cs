using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Diagnostico;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Worker;

/// <summary>
/// Amostragem periódica de memória do processo e do servidor (padrão a cada 15 min, <c>Diagnostico:IntervaloAmostraMinutos</c>;
/// 0 desliga). Cada amostra vai para o diário do diagnóstico; a decisão do alerta de memória livre é do
/// <see cref="ServicoAmostraSaude"/>. Roda isolada do <see cref="Worker"/> do pacote e nunca derruba o host.
/// </summary>
public sealed class SaudeProcessoWorker : BackgroundService
{
    private static readonly TimeSpan AtrasoInicial = TimeSpan.FromSeconds(15);

    private readonly ServicoAmostraSaude _servico;
    private readonly OpcoesDiagnostico _opcoes;
    private readonly ILogger<SaudeProcessoWorker> _logger;

    /// <summary>Cria o worker.</summary>
    public SaudeProcessoWorker(ServicoAmostraSaude servico, IOptions<OpcoesDiagnostico> opcoes, ILogger<SaudeProcessoWorker> logger)
    {
        _servico = servico;
        _opcoes = opcoes.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_opcoes.IntervaloAmostraMinutos <= 0 || _opcoes.IntervaloAmostraMinutos > 1440)
        {
            _logger.LogInformation(
                "Amostragem de memória desligada (Diagnostico:IntervaloAmostraMinutos={Valor}; use 1 a 1440).", _opcoes.IntervaloAmostraMinutos);
            return;
        }

        var intervalo = TimeSpan.FromMinutes(_opcoes.IntervaloAmostraMinutos);
        _logger.LogInformation(
            "Amostragem de memória a cada {Minutos} min; alerta de memória livre abaixo de {Limite} MB (0 = sem alerta).",
            _opcoes.IntervaloAmostraMinutos, _opcoes.AlertaMemoriaLivreMb);

        try
        {
            await Task.Delay(AtrasoInicial, stoppingToken).ConfigureAwait(false);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _servico.AmostrarAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Falha na amostragem de memória (ignorada).");
                }

                await Task.Delay(intervalo, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // parada limpa
        }
    }
}
