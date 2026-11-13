using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Separacao;

/// <summary>Contagens do casamento de uma resposta da I51.</summary>
/// <param name="Recebidos">Documentos válidos recebidos.</param>
/// <param name="NotasSeparadas">Notas que passaram a <c>Separada</c>.</param>
/// <param name="Orfaos">Documentos guardados sem nota correspondente.</param>
/// <param name="OutroProprietario">Documentos de outro proprietário (contados e ignorados).</param>
/// <param name="Descartados">Documentos de notas já separadas (ignorados).</param>
/// <param name="Falhas">Gravações de estado/órfão que falharam (o bruto deve ser reprocessado).</param>
public sealed record ResultadoCasamento(
    int Recebidos, int NotasSeparadas, int Orfaos, int OutroProprietario, int Descartados, int Falhas);

/// <summary>
/// Casa documentos da I51 com as notas que aguardam separação: número sem zeros à esquerda,
/// filtro de proprietário pelo <c>CnpjProprietario</c> do documento (nunca pelo CNPJ usado na
/// consulta), desempate por CNPJ destinatário e depois pela nota mais antiga.
/// </summary>
public sealed class CasadorDocumentoNota
{
    private readonly IRepositorioSeparacao _repositorio;
    private readonly RepositorioOrfaosI51 _orfaos;
    private readonly OpcoesI51 _opcoes;
    private readonly OpcoesOperacionais _operacional;
    private readonly IClock _clock;
    private readonly ILogger<CasadorDocumentoNota> _logger;

