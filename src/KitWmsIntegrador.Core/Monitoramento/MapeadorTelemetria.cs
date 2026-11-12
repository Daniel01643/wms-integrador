using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;

namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Converte os registros do diário de um pacote em <see cref="RegistroTelemetria"/>. As contagens
/// por status reusam <see cref="ModeloRelatorio"/>; as chamadas ao WMS são uma heurística sobre o
/// texto do histórico (prefixos "I50"/"I10"/"I30"; I60 e desconhecidos são ignorados), por isso
/// <see cref="RegistroTelemetria.ChamadasWmsAproximado"/> é sempre verdadeiro.
/// </summary>
public static class MapeadorTelemetria
{
    private static readonly string[] InterfacesWms = { "I50", "I10", "I30" };

    /// <summary>Monta a telemetria do pacote a partir dos registros do diário.</summary>
    public static RegistroTelemetria De(
        IReadOnlyList<RegistroDiario> registros,
        DateTimeOffset inicio,
        DateTimeOffset fim,
        string idPacote,
        MedicaoTempo? medicao = null,
        long tempoRelatorioMs = 0)
    {
        ArgumentNullException.ThrowIfNull(registros);

        var linhas = registros.Select(r => r.ParaLinha()).ToList();
        var resumo = ModeloRelatorio.Montar(linhas).Resumo;

        var xmlInvalido = registros.Count(r => r.XmlInvalido);

        var tentativas = new Dictionary<string, int> { ["1"] = 0, ["2"] = 0, ["3+"] = 0 };
        foreach (var r in registros)
        {
            var faixa = r.Tentativas <= 1 ? "1" : r.Tentativas == 2 ? "2" : "3+";
            tentativas[faixa]++;
        }

        var errosPorCategoria = registros
            .Where(r => r.Categoria is not null)
            .GroupBy(r => r.Categoria!.Value.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        var chamadasWms = new Dictionary<string, int>();
        foreach (var r in registros)
        {
            foreach (var linha in r.Historico ?? Array.Empty<string>())
            {
                var texto = linha?.TrimStart() ?? string.Empty;
                var iface = InterfacesWms.FirstOrDefault(
                    i => texto.StartsWith(i, StringComparison.Ordinal));
                if (iface is not null)
                {
                    chamadasWms[iface] = chamadasWms.GetValueOrDefault(iface) + 1;
                }
            }
        }

        var tempoTotalMs = (long)(fim - inicio).TotalMilliseconds;
        var tempoMedioPorNotaMs = tempoTotalMs / Math.Max(1, resumo.Total);

        var ondas = resumo.NotasPorOnda is { Count: > 0 }
            ? resumo.NotasPorOnda
                .Select(o => new TelemetriaOnda(
                    o.Onda, o.Notas, o.Transportes,
                    medicao?.Ondas.FirstOrDefault(m => string.Equals(m.Onda, o.Onda, StringComparison.Ordinal))?.DuracaoMs ?? 0))
                .ToList()
            : null;

        return new RegistroTelemetria(
            IdPacote: idPacote,
            AberturaLocal: inicio.ToLocalTime(),
            FechamentoLocal: fim.ToLocalTime(),
            TotalNotas: resumo.Total,
            Integradas: resumo.Integradas,
            IntegradasRevisarProduto: resumo.IntegradasRevisarProduto,
            Pendentes: resumo.Pendentes,
            NaoIntegradas: resumo.NaoIntegradas,
            XmlInvalido: xmlInvalido,
            Transportes: resumo.Transportes,
            TempoTotalMs: tempoTotalMs,
            TempoMedioPorNotaMs: tempoMedioPorNotaMs,
            Tentativas: tentativas,
            ChamadasWms: chamadasWms,
            ChamadasWmsAproximado: true,
            ErrosPorCategoria: errosPorCategoria,
            NotasPuxada: resumo.NotasPuxada,
            NotasTsp: resumo.NotasTsp,
            NotasRaiz: resumo.NotasRaiz,
            EsperaJanelaMs: medicao?.EsperaJanelaMs ?? 0,
            LeituraMs: medicao?.LeituraMs ?? 0,
            IntegracaoWmsMs: medicao?.IntegracaoFaseMs ?? 0,
            ChamadasWmsMs: medicao?.ChamadasWmsMs ?? 0,
            TempoRelatorioMs: Math.Max(0L, tempoRelatorioMs),
            Ondas: ondas);
    }
}
