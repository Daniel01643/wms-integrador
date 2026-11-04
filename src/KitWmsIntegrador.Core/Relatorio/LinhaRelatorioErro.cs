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
    IReadOnlyList<string>? Historico = null)
{
    /// <summary>Limite de caracteres do último erro resumido.</summary>
    public const int LimiteMensagemResumida = 160;

    /// <summary>Status efetivo: o informado ou, na ausência, derivado de <see cref="Integrada"/>.</summary>
    public StatusLinhaRelatorio StatusEfetivo =>
        Status ?? (Integrada ? StatusLinhaRelatorio.Integrada : StatusLinhaRelatorio.NaoIntegrada);

    /// <summary>Há produto provisório (criado por contenção) a revisar no cadastro.</summary>
    public bool RevisaoProdutoPendente => ProdutosProvisorios is { Count: > 0 };

    /// <summary>Status único exibido: Integrada, Integrada — revisar produto, Pendente ou Não integrada.</summary>
    public string StatusDescricao =>
        IntegradaRevisarProduto ? "Integrada — revisar produto"
        : EstaIntegrada ? "Integrada"
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
