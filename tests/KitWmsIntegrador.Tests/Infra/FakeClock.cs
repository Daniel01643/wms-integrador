using KitWmsIntegrador.Core.Tempo;

namespace KitWmsIntegrador.Tests.Infra;

/// <summary>Relógio controlável para testes de cache/expiração de token.</summary>
public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset inicio) => UtcNow = inicio;

    public DateTimeOffset UtcNow { get; private set; }

    public void Avancar(TimeSpan delta) => UtcNow += delta;
}
