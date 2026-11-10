using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Devolucao;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Worker.TelaDevolucao;

/// <summary>
/// Hospeda a tela de devolução (Kestrel) dentro do Worker. Só liga se
/// <c>Devolucao:Habilitada=true</c>; com a flag desligada não resolve nada e não abre porta.
/// ISOLAMENTO: todo o trabalho (opções, serviços, bind da porta) fica dentro do try/catch; qualquer
/// falha é logada e o serviço termina sozinho, sem derrubar a integração.
/// </summary>
public sealed class ServidorTelaDevolucao : BackgroundService
{
    private readonly IServiceProvider _servicos;
    private readonly ILogger<ServidorTelaDevolucao> _logger;

    public ServidorTelaDevolucao(IServiceProvider servicos, ILogger<ServidorTelaDevolucao> logger)
    {
        _servicos = servicos;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Cede o controle para não atrasar a partida dos demais serviços hospedados.
        await Task.Yield();

        try
        {
            var opcoes = _servicos.GetRequiredService<IOptions<OpcoesDevolucao>>().Value;
            if (!opcoes.Habilitada)
            {
                _logger.LogInformation("Tela de devolução desligada (Devolucao:Habilitada=false).");
                return;
            }

            if (opcoes.Porta is < 1 or > 65535)
            {
                _logger.LogError("Tela de devolução não iniciada: Devolucao:Porta inválida ({Porta}).", opcoes.Porta);
                return;
            }

            var servico = _servicos.GetRequiredService<IServicoDevolucao>();
            var app = TelaDevolucaoApp.Criar(
                servico, opcoes, _servicos.GetRequiredService<ILoggerFactory>(), $"http://0.0.0.0:{opcoes.Porta}");

            await using (app)
            {
                _logger.LogInformation("Tela de devolução ligada na porta {Porta} (sem login; restrinja pelo firewall).", opcoes.Porta);
                await app.RunAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Parada normal do serviço.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tela de devolução falhou e foi desligada; a integração continua normalmente: {Mensagem}", ex.Message);
        }
    }
}
