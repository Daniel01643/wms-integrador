namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Evento de saúde do serviço (uma linha em <c>servico.jsonl</c>), ex.: partida do Worker.
/// </summary>
/// <param name="Tipo">Tipo do evento (ex.: "servico-iniciado").</param>
/// <param name="QuandoLocal">Momento do evento (horário local).</param>
/// <param name="Versao">Versão do assembly do serviço.</param>
public sealed record EventoServico(
    string Tipo,
    DateTimeOffset QuandoLocal,
    string Versao);
