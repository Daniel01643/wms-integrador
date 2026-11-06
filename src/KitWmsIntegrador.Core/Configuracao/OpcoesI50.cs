namespace KitWmsIntegrador.Core.Configuracao;

/// <summary>
/// Parâmetros de negócio da I50 (PEDIDO/DocCarga). Configurável via
/// <c>appsettings.json</c> (seção "I50") e variáveis de ambiente
/// (ex.: <c>I50__TipoCarga</c>).
///
/// Pedido explícito do usuário (mensagem 12): tipo de carga e tipos de separação 1 e 2
/// NÃO podem ser hardcoded — vêm daqui. Os mesmos valores de separação são aplicados
/// aos itens (ITEM_PEDIDO). Os defaults são os assumidos para a BRF (ver a tabela
/// "Campos a confirmar" em <c>docs/TEMPLATES-INTERFACES.md</c>) e devem ser revisados
/// com o cliente.
/// </summary>
public sealed class OpcoesI50
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string Secao = "I50";

    /// <summary>
    /// Tipo de carga (campo <c>tipoCarga</c>). "CD" distribuição / "CN" normal /
    /// "TR" transferência. Default <c>CN</c> (a confirmar p/ BRF).
    /// </summary>
    public string TipoCarga { get; set; } = "CN";

    /// <summary>
    /// Tipo de separação 1 — busca da 1ª carga (campo <c>tipoSeparacao</c>).
    /// Default do template (a confirmar p/ BRF).
    /// </summary>
    public string TipoSeparacao { get; set; } = "RES;FEF;PLI";

    /// <summary>
    /// Tipo de separação 2 — busca da 2ª carga (campo <c>tipoSeparacao2</c>).
    /// Default do template (a confirmar p/ BRF).
    /// </summary>
    public string TipoSeparacao2 { get; set; } = "AGL;FEF;PLL";

    /// <summary>
    /// Tipo de acondicionamento (campo <c>tipoAcondicionamento</c>). "PP" palete padrão
    /// / "PE" / "PC" picking / "PA" packing. Default <c>PC</c> — A CONFIRMAR (PC vs PP).
    /// </summary>
    public string TipoAcondicionamento { get; set; } = "PC";

    /// <summary>
    /// Base da quantidade do item (campo <c>qtdMandante</c>). "U" produtos /
    /// "E" embalagens/caixas. Configurável; BRF confirmado = <c>E</c> (caixas).
    /// </summary>
    public string QtdMandante { get; set; } = "E";

    /// <summary>
    /// Liga/desliga o envio da placa do veículo lida do XML (<c>transp/veicTransp/placa</c>)
    /// no campo <c>placaVeiculo</c> da I50. <c>true</c> (default) = envia a placa como está no
    /// XML; <c>false</c> = envia <c>placaVeiculo</c> em branco. Override por env
    /// <c>I50__EnviarPlacaVeiculo</c>.
    /// </summary>
    public bool EnviarPlacaVeiculo { get; set; } = true;
}
