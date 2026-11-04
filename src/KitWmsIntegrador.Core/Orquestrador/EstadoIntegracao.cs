namespace KitWmsIntegrador.Core.Orquestrador;

/// <summary>
/// Estado final da integração de uma nota (confirmado pelo usuário, msg 12/5): o objetivo
/// é agilidade, não 100% — pendência e não-integrada são resultados aceitáveis desde que
/// bem registrados.
/// </summary>
public enum EstadoIntegracao
{
    /// <summary>Nota aceita pelo WMS (I50 com sucesso).</summary>
    Integrada,

    /// <summary>Importada mas pendente na interface do WMS (aguarda processamento).</summary>
    Pendente,

    /// <summary>Não integrada após esgotar as tentativas ou por erro não recuperável.</summary>
    NaoIntegrada,
}
