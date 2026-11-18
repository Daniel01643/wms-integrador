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

    /// <summary>Padrão 30 dias (o Plano B precisa achar o XML da venda original). Arquivos mais antigos que este número de dias são removidos.</summary>
    public int DiasRetencao { get; set; } = 30;

    /// <summary>
    /// Retenção, em dias, dos logs rotacionados do NSSM (<c>worker.out-*.log</c>/<c>worker.err-*.log</c>). Vale 1 ou mais;
    /// o arquivo atual (<c>worker.out.log</c>) nunca é apagado.
    /// </summary>
    public int DiasRetencaoLogs { get; set; } = 30;

    /// <summary>
    /// Pasta dos logs do NSSM. Vazio = <c>&lt;pasta do app&gt;\..\..\logs</c> (resolvido para caminho absoluto). Pasta
    /// inexistente é ignorada.
    /// </summary>
    public string PastaLogs { get; set; } = string.Empty;
}
