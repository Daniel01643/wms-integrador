using System.Globalization;

namespace KitWmsIntegrador.Core.Configuracao;

/// <summary>
/// Opções do relatório diário da I51/I60 (seção "RelatorioI60"). Só atua com
/// <c>I51:Habilitada=true</c>. Override por env: <c>RelatorioI60__Hora</c> etc.
/// </summary>
public sealed class OpcoesRelatorioI60
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string Secao = "RelatorioI60";

    private static readonly TimeOnly HoraPadrao = new(12, 0);

    /// <summary>Liga o relatório diário (só atua com a I51 ligada).</summary>
    public bool Habilitado { get; set; } = true;

    /// <summary>Hora do envio, "HH:mm" local. Inválida cai para 12:00.</summary>
    public string Hora { get; set; } = "12:00";

    /// <summary>Prefixo do assunto do e-mail.</summary>
    public string AssuntoPrefixo { get; set; } = "[KitWms]";

    /// <summary>Arquivo de destinatários (vazio = <c>&lt;telemetria&gt;/destinatarios-i60.txt</c>).</summary>
    public string CaminhoDestinatarios { get; set; } = string.Empty;

    /// <summary>Lista as notas sem associação como candidatas à devolução pela I40.</summary>
    public bool ListarCandidatasI40 { get; set; } = true;

    /// <summary>Gera o relatório mesmo sem nenhuma atividade.</summary>
    public bool EnviarQuandoVazio { get; set; } = false;

    /// <summary>Teto de linhas por seção (10 a 5000).</summary>
    public int MaxLinhasPorSecao { get; set; } = 300;

    /// <summary>Hora parseada de <see cref="Hora"/>; cai para 12:00 se inválida.</summary>
    public TimeOnly HoraDoDia =>
        TimeOnly.TryParseExact(Hora, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var h) ? h : HoraPadrao;

    /// <summary>Devolve a lista de problemas da configuração (vazia = válida).</summary>
    public IReadOnlyList<string> Validar()
    {
        var p = new List<string>();
        if (!TimeOnly.TryParseExact(Hora, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            p.Add($"RelatorioI60:Hora deve estar no formato HH:mm (valor atual: '{Hora}'; em execução vale 12:00).");
        }

        if (MaxLinhasPorSecao < 10 || MaxLinhasPorSecao > 5000)
        {
            p.Add($"RelatorioI60:MaxLinhasPorSecao deve estar entre 10 e 5000 (valor atual: {MaxLinhasPorSecao}).");
        }

        if ((AssuntoPrefixo?.Length ?? 0) > 40 || (AssuntoPrefixo?.IndexOfAny(new[] { '\r', '\n' }) ?? -1) >= 0)
        {
            p.Add("RelatorioI60:AssuntoPrefixo deve ter até 40 caracteres e nenhuma quebra de linha.");
        }

        return p;
    }
}
