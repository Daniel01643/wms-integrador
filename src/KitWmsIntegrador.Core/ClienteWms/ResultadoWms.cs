namespace KitWmsIntegrador.Core.ClienteWms;

/// <summary>
/// Classificação derivada do retorno do WMS. É um HELPER de conveniência que
/// deriva um resultado a partir do texto da mensagem; a regra de negócio final
/// (reenvio, I10, alerta) é decidida pelo Orquestrador em etapa futura.
/// </summary>
public enum ResultadoWms
{
    /// <summary>Importado e aceito — <c>sucesso="S"</c>.</summary>
    Sucesso,

    /// <summary>Importado, porém aguardando ação na interface (ex.: empresa não localizada).</summary>
    Pendente,

    /// <summary>Rejeitado — estrutura mal-formada ou erro definitivo.</summary>
    Rejeitado,
}
