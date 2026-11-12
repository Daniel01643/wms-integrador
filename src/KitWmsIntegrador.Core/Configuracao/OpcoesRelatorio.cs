namespace KitWmsIntegrador.Core.Configuracao;

/// <summary>
/// Opções do relatório de erros/pendências. Configurável via <c>appsettings.json</c>
/// (seção "Relatorio") e variáveis de ambiente (ex.: <c>Relatorio__PastaSaida</c>).
/// </summary>
public sealed class OpcoesRelatorio
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string Secao = "Relatorio";

    /// <summary>
    /// Pasta de saída dos arquivos de relatório (HTML e PDF). Configurável — NÃO
    /// hardcode no gerador. Default relativo <c>relatorios</c>.
    /// </summary>
    public string PastaSaida { get; set; } = "relatorios";

    /// <summary>
    /// Caminho opcional de outro logo PNG (versão negativa/branca, que aparece sobre a faixa azul do
    /// cabeçalho) para o HTML (base64), o PDF e o e-mail. Vazio por padrão: se vazio, inexistente ou
    /// inválido, usa a logo Friozem embutida no programa (<c>IdentidadeVisual.ObterLogo</c>); só sem
    /// ela cai no texto "Friozem". Override por <c>Relatorio__LogoPath</c>.
    /// </summary>
    public string LogoPath { get; set; } = string.Empty;
}
