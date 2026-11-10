namespace KitWmsIntegrador.Core.Configuracao;

/// <summary>
/// Opções da devolução pela I40 (Plano B), seção "Devolucao". Override por variável de
/// ambiente no padrão <c>Devolucao__NomeMotorista</c>, <c>Devolucao__InverterPartes</c> etc.
///
/// Fase 1: só o modo manual de teste (<c>--enviar-i40</c>) usa estas opções; a tela web
/// (fase 2) usará <see cref="Habilitada"/> e <see cref="Porta"/>.
/// </summary>
public sealed class OpcoesDevolucao
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string Secao = "Devolucao";

    /// <summary>Valor padrão de <see cref="TipoOperacaoNota"/>: ICD gera chegada de veículo e descarga.</summary>
    public const string TipoOperacaoNotaPadrao = "ICD";

    /// <summary>Valor padrão de <see cref="TipoNota"/>: devolução.</summary>
    public const string TipoNotaPadrao = "DE";

    /// <summary>Valor padrão de <see cref="FormaTransporte"/> (PR/CP/CD).</summary>
    public const string FormaTransportePadrao = "PR";

    /// <summary>Valor padrão de <see cref="CodSituacaoTributaria"/> (o mesmo da I60).</summary>
    public const string CodSituacaoTributariaPadrao = "040";

    /// <summary>
    /// Liga a devolução (fase 2: tela web). Default <c>false</c>. O modo manual de teste
    /// <c>--enviar-i40</c> não depende desta flag.
    /// </summary>
    public bool Habilitada { get; set; } = false;

    /// <summary><c>tipoOperacaoNota</c> da I40 (IMC/ICD/IMP). Default ICD.</summary>
    public string TipoOperacaoNota { get; set; } = TipoOperacaoNotaPadrao;

    /// <summary><c>tipoNota</c> da I40 (OR/AR/NO/OI/OE/DE/CD/OC/SU/SD). Default DE.</summary>
    public string TipoNota { get; set; } = TipoNotaPadrao;

    /// <summary>
    /// Nome FIXO do motorista (<c>nomeMotorista</c>), cadastrado previamente no WMS.
    /// Default vazio: é obrigatório configurar; o construtor falha com mensagem clara se vazio.
    /// </summary>
    public string NomeMotorista { get; set; } = string.Empty;

    /// <summary>
    /// Quando <c>true</c>, troca emitente/remetente com destinatário/local de entrega
    /// (o cliente devolve para nós). <c>cnpjNota</c> segue o emitente do envio (o CNPJ que
    /// sai em <c>cnpjEmitente</c>). Default <c>false</c>; o teste ao vivo decide o default.
    /// </summary>
    public bool InverterPartes { get; set; } = false;

    /// <summary><c>formaTransporte</c> (PR/CP/CD). Default PR.</summary>
    public string FormaTransporte { get; set; } = FormaTransportePadrao;

    /// <summary><c>codSituacaoTributaria</c> dos itens. Default 040.</summary>
    public string CodSituacaoTributaria { get; set; } = CodSituacaoTributariaPadrao;

    /// <summary>Porta HTTP da tela (fase 2). <c>null</c> na fase 1.</summary>
    public int? Porta { get; set; }
}