    /// <summary>Cria o casador.</summary>
    public CasadorDocumentoNota(
        IRepositorioSeparacao repositorio, RepositorioOrfaosI51 orfaos, IOptions<OpcoesI51> opcoes,
        IOptions<OpcoesOperacionais> operacional, IClock clock, ILogger<CasadorDocumentoNota> logger)
    {
        _repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        _orfaos = orfaos ?? throw new ArgumentNullException(nameof(orfaos));
        _opcoes = opcoes?.Value ?? throw new ArgumentNullException(nameof(opcoes));
        _operacional = operacional?.Value ?? throw new ArgumentNullException(nameof(operacional));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Casa os documentos de uma resposta; <paramref name="brutoOrigem"/> é o nome do arquivo bruto.</summary>
    public ResultadoCasamento Processar(RespostaI51 resposta, string brutoOrigem)
    {
        ArgumentNullException.ThrowIfNull(resposta);
        var proprietario = ParserI51.Digitos(_operacional.CnpjDepositante);
        var aceitos = new List<DocumentoI51>();
        var outro = 0;
        foreach (var doc in resposta.Documentos)
        {
            if (doc.CnpjProprietario.Length > 0 && proprietario.Length > 0 && doc.CnpjProprietario != proprietario)
            {
                outro++;
                continue;
            }

            aceitos.Add(doc);
        }

        if (outro > 0)
        {
            _logger.LogInformation("{Quantidade} documento(s) da I51 de outro proprietário ignorado(s).", outro);
        }

        var r = Resolver(aceitos, brutoOrigem, guardarOrfaos: true);
        return r with { Recebidos = aceitos.Count, OutroProprietario = outro };
    }

    /// <summary>
    /// Confronta os órfãos guardados com as notas aguardando (chamado antes do TODOS e quando uma
    /// nota é registrada). Órfão de nota já separada é descartado.
    /// </summary>
    public ResultadoCasamento ReprocessarOrfaos()
    {
        var lista = _orfaos.Listar();
        if (lista.Count == 0)
        {
            return new ResultadoCasamento(0, 0, 0, 0, 0, 0);
        }

        var docs = lista.Select(x => Converter(x.Orfao)).ToList();
        var r = Resolver(docs, string.Empty, guardarOrfaos: false);
        // Remove os órfãos consumidos (casados ou descartados); os demais ficam.
        var consumidos = 0;
        foreach (var (arquivo, o) in lista)
        {
            if (Destino(Converter(o), out _, out _) == DestinoDoc.Descartar)
            {
                _orfaos.Remover(arquivo);
                consumidos++;
            }
        }

        _logger.LogInformation("Órfãos da I51 reavaliados: {Casados} nota(s) separada(s), {Consumidos} consumido(s).", r.NotasSeparadas, consumidos);
        return r with { Recebidos = lista.Count };
    }

    private enum DestinoDoc { SemNota, Casa, Descartar }

    private ResultadoCasamento Resolver(IReadOnlyList<DocumentoI51> docs, string brutoOrigem, bool guardarOrfaos)
    {
        var porNota = new Dictionary<string, List<DocumentoI51>>(StringComparer.Ordinal);
        var ambiguas = new HashSet<string>(StringComparer.Ordinal);
        var orfaos = 0;
        var descartados = 0;
        var falhas = 0;

        foreach (var doc in docs)
        {
            switch (Destino(doc, out var nota, out var ambigua))
            {
                case DestinoDoc.Casa:
                    if (!porNota.TryGetValue(nota!.Chave, out var lista))
                    {
                        porNota[nota.Chave] = lista = new List<DocumentoI51>();
                    }

                    lista.Add(doc);
                    if (ambigua)
                    {
                        ambiguas.Add(nota.Chave);
                    }

                    break;
                case DestinoDoc.Descartar:
                    descartados++;
                    _logger.LogDebug("Documento {Numero} da I51 já tratado; ignorado.", doc.Numero);
                    break;
                default:
                    if (guardarOrfaos)
                    {
                        if (_orfaos.Salvar(ParaOrfao(doc, brutoOrigem)))
                        {
                            orfaos++;
                            _logger.LogInformation("Documento {Numero} da I51 sem nota correspondente; guardado como órfão.", doc.Numero);
                        }
                        else
                        {
                            falhas++;
                        }
                    }

                    break;
            }
        }

        var separadas = 0;
        foreach (var (chave, docsNota) in porNota)
        {
            if (Aplicar(chave, docsNota, brutoOrigem, ambiguas.Contains(chave)))
            {
                separadas++;
            }
            else
            {
                falhas++;
            }
        }

        return new ResultadoCasamento(docs.Count, separadas, orfaos, 0, descartados, falhas);
    }

    // Decide o destino de um documento. Desempate: CNPJ destinatário, depois a nota mais antiga.
    private DestinoDoc Destino(DocumentoI51 doc, out NotaSeparacao? nota, out bool ambigua)
    {
        nota = null;
        ambigua = false;
        var todas = _repositorio.Listar().Where(n => Igual(n.Numero, doc.Numero)).ToList();
        var candidatas = todas.Where(n => n.Situacao is SituacaoNota.AguardandoSeparacao or SituacaoNota.Atrasada).ToList();
        if (candidatas.Count == 0)
        {
            return todas.Count > 0 ? DestinoDoc.Descartar : DestinoDoc.SemNota;
        }

        if (candidatas.Count > 1 && doc.CnpjDestinatario.Length > 0)
        {
            var porCnpj = candidatas.Where(n => ParserI51.Digitos(n.CnpjDestinatario) == doc.CnpjDestinatario).ToList();
            if (porCnpj.Count > 0)
            {
                candidatas = porCnpj;
            }
        }

        nota = candidatas.OrderBy(n => n.RegistradaEm).First();
        if (candidatas.Count > 1)
        {
            ambigua = true;
            _logger.LogWarning("Documento {Numero} da I51 ambíguo ({Quantidade} notas); casado com a mais antiga.", doc.Numero, candidatas.Count);
        }

        return DestinoDoc.Casa;
    }

    private bool Aplicar(string chave, List<DocumentoI51> docs, string brutoOrigem, bool ambigua)
    {
        var nota = _repositorio.Obter(chave);
        if (nota is null)
        {
            return false;
        }

        var ajuste = CalculadorAjuste.Calcular(nota.Itens, docs.SelectMany(d => d.Itens), _opcoes.OmitirItemSemRetorno);
        var volumes = docs.Sum(d => d.QtdeVolumes);
        if (docs.Count > 1)
        {
            _logger.LogInformation("{Quantidade} documentos da I51 somados para a nota {Numero}.", docs.Count, nota.Numero);
        }

        var agora = _clock.UtcNow;
        return _repositorio.Atualizar(chave, n => n with
        {
            Situacao = SituacaoNota.Separada,
            SeparadaEm = agora,
            BrutoOrigem = brutoOrigem,
            QtdeVolumesI51 = volumes,
            Itens = ajuste.Itens,
            ProximaTentativaI60Em = null,
            Observacao = ambigua ? Juntar(n.Observacao, "ambígua: casada com a mais antiga") : n.Observacao,
        });
    }

    private OrfaoI51 ParaOrfao(DocumentoI51 d, string brutoOrigem) => new()
    {
        Numero = d.Numero,
        CnpjDestinatario = d.CnpjDestinatario,
        CnpjProprietario = d.CnpjProprietario,
        Tipo = d.Tipo,
        SubTipo = d.SubTipo,
        BrutoOrigem = brutoOrigem,
        RecebidoEm = _clock.UtcNow,
        QtdeVolumesI51 = d.QtdeVolumes,
        Itens = d.Itens.Select(i => new ItemOrfaoI51(i.Sequencia ?? 0, i.CodProduto, i.QtdeProdutoSep)).ToList(),
    };

    private static DocumentoI51 Converter(OrfaoI51 o) => new(
        o.Numero, o.CnpjProprietario, o.CnpjDestinatario, o.Tipo, o.SubTipo, o.QtdeVolumesI51,
        o.Itens.Select(i => new ItemI51(i.NumeroSequencialItem == 0 ? null : i.NumeroSequencialItem, i.CodProduto, i.QtdeSeparada, i.QtdeSeparada)).ToList());

    private static bool Igual(string notaNumero, string docNumero) =>
        notaNumero.Trim().TrimStart('0') == docNumero.Trim().TrimStart('0');

    private static string Juntar(string a, string b) => string.IsNullOrEmpty(a) ? b : a + "; " + b;
}
