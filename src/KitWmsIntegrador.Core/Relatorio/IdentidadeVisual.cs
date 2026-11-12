using KitWmsIntegrador.Core.Pacote;

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

    /// <summary>Cinza neutro semântico (Arquivo ignorado / não-XML) — fora da paleta da marca.</summary>
    public const string CinzaNeutro = "#6B7280";

    /// <summary>
    /// Título do relatório. Renomeado de "Relatório de Erros de Integração" (o relatório cobre todas as
    /// notas, não só as com erro). Para reverter, basta voltar este valor.
    /// </summary>
    public const string Titulo = "Relatório de Integração";

    /// <summary>Nome do recurso embutido com a logo Friozem (versão negativa, para fundo azul).</summary>
    public const string RecursoLogo = "logo-friozem-negativo.png";

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

    private static readonly Lazy<byte[]?> LogoEmbutido = new(LerLogoEmbutida);

    /// <summary>Logo Friozem embutida no assembly (PNG negativo); <c>null</c> se o recurso faltar. Nunca lança.</summary>
    public static byte[]? LogoEmbutidoPng => LogoEmbutido.Value;

    private static byte[]? LerLogoEmbutida()
    {
        try
        {
            using var stream = typeof(IdentidadeVisual).Assembly.GetManifestResourceStream(RecursoLogo);
            if (stream is null)
            {
                return null;
            }

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var bytes = ms.ToArray();
            return bytes.Length > AssinaturaPng.Length && bytes.AsSpan(0, AssinaturaPng.Length).SequenceEqual(AssinaturaPng)
                ? bytes
                : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolve a logo: o PNG de <paramref name="caminho"/> (Relatorio:LogoPath) se válido; senão a logo
    /// embutida; <c>null</c> só se nem ela existir (os renderizadores caem para o texto "Friozem").
    /// </summary>
    public static byte[]? ObterLogo(string? caminho) => LerLogoPng(caminho) ?? LogoEmbutidoPng;

    /// <summary>Tipo do pacote para o cabeçalho: "PUXADA", "TSP" ou "PUXADA + TSP" (sem notas: "PUXADA").</summary>
    public static string RotuloTipoPacote(ResumoRelatorio resumo)
    {
        ArgumentNullException.ThrowIfNull(resumo);
        return resumo.NotasTsp > 0 && resumo.NotasPuxada > 0 ? "PUXADA + TSP"
            : resumo.NotasTsp > 0 ? "TSP"
            : "PUXADA";
    }

    /// <summary>
    /// Itens da linha de informações do cabeçalho (HTML e PDF): pacote e tipo, geração e, quando há,
    /// o período de coleta. Cada item é (rótulo, valor).
    /// </summary>
    public static IReadOnlyList<(string Rotulo, string Valor)> ItensInformacao(
        ResumoRelatorio resumo, DateTimeOffset geradoEm, PeriodoColeta? periodo, DadosPacote? pacote)
    {
        var tipo = RotuloTipoPacote(resumo);
        var itens = new List<(string, string)>
        {
            string.IsNullOrWhiteSpace(pacote?.Identificador)
                ? ("Tipo:", tipo)
                : ("Pacote", pacote!.Identificador + " · " + tipo),
            ("Gerado em", Horario(geradoEm)),
        };
        if (periodo is not null)
        {
            itens.Add(("Coleta:", Horario(periodo.PrimeiraGravacao) + " a " + Horario(periodo.UltimaGravacao)));
        }

        return itens;
    }

    /// <summary>Plural simples: "1 nota" / "3 notas".</summary>
    public static string Plural(int n, string singular, string plural) =>
        n.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + (n == 1 ? singular : plural);
}
