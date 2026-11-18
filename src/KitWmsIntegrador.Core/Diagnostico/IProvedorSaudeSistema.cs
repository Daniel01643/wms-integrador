namespace KitWmsIntegrador.Core.Diagnostico;

/// <summary>Medidas do ambiente (disco e pastas) para a seção de saúde. Permite fake nos testes.</summary>
public interface IProvedorSaudeSistema
{
    /// <summary>Espaço livre, em bytes, do drive que contém <paramref name="caminho"/>; <c>null</c> se não der para medir.</summary>
    long? EspacoLivre(string caminho);

    /// <summary>Tamanho total, em bytes, dos arquivos sob <paramref name="caminho"/>; <c>null</c> se ausente, com erro ou acima do teto de tempo.</summary>
    long? TamanhoPasta(string caminho);
}

/// <summary>
/// Implementação real: <see cref="DriveInfo"/> e enumeração de arquivos com teto de 20 s por pasta e tolerância a
/// erro (nunca lança).
/// </summary>
public sealed class ProvedorSaudeSistema : IProvedorSaudeSistema
{
    /// <summary>Teto de tempo para somar uma pasta.</summary>
    public static readonly TimeSpan TetoPorPasta = TimeSpan.FromSeconds(20);

    /// <inheritdoc />
    public long? EspacoLivre(string caminho)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(caminho))
            {
                return null;
            }

            var raiz = Path.GetPathRoot(Path.GetFullPath(caminho));
            return string.IsNullOrEmpty(raiz) ? null : new DriveInfo(raiz).AvailableFreeSpace;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public long? TamanhoPasta(string caminho)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(caminho) || !Directory.Exists(caminho))
            {
                return null;
            }

            var limite = DateTime.UtcNow + TetoPorPasta;
            long total = 0;
            var opcoes = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var arquivo in new DirectoryInfo(caminho).EnumerateFiles("*", opcoes))
            {
                if (DateTime.UtcNow > limite)
                {
                    return null;
                }

                try
                {
                    total += arquivo.Length;
                }
                catch (Exception)
                {
                    // arquivo sumiu durante a soma
                }
            }

            return total;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
