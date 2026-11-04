namespace KitWmsIntegrador.Core.Configuracao;

/// <summary>Como as notas com erro são organizadas dentro da pasta de erro.</summary>
public enum OrganizacaoErro
{
    /// <summary><c>Erro\&lt;Categoria&gt;\arquivo.xml</c> (padrão).</summary>
    PorCategoria,

    /// <summary><c>Erro\&lt;nome-do-arquivo-sem-extensão&gt;\arquivo.xml</c>.</summary>
    PorArquivo,

    /// <summary><c>Erro\arquivo.xml</c>.</summary>
    Plana,
}

/// <summary>
/// Pastas do fluxo de arquivos (seção "Pastas"). Caminhos relativos de Processados/Erro/Estado
/// são resolvidos contra a Entrada.
/// </summary>
public sealed class OpcoesPasta
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string Secao = "Pastas";

    /// <summary>Pasta de entrada dos XMLs.</summary>
    public string Entrada { get; set; } = string.Empty;

    /// <summary>Pasta dos arquivos processados (relativa à Entrada ou absoluta).</summary>
    public string Processados { get; set; } = "Processados";

    /// <summary>Pasta dos arquivos com erro (relativa à Entrada ou absoluta).</summary>
    public string Erro { get; set; } = "Erro";

    /// <summary>Pasta de estado/diário do pacote (relativa à Entrada ou absoluta).</summary>
    public string Estado { get; set; } = "Estado";

    /// <summary>Organização dos arquivos dentro da pasta de erro.</summary>
    public OrganizacaoErro OrganizacaoErro { get; set; } = OrganizacaoErro.PorCategoria;

    /// <summary>Entrada resolvida (caminho completo, sem separador final).</summary>
    public string CaminhoEntrada => Normalizar(Path.GetFullPath(Entrada));

    /// <summary>Processados resolvido contra a Entrada.</summary>
    public string CaminhoProcessados => Resolver(Processados);

    /// <summary>Erro resolvido contra a Entrada.</summary>
    public string CaminhoErro => Resolver(Erro);

    /// <summary>Estado resolvido contra a Entrada.</summary>
    public string CaminhoEstado => Resolver(Estado);

    /// <summary>Devolve a lista de problemas da configuração (vazia = válida).</summary>
    public IReadOnlyList<string> Validar() => Validar(null);

    /// <summary>
    /// Devolve a lista de problemas da configuração (vazia = válida). Além das regras de
    /// Entrada/Processados/Erro, recusa Estado e a pasta de relatórios (quando informada) iguais ou
    /// dentro de Processados/Erro, pois a limpeza periódica apagaria o diário e os relatórios.
    /// </summary>
    /// <param name="pastaRelatorios">Pasta de saída dos relatórios (relativa ao diretório atual ou absoluta).</param>
    public IReadOnlyList<string> Validar(string? pastaRelatorios)
    {
        var problemas = new List<string>();

        if (string.IsNullOrWhiteSpace(Entrada))
        {
            problemas.Add("Pastas:Entrada não informada.");
            return problemas;
        }

        if (string.IsNullOrWhiteSpace(Processados) || string.IsNullOrWhiteSpace(Erro))
        {
            problemas.Add("Pastas:Processados e Pastas:Erro devem ser informadas.");
            return problemas;
        }

        var entrada = CaminhoEntrada;
        var processados = CaminhoProcessados;
        var erro = CaminhoErro;

        foreach (var (nome, caminho) in new[] { ("Entrada", entrada), ("Processados", processados), ("Erro", erro) })
        {
            if (EhRaiz(caminho))
            {
                problemas.Add($"Pastas:{nome} não pode ser a raiz de um drive ({caminho}).");
            }
        }

        foreach (var (nome, caminho) in new[] { ("Processados", processados), ("Erro", erro) })
        {
            if (Mesmo(caminho, entrada))
            {
                problemas.Add($"Pastas:{nome} não pode ser igual à Entrada.");
            }
            else if (EstaDentro(entrada, caminho))
            {
                problemas.Add($"Pastas:{nome} não pode conter a Entrada.");
            }
        }

        if (Mesmo(processados, erro))
        {
            problemas.Add("Pastas:Processados e Pastas:Erro não podem ser a mesma pasta.");
        }
        else if (EstaDentro(processados, erro) || EstaDentro(erro, processados))
        {
            problemas.Add("Pastas:Processados e Pastas:Erro não podem estar uma dentro da outra.");
        }

        if (string.IsNullOrWhiteSpace(Estado))
        {
            problemas.Add("Pastas:Estado deve ser informada.");
            return problemas;
        }

        var protegidas = new List<(string Nome, string Caminho)> { ("Pastas:Estado", CaminhoEstado) };
        if (!string.IsNullOrWhiteSpace(pastaRelatorios))
        {
            protegidas.Add(("Relatorio:PastaSaida", Normalizar(Path.GetFullPath(pastaRelatorios))));
        }

        foreach (var (nome, caminho) in protegidas)
        {
            foreach (var (limpa, pasta) in new[] { ("Processados", processados), ("Erro", erro) })
            {
                if (Mesmo(caminho, pasta) || EstaDentro(caminho, pasta))
                {
                    problemas.Add($"{nome} não pode ficar dentro de Pastas:{limpa} (a limpeza apagaria o conteúdo): {caminho}.");
                }
            }
        }

        return problemas;
    }

    private string Resolver(string valor) =>
        Normalizar(Path.GetFullPath(valor, CaminhoEntrada));

    private static string Normalizar(string caminho)
    {
        var raiz = Path.GetPathRoot(caminho);
        return caminho.Length > (raiz?.Length ?? 0)
            ? caminho.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : caminho;
    }

    private static bool EhRaiz(string caminho) =>
        string.Equals(Path.GetPathRoot(caminho), caminho, StringComparison.OrdinalIgnoreCase);

    private static bool Mesmo(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>True se <paramref name="filho"/> está estritamente dentro de <paramref name="pai"/>.</summary>
    private static bool EstaDentro(string filho, string pai) =>
        filho.StartsWith(pai + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
