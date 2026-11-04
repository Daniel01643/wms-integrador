namespace KitWmsIntegrador.Core.Configuracao;

/// <summary>
/// Limpeza das pastas Processados/Erro (seção "Limpeza"). Idade medida por
/// <c>LastWriteTimeUtc</c> (carimbado na movimentação).
/// </summary>
public sealed class OpcoesLimpeza
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string Secao = "Limpeza";

    /// <summary>Liga/desliga a limpeza.</summary>
    public bool Habilitada { get; set; } = true;

    /// <summary>Arquivos mais antigos que este número de dias são removidos.</summary>
    public int DiasRetencao { get; set; } = 10;
}
