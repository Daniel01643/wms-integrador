namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Agendador puro (sem I/O) do relatório diário da I51/I60. O slot é "hoje às <c>hora</c>"; é devido
/// quando já passou e o último slot enviado é anterior a ele. Recupera slot perdido no mesmo dia,
/// nunca envia mais de um por dia (parada de vários dias gera um único relatório) e reabre no dia seguinte.
/// </summary>
public static class AgendadorRelatorioDiario
{
    /// <summary>Avalia o agendamento em <paramref name="agora"/> (horário local).</summary>
    public static (bool Deve, DateTimeOffset Slot) DeveEnviar(DateTimeOffset agora, DateTimeOffset? ultimoSlotEnviado, TimeOnly hora)
    {
        var slot = new DateTimeOffset(agora.Date + hora.ToTimeSpan(), agora.Offset);
        var deve = agora >= slot && (ultimoSlotEnviado is null || ultimoSlotEnviado.Value < slot);
        return (deve, slot);
    }
}
