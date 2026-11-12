namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Telemetria de um pacote fechado (uma linha em <c>telemetria.jsonl</c>). Horários em local.
/// </summary>
/// <param name="IdPacote">Identificador do pacote.</param>
/// <param name="AberturaLocal">Abertura do pacote (horário local).</param>
/// <param name="FechamentoLocal">Fechamento do pacote (horário local).</param>
/// <param name="TotalNotas">Notas no pacote.</param>
/// <param name="Integradas">Notas integradas (inclui "revisar produto").</param>
/// <param name="IntegradasRevisarProduto">Subconjunto de integradas com produto a revisar.</param>
/// <param name="Pendentes">Notas pendentes.</param>
/// <param name="NaoIntegradas">Notas não integradas.</param>
/// <param name="XmlInvalido">Arquivos com XML inválido.</param>
/// <param name="Transportes">Transportes distintos.</param>
/// <param name="TempoTotalMs">Duração do pacote em ms.</param>
/// <param name="TempoMedioPorNotaMs">Tempo médio por nota em ms.</param>
/// <param name="Tentativas">Contagem por faixa de tentativas ("1"/"2"/"3+").</param>
/// <param name="ChamadasWms">Contagem heurística de chamadas por interface ("I50"/"I10"/"I30").</param>
/// <param name="ChamadasWmsAproximado">Indica que <see cref="ChamadasWms"/> é heurístico (texto do histórico).</param>
/// <param name="ErrosPorCategoria">Contagem de erros por categoria.</param>
/// <param name="NotasPuxada">Notas de tipo PUXADA (inclui as da raiz). Linhas antigas leem 0.</param>
/// <param name="NotasTsp">Notas de tipo TSP. Linhas antigas leem 0.</param>
/// <param name="NotasRaiz">Notas que vieram soltas na raiz. Linhas antigas leem 0.</param>
/// <param name="EsperaJanelaMs">Abertura do pacote até a foto da pasta (janela de coleta). 0 = não medido.</param>
/// <param name="LeituraMs">Leitura do ZIP/XML. 0 = não medido.</param>
/// <param name="IntegracaoWmsMs">Fase de integração no WMS em tempo de parede (primeira nota até a última). 0 = não medido.</param>
/// <param name="ChamadasWmsMs">Soma das chamadas ao WMS (I50/I10/I30/I60). 0 = não medido. Base do tempo de WMS por nota.</param>
/// <param name="TempoRelatorioMs">Geração e gravação do HTML+PDF deste pacote (não aparece no relatório). 0 = não medido.</param>
/// <param name="Ondas">Tempo por onda TSP. <c>null</c> em linhas antigas e em pacotes sem TSP.</param>
public sealed record RegistroTelemetria(
    string IdPacote,
    DateTimeOffset AberturaLocal,
    DateTimeOffset FechamentoLocal,
    int TotalNotas,
    int Integradas,
    int IntegradasRevisarProduto,
    int Pendentes,
    int NaoIntegradas,
    int XmlInvalido,
    int Transportes,
    long TempoTotalMs,
    long TempoMedioPorNotaMs,
    IReadOnlyDictionary<string, int> Tentativas,
    IReadOnlyDictionary<string, int> ChamadasWms,
    bool ChamadasWmsAproximado,
    IReadOnlyDictionary<string, int> ErrosPorCategoria,
    int NotasPuxada = 0,
    int NotasTsp = 0,
    int NotasRaiz = 0,
    long EsperaJanelaMs = 0,
    long LeituraMs = 0,
    long IntegracaoWmsMs = 0,
    long ChamadasWmsMs = 0,
    long TempoRelatorioMs = 0,
    IReadOnlyList<TelemetriaOnda>? Ondas = null);

/// <summary>Tempo de uma onda TSP na telemetria.</summary>
/// <param name="Onda">Nome da onda.</param>
/// <param name="Notas">Notas da onda.</param>
/// <param name="Transportes">Transportes distintos da onda.</param>
/// <param name="DuracaoMs">Da primeira à última nota da onda (0 = não medido).</param>
public sealed record TelemetriaOnda(string Onda, int Notas, int Transportes, long DuracaoMs);
