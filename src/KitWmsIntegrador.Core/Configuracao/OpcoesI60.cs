namespace KitWmsIntegrador.Core.Configuracao;

/// <summary>
/// Flag de configuração da I60 (NOTA/DocCarga). Configurável via
/// <c>appsettings.json</c> (seção "I60") e variáveis de ambiente
/// (ex.: <c>I60__Habilitada</c>).
///
/// Pedido do usuário (mensagem 12): desligar a I60 por ora, pois a I50 agora
/// carrega os dados fiscais (chave, protocolo, números, valores) nos atributos
/// e observações. O código da I60 (<c>ConstrutorI60</c>, <c>PayloadI60</c> e
/// seus testes) permanece intacto e é reativável — basta ligar esta flag.
///
/// Esta flag apenas sinaliza se o envio da I60 deve ocorrer; o fluxo de envio
/// do Orquestrador (Etapa 5, ainda inexistente) deve respeitá-la.
/// </summary>
public sealed class OpcoesI60
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string Secao = "I60";

    /// <summary>
    /// Indica se o envio da I60 está habilitado. Default <c>false</c> — a I60
    /// está desligada por ora (ver observação da classe). O fluxo de envio da
    /// Etapa 5 deve consultar esta flag antes de montar/enviar a I60.
    /// </summary>
    public bool Habilitada { get; set; } = false;

    /// <summary>Valor padrão de <see cref="TipoOperacaoNota"/> (o do template atual).</summary>
    public const string TipoOperacaoNotaPadrao = "INE";

    /// <summary>
    /// Tipo de operação da nota na I60: preenche <c>tipoOperacaoNota</c> e <c>Tpsit</c> com o
    /// mesmo valor (como nos JSONs do chamado). Default <c>INE</c> (vincula sem gerar
    /// embarque). Valores citados na documentação do repo: IMB, INE, IMP. "INV" NÃO consta
    /// em nenhum documento versionado: trocar só após confirmação do fornecedor/homolog.
    /// Troca sem release: variável de ambiente <c>I60__TipoOperacaoNota</c>.
    /// </summary>
    public string TipoOperacaoNota { get; set; } = TipoOperacaoNotaPadrao;

    /// <summary>
    /// Diagnóstico de homolog: quando <c>true</c>, a I60 envia <c>qtdeVolume = 0</c> em vez da
    /// soma das caixas dos itens. Serve para testar se a conferência de volume do WMS passa
    /// com o valor que ele lê hoje (0) e expor a próxima conferência. Default <c>false</c>
    /// (envia a soma). Troca sem release: variável de ambiente <c>I60__QtdeVolumeZero</c>.
    /// </summary>
    public bool QtdeVolumeZero { get; set; } = false;
}

