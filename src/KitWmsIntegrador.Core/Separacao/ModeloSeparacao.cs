namespace KitWmsIntegrador.Core.Separacao;

/// <summary>Situação de uma nota no acompanhamento I50 → separação (I51) → I60.</summary>
public enum SituacaoNota
{
    /// <summary>I50 aceita; aguardando o retorno da separação (I51).</summary>
    AguardandoSeparacao,

    /// <summary>Sem retorno da I51 há mais de <c>HorasParaAtraso</c>; continua sendo consultada.</summary>
    Atrasada,

    /// <summary>I51 retornou (separação concluída); a I60 ainda não foi aceita pelo WMS.</summary>
    Separada,

    /// <summary>O WMS aceitou a chamada da I60 (importada, possivelmente pendente na interface).</summary>
    I60Enviada,

    /// <summary>I60 <c>INE</c> com sucesso confirmado (terminal).</summary>
    Associada,

    /// <summary>I60 recusada por motivo de negócio (terminal; só sai por reenfileiramento manual).</summary>
    Rejeitada,
}

/// <summary>Motivo/categoria do resultado da I60.</summary>
public enum MotivoI60
{
    /// <summary>Sem problema.</summary>
    Nenhum,

    /// <summary>Doccarga encerrado (ENC): reenviada como IMP, sem associação automática.</summary>
    DocCargaEncerrado,

    /// <summary>WMS recusou por quantidade diferente.</summary>
    QuantidadeDiferente,

    /// <summary>WMS recusou por volume diferente.</summary>
    VolumeDiferente,

    /// <summary>Soma separada igual a zero: a I60 não foi enviada.</summary>
    SemQuantidadeSeparada,

    /// <summary>"Nota já existe, TpAcao precisa ser UPD" mesmo após o reenvio UPD.</summary>
    JaExiste,

    /// <summary>Importada e pendente na interface do WMS (sem ressalva).</summary>
    PendenteNaInterface,

    /// <summary>Importada e pendente, com ressalva não bloqueante.</summary>
    PendenteComRessalva,

    /// <summary>XML da nota indisponível (cópia e fallback ausentes).</summary>
    XmlIndisponivel,

    /// <summary>Reservado: erro HTTP definitivo (recurso cortável; hoje comunicação nunca é terminal).</summary>
    ErroDefinitivoHttp,

    /// <summary>Resposta desconhecida do WMS esgotou as tentativas.</summary>
    Outro,
}

/// <summary>Quantidade original × separada de um item (registro do ajuste).</summary>
public sealed record ItemSeparacao
{
    /// <summary>Sequência do item (mesma numeração i+1 da I50/I60).</summary>
    public int Sequencia { get; init; }

    /// <summary>Código do produto.</summary>
    public string CodProduto { get; init; } = string.Empty;

    /// <summary>Quantidade da nota (qCom).</summary>
    public decimal QtdeOriginal { get; init; }

    /// <summary>Quantidade separada devolvida pela I51; <c>null</c> até a separação.</summary>
    public decimal? QtdeSeparada { get; init; }

    /// <summary>Separada − original.</summary>
    public decimal Diferenca { get; init; }

    /// <summary>Item que o WMS não devolveu.</summary>
    public bool SemRetorno { get; init; }
}

/// <summary>Estado persistente de uma nota acompanhada (um JSON por nota).</summary>
public sealed record NotaSeparacao
{
    /// <summary>Chave de 44 dígitos ou chave sintética <c>nf-numero-serie-cnpj</c>.</summary>
    public string Chave { get; init; } = string.Empty;

    /// <summary>Número da nota.</summary>
    public string Numero { get; init; } = string.Empty;

    /// <summary>Série.</summary>
    public string Serie { get; init; } = string.Empty;

    /// <summary>CNPJ do destinatário (só dígitos).</summary>
    public string CnpjDestinatario { get; init; } = string.Empty;

    /// <summary>CNPJ do proprietário (emitente; só dígitos).</summary>
    public string CnpjProprietario { get; init; } = string.Empty;

    /// <summary><c>PUXADA</c> ou <c>TSP</c>.</summary>
    public string Tipo { get; init; } = "PUXADA";

    /// <summary>Número do transporte.</summary>
    public string NumeroTransporte { get; init; } = string.Empty;

    /// <summary>Nome da onda TSP (vazio na Puxada).</summary>
    public string Onda { get; init; } = string.Empty;

    /// <summary>Identificador do pacote.</summary>
    public string IdentificadorPacote { get; init; } = string.Empty;

    /// <summary>Caminho do XML copiado, relativo a <c>Estado\i51\</c>.</summary>
    public string CaminhoXml { get; init; } = string.Empty;

    /// <summary>A cópia do XML foi gravada.</summary>
    public bool XmlCopiado { get; init; }

    /// <summary>Situação atual.</summary>
    public SituacaoNota Situacao { get; init; } = SituacaoNota.AguardandoSeparacao;

