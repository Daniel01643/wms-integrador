using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Separacao;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Worker;

/// <summary>
/// Consulta periódica da I51 (retorno de separação) e envio da I60 depois dela. Roda isolado do
/// <see cref="Worker"/> de pacote. Desligado (<c>I51:Habilitada=false</c>): loga e termina sem timer.
/// Configuração inválida: loga o erro e encerra sem consultar.
/// </summary>
public sealed class ConsultaI51Worker : BackgroundService
{
    private readonly ServicoSeparacaoI51 _servico;
    private readonly OpcoesI51 _i51;
    private readonly OpcoesI60 _i60;
    private readonly ILogger<ConsultaI51Worker> _logger;

    /// <summary>Cria o worker.</summary>
    public ConsultaI51Worker(
        ServicoSeparacaoI51 servico, IOptions<OpcoesI51> i51, IOptions<OpcoesI60> i60, ILogger<ConsultaI51Worker> logger)
    {
        _servico = servico;
        _i51 = i51.Value;
        _i60 = i60.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_i51.Habilitada)
        {
            _logger.LogInformation("Consulta da I51 desligada (I51:Habilitada=false); a I60 segue o fluxo anterior.");
            return;
        }

        var problemas = _i51.Validar().Concat(_i60.Validar()).ToList();
        if (problemas.Count > 0)
        {
            foreach (var problema in problemas)
            {
                _logger.LogError("Configuração inválida: {Problema}", problema);
            }

            _logger.LogError("Consulta da I51 não iniciada: corrija as seções 'I51' e 'I60' do appsettings.");
            return;
        }

        _logger.LogInformation(
            "Consulta da I51 iniciada: identificador={Identificador} intervalo={Intervalo} min, atraso após {Horas} h, I60 {I60}.",
            _i51.Identificador, _i51.IntervaloMinutos, _i51.HorasParaAtraso, _i60.Habilitada ? "habilitada" : "desabilitada");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _i51.AtrasoInicialSegundos)), stoppingToken).ConfigureAwait(false);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _servico.CicloAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falha no ciclo da I51; tentará novamente no próximo intervalo.");
                }

                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, _i51.IntervaloMinutos)), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // parada limpa
        }

        _logger.LogInformation("Consulta da I51 encerrada.");
    }
}
