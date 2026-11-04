namespace KitWmsIntegrador.Core.Pacote;

/// <summary>
/// Horário da primeira e da última gravação (LastWriteTime) dos arquivos que formam o pacote
/// ("foto" da pasta). Usado no log, no corpo do e-mail e no cabeçalho do relatório.
/// </summary>
public sealed record PeriodoColeta(DateTimeOffset PrimeiraGravacao, DateTimeOffset UltimaGravacao)
{
    /// <summary>
    /// Calcula o período (em horário local) a partir do LastWriteTime dos arquivos.
    /// Arquivos inexistentes/inacessíveis são ignorados; devolve null se nenhum puder ser lido.
    /// </summary>
    public static PeriodoColeta? Calcular(IEnumerable<string> arquivos)
    {
        ArgumentNullException.ThrowIfNull(arquivos);
        DateTime? min = null;
        DateTime? max = null;
        foreach (var arquivo in arquivos)
        {
            DateTime gravacao;
            try
            {
                var info = new FileInfo(arquivo);
                if (!info.Exists)
                {
                    continue;
                }

                gravacao = info.LastWriteTimeUtc;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (min is null || gravacao < min)
            {
                min = gravacao;
            }

            if (max is null || gravacao > max)
            {
                max = gravacao;
            }
        }

        return min is null || max is null
            ? null
            : new PeriodoColeta(Local(min.Value), Local(max.Value));
    }

    private static DateTimeOffset Local(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToLocalTime();
}
