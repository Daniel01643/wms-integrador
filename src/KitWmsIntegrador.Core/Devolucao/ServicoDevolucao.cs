using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Construtores.Dtos;
using KitWmsIntegrador.Core.Leitura;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Devolucao;

/// <summary>Núcleo da tela de devolução (sem HTTP): valida, localiza XML, monta a I40 e envia o lote.</summary>
public interface IServicoDevolucao
{
    /// <summary>
    /// Processa o pedido. Chaves inválidas ou sem XML não bloqueiam as demais.
    /// </summary>
    /// <exception cref="SolicitacaoDevolucaoInvalidaException">Lista vazia ou excesso de chaves.</exception>
    /// <exception cref="ConfiguracaoDevolucaoException">Pasta de entrada não configurada.</exception>
    Task<ResultadoLoteDevolucao> EnviarAsync(SolicitacaoDevolucao solicitacao, CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca as chaves da nota pelo número + série na retenção (Processados e pacote_*), filtrando
    /// pelo CNPJ do depositante. Não lê os XMLs. Pode devolver zero, uma ou várias chaves distintas;
    /// quem decide o que enviar é o colaborador.
    /// </summary>
    /// <exception cref="SolicitacaoDevolucaoInvalidaException">Número ou série vazios, não numéricos ou grandes demais.</exception>
    /// <exception cref="ConfiguracaoDevolucaoException">Pasta de entrada ou CNPJ do depositante não configurados.</exception>
    ResultadoBuscaDevolucao Buscar(string? numero, string? serie);
}

/// <summary>
/// Implementação do <see cref="IServicoDevolucao"/>. Envia UMA requisição I40 com todas as notas
/// válidas do pedido e mapeia a resposta por documento (número da nota).
/// </summary>
public sealed class ServicoDevolucao : IServicoDevolucao
{
    private readonly IClienteWms _cliente;
    private readonly IConstrutorI40 _construtor;
    private readonly LeitorNotaFiscalXml _leitor;
    private readonly AuditoriaDevolucao _auditoria;
    private readonly OpcoesPasta _pastas;
    private readonly OpcoesOperacionais _operacional;
    private readonly OpcoesDevolucao _opcoes;
    private readonly ILogger<ServicoDevolucao> _logger;

