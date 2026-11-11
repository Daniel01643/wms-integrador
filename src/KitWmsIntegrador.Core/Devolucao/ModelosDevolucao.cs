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

/// <summary>Pedido de envio de devolução: lista de chaves.</summary>
public sealed record SolicitacaoDevolucao
{
    /// <summary>Chaves de acesso (44 dígitos). Duplicadas e brancos são removidos.</summary>
    public IReadOnlyList<string> Chaves { get; init; } = Array.Empty<string>();


    /// <summary>Origem do pedido (IP), só para auditoria.</summary>
    public string? Origem { get; init; }
}

/// <summary>Resultado de uma chave.</summary>
public sealed record ResultadoChaveDevolucao
{
    public string Chave { get; init; } = string.Empty;

    /// <summary>Número da nota (do XML; na falta dele, derivado da chave).</summary>
    public string Nota { get; init; } = string.Empty;

    /// <summary>Série da nota, derivada da chave (sem zeros à esquerda); vazia se a chave for inválida.</summary>
    public string Serie { get; init; } = string.Empty;

    public StatusChaveDevolucao Status { get; init; }

    public bool Sucesso => Status == StatusChaveDevolucao.Enviada;

    /// <summary>Texto para o usuário (texto do WMS, sem alteração, quando for erro do WMS).</summary>
    public string Mensagem { get; init; } = string.Empty;
}

/// <summary>Resultado do lote.</summary>
public sealed record ResultadoLoteDevolucao
{
    /// <summary>Quantidade de chaves únicas processadas.</summary>
    public int Total { get; init; }

    /// <summary>Quantidade enviada com sucesso.</summary>
    public int Enviadas { get; init; }

    /// <summary>Transação do WMS (vazia se nada foi enviado).</summary>
    public string Transacao { get; init; } = string.Empty;

    public IReadOnlyList<ResultadoChaveDevolucao> Resultados { get; init; } = Array.Empty<ResultadoChaveDevolucao>();
}

/// <summary>Pedido inválido (lista vazia, excesso de chaves, número/série inválidos). A tela responde 400.</summary>
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

/// <summary>Uma chave candidata achada pela busca por número + série.</summary>
public sealed record OpcaoNotaDevolucao
{
    public string Chave { get; init; } = string.Empty;

    /// <summary>Número da nota (sem zeros à esquerda).</summary>
    public string Numero { get; init; } = string.Empty;

    /// <summary>Série (sem zeros à esquerda).</summary>
    public string Serie { get; init; } = string.Empty;

    /// <summary>Data/hora local do arquivo no formato <c>dd/MM/yyyy HH:mm</c>.</summary>
    public string DataArquivo { get; init; } = string.Empty;

    /// <summary>Pasta onde o XML está: <c>pacote_*</c> ou <c>Processados</c>.</summary>
    public string Pacote { get; init; } = string.Empty;
}

/// <summary>Resultado da busca por número + série: uma ou mais chaves distintas.</summary>
public sealed record ResultadoBuscaDevolucao
{
    public string Numero { get; init; } = string.Empty;

    public string Serie { get; init; } = string.Empty;

    public IReadOnlyList<OpcaoNotaDevolucao> Opcoes { get; init; } = Array.Empty<OpcaoNotaDevolucao>();
}
