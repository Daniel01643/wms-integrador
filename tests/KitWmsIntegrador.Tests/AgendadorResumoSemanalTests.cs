using KitWmsIntegrador.Core.Monitoramento;

namespace KitWmsIntegrador.Tests;

public class AgendadorResumoSemanalTests
{
    private static readonly TimeSpan Fuso = TimeSpan.FromHours(-3);
    private static readonly TimeOnly MeioDia = new(12, 0);

    private static DateTimeOffset Em(int ano, int mes, int dia, int hora, int min) =>
        new(ano, mes, dia, hora, min, 0, Fuso);

    [Fact]
    public void AntesDoSlot_NaoEnvia()
    {
        // Segunda 2025-06-02, 11:59 — antes do slot de segunda 12:00.
        var agora = Em(2025, 6, 2, 11, 59);

        var (deve, slot) = AgendadorResumoSemanal.DeveEnviar(agora, null, DayOfWeek.Monday, MeioDia);

        Assert.False(deve);
        Assert.Equal(Em(2025, 6, 2, 12, 0), slot);
    }

    [Fact]
    public void NoSlot_Envia()
    {
        var agora = Em(2025, 6, 2, 12, 0);

        var (deve, slot) = AgendadorResumoSemanal.DeveEnviar(agora, null, DayOfWeek.Monday, MeioDia);

        Assert.True(deve);
        Assert.Equal(Em(2025, 6, 2, 12, 0), slot);
    }

    [Fact]
    public void AposMarcarEnviado_NaoReenviaNoMesmoSlot()
    {
        var slot = Em(2025, 6, 2, 12, 0);
        // Mesmo tempo depois, no mesmo slot já enviado.
        var agora = Em(2025, 6, 2, 15, 0);

        var (deve, _) = AgendadorResumoSemanal.DeveEnviar(agora, slot, DayOfWeek.Monday, MeioDia);

        Assert.False(deve);
    }

    [Fact]
    public void SlotPerdido_EnviaNoProximoCiclo()
    {
        // Serviço voltou terça 10:00; o slot de segunda 12:00 nunca foi enviado.
        var agora = Em(2025, 6, 3, 10, 0);

        var (deve, slot) = AgendadorResumoSemanal.DeveEnviar(agora, null, DayOfWeek.Monday, MeioDia);

        Assert.True(deve);
        Assert.Equal(Em(2025, 6, 2, 12, 0), slot);
    }

    [Fact]
    public void ProximaSemana_ReabreNovoSlot()
    {
        var slotAnterior = Em(2025, 6, 2, 12, 0);
        // Segunda seguinte, 12:00.
        var agora = Em(2025, 6, 9, 12, 0);

        var (deve, slot) = AgendadorResumoSemanal.DeveEnviar(agora, slotAnterior, DayOfWeek.Monday, MeioDia);

        Assert.True(deve);
        Assert.Equal(Em(2025, 6, 9, 12, 0), slot);
    }

    [Fact]
    public void Domingo_SlotCaiNoFimDaSemanaCorrente()
    {
        // agora = quarta 2025-06-04; dia alvo domingo deve ser 2025-06-08 (fim da semana começando na segunda).
        var agora = Em(2025, 6, 4, 9, 0);

        var (_, slot) = AgendadorResumoSemanal.DeveEnviar(agora, null, DayOfWeek.Sunday, MeioDia);

        Assert.Equal(Em(2025, 6, 8, 12, 0), slot);
        Assert.Equal(DayOfWeek.Sunday, slot.DayOfWeek);
    }
}
