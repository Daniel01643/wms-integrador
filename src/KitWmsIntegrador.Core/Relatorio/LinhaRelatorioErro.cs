namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>
/// Uma linha do relatório de erros/pendências de integração. Cada linha representa
/// uma nota com problema (ou integrada com ressalva), com os dados que o usuário
/// (mensagem 11) pediu para o relatório estruturado.
/// </summary>
/// <param name="NumeroNota">Número da nota fiscal.</param>
/// <param name="NumeroTransporte">Número do transporte (DocCarga) associado à nota.</param>
/// <param name="HorarioErro">Horário do erro, com fuso (<see cref="DateTimeOffset"/>).</param>
/// <param name="Mensagem">Mensagem do último erro retornada/registrada na integração.</param>
/// <param name="Integrada">Se a nota acabou integrada (S/N no relatório). Default <c>false</c>.</param>
/// <param name="Tentativas">Quantas tentativas de integração foram feitas. Default <c>0</c>.</param>
/// <param name="IdTransacao">ID da transação (<c>transacao</c>) da última chamada da I50.</param>
/// <param name="IdDocumento">ID do documento (<c>retornos[].idDocumento</c>) da última chamada da I50.</param>
/// <param name="Status">Status base; quando nulo, deriva de <paramref name="Integrada"/>.</param>
/// <param name="ProdutosProvisorios">Códigos de produtos criados por contenção (I30), a revisar.</param>
/// <param name="Historico">Histórico completo das chamadas (exibido apenas no HTML).</param>
/// <param name="Tipo">PUXADA ou TSP (decidido pela pasta de origem).</param>
/// <param name="Origem">raiz, Puxada ou PuxadaTSP (pasta onde o arquivo foi depositado).</param>
/// <param name="Onda">Nome da onda da TSP (vazio na PUXADA).</param>
/// <param name="Zip">Nome do ZIP de onde a nota saiu (vazio se XML solto).</param>
public record LinhaRelatorioErro(
    string NumeroNota,
    string NumeroTransporte,
    DateTimeOffset HorarioErro,
    string Mensagem,
    bool Integrada = false,
    int Tentativas = 0,
    string IdTransacao = "",
    string IdDocumento = "",
    StatusLinhaRelatorio? Status = null,
    IReadOnlyList<string>? ProdutosProvisorios = null,
    IReadOnlyList<string>? Historico = null,
    string Tipo = "PUXADA",
    string Origem = "",
    string Onda = "",
    string Zip = "")
{
    /// <summary>Nota de Puxada TSP (pasta PuxadaTSP): agrupada por onda em vez de transporte.</summary>
    public bool EhTsp => string.Equals(Tipo, "TSP", StringComparison.OrdinalIgnoreCase);

    /// <summary>Nota que veio solta na raiz da entrada (etiqueta discreta "raiz" no relatório).</summary>
    public bool EhRaiz => string.Equals(Origem, "raiz", StringComparison.OrdinalIgnoreCase);

    /// <summary>Limite de caracteres do último erro resumido.</summary>
    public const int LimiteMensagemResumida = 160;

    /// <summary>Início da mensagem de um XML repetido no pacote (ignorado, não é erro).</summary>
    public const string PrefixoXmlRepetido = "XML repetido";

    /// <summary>Status efetivo: o informado ou, na ausência, derivado de <see cref="Integrada"/>.</summary>
    public StatusLinhaRelatorio StatusEfetivo =>
        Status ?? (Integrada ? StatusLinhaRelatorio.Integrada : StatusLinhaRelatorio.NaoIntegrada);

    /// <summary>Há produto provisório (criado por contenção) a revisar no cadastro.</summary>
    public bool RevisaoProdutoPendente => ProdutosProvisorios is { Count: > 0 };

    /// <summary>Arquivo não-XML ignorado pela integração (não é falha de WMS, apenas aviso).</summary>
    public bool ArquivoIgnorado => StatusEfetivo == StatusLinhaRelatorio.ArquivoIgnorado;

    /// <summary>Status único exibido: Integrada, Integrada — revisar produto, Pendente, Não integrada ou Arquivo ignorado.</summary>
    public string StatusDescricao =>
        IntegradaRevisarProduto ? "Integrada — revisar produto"
        : EstaIntegrada ? "Integrada"
        : ArquivoIgnorado ? ((Mensagem ?? string.Empty).StartsWith(PrefixoXmlRepetido, StringComparison.Ordinal)
            ? "Arquivo ignorado (XML repetido)" : "Arquivo ignorado (não é XML)")
        : StatusEfetivo == StatusLinhaRelatorio.Pendente ? "Pendente"
        : "Não integrada";

    /// <summary>
    /// Nota aceita pelo WMS. O flag <see cref="Integrada"/> prevalece sobre o <see cref="Status"/>
    /// (o Orquestrador devolve Estado=Pendente com Integrada=true quando há produto a revisar).
    /// </summary>
    public bool EstaIntegrada => Integrada || Status == StatusLinhaRelatorio.Integrada;

    /// <summary>Integrada com ressalva: aceita pelo WMS, mas com produto provisório a revisar.</summary>
    public bool IntegradaRevisarProduto => EstaIntegrada && RevisaoProdutoPendente;

    /// <summary>Último erro resumido (até 160 caracteres, sem histórico).</summary>
    public string UltimoErroResumido => ResumoMensagem.Resumir(Mensagem, LimiteMensagemResumida);
}
