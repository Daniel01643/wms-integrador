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
    /// Procura notas pelo número + série SEM abrir os XMLs: número, série e CNPJ do emitente são
    /// lidos da própria chave, que é o nome do arquivo (<c>NFE&lt;chave&gt;.XML</c>). Mesmas pastas e
    /// retenção do <see cref="Localizar"/> (Processados e <c>pacote_*</c>, nunca Erro). Só entram
    /// chaves válidas (44 dígitos + DV) do CNPJ do depositante; número e série são comparados como
    /// inteiros (zeros à esquerda não contam). A mesma chave em mais de um pacote vale uma vez
    /// (a mais recente). Resultado ordenado do mais recente para o mais antigo.
    /// </summary>
    /// <param name="numero">Número da nota (já validado, ver <see cref="ValidadorChavesDevolucao.ValidarNumeroSerie"/>).</param>
    /// <param name="serie">Série (já validada).</param>
    /// <param name="cnpjEmitente">CNPJ do depositante; só os dígitos são comparados.</param>
    public IReadOnlyList<OpcaoNotaDevolucao> BuscarPorNumeroSerie(int numero, int serie, string cnpjEmitente)
    {
        var cnpj = ValidadorChavesDevolucao.SoDigitos(cnpjEmitente);
        if (cnpj.Length != 14)
        {
            throw new ArgumentException("CNPJ do emitente inválido.", nameof(cnpjEmitente));
        }

        if (!Directory.Exists(_pastaProcessados))
        {
            return Array.Empty<OpcaoNotaDevolucao>();
        }

        var pastas = new List<string> { _pastaProcessados };
        pastas.AddRange(Directory.EnumerateDirectories(_pastaProcessados, "pacote_*", SearchOption.TopDirectoryOnly));

        var porChave = new Dictionary<string, (DateTime Data, string Pacote)>(StringComparer.Ordinal);
        foreach (var pasta in pastas)
        {
            var nomePacote = Path.GetFileName(pasta);
            foreach (var arquivo in Directory.EnumerateFiles(pasta, "*", SearchOption.TopDirectoryOnly))
            {
                var chave = ChaveDoNomeDoArquivo(Path.GetFileName(arquivo));
                if (chave is null
                    || ValidarChave(chave) is not null
                    || ValidadorChavesDevolucao.CnpjDaChave(chave) != cnpj
                    || ValidadorChavesDevolucao.NumeroDaChave(chave) != numero
                    || ValidadorChavesDevolucao.SerieDaChave(chave) != serie)
                {
                    continue;
                }

                var data = File.GetLastWriteTimeUtc(arquivo);
                if (!porChave.TryGetValue(chave, out var atual) || data > atual.Data)
                {
                    porChave[chave] = (data, nomePacote);
                }
            }
        }

        return porChave
            .OrderByDescending(p => p.Value.Data)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new OpcaoNotaDevolucao
            {
                Chave = p.Key,
                Numero = ValidadorChavesDevolucao.NotaDaChave(p.Key),
                Serie = ValidadorChavesDevolucao.SerieDaChave(p.Key)!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                DataArquivo = p.Value.Data.ToLocalTime().ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                Pacote = p.Value.Pacote,
            })
            .ToList();
    }

    /// <summary>Chave (44 dígitos) do nome <c>NFE&lt;chave&gt;.XML</c> (sem diferenciar maiúsculas); <c>null</c> se não casar.</summary>
    private static string? ChaveDoNomeDoArquivo(string nome)
    {
        const string prefixo = "NFE";
        const string sufixo = ".XML";
        if (nome.Length != prefixo.Length + 44 + sufixo.Length
            || !nome.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase)
            || !nome.EndsWith(sufixo, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var chave = nome.Substring(prefixo.Length, 44);
        return chave.All(char.IsAsciiDigit) ? chave : null;
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
