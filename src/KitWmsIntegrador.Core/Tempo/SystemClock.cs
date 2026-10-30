namespace KitWmsIntegrador.Core.Tempo;

/// <summary>Implementação padrão de <see cref="IClock"/> baseada no relógio do sistema.</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
