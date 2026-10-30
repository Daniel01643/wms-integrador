namespace KitWmsIntegrador.Worker.Configuracao;

/// <summary>
/// Opções da pasta de entrada. Configurável via <c>appsettings.json</c>
/// (seção "Entrada") e variáveis de ambiente (ex.: <c>Entrada__PastaEntrada</c>).
/// O caminho NUNCA é hardcodado no código de domínio.
/// </summary>
public sealed class OpcoesEntrada
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string Secao = "Entrada";

    /// <summary>
    /// Pasta onde ficam os XMLs de NF-e (somente leitura). Default de
    /// teste/produção inicial: <c>C:\Envia xml</c>.
    /// </summary>
    public string PastaEntrada { get; set; } = @"C:\Envia xml";
}
