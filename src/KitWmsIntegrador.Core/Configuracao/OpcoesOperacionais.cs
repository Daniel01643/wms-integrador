namespace KitWmsIntegrador.Core.Configuracao;

/// <summary>
/// Dados operacionais BRF/Friozem usados nos payloads (CNPJs e depósito).
/// Configurável via <c>appsettings.json</c> (seção "Operacional") e variáveis de
/// ambiente (ex.: <c>Operacional__CnpjArmazem</c>).
///
/// Os defaults trazem os valores BRF/Friozem já confirmados
/// (ver <c>docs/AMBIENTE-SERVIDOR.md</c>), mas NADA é hardcoded nos construtores:
/// todos os campos são parametrizáveis e podem ser sobrescritos por ambiente.
/// </summary>
public sealed class OpcoesOperacionais
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string Secao = "Operacional";

    /// <summary>
    /// Depósito operacional (campo <c>deposito</c> das I50/I60/I10). BRF: <c>JD</c>.
    /// </summary>
    public string Deposito { get; set; } = "JD";

    /// <summary>
    /// Identificador numérico do depósito (iddeposito). BRF: <c>8</c>. Mantido para
    /// uso futuro; os templates atuais usam a sigla <see cref="Deposito"/>.
    /// </summary>
    public int IdDeposito { get; set; } = 8;

    /// <summary>
    /// CNPJ do armazém (campo <c>cnpjArmazem</c>). BRF: <c>14.605.065/0001-75</c>.
    /// </summary>
    public string CnpjArmazem { get; set; } = "14.605.065/0001-75";

    /// <summary>
    /// CNPJ do depositante / empresa proprietária (campos <c>cnpjProprietario</c>,
    /// <c>cnpjProprietariaProduto</c>, <c>cnpjProprietaria</c>).
    /// BRF: <c>01.838.723/0098-50</c>.
    /// </summary>
    public string CnpjDepositante { get; set; } = "01.838.723/0098-50";

    /// <summary>
    /// Código ERP do depositante (ver <c>codigoempresa</c> da I10 — a confirmar se é
    /// este o código a usar). BRF: <c>3509308</c>.
    /// </summary>
    public string CodigoDepositante { get; set; } = "3509308";
}