    public ServicoDevolucao(
        IClienteWms cliente,
        IConstrutorI40 construtor,
        LeitorNotaFiscalXml leitor,
        AuditoriaDevolucao auditoria,
        IOptions<OpcoesPasta> pastas,
        IOptions<OpcoesOperacionais> operacional,
        IOptions<OpcoesDevolucao> opcoes,
        ILogger<ServicoDevolucao> logger)
    {
        _cliente = cliente;
        _construtor = construtor;
        _leitor = leitor;
        _auditoria = auditoria;
        _pastas = pastas.Value;
        _operacional = operacional.Value;
        _opcoes = opcoes.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ResultadoLoteDevolucao> EnviarAsync(
        SolicitacaoDevolucao solicitacao, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solicitacao);

        var chaves = ValidadorChavesDevolucao.Normalizar(solicitacao.Chaves);
        var max = _opcoes.MaxChavesPorEnvio > 0 ? _opcoes.MaxChavesPorEnvio : OpcoesDevolucao.MaxChavesPorEnvioPadrao;
        if (chaves.Count == 0)
        {
            throw new SolicitacaoDevolucaoInvalidaException("Informe ao menos uma chave de acesso.");
        }

        if (chaves.Count > max)
        {
            throw new SolicitacaoDevolucaoInvalidaException($"Máximo de {max} chaves por envio.");
        }

        if (string.IsNullOrWhiteSpace(_pastas.Entrada))
        {
            throw new ConfiguracaoDevolucaoException("Pastas:Entrada não está configurada.");
        }

        var localizador = new LocalizadorXmlDevolucao(_pastas.CaminhoProcessados);
        var resultados = new ResultadoChaveDevolucao?[chaves.Count];
        var paraEnvio = new List<(int Indice, string Chave, string Nota, NotaFiscalI40 Payload)>();

        for (var i = 0; i < chaves.Count; i++)
        {
            var chave = chaves[i];
            var notaDaChave = ValidadorChavesDevolucao.NotaDaChave(chave);

            var erroChave = LocalizadorXmlDevolucao.ValidarChave(chave);
            if (erroChave is not null)
            {
                resultados[i] = Local(chave, string.Empty, StatusChaveDevolucao.ChaveInvalida, erroChave);
                continue;
            }

            try
            {
                var caminho = localizador.Localizar(chave);
                if (caminho is null)
                {
                    resultados[i] = Local(chave, notaDaChave, StatusChaveDevolucao.XmlNaoEncontrado,
                        "XML não encontrado na retenção de 60 dias.");
                    continue;
                }

                var nota = _leitor.ParseArquivo(caminho);
                var payload = _construtor.Construir(nota);
                paraEnvio.Add((i, chave, nota.Numero, payload.NotaFiscal[0]));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Devolução: falha local ao preparar a chave {Chave}", chave);
                resultados[i] = Local(chave, notaDaChave, StatusChaveDevolucao.ErroInterno,
                    "Não foi possível ler o XML desta nota.");
            }
        }

        var transacao = string.Empty;
        if (paraEnvio.Count > 0)
        {
            var corpo = new PayloadI40 { NotaFiscal = paraEnvio.Select(p => p.Payload).ToArray() };
            RespostaWms? resposta = null;
            string? falha = null;
            try
            {
                resposta = await _cliente.EnviarAsync(InterfaceWms.I40, corpo, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Devolução: falha no envio da I40 ({Qtde} nota(s))", paraEnvio.Count);
                falha = string.IsNullOrWhiteSpace(ex.Message) ? "Falha ao comunicar com o WMS." : ex.Message;
            }

            transacao = resposta?.Transacao ?? string.Empty;
            foreach (var (indice, chave, nota, _) in paraEnvio)
            {
                var (sucesso, mensagem) = falha is not null
                    ? (false, falha)
                    : MapearResposta(resposta!, nota, paraEnvio.Count, paraEnvio.FindIndex(p => p.Indice == indice));
                resultados[indice] = new ResultadoChaveDevolucao
                {
                    Chave = chave,
                    Nota = nota,
                    Serie = ValidadorChavesDevolucao.SerieDaChave(chave)?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    Status = sucesso ? StatusChaveDevolucao.Enviada : StatusChaveDevolucao.ErroWms,
                    Mensagem = mensagem,
                };
            }
        }

        var finais = resultados.Select(r => r!).ToList();
        foreach (var r in finais)
        {
            _logger.LogInformation(
                "Devolução: chave={Chave} nota={Nota} status={Status} transacao={Transacao}",
                r.Chave, r.Nota, r.Status, string.IsNullOrEmpty(transacao) ? "-" : transacao);
        }

        _auditoria.Registrar(solicitacao.Origem, transacao, finais);

        return new ResultadoLoteDevolucao
        {
            Total = finais.Count,
            Enviadas = finais.Count(r => r.Sucesso),
            Transacao = transacao,
            Resultados = finais,
        };
    }

    /// <inheritdoc />
    public ResultadoBuscaDevolucao Buscar(string? numero, string? serie)
    {
        var erro = ValidadorChavesDevolucao.ValidarNumeroSerie(numero, serie, out var numeroInt, out var serieInt);
        if (erro is not null)
        {
            throw new SolicitacaoDevolucaoInvalidaException(erro);
        }

        if (string.IsNullOrWhiteSpace(_pastas.Entrada))
        {
            throw new ConfiguracaoDevolucaoException("Pastas:Entrada não está configurada.");
        }

        var cnpj = ValidadorChavesDevolucao.SoDigitos(_operacional.CnpjDepositante);
        if (cnpj.Length != 14)
        {
            throw new ConfiguracaoDevolucaoException("Operacional:CnpjDepositante não está configurado com 14 dígitos.");
        }

        var opcoes = new LocalizadorXmlDevolucao(_pastas.CaminhoProcessados).BuscarPorNumeroSerie(numeroInt, serieInt, cnpj);
        return new ResultadoBuscaDevolucao
        {
            Numero = numeroInt.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Serie = serieInt.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Opcoes = opcoes,
        };
    }

    /// <summary>
    /// Mapeia a resposta do WMS para uma nota: primeiro por <c>documento</c> (número da nota);
    /// se nenhum casar, por posição quando há um retorno por nota; senão consolida todos os retornos.
    /// </summary>
    private static (bool Sucesso, string Mensagem) MapearResposta(RespostaWms resposta, string nota, int totalNotas, int posicao)
    {
        var retornos = resposta.Retornos;
        if (retornos.Count == 0)
        {
            var ok = EhSucesso(resposta.Sucesso);
            return (ok, ok ? "Registrado com sucesso." : "O WMS não retornou detalhes para esta nota.");
        }

        var doNumero = retornos.Where(r => MesmoDocumento(r.Documento, nota)).ToList();
        if (doNumero.Count == 0 && retornos.Count == totalNotas && retornos.All(r => string.IsNullOrWhiteSpace(r.Documento)))
        {
            doNumero = new List<RetornoWms> { retornos[posicao] };
        }

        if (doNumero.Count == 0)
        {
            doNumero = retornos.ToList();
        }

        var sucesso = doNumero.All(r => EhSucesso(r.Sucesso));
        var mensagem = string.Join(" | ", doNumero
            .Select(r => r.Mensagem?.Trim())
            .Where(m => !string.IsNullOrEmpty(m))
            .Distinct());
        return (sucesso, mensagem);
    }

    private static bool EhSucesso(string? valor) =>
        string.Equals(valor?.Trim(), "S", StringComparison.OrdinalIgnoreCase);

    private static bool MesmoDocumento(string? documento, string nota)
    {
        var d = documento?.Trim().TrimStart('0') ?? string.Empty;
        var n = nota.Trim().TrimStart('0');
        return d.Length > 0 && string.Equals(d, n, StringComparison.Ordinal);
    }

    private static ResultadoChaveDevolucao Local(string chave, string nota, StatusChaveDevolucao status, string mensagem) =>
        new()
        {
            Chave = chave,
            Nota = nota,
            Serie = ValidadorChavesDevolucao.SerieDaChave(chave)?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            Status = status,
            Mensagem = mensagem,
        };
}
