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
    /// Caminho de um logo PNG (ex.: <c>docs/identidade/logo-friozem.png</c>) usado no
    /// cabeçalho do HTML (base64) e do PDF. Vazio por padrão; se vazio ou inexistente, o
    /// cabeçalho usa o texto "Friozem" na cor da marca, sem erro. Override por
    /// <c>Relatorio__LogoPath</c>.
    /// </summary>
    public string LogoPath { get; set; } = string.Empty;
}
