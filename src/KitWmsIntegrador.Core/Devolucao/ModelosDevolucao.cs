using System.Text.Json.Serialization;

namespace KitWmsIntegrador.Core.Devolucao;

/// <summary>Situação de uma chave no envio da devolução (tela).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StatusChaveDevolucao
{
    /// <summary>WMS registrou a I40 da nota.</summary>
    Enviada,

    /// <summary>WMS recusou ou o envio falhou; a mensagem traz o texto do WMS.</summary>
    ErroWms,

    /// <summary>XML da venda não encontrado em Processados (retenção de 60 dias).</summary>
    XmlNaoEncontrado,

    /// <summary>Chave sem 44 dígitos ou com dígito verificador inválido.</summary>
    ChaveInvalida,

    /// <summary>Falha local ao ler o XML ou montar a I40.</summary>
    ErroInterno,
}

/// <summary>Pedido de envio de devolução: lista de chaves e opção da placa.</summary>
public sealed record SolicitacaoDevolucao
{
    /// <summary>Chaves de acesso (44 dígitos). Duplicadas e brancos são removidos.</summary>
    public IReadOnlyList<string> Chaves { get; init; } = Array.Empty<string>();

    /// <summary>Quando <c>true</c>, envia a placa (ICD, com motorista fixo).</summary>
    public bool EnviarPlaca { get; init; }

    /// <summary>Placa do veículo; obrigatória se <see cref="EnviarPlaca"/>.</summary>
    public string? Placa { get; init; }

    /// <summary>Origem do pedido (IP), só para auditoria.</summary>
    public string? Origem { get; init; }
}

/// <summary>Resultado de uma chave.</summary>
public sealed record ResultadoChaveDevolucao
{
    public string Chave { get; init; } = string.Empty;

    /// <summary>Número da nota (do XML; na falta dele, derivado da chave).</summary>
    public string Nota { get; init; } = string.Empty;

    public StatusChaveDevolucao Status { get; init; }

    public bool Sucesso => Status == StatusChaveDevolucao.Enviada;

    /// <summary>Texto para o usuário (texto do WMS, sem alteração, quando for erro do WMS).</summary>
    public string Mensagem { get; init; } = string.Empty;
}

/// <summary>Resultado do lote.</summary>
public sealed record ResultadoLoteDevolucao
{
    public bool EnviarPlaca { get; init; }

    /// <summary>Quantidade de chaves únicas processadas.</summary>
    public int Total { get; init; }

    /// <summary>Quantidade enviada com sucesso.</summary>
    public int Enviadas { get; init; }

    /// <summary>Transação do WMS (vazia se nada foi enviado).</summary>
    public string Transacao { get; init; } = string.Empty;

    public IReadOnlyList<ResultadoChaveDevolucao> Resultados { get; init; } = Array.Empty<ResultadoChaveDevolucao>();
}

/// <summary>Pedido inválido (lista vazia, excesso de chaves, placa ausente). A tela responde 400.</summary>
public sealed class SolicitacaoDevolucaoInvalidaException : Exception
{
    public SolicitacaoDevolucaoInvalidaException(string mensagem) : base(mensagem)
    {
    }
}

/// <summary>Configuração insuficiente para atender o pedido. A tela responde 503.</summary>
public sealed class ConfiguracaoDevolucaoException : Exception
{
    public ConfiguracaoDevolucaoException(string mensagem) : base(mensagem)
    {
    }
}
