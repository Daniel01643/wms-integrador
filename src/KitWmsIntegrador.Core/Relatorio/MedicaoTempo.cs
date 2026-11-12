using KitWmsIntegrador.Core.Pacote;

namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>Tempo de uma onda TSP (da primeira à última nota). <c>null</c> = não medido.</summary>
/// <param name="Onda">Nome da onda (ex.: TSP-20261112-01-SOROCABA).</param>
/// <param name="DuracaoMs">Do início da primeira nota ao registro da última nota da onda.</param>
public sealed record MedicaoOnda(string Onda, long? DuracaoMs);

/// <summary>
/// Medição de tempo de um pacote, medida com o relógio do sistema (<c>IClock</c>) e nunca estimada.
/// Valores <c>null</c> são "desconhecidos" (pacote retomado/antigo) e a linha é omitida no relatório.
/// </summary>
/// <param name="Inicio">Abertura do pacote.</param>
/// <param name="Fim">Início do fechamento (a geração do relatório fica fora, ver <see cref="DadosPacote"/>).</param>
/// <param name="DuracaoTotalMs">Fim menos início.</param>
/// <param name="EsperaJanelaMs">Abertura até a foto da pasta (janela de coleta).</param>
/// <param name="LeituraMs">Leitura do ZIP/XML (fase de parse).</param>
/// <param name="IntegracaoFaseMs">Fase de integração no WMS, em tempo de parede (primeira nota até a última).</param>
/// <param name="ChamadasWmsMs">Soma das chamadas ao WMS (I50/I10/I30/I60) de todas as notas.</param>
/// <param name="Ondas">Tempo por onda (só TSP).</param>
public sealed record MedicaoTempo(
    DateTimeOffset Inicio,
    DateTimeOffset Fim,
    long DuracaoTotalMs,
    long? EsperaJanelaMs,
    long? LeituraMs,
    long? IntegracaoFaseMs,
    long? ChamadasWmsMs,
    IReadOnlyList<MedicaoOnda> Ondas)
{
    /// <summary>Deriva a medição dos registros do diário e dos tempos de fase conhecidos.</summary>
    public static MedicaoTempo De(
        IReadOnlyList<RegistroDiario> registros,
        DateTimeOffset inicio,
        DateTimeOffset fim,
        long? esperaJanelaMs,
        long? leituraMs)
    {
        ArgumentNullException.ThrowIfNull(registros);

        var notas = registros
            .Where(r => r.Status != StatusLinhaRelatorio.ArquivoIgnorado)
            .ToList();
        var medidas = notas.Where(r => r.InicioNota is not null).ToList();

        long? Intervalo(IReadOnlyCollection<RegistroDiario> grupo) =>
            grupo.Count == 0
                ? null
                : Math.Max(0L, (long)(grupo.Max(r => r.Horario) - grupo.Min(r => r.InicioNota!.Value)).TotalMilliseconds);

        var chamadas = notas.Sum(r => Math.Max(0L, r.DuracaoWmsMs));

        var ondas = medidas
            .Where(r => string.Equals(r.Tipo, TipoNotaExtensoes.RotuloTsp, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(r.Onda))
            .GroupBy(r => r.Onda, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new MedicaoOnda(g.Key, Intervalo(g.ToList())))
            .ToList();

        return new MedicaoTempo(
            inicio,
            fim,
            Math.Max(0L, (long)(fim - inicio).TotalMilliseconds),
            esperaJanelaMs,
            leituraMs,
            Intervalo(medidas),
            chamadas > 0 ? chamadas : null,
            ondas);
    }
}

/// <summary>
/// Dados do pacote que os renderizadores usam além das linhas: identificador (cabeçalho) e a
/// medição de tempo (bloco "Tempo de integração"). Ambos opcionais.
/// </summary>
/// <param name="Identificador">Id do pacote (ex.: pacote_20261112_083015).</param>
/// <param name="Medicao">Medição de tempo, ou <c>null</c> (o bloco é omitido).</param>
public sealed record DadosPacote(string? Identificador, MedicaoTempo? Medicao);
