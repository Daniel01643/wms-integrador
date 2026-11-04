using KitWmsIntegrador.Core.Orquestrador;

namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>Converte <see cref="ResultadoIntegracaoNota"/> em <see cref="LinhaRelatorioErro"/>.</summary>
public static class MapeadorRelatorio
{
    /// <summary>
    /// Mapeia um resultado. O Resultado não traz horário; usa-se <paramref name="horario"/>
    /// (horário do processamento da nota).
    /// </summary>
    public static LinhaRelatorioErro De(ResultadoIntegracaoNota resultado, DateTimeOffset horario)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        var status = resultado.Estado switch
        {
            EstadoIntegracao.Integrada => StatusLinhaRelatorio.Integrada,
            EstadoIntegracao.Pendente => StatusLinhaRelatorio.Pendente,
            _ => StatusLinhaRelatorio.NaoIntegrada,
        };

        return new LinhaRelatorioErro(
            resultado.NumeroNota,
            resultado.NumeroTransporte,
            horario,
            resultado.UltimaMensagem,
            resultado.Integrada,
            resultado.Tentativas,
            resultado.IdTransacao,
            resultado.IdDocumento,
            status,
            resultado.ProdutosProvisorios,
            resultado.Historico);
    }

    /// <summary>Mapeia vários resultados com o mesmo horário.</summary>
    public static IReadOnlyList<LinhaRelatorioErro> De(
        IEnumerable<ResultadoIntegracaoNota> resultados, DateTimeOffset horario) =>
        resultados.Select(r => De(r, horario)).ToList();
}
