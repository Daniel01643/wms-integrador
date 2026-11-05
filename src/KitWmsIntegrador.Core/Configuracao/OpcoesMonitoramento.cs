using System.Globalization;

namespace KitWmsIntegrador.Core.Configuracao;

/// <summary>
/// Opções da telemetria/monitoramento do agente (seção "Monitoramento"). O agendamento do
/// resumo roda dentro do Worker (sem Task Scheduler). A LISTA de destinatários NÃO fica aqui:
/// vive num arquivo txt fora do git (<see cref="CaminhoDestinatarios"/>), editável a quente.
/// </summary>
public sealed class OpcoesMonitoramento
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string Secao = "Monitoramento";

    /// <summary>Hora padrão (fallback) quando <see cref="Hora"/> é inválida.</summary>
    private static readonly TimeOnly HoraPadrao = new(12, 0);

    /// <summary>Liga a coleta de telemetria e o resumo semanal. Default ligado.</summary>
    public bool Habilitado { get; set; } = true;

    /// <summary>Dia da semana do envio do resumo (horário local do servidor). Default segunda-feira.</summary>
    public DayOfWeek DiaDaSemana { get; set; } = DayOfWeek.Monday;

    /// <summary>Hora do envio no formato "HH:mm" (horário local). Default "12:00".</summary>
    public string Hora { get; set; } = "12:00";

    /// <summary>Janela do resumo, em dias. Default 7.</summary>
    public int PeriodoDias { get; set; } = 7;

    /// <summary>Prefixo do assunto do e-mail de resumo. Default "[KitWms]".</summary>
    public string AssuntoPrefixo { get; set; } = "[KitWms]";

    /// <summary>Pasta da telemetria. Vazio = <c>&lt;Estado&gt;/telemetria</c>.</summary>
    public string CaminhoTelemetria { get; set; } = string.Empty;

    /// <summary>Arquivo de destinatários do resumo. Vazio = <c>&lt;telemetria&gt;/destinatarios.txt</c>.</summary>
    public string CaminhoDestinatarios { get; set; } = string.Empty;

    /// <summary>Hora do dia parseada de <see cref="Hora"/>; cai para 12:00 se o valor for inválido.</summary>
    public TimeOnly HoraDoDia =>
        TimeOnly.TryParse(Hora, CultureInfo.InvariantCulture, out var hora) ? hora : HoraPadrao;
}
