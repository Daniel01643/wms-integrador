namespace KitWmsIntegrador.Core.Pacote;

/// <summary>
/// Tempos das etapas anteriores à integração que só o <see cref="GerenciadorPacote"/> conhece.
/// <c>null</c> = não medido (pacote retomado de uma queda): a linha some do relatório.
/// </summary>
/// <param name="EsperaJanelaMs">Da abertura do pacote até a foto da pasta (janela de coleta).</param>
/// <param name="LeituraMs">Leitura do ZIP/XML (fase de parse).</param>
/// <param name="FalhasGetToken">Falhas definitivas do GetToken ocorridas durante o pacote.</param>
/// <param name="AbortadoPorAutenticacao">O pacote foi interrompido por falha de autenticação.</param>
/// <param name="FalhasPorBackend">Tentativas de GetToken que falharam durante o pacote, por servidor (hash do cookie de afinidade).</param>
public sealed record EtapasPacote(
    long? EsperaJanelaMs, long? LeituraMs, int FalhasGetToken = 0, bool AbortadoPorAutenticacao = false,
    IReadOnlyDictionary<string, long>? FalhasPorBackend = null);
