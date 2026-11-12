using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Relatorio;
using KitWmsIntegrador.Core.Separacao;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Ciclo do relatório diário da I51/I60: decide (<see cref="AgendadorRelatorioDiario"/>) se o slot das
/// 12:00 é devido, monta o modelo do estado das notas, grava/envia e SÓ ENTÃO marca o slot. Falha
/// de geração/gravação não marca (tenta no próximo ciclo). Só atua com <c>I51:Habilitada=true</c>.
/// </summary>
public sealed class ServicoRelatorioDiarioI60
{
    private readonly IClock _clock;
    private readonly IRepositorioSeparacao _repositorio;
    private readonly ArmazenamentoBrutoI51 _brutos;
    private readonly RepositorioOrfaosI51 _orfaos;
    private readonly MarcaUltimoRelatorioI60 _marca;
    private readonly EnviadorRelatorioDiarioI60 _enviador;
    private readonly OpcoesI51 _i51;
    private readonly OpcoesI60 _i60;
    private readonly OpcoesRelatorioI60 _opcoes;
    private readonly OpcoesDevolucao _devolucao;
    private readonly ILogger<ServicoRelatorioDiarioI60> _logger;

    /// <summary>Cria o serviço.</summary>
    public ServicoRelatorioDiarioI60(
        IClock clock, IRepositorioSeparacao repositorio, ArmazenamentoBrutoI51 brutos, RepositorioOrfaosI51 orfaos,
        MarcaUltimoRelatorioI60 marca, EnviadorRelatorioDiarioI60 enviador, IOptions<OpcoesI51> i51, IOptions<OpcoesI60> i60,
        IOptions<OpcoesRelatorioI60> opcoes, IOptions<OpcoesDevolucao> devolucao, ILogger<ServicoRelatorioDiarioI60> logger)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        _brutos = brutos ?? throw new ArgumentNullException(nameof(brutos));
        _orfaos = orfaos ?? throw new ArgumentNullException(nameof(orfaos));
        _marca = marca ?? throw new ArgumentNullException(nameof(marca));
        _enviador = enviador ?? throw new ArgumentNullException(nameof(enviador));
        _i51 = i51?.Value ?? throw new ArgumentNullException(nameof(i51));
        _i60 = i60?.Value ?? throw new ArgumentNullException(nameof(i60));
        _opcoes = opcoes?.Value ?? throw new ArgumentNullException(nameof(opcoes));
        _devolucao = devolucao?.Value ?? throw new ArgumentNullException(nameof(devolucao));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Monta o modelo do estado atual com a janela de 24 h até <paramref name="agora"/> (ou até o slot).</summary>
    public ModeloRelatorioI60 MontarModelo(DateTimeOffset agora, DateTimeOffset? fimJanela = null)
    {
        var fim = fimJanela ?? agora;
        var inicio = fim - TimeSpan.FromHours(24);
        var entrada = new EntradaRelatorioI60(
            _repositorio.Listar(), _brutos.LerSaude(), _brutos.ContarFalhos(), _orfaos.ContarDesde(inicio),
            agora, inicio, _devolucao.Habilitada);
        return MontadorRelatorioI60.Montar(entrada, _opcoes, _i60);
    }

    /// <summary>Gera o relatório do estado atual sob demanda (CLI <c>--relatorio-i60</c>).</summary>
    public Task<(string Html, string Pdf, bool EmailEnviado)> GerarAgoraAsync(bool enviarEmail, CancellationToken ct) =>
        _enviador.EnviarAsync(MontarModelo(_clock.UtcNow), enviarEmail, ct);

    /// <summary>Verifica o agendamento e, quando devido, gera/envia e marca o slot. Nunca derruba o Worker.</summary>
    public async Task VerificarEEnviarAsync(CancellationToken ct)
    {
        if (!_i51.Habilitada || !_opcoes.Habilitado)
        {
            return;
        }

        try
        {
            var agora = _clock.UtcNow.ToLocalTime();
            var (deve, slot) = AgendadorRelatorioDiario.DeveEnviar(agora, _marca.Ler(), _opcoes.HoraDoDia);
            if (!deve)
            {
                return;
            }

            var modelo = MontarModelo(agora, slot);
            if (modelo.SemAtividade && !_opcoes.EnviarQuandoVazio)
            {
                _logger.LogInformation("Relatório diário I51/I60 sem atividade; slot marcado sem gerar.");
                _marca.Marcar(slot);
                return;
            }

            await _enviador.EnviarAsync(modelo, enviarEmail: true, ct).ConfigureAwait(false);
            _marca.Marcar(slot);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha no relatório diário I51/I60 (slot não marcado; tenta no próximo ciclo).");
        }
    }
}
