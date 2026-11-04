namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>Identidade visual Friozem e utilitários compartilhados pelos renderizadores.</summary>
internal static class IdentidadeVisual
{
    public const string Azul = "#17479E";
    public const string AzulClaro = "#54B6E7";
    public const string Verde = "#009A83";
    public const string Texto = "#333333";
    public const string Cinza = "#C1C1C1";

    /// <summary>Âmbar semântico (Pendente) — fora da paleta da marca.</summary>
    public const string Ambar = "#F5A623";

    /// <summary>Vermelho semântico (Não integrada) — fora da paleta da marca.</summary>
    public const string Vermelho = "#D32F2F";

    public const string Titulo = "Relatório de Erros de Integração";

    /// <summary>
    /// Formato de data/hora exibido no relatório: horário LOCAL do servidor, sem o sufixo de
    /// offset (ex.: "04/11/2026 14:56:20"). Valores em <see cref="DateTimeOffset"/> devem ser
    /// convertidos para local com <see cref="Horario"/> antes de formatar.
    /// </summary>
    public const string FormatoHorario = "dd/MM/yyyy HH:mm:ss";

    /// <summary>Formata um instante no horário local do servidor, sem offset UTC.</summary>
    public static string Horario(DateTimeOffset instante) =>
        instante.ToLocalTime().ToString(FormatoHorario, System.Globalization.CultureInfo.InvariantCulture);

    private static readonly byte[] AssinaturaPng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>
    /// Lê o logo PNG configurado. Devolve <c>null</c> (sem lançar) se o caminho for vazio,
    /// inexistente, ilegível ou não for um PNG válido.
    /// </summary>
    public static byte[]? LerLogoPng(string? caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho))
        {
            return null;
        }

        try
        {
            if (!File.Exists(caminho) ||
                !string.Equals(Path.GetExtension(caminho), ".png", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var bytes = File.ReadAllBytes(caminho);
            return bytes.Length > AssinaturaPng.Length && bytes.AsSpan(0, AssinaturaPng.Length).SequenceEqual(AssinaturaPng)
                ? bytes
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Plural simples: "1 nota" / "3 notas".</summary>
    public static string Plural(int n, string singular, string plural) =>
        n.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + (n == 1 ? singular : plural);
}
