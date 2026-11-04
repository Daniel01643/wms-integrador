using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Worker;

/// <summary>
/// Worker contínuo: a cada intervalo executa um ciclo do <see cref="GerenciadorPacote"/>
/// (watcher por polling → pacote → integração → movimentação → relatório/e-mail) e roda a
/// limpeza das pastas Processados/Erro na partida e a cada 24 h. Exceção de ciclo é apenas
/// logada; a parada é limpa via <c>stoppingToken</c>.
/// </summary>
public class Worker : BackgroundService
{
    private static readonly TimeSpan IntervaloLimpeza = TimeSpan.FromHours(24);

    private readonly ILogger<Worker> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly GerenciadorPacote _gerenciador;
    private readonly LimpadorPastas _limpador;
    private readonly IClock _clock;
    private readonly OpcoesPasta _pastas;
    private readonly OpcoesPacote _pacote;

    public Worker(
        ILogger<Worker> logger,
        IHostApplicationLifetime lifetime,
        GerenciadorPacote gerenciador,
        LimpadorPastas limpador,
        IClock clock,
        IOptions<OpcoesPasta> pastas,
        IOptions<OpcoesPacote> pacote)
    {
        _logger = logger;
        _lifetime = lifetime;
        _gerenciador = gerenciador;
        _limpador = limpador;
        _clock = clock;
        _pastas = pastas.Value;
        _pacote = pacote.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var problemas = _pastas.Validar();
        if (problemas.Count > 0)
        {
            foreach (var problema in problemas)
            {
                _logger.LogError("Configuração inválida: {Problema}", problema);
            }

            _logger.LogError("Serviço não iniciado: corrija a seção 'Pastas' do appsettings.");
            _lifetime.StopApplication();
            return;
        }

        _logger.LogInformation(
            "Watcher iniciado. Entrada={Entrada} Processados={Processados} Erro={Erro}",
            _pastas.CaminhoEntrada, _pastas.CaminhoProcessados, _pastas.CaminhoErro);

        var intervalo = TimeSpan.FromSeconds(Math.Max(1, _pacote.IntervaloVarreduraSegundos));
        var ultimaLimpeza = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_clock.UtcNow - ultimaLimpeza >= IntervaloLimpeza)
                {
                    ultimaLimpeza = _clock.UtcNow;
                    var limpeza = _limpador.Limpar();
                    _logger.LogInformation(
                        "Limpeza: {Arquivos} arquivo(s) e {Pastas} pasta(s) removidos, {Erros} erro(s).",
                        limpeza.ArquivosRemovidos, limpeza.PastasRemovidas, limpeza.Erros.Count);
                }

                await _gerenciador.CicloAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha no ciclo do pacote; tentará novamente no próximo ciclo.");
            }

            try
            {
                await Task.Delay(intervalo, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Watcher encerrado.");
    }
}