    /// <summary>Quando a I50 foi aceita e a nota registrada.</summary>
    public DateTimeOffset RegistradaEm { get; init; }

    /// <summary>Quando virou atrasada.</summary>
    public DateTimeOffset? AtrasadaEm { get; init; }

    /// <summary>Quando a I51 retornou.</summary>
    public DateTimeOffset? SeparadaEm { get; init; }

    /// <summary>Quando o WMS aceitou a I60.</summary>
    public DateTimeOffset? I60EnviadaEm { get; init; }

    /// <summary>Quando chegou a um estado terminal (associada/rejeitada/enviada).</summary>
    public DateTimeOffset? FinalizadaEm { get; init; }

    /// <summary>Última alteração.</summary>
    public DateTimeOffset AtualizadoEm { get; init; }

    /// <summary>Envios da I60 feitos (write-ahead antes de cada tentativa).</summary>
    public int TentativasI60 { get; init; }

    /// <summary>Falhas de comunicação seguidas (zera ao obter resposta).</summary>
    public int FalhasComunicacao { get; init; }

    /// <summary>Respostas desconhecidas (Outro) seguidas.</summary>
    public int TentativasOutro { get; init; }

    /// <summary>Instante da última tentativa da I60.</summary>
    public DateTimeOffset? UltimaTentativaI60Em { get; init; }

    /// <summary>Backoff: não reenviar antes deste instante.</summary>
    public DateTimeOffset? ProximaTentativaI60Em { get; init; }

    /// <summary>Instante do último UNICO de recuperação (gravado antes da chamada).</summary>
    public DateTimeOffset? UltimoUnicoEm { get; init; }

    /// <summary>Quantidade de UNICO de recuperação tentados.</summary>
    public int TentativasUnico { get; init; }

    /// <summary>Nome do arquivo bruto que separou a nota.</summary>
    public string BrutoOrigem { get; init; } = string.Empty;

    /// <summary>Contagem de volumes (<c>volumes[]</c>) devolvida pela I51.</summary>
    public int QtdeVolumesI51 { get; init; }

    /// <summary>Observações (ambiguidade, reenfileiramento, volume...).</summary>
    public string Observacao { get; init; } = string.Empty;

    /// <summary>Operação da I60 aceita (<c>INE</c>/<c>IMP</c>).</summary>
    public string OperacaoI60 { get; init; } = string.Empty;

    /// <summary><c>tpacao</c> usado na I60 aceita.</summary>
    public string TpAcaoI60 { get; init; } = string.Empty;

    /// <summary>Transação da última resposta da I60.</summary>
    public string TransacaoI60 { get; init; } = string.Empty;

    /// <summary>Id do documento na última resposta da I60.</summary>
    public string IdDocumentoI60 { get; init; } = string.Empty;

    /// <summary>Ressalvas da resposta (até 5 itens de 200 caracteres).</summary>
    public IReadOnlyList<string> Ressalvas { get; init; } = Array.Empty<string>();

    /// <summary>Motivo/categoria do resultado.</summary>
    public MotivoI60 Motivo { get; init; } = MotivoI60.Nenhum;

    /// <summary>Mensagem do resultado (até 500 caracteres; nunca o corpo).</summary>
    public string Mensagem { get; init; } = string.Empty;

    /// <summary>Itens com original × separada.</summary>
    public IReadOnlyList<ItemSeparacao> Itens { get; init; } = Array.Empty<ItemSeparacao>();

    /// <summary>Versão do formato.</summary>
    public int Versao { get; init; } = 1;
}

/// <summary>Documento da I51 sem nota correspondente (guardado até a nota ser registrada).</summary>
public sealed record OrfaoI51
{
    /// <summary>Número do documento (sem zeros à esquerda).</summary>
    public string Numero { get; init; } = string.Empty;

    /// <summary>CNPJ do destinatário (dígitos).</summary>
    public string CnpjDestinatario { get; init; } = string.Empty;

    /// <summary>CNPJ do proprietário (dígitos).</summary>
    public string CnpjProprietario { get; init; } = string.Empty;

    /// <summary>Tipo do documento na I51.</summary>
    public string Tipo { get; init; } = string.Empty;

    /// <summary>Subtipo do documento na I51.</summary>
    public string SubTipo { get; init; } = string.Empty;

    /// <summary>Nome do arquivo bruto de origem (carimbo).</summary>
    public string BrutoOrigem { get; init; } = string.Empty;

    /// <summary>Quando foi recebido.</summary>
    public DateTimeOffset RecebidoEm { get; init; }

    /// <summary>Contagem de volumes.</summary>
    public int QtdeVolumesI51 { get; init; }

    /// <summary>Itens (sequência, produto, quantidade separada).</summary>
    public IReadOnlyList<ItemOrfaoI51> Itens { get; init; } = Array.Empty<ItemOrfaoI51>();
}

/// <summary>Item de um documento órfão.</summary>
public sealed record ItemOrfaoI51(int NumeroSequencialItem, string CodProduto, decimal QtdeSeparada);
