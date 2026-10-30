namespace KitWmsIntegrador.Core.Tempo;

/// <summary>
/// Abstração de relógio. Permite injetar o tempo atual (e controlá-lo nos testes),
/// usada pelo cache de token do cliente WMS para decidir expiração/renovação.
/// </summary>
public interface IClock
{
    /// <summary>Instante atual em UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
