namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Parsing puro de uma lista de e-mails em arquivo txt: um e-mail por linha, espaços aparados,
/// linhas em branco e iniciadas por '#' ignoradas. Relê o arquivo a cada chamada (sem cache),
/// para refletir edições a quente. Arquivo ausente, vazio ou ilegível devolve lista VAZIA — a
/// decisão de fallback é de quem chama (cada fonte aplica a sua).
/// </summary>
public static class LeitorListaEmails
{
    /// <summary>
    /// Lê o arquivo em <paramref name="caminho"/> e devolve os e-mails encontrados (um por linha,
    /// com <c>Trim</c>, ignorando linhas em branco e comentários). Devolve lista vazia quando o
    /// arquivo está ausente, vazio ou não pôde ser lido.
    /// </summary>
    public static IReadOnlyList<string> Ler(string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho))
        {
            return Array.Empty<string>();
        }

        try
        {
            var emails = new List<string>();
            foreach (var bruta in File.ReadAllLines(caminho))
            {
                var linha = bruta.Trim();
                if (linha.Length == 0 || linha.StartsWith('#'))
                {
                    continue;
                }

                emails.Add(linha);
            }

            return emails;
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
