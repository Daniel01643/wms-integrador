using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Monitoramento;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Worker;

/// <summary>
/// Agenda o "Diagnóstico agente integrador" (e-mail diário às 14:00, só para a TI). Roda isolado do <see cref="Worker"/>
/// do pacote, para que a coleta (tamanho de pastas) nunca atrase o ciclo de notas. Atraso inicial de 30 s e verificação
/// a cada 60 s; a decisão de enviar é do <see cref="ServicoDiagnostico"/>.
/// </summary>
public sealed class DiagnosticoWorker : BackgroundService
{
    private static readonly TimeSpan AtrasoInicial = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(60);

    private readonly ServicoDiagnostico _servico;
    private readonly OpcoesDiagnostico _opcoes;
    private readonly ILogger<DiagnosticoWorker> _logger;

    /// <summary>Cria o worker.</summary>
    public DiagnosticoWorker(ServicoDiagnostico servico, IOptions<OpcoesDiagnostico> opcoes, ILogger<DiagnosticoWorker> logger)
    {
        _servico = servico;
        _opcoes = opcoes.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opcoes.Habilitado)
        {
            _logger.LogInformation("Diagnóstico agente integrador desligado (Diagnostico:Habilitado=false).");
            return;
        }

        foreach (var problema in _opcoes.Validar())
        {
            _logger.LogWarning("Configuração do diagnóstico: {Problema}", problema);
        }

        _logger.LogInformation("Diagnóstico agente integrador agendado para {Hora:HH\\:mm} (horário local).", _opcoes.Horario);

        try
        {
            await Task.Delay(AtrasoInicial, stoppingToken).ConfigureAwait(false);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _servico.VerificarEEnviarAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falha no ciclo do diagnóstico; tentará novamente no próximo intervalo.");
                }

                await Task.Delay(Intervalo, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // parada limpa
        }
    }
}
