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
    IReadOnlyDictionary<string, int> ErrosPorCategoria);
