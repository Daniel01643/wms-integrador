namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Faixa do dia (horário local) em que um pacote foi fechado. Usada na distribuição de
/// frequência do resumo semanal.
/// </summary>
public enum FaixaHorario
{
    /// <summary>Manhã: das 06:00 às 11:59 (inclusive nas duas bordas).</summary>
    Manha,

    /// <summary>Tarde: das 12:00 às 17:59 (inclusive nas duas bordas).</summary>
    Tarde,

    /// <summary>Noite: das 18:00 às 05:59 (atravessa a meia-noite).</summary>
    Noite,
}

/// <summary>Classifica um instante local em <see cref="FaixaHorario"/> com bordas FIXAS.</summary>
public static class FaixaHorarioExtensoes
{
    /// <summary>
    /// Classifica <paramref name="local"/> pela hora local:
    /// <list type="bullet">
    /// <item><description>Manhã: 06:00–11:59 (hora &gt;= 6 e &lt; 12).</description></item>
    /// <item><description>Tarde: 12:00–17:59 (hora &gt;= 12 e &lt; 18).</description></item>
    /// <item><description>Noite: 18:00–05:59 (hora &gt;= 18 ou &lt; 6), atravessando a meia-noite.</description></item>
    /// </list>
    /// As bordas 05:59, 06:00, 11:59, 12:00, 17:59 e 18:00 seguem exatamente essas regras.
    /// </summary>
    public static FaixaHorario De(DateTimeOffset local)
    {
        var hora = local.Hour;
        return hora switch
        {
            >= 6 and < 12 => FaixaHorario.Manha,
            >= 12 and < 18 => FaixaHorario.Tarde,
            _ => FaixaHorario.Noite,
        };
    }
}
