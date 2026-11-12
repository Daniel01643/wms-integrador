namespace KitWmsIntegrador.Core.Configuracao;

/// <summary>
/// Limites do leitor de ZIP (seção "Zip"). Protegem o servidor contra ZIP gigante ou "ZIP bomba".
/// </summary>
public sealed class OpcoesZip
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string Secao = "Zip";

    /// <summary>Máximo de arquivos (entradas) aceitos dentro de um ZIP.</summary>
    public int MaxArquivos { get; set; } = 1000;

    /// <summary>Máximo, em MB, do conteúdo descompactado de um ZIP.</summary>
    public int MaxTamanhoMb { get; set; } = 500;

    /// <summary>Devolve a lista de problemas da configuração (vazia = válida).</summary>
    public IReadOnlyList<string> Validar()
    {
        var problemas = new List<string>();
        if (MaxArquivos <= 0)
        {
            problemas.Add("Zip:MaxArquivos deve ser maior que zero.");
        }

        if (MaxTamanhoMb <= 0)
        {
            problemas.Add("Zip:MaxTamanhoMb deve ser maior que zero.");
        }

        return problemas;
    }
}
