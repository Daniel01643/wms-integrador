using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Relatorio;

namespace KitWmsIntegrador.Core.Diagnostico;

/// <summary>Dados de saúde coletados do ambiente (já medidos; o montador não faz I/O).</summary>
public sealed record DadosSaude(
    string Versao, DateTimeOffset? UltimoInicio, int Reinicios,
    long? EspacoLivreEntrada, long? EspacoLivreAplicacao,
    long? TamanhoProcessados, long? TamanhoErro, long? TamanhoEstado);

/// <summary>
/// Tudo que o <see cref="MontadorDiagnostico"/> precisa, já lido do disco. <see cref="ModeloI60"/> é <c>null</c>
/// quando a I51 está desligada. <see cref="Devolucao"/> traz só contagens por usuário.
/// </summary>
public sealed record EntradaDiagnostico(
    DateTimeOffset GeradoEm, DateTimeOffset Inicio, DateTimeOffset Fim,
    IReadOnlyList<EventoDiagnostico> Eventos,
    ResumoPeriodo Resumo,
    ModeloRelatorioI60? ModeloI60,
    bool DevolucaoHabilitada,
    IReadOnlyList<LinhaUsuarioDevolucao> Devolucao,
    DadosSaude Saude,
    int MaxLinhasPorSecao = 100,
    int MaxGruposMensagem = 10);
