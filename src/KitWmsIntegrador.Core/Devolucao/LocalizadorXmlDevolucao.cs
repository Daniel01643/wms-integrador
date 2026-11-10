namespace KitWmsIntegrador.Core.Devolucao;

/// <summary>
/// Localiza o XML da venda original pela chave de acesso para a devolução (Plano B).
/// Procura <c>NFE&lt;chave&gt;.XML</c> (sem diferenciar maiúsculas) em
/// <c>&lt;Processados&gt;\pacote_*\</c> e diretamente em <c>Processados</c>; havendo mais de
/// um, usa o mais recente por <c>LastWriteTimeUtc</c>. NÃO procura em Erro (nota que não
/// integrou não existe no WMS). Somente leitura.
/// </summary>
public sealed class LocalizadorXmlDevolucao
{
    private readonly string _pastaProcessados;

    /// <param name="pastaProcessados">Caminho completo da pasta Processados.</param>
    public LocalizadorXmlDevolucao(string pastaProcessados)
    {
        if (string.IsNullOrWhiteSpace(pastaProcessados))
        {
            throw new ArgumentException("Pasta Processados não informada.", nameof(pastaProcessados));
        }

        _pastaProcessados = pastaProcessados;
    }

    /// <summary>
    /// Devolve o caminho do XML mais recente da chave, ou <c>null</c> se não existir.
    /// Lança <see cref="ArgumentException"/> se a chave for inválida (ver <see cref="ValidarChave"/>).
    /// </summary>
    public string? Localizar(string chave)
    {
        var erro = ValidarChave(chave);
        if (erro is not null)
        {
            throw new ArgumentException(erro, nameof(chave));
        }

        if (!Directory.Exists(_pastaProcessados))
        {
            return null;
        }

        var nomeAlvo = $"NFE{chave.Trim()}.XML";
        var pastas = new List<string> { _pastaProcessados };
        pastas.AddRange(Directory.EnumerateDirectories(_pastaProcessados, "pacote_*", SearchOption.TopDirectoryOnly));

        string? melhor = null;
        var melhorData = DateTime.MinValue;
        foreach (var pasta in pastas)
        {
            foreach (var arquivo in Directory.EnumerateFiles(pasta, "*", SearchOption.TopDirectoryOnly))
            {
                if (!string.Equals(Path.GetFileName(arquivo), nomeAlvo, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var data = File.GetLastWriteTimeUtc(arquivo);
                if (melhor is null || data > melhorData)
                {
                    melhor = arquivo;
                    melhorData = data;
                }
            }
        }

        return melhor;
    }

    /// <summary>
    /// Valida a chave: 44 dígitos numéricos e dígito verificador (módulo 11).
    /// Devolve <c>null</c> se válida ou a mensagem do problema.
    /// </summary>
    public static string? ValidarChave(string? chave)
    {
        var c = chave?.Trim() ?? string.Empty;
        if (c.Length != 44 || !c.All(char.IsAsciiDigit))
        {
            return "A chave de acesso deve ter 44 dígitos numéricos.";
        }

        var soma = 0;
        var peso = 2;
        for (var i = 42; i >= 0; i--)
        {
            soma += (c[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }

        var resto = soma % 11;
        var dv = resto < 2 ? 0 : 11 - resto;
        return dv == c[43] - '0' ? null : "Dígito verificador da chave de acesso inválido.";
    }
}
