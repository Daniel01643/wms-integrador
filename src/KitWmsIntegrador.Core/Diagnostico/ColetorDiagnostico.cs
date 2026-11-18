using System.Reflection;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Diagnostico;

/// <summary>Coleta os dados e devolve o modelo do diagnóstico para uma janela.</summary>
public interface IColetorDiagnostico
{
    /// <summary>Coleta e monta o modelo. <paramref name="agora"/> é o estado atual; a janela é [inicio, fim].</summary>
    ModeloDiagnostico Coletar(DateTimeOffset agora, DateTimeOffset inicio, DateTimeOffset fim);
}

/// <summary>
/// Faz o I/O do diagnóstico (diário, telemetria, estado I51/I60, auditoria da devolução, disco) e entrega ao
/// <see cref="MontadorDiagnostico"/>. Cada fonte é tolerante a erro: uma fonte que falha vira seção vazia, não derruba o relatório.
/// </summary>
public sealed class ColetorDiagnostico : IColetorDiagnostico
{
    private readonly RegistroDiagnosticoArquivo _diario;
    private readonly RegistradorTelemetria _telemetria;
    private readonly ServicoRelatorioDiarioI60 _relatorioI60;
    private readonly LeitorAuditoriaDevolucao _devolucao;
    private readonly IProvedorSaudeSistema _saude;
    private readonly OpcoesPasta _pastas;
    private readonly OpcoesI51 _i51;
    private readonly OpcoesDevolucao _opcoesDevolucao;
    private readonly OpcoesDiagnostico _opcoes;
    private readonly ILogger<ColetorDiagnostico> _logger;

    /// <summary>Cria o coletor.</summary>
    public ColetorDiagnostico(
        RegistroDiagnosticoArquivo diario, RegistradorTelemetria telemetria, ServicoRelatorioDiarioI60 relatorioI60,
        LeitorAuditoriaDevolucao devolucao, IProvedorSaudeSistema saude, IOptions<OpcoesPasta> pastas,
        IOptions<OpcoesI51> i51, IOptions<OpcoesDevolucao> opcoesDevolucao, IOptions<OpcoesDiagnostico> opcoes,
        ILogger<ColetorDiagnostico> logger)
    {
        _diario = diario ?? throw new ArgumentNullException(nameof(diario));
        _telemetria = telemetria ?? throw new ArgumentNullException(nameof(telemetria));
        _relatorioI60 = relatorioI60 ?? throw new ArgumentNullException(nameof(relatorioI60));
        _devolucao = devolucao ?? throw new ArgumentNullException(nameof(devolucao));
        _saude = saude ?? throw new ArgumentNullException(nameof(saude));
        _pastas = pastas?.Value ?? throw new ArgumentNullException(nameof(pastas));
        _i51 = i51?.Value ?? throw new ArgumentNullException(nameof(i51));
        _opcoesDevolucao = opcoesDevolucao?.Value ?? throw new ArgumentNullException(nameof(opcoesDevolucao));
        _opcoes = opcoes?.Value ?? throw new ArgumentNullException(nameof(opcoes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public ModeloDiagnostico Coletar(DateTimeOffset agora, DateTimeOffset inicio, DateTimeOffset fim)
    {
        var eventos = _diario.Ler(inicio, fim);
        var servico = _telemetria.LerEventosServico();
        var resumo = AgregadorTelemetria.Agregar(_telemetria.LerPacotes(), servico, inicio, fim);

        ModeloRelatorioI60? modeloI60 = null;
        if (_i51.Habilitada)
        {
            try
            {
                modeloI60 = _relatorioI60.MontarModelo(agora, fim);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Diagnóstico: não foi possível montar o estado da I51/I60: {Mensagem}", ex.Message);
            }
        }

        var inicios = servico
            .Where(s => string.Equals(s.Tipo, "servico-iniciado", StringComparison.OrdinalIgnoreCase)
                        && s.QuandoLocal >= inicio && s.QuandoLocal <= fim)
            .ToList();
        var saude = new DadosSaude(
            Versao(),
            inicios.Count == 0 ? null : inicios.Max(s => s.QuandoLocal),
            inicios.Count,
            _saude.EspacoLivre(_pastas.CaminhoEntrada),
            _saude.EspacoLivre(AppContext.BaseDirectory),
            _saude.TamanhoPasta(_pastas.CaminhoProcessados),
            _saude.TamanhoPasta(_pastas.CaminhoErro),
            _saude.TamanhoPasta(_pastas.CaminhoEstado));

        return MontadorDiagnostico.Montar(new EntradaDiagnostico(
            agora, inicio, fim, eventos, resumo, modeloI60,
            _opcoesDevolucao.Habilitada, _devolucao.Ler(inicio, fim), saude,
            _opcoes.MaxLinhasPorSecao, _opcoes.MaxGruposMensagem));
    }

    private static string Versao()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(ColetorDiagnostico).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "desconhecida";
    }
}
