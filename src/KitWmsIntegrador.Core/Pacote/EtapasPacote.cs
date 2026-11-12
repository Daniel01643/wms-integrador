namespace KitWmsIntegrador.Core.Pacote;

/// <summary>
/// Tempos das etapas anteriores à integração que só o <see cref="GerenciadorPacote"/> conhece.
/// <c>null</c> = não medido (pacote retomado de uma queda): a linha some do relatório.
/// </summary>
/// <param name="EsperaJanelaMs">Da abertura do pacote até a foto da pasta (janela de coleta).</param>
/// <param name="LeituraMs">Leitura do ZIP/XML (fase de parse).</param>
public sealed record EtapasPacote(long? EsperaJanelaMs, long? LeituraMs);
