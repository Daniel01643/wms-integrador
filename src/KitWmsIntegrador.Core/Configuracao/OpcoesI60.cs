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
}
