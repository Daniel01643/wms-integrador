using System.Globalization;

namespace KitWmsIntegrador.Core.Configuracao;

/// <summary>
/// Opções do "Diagnóstico agente integrador" (seção "Diagnostico"): relatório técnico diário só para a TI e
/// destinatários do alerta de autenticação. Override por env: <c>Diagnostico__HoraDoDia</c> etc.
/// </summary>
public sealed class OpcoesDiagnostico
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string Secao = "Diagnostico";

    private static readonly TimeOnly HoraPadrao = new(14, 0);

    /// <summary>Liga o relatório diário (o alerta de autenticação não depende deste valor).</summary>
    public bool Habilitado { get; set; } = true;

    /// <summary>Hora do envio, "HH:mm" local. Inválida cai para 14:00.</summary>
    public string HoraDoDia { get; set; } = "14:00";

    /// <summary>Hora parseada de <see cref="HoraDoDia"/>; cai para 14:00 se inválida.</summary>
    public TimeOnly Horario =>
        TimeOnly.TryParseExact(HoraDoDia, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var h) ? h : HoraPadrao;

    /// <summary>Prefixo do assunto do e-mail.</summary>
    public string AssuntoPrefixo { get; set; } = "[KitWms]";

    /// <summary>Arquivo de destinatários (vazio = <c>&lt;telemetria&gt;/destinatarios-diagnostico.txt</c>).</summary>
    public string CaminhoDestinatarios { get; set; } = string.Empty;

    /// <summary>Destinatários separados por ';', usados quando o txt está ausente ou vazio.</summary>
    public string Destinatarios { get; set; } = "ti@friozem.com.br";

    /// <summary>Teto de linhas por seção do relatório (10 a 1000).</summary>
    public int MaxLinhasPorSecao { get; set; } = 100;

    /// <summary>Quantidade máxima de grupos de mensagem listados por tabela.</summary>
    public int MaxGruposMensagem { get; set; } = 10;

    /// <summary>
    /// Intervalo, em minutos, da amostra de memória do processo e do servidor (padrão 15). 0 desliga a amostragem
    /// (e o alerta de memória). Valores de 1 a 1440.
    /// </summary>
    public int IntervaloAmostraMinutos { get; set; } = 15;

    /// <summary>
    /// Alerta por e-mail (só destinatários de diagnóstico) quando a memória física livre do servidor ficar abaixo deste
    /// valor, em MB, por 2 amostras seguidas (padrão 500; no máximo 1 e-mail por hora). 0 desliga o alerta.
    /// </summary>
    public int AlertaMemoriaLivreMb { get; set; } = 500;

    /// <summary>Destinatários já separados, sem vazios.</summary>
    public IReadOnlyList<string> ListaDestinatarios =>
        (Destinatarios ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Devolve a lista de problemas da configuração (vazia = válida).</summary>
    public IReadOnlyList<string> Validar()
    {
        var p = new List<string>();
        if (!TimeOnly.TryParseExact(HoraDoDia, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            p.Add($"Diagnostico:HoraDoDia deve estar no formato HH:mm (valor atual: '{HoraDoDia}'; em execução vale 14:00).");
        }

        if (MaxLinhasPorSecao < 10 || MaxLinhasPorSecao > 1000)
        {
            p.Add($"Diagnostico:MaxLinhasPorSecao deve estar entre 10 e 1000 (valor atual: {MaxLinhasPorSecao}).");
        }

        if (MaxGruposMensagem < 1 || MaxGruposMensagem > 100)
        {
            p.Add($"Diagnostico:MaxGruposMensagem deve estar entre 1 e 100 (valor atual: {MaxGruposMensagem}).");
        }

        if (IntervaloAmostraMinutos < 0 || IntervaloAmostraMinutos > 1440)
        {
            p.Add($"Diagnostico:IntervaloAmostraMinutos deve estar entre 0 (desliga) e 1440 (valor atual: {IntervaloAmostraMinutos}).");
        }

        if (AlertaMemoriaLivreMb < 0 || AlertaMemoriaLivreMb > 1_048_576)
        {
            p.Add($"Diagnostico:AlertaMemoriaLivreMb deve estar entre 0 (desliga) e 1048576 (valor atual: {AlertaMemoriaLivreMb}).");
        }

        if ((AssuntoPrefixo?.Length ?? 0) > 40 || (AssuntoPrefixo?.IndexOfAny(new[] { '\r', '\n' }) ?? -1) >= 0)
        {
            p.Add("Diagnostico:AssuntoPrefixo deve ter até 40 caracteres e nenhuma quebra de linha.");
        }

        return p;
    }
}
