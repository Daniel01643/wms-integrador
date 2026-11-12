namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Resumo agregado de um período (ex.: 7 dias) a partir dos <see cref="RegistroTelemetria"/> e
/// <see cref="EventoServico"/>. Imutável; é a entrada dos renderizadores do resumo semanal.
/// Horários em horário local do servidor.
/// </summary>
/// <param name="InicioPeriodo">Início da janela (inclusive).</param>
/// <param name="FimPeriodo">Fim da janela (exclusivo).</param>
/// <param name="TotalPacotes">Pacotes fechados no período.</param>
/// <param name="TotalNotas">Notas somadas no período.</param>
/// <param name="MediaNotasPorPacote">Média de notas por pacote (0 sem pacotes).</param>
/// <param name="Integradas">Notas integradas (inclui "revisar produto").</param>
/// <param name="IntegradasRevisarProduto">Subconjunto de integradas com produto a revisar.</param>
/// <param name="Pendentes">Notas pendentes.</param>
/// <param name="NaoIntegradas">Notas não integradas.</param>
/// <param name="XmlInvalido">Arquivos com XML inválido.</param>
/// <param name="PercentualIntegradas">% de <see cref="Integradas"/> sobre <see cref="TotalNotas"/>.</param>
/// <param name="PercentualIntegradasRevisarProduto">% de <see cref="IntegradasRevisarProduto"/> sobre <see cref="TotalNotas"/>.</param>
/// <param name="PercentualPendentes">% de <see cref="Pendentes"/> sobre <see cref="TotalNotas"/>.</param>
/// <param name="PercentualNaoIntegradas">% de <see cref="NaoIntegradas"/> sobre <see cref="TotalNotas"/>.</param>
/// <param name="PercentualXmlInvalido">% de <see cref="XmlInvalido"/> sobre <see cref="TotalNotas"/>.</param>
/// <param name="DiasComAtividade">Quantidade de datas (local) distintas com ao menos um pacote.</param>
/// <param name="PacotesPorDiaSemana">Pacotes por dia da semana.</param>
/// <param name="NotasPorDiaSemana">Notas por dia da semana.</param>
/// <param name="PacotesPorFaixa">Pacotes por faixa Manhã/Tarde/Noite.</param>
/// <param name="NotasPorFaixa">Notas por faixa Manhã/Tarde/Noite.</param>
/// <param name="TempoMedioPorNotaMs">Tempo médio por nota (ms), ponderado pelas notas.</param>
/// <param name="TempoMedioPorPacoteMs">Tempo médio por pacote (ms).</param>
/// <param name="TotalChamadasWms">Total de chamadas WMS somadas no período.</param>
/// <param name="ChamadasWmsPorInterface">Chamadas WMS por interface ("I50"/"I10"/"I30").</param>
/// <param name="ChamadasWmsAproximado">Indica contagem heurística (texto do histórico).</param>
/// <param name="ErrosPorCategoria">Contagem de erros por categoria.</param>
/// <param name="ServicoDesde">Menor <see cref="EventoServico.QuandoLocal"/> conhecido, ou <c>null</c>.</param>
/// <param name="InicializacoesNoPeriodo">Eventos "servico-iniciado" dentro da janela.</param>
/// <param name="SemAtividade"><c>true</c> quando não houve pacotes no período.</param>
/// <param name="NotasPuxada">Notas de tipo PUXADA no período (inclui as da raiz).</param>
/// <param name="NotasTsp">Notas de tipo TSP no período.</param>
/// <param name="NotasRaiz">Notas que vieram soltas na raiz da entrada no período.</param>
/// <param name="TempoWmsMedioPorNotaMs">Tempo médio das chamadas ao WMS por nota (ms), só dos pacotes com medição; 0 = sem medição.</param>
public sealed record ResumoPeriodo(
    DateTimeOffset InicioPeriodo,
    DateTimeOffset FimPeriodo,
    int TotalPacotes,
    int TotalNotas,
    double MediaNotasPorPacote,
    int Integradas,
    int IntegradasRevisarProduto,
    int Pendentes,
    int NaoIntegradas,
    int XmlInvalido,
    double PercentualIntegradas,
    double PercentualIntegradasRevisarProduto,
    double PercentualPendentes,
    double PercentualNaoIntegradas,
    double PercentualXmlInvalido,
    int DiasComAtividade,
    IReadOnlyDictionary<DayOfWeek, int> PacotesPorDiaSemana,
    IReadOnlyDictionary<DayOfWeek, int> NotasPorDiaSemana,
    IReadOnlyDictionary<FaixaHorario, int> PacotesPorFaixa,
    IReadOnlyDictionary<FaixaHorario, int> NotasPorFaixa,
    long TempoMedioPorNotaMs,
    long TempoMedioPorPacoteMs,
    int TotalChamadasWms,
    IReadOnlyDictionary<string, int> ChamadasWmsPorInterface,
    bool ChamadasWmsAproximado,
    IReadOnlyDictionary<string, int> ErrosPorCategoria,
    DateTimeOffset? ServicoDesde,
    int InicializacoesNoPeriodo,
    bool SemAtividade,
    int NotasPuxada = 0,
    int NotasTsp = 0,
    int NotasRaiz = 0,
    long TempoWmsMedioPorNotaMs = 0);
