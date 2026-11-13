using System.Reflection;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Separacao;
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
    private readonly RegistradorTelemetria _telemetria;
    private readonly ServicoResumoSemanal _servicoResumo;
    private readonly OpcoesPasta _pastas;
    private readonly OpcoesPacote _pacote;
    private readonly OpcoesRelatorio _relatorio;
    private readonly OpcoesMonitoramento _monitoramento;
    private readonly OpcoesZip _zip;
    private readonly ServicoRelatorioDiarioI60? _relatorioDiario;
    private readonly OpcoesI51 _i51;
    private readonly OpcoesI60 _i60;
    private readonly OpcoesRelatorioI60 _relatorioI60;

    public Worker(
        ILogger<Worker> logger,
        IHostApplicationLifetime lifetime,
        GerenciadorPacote gerenciador,
        LimpadorPastas limpador,
        IClock clock,
        RegistradorTelemetria telemetria,
        ServicoResumoSemanal servicoResumo,
        IOptions<OpcoesPasta> pastas,
        IOptions<OpcoesPacote> pacote,
        IOptions<OpcoesRelatorio> relatorio,
        IOptions<OpcoesMonitoramento> monitoramento,
        IOptions<OpcoesZip> zip,
        IOptions<OpcoesI51>? i51 = null,
        IOptions<OpcoesI60>? i60 = null,
        IOptions<OpcoesRelatorioI60>? relatorioI60 = null,
        ServicoRelatorioDiarioI60? relatorioDiario = null)
    {
        _relatorioDiario = relatorioDiario;
        _i51 = i51?.Value ?? new OpcoesI51();
        _i60 = i60?.Value ?? new OpcoesI60();
        _relatorioI60 = relatorioI60?.Value ?? new OpcoesRelatorioI60();
        _zip = zip.Value;
        _relatorio = relatorio.Value;
        _logger = logger;
        _lifetime = lifetime;
        _gerenciador = gerenciador;
        _limpador = limpador;
        _clock = clock;
        _telemetria = telemetria;
        _servicoResumo = servicoResumo;
        _pastas = pastas.Value;
        _pacote = pacote.Value;
        _monitoramento = monitoramento.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var problemas = _pastas.Validar(_relatorio.PastaSaida);
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

        var problemasPacote = _pacote.Validar();
        if (problemasPacote.Count > 0)
        {
            foreach (var problema in problemasPacote)
            {
                _logger.LogError("Configuração inválida: {Problema}", problema);
            }

            _logger.LogError("Serviço não iniciado: corrija a seção 'Pacote' do appsettings.");
            _lifetime.StopApplication();
            return;
        }

        foreach (var aviso in _pacote.Avisos())
        {
            _logger.LogWarning("Configuração ajustada: {Aviso}", aviso);
        }

        var problemasZip = _zip.Validar();
        if (problemasZip.Count > 0)
        {
            foreach (var problema in problemasZip)
            {
                _logger.LogError("Configuração inválida: {Problema}", problema);
            }

            _logger.LogError("Serviço não iniciado: corrija a seção 'Zip' do appsettings.");
            _lifetime.StopApplication();
            return;
        }

        // I51/I60/RelatorioI60: só validadas com a I51 ligada (desligada = comportamento anterior, nada novo roda).
        if (_i51.Habilitada)
        {
            var problemasI51 = _i51.Validar().Concat(_i60.Validar()).Concat(_relatorioI60.Validar()).ToList();
            if (problemasI51.Count > 0)
            {
                foreach (var problema in problemasI51)
                {
                    _logger.LogError("Configuração inválida: {Problema}", problema);
                }

                _logger.LogError("Serviço não iniciado: corrija as seções 'I51', 'I60' e 'RelatorioI60' do appsettings.");
                _lifetime.StopApplication();
                return;
            }
        }

        // Garante as subpastas de depósito (Puxada e PuxadaTSP) dentro da entrada.
        foreach (var subpasta in new[] { _pastas.CaminhoPuxada, _pastas.CaminhoPuxadaTsp })
        {
            try
            {
                Directory.CreateDirectory(subpasta);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Não foi possível criar a pasta {Pasta}.", subpasta);
            }
        }

        _logger.LogInformation(
            "Watcher iniciado. Entrada={Entrada} Processados={Processados} Erro={Erro}",
            _pastas.CaminhoEntrada, _pastas.CaminhoProcessados, _pastas.CaminhoErro);

        // Telemetria de saúde (aditiva): registra a partida do serviço; nunca derruba o Worker.
        if (_monitoramento.Habilitado)
        {
            try
            {
                var versao = typeof(Worker).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? typeof(Worker).Assembly.GetName().Version?.ToString()
                    ?? "desconhecida";
                _telemetria.RegistrarEventoServico(
                    new EventoServico("servico-iniciado", _clock.UtcNow.ToLocalTime(), versao));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao registrar o evento de partida do serviço (ignorada).");
            }
        }

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

                // Resumo semanal (aditivo): agendador idempotente dentro do Worker. Exceção
                // é apenas logada pelo próprio serviço; não atrapalha a cadência do pacote.
                await _servicoResumo.VerificarEEnviarAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha no ciclo do pacote; tentará novamente no próximo ciclo.");
            }

            // Relatório diário da I51/I60 (12:00): try/catch PRÓPRIO, fora do ciclo do pacote, para que uma
            // exceção do pacote não o impeça e vice-versa.
            try
            {
                await ExecutarRelatorioDiarioAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
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

    /// <summary>
    /// Verifica e envia o relatório diário da I51/I60. Exceção é só logada (cancelamento propaga) e nunca
    /// afeta o ciclo do pacote.
    /// </summary>
    public async Task ExecutarRelatorioDiarioAsync(CancellationToken ct)
    {
        if (_relatorioDiario is null)
        {
            return;
        }

        try
        {
            await _relatorioDiario.VerificarEEnviarAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha no relatório diário da I51/I60 (ignorada; não derruba o Worker).");
        }
    }
}
