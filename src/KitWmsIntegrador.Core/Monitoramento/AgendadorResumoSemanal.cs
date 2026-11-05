namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Agendador puro (sem I/O, testável) do resumo semanal. Decide, para o instante atual, se o
/// envio do slot da semana corrente já é devido e ainda não foi feito. É idempotente (não
/// reenvia o mesmo slot) e recupera slot perdido.
/// </summary>
public static class AgendadorResumoSemanal
{
    /// <summary>
    /// Avalia o agendamento no instante <paramref name="agora"/> (horário local).
    /// </summary>
    /// <param name="agora">Instante atual, em horário local.</param>
    /// <param name="ultimoSlotEnviado">Último slot já enviado (persistido), ou <c>null</c> se nunca houve envio.</param>
    /// <param name="dia">Dia da semana configurado para o envio.</param>
    /// <param name="hora">Hora do dia configurada para o envio (horário local).</param>
    /// <returns>
    /// <c>Deve</c> = <c>true</c> quando <paramref name="agora"/> já passou do <c>Slot</c> da semana
    /// corrente E aquele slot ainda não foi enviado (<paramref name="ultimoSlotEnviado"/> é
    /// <c>null</c> ou anterior ao slot). <c>Slot</c> é o instante do <paramref name="dia"/>/
    /// <paramref name="hora"/> na semana de <paramref name="agora"/>, no mesmo offset local.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Slot perdido:</b> se o serviço estava parado no horário do slot e só voltou depois
    /// (ainda na mesma semana), o slot permanece "não enviado"; no primeiro ciclo após a volta
    /// <c>Deve</c> será <c>true</c> e o resumo é emitido com atraso. Assim nenhuma semana é
    /// silenciosamente pulada por uma parada curta.
    /// </para>
    /// <para>
    /// <b>Idempotência:</b> após <see cref="MarcaUltimoResumo.Marcar"/> gravar o slot, as demais
    /// avaliações da mesma semana retornam <c>Deve</c> = <c>false</c>. Na semana seguinte um novo
    /// slot (posterior ao marcado) reabre o envio.
    /// </para>
    /// </remarks>
    public static (bool Deve, DateTimeOffset Slot) DeveEnviar(
        DateTimeOffset agora, DateTimeOffset? ultimoSlotEnviado, DayOfWeek dia, TimeOnly hora)
    {
        var slot = SlotDaSemana(agora, dia, hora);
        var deve = agora >= slot && (ultimoSlotEnviado is null || ultimoSlotEnviado.Value < slot);
        return (deve, slot);
    }

    /// <summary>
    /// Instante do <paramref name="dia"/>/<paramref name="hora"/> na semana corrente de
    /// <paramref name="agora"/> (segunda a domingo), preservando o offset local de
    /// <paramref name="agora"/>.
    /// </summary>
    private static DateTimeOffset SlotDaSemana(DateTimeOffset agora, DayOfWeek dia, TimeOnly hora)
    {
        // Diferença de dias de 'agora' até o início da semana (segunda-feira) e daí até o dia alvo,
        // tratando domingo (0) como o último dia (6) para a semana começar na segunda.
        var diaAtual = ((int)agora.DayOfWeek + 6) % 7;
        var diaAlvo = ((int)dia + 6) % 7;
        var deltaDias = diaAlvo - diaAtual;

        var dataSlot = agora.Date.AddDays(deltaDias) + hora.ToTimeSpan();
        return new DateTimeOffset(dataSlot, agora.Offset);
    }
}
