using KitWmsIntegrador.Core.Agrupamento;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Modelos;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Orquestrador;

/// <summary>
/// Implementação reativa do <see cref="IOrquestrador"/>. Processa as notas de forma
/// sequencial (uma falha/pendência de uma nota não impede as demais) e, por nota,
/// executa o algoritmo orientado pelo erro com até <see cref="MaxTentativas"/> tentativas:
///
/// <list type="number">
///   <item>Envia a I50; se sucesso → integrada (e envia I60 só se a flag estiver ligada).</item>
///   <item>Empresa não localizada → envia I10 ("já existe" = OK) e reenvia a I50.</item>
///   <item>Produto não cadastrado → envia I30 de TODOS os itens ("já existe" = OK),
///         registra pendência de revisão e reenvia a I50.</item>
///   <item>Pendente na interface → estado Pendente, registra e segue (sem loop).</item>
///   <item>Qualquer outro erro → registra e segue.</item>
/// </list>
///
/// Ao esgotar as tentativas sem sucesso → não-integrada com motivo e contagem. Nunca
/// trava o lote nem entra em loop. NÃO faz e-mail nem move arquivos; a comunicação é
/// delegada ao <see cref="IClienteWms"/>.
/// </summary>
public sealed class Orquestrador : IOrquestrador
{
    /// <summary>Máximo de tentativas de envio da I50 por nota (confirmado pelo usuário, msg 12).</summary>
    public const int MaxTentativas = 3;

    private readonly IClienteWms _cliente;
    private readonly IConstrutorI50 _i50;
    private readonly IConstrutorI10 _i10;
    private readonly IConstrutorI30 _i30;
    private readonly IConstrutorI60 _i60;
    private readonly ClassificadorRetorno _classificador;
    private readonly OpcoesI60 _opcoesI60;
    private readonly OpcoesI51 _opcoesI51;
    private readonly IClock _clock;
    private readonly ILogger<Orquestrador> _logger;

    public Orquestrador(
        IClienteWms cliente,
        IConstrutorI50 i50,
        IConstrutorI10 i10,
        IConstrutorI30 i30,
        IConstrutorI60 i60,
        ClassificadorRetorno classificador,
        IOptions<OpcoesI60> opcoesI60,
        IClock clock,
        ILogger<Orquestrador> logger,
        IOptions<OpcoesI51>? opcoesI51 = null)
    {
        _opcoesI51 = opcoesI51?.Value ?? new OpcoesI51();
        _cliente = cliente;
        _i50 = i50;
        _i10 = i10;
        _i30 = i30;
        _i60 = i60;
        _classificador = classificador;
        _opcoesI60 = opcoesI60.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ResultadoIntegracaoNota>> IntegrarAsync(
        Onda onda, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onda);
        return IntegrarAsync(onda.Notas, onda.NumeroTransporte, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResultadoIntegracaoNota>> IntegrarAsync(
        IEnumerable<NotaFiscal> notas, string numeroTransporte,
        CancellationToken cancellationToken = default, bool ehTsp = false)
    {
        ArgumentNullException.ThrowIfNull(notas);

        var resultados = new List<ResultadoIntegracaoNota>();
        foreach (var nota in notas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            resultados.Add(await IntegrarNotaAsync(nota, numeroTransporte, ehTsp, cancellationToken)
                .ConfigureAwait(false));
        }

        return resultados;
    }

    private async Task<ResultadoIntegracaoNota> IntegrarNotaAsync(
        NotaFiscal nota, string? numeroTransporte, bool ehTsp, CancellationToken cancellationToken)
    {
        var historico = new List<string>();

        // Transporte da onda tem prioridade; cai para o da própria nota quando ausente.
        var transporte = string.IsNullOrWhiteSpace(numeroTransporte)
            ? nota.NumeroTransporte
            : numeroTransporte;

        // Caso B: nota sem transporte não pode lançar — vira não-integrada registrada.
        if (string.IsNullOrWhiteSpace(transporte))
        {
            const string motivo = "Nota sem número de transporte (nroagrupseparacao) — não é possível montar a I50.";
            historico.Add(motivo);
            _logger.LogWarning("Nota {Numero} sem transporte; registrada como não integrada.", nota.Numero);
            return new ResultadoIntegracaoNota(
                nota.Numero, string.Empty, Integrada: false, Tentativas: 0,
                EstadoIntegracao.NaoIntegrada, motivo, historico);
        }

        var pendenteRevisaoProduto = false;
        var tentativas = 0;
        var ultimaMensagem = string.Empty;
        // Captura para o relatório (não influencia nenhuma decisão do fluxo).
        var idTransacao = string.Empty;
        var idDocumento = string.Empty;
        var produtosProvisorios = new List<string>();

        // Tempo acumulado das chamadas ao WMS (I50/I10/I30/I60) desta nota, medido com o IClock.
        // Só mede: não influencia nenhuma decisão do fluxo.
        long wmsMs = 0;
        async Task<RespostaWms> Chamar(Func<Task<RespostaWms>> envio)
        {
            var antes = _clock.UtcNow;
            try
            {
                return await envio().ConfigureAwait(false);
            }
            finally
            {
                wmsMs += Math.Max(0L, (long)(_clock.UtcNow - antes).TotalMilliseconds);
            }
        }

        while (tentativas < MaxTentativas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tentativas++;

            var respI50 = await Chamar(() => _cliente
                .EnviarAsync(InterfaceWms.I50, _i50.Construir(nota, transporte, ehTsp), cancellationToken))
                .ConfigureAwait(false);
            ultimaMensagem = _classificador.PrimeiraMensagem(respI50);
            (idTransacao, idDocumento) = Capturar(respI50);
            historico.Add($"I50 (tentativa {tentativas}): {Descrever(respI50, ultimaMensagem)}");

            // (b) Sucesso: integra (e envia I60 só se a flag estiver ligada).
            if (_classificador.EhSucesso(respI50))
            {
                // Com a I51 ligada, a I60 NÃO sai junto da I50: só depois do retorno da separação.
                if (_opcoesI60.Habilitada && _opcoesI51.Habilitada)
                {
                    historico.Add("I60 adiada: será enviada após o retorno da separação (I51).");
                }
                else if (_opcoesI60.Habilitada)
                {
                    var respI60 = await Chamar(() => _cliente
                        .EnviarAsync(InterfaceWms.I60, _i60.Construir(nota), cancellationToken))
                        .ConfigureAwait(false);
                    historico.Add($"I60: {Descrever(respI60, _classificador.PrimeiraMensagem(respI60))}");
                }

                _logger.LogInformation(
                    "Nota {Numero} integrada em {Tentativas} tentativa(s).", nota.Numero, tentativas);

                var estado = pendenteRevisaoProduto ? EstadoIntegracao.Pendente : EstadoIntegracao.Integrada;
                return new ResultadoIntegracaoNota(
                    nota.Numero, transporte, Integrada: true, tentativas,
                    estado, ultimaMensagem, historico)
                {
                    IdTransacao = idTransacao,
                    IdDocumento = idDocumento,
                    ProdutosProvisorios = produtosProvisorios,
                    DuracaoWmsMs = wmsMs,
                };
            }

            // (c) Empresa não localizada: envia I10 e reenvia a I50 (nova tentativa).
            if (_classificador.EhEmpresaNaoLocalizada(respI50))
            {
                var respI10 = await Chamar(() => _cliente
                    .EnviarAsync(InterfaceWms.I10, _i10.Construir(nota), cancellationToken))
                    .ConfigureAwait(false);
                historico.Add($"I10 (empresa): {Descrever(respI10, _classificador.PrimeiraMensagem(respI10))}");
                continue;
            }

            // (d) Produto não cadastrado: envia I30 de TODOS os itens, registra pendência
            // de revisão (produto provisório) e reenvia a I50 (nova tentativa).
            if (_classificador.EhProdutoNaoCadastrado(respI50))
            {
                var respI30 = await Chamar(() => _cliente
                    .EnviarAsync(InterfaceWms.I30, _i30.Construir(nota), cancellationToken))
                    .ConfigureAwait(false);
                pendenteRevisaoProduto = true;
                foreach (var item in nota.Itens)
                {
                    if (!string.IsNullOrWhiteSpace(item.CodigoProduto)
                        && !produtosProvisorios.Contains(item.CodigoProduto))
                    {
                        produtosProvisorios.Add(item.CodigoProduto);
                    }
                }
                historico.Add($"I30 (produto provisório — requer revisão): {Descrever(respI30, _classificador.PrimeiraMensagem(respI30))}");
                continue;
            }

            // (e) Pendente na interface: resultado aceitável; registra e segue (sem loop).
            if (_classificador.EhPendencia(respI50))
            {
                _logger.LogInformation("Nota {Numero} pendente na interface do WMS.", nota.Numero);
                return new ResultadoIntegracaoNota(
                    nota.Numero, transporte, Integrada: false, tentativas,
                    EstadoIntegracao.Pendente, ultimaMensagem, historico)
                {
                    IdTransacao = idTransacao,
                    IdDocumento = idDocumento,
                    ProdutosProvisorios = produtosProvisorios,
                    DuracaoWmsMs = wmsMs,
                };
            }

            // (f) Qualquer outro erro: registra e segue (sem loop).
            _logger.LogWarning(
                "Nota {Numero} com erro não recuperável: {Mensagem}", nota.Numero, ultimaMensagem);
            return new ResultadoIntegracaoNota(
                nota.Numero, transporte, Integrada: false, tentativas,
                EstadoIntegracao.NaoIntegrada, ultimaMensagem, historico)
            {
                IdTransacao = idTransacao,
                IdDocumento = idDocumento,
                ProdutosProvisorios = produtosProvisorios,
                DuracaoWmsMs = wmsMs,
            };
        }

        // Esgotou as tentativas sem sucesso → não-integrada com motivo e contagem.
        var motivoFinal = string.IsNullOrWhiteSpace(ultimaMensagem)
            ? $"Não integrada após {MaxTentativas} tentativas."
            : ultimaMensagem;
        historico.Add($"Esgotadas {MaxTentativas} tentativas sem sucesso.");
        _logger.LogWarning(
            "Nota {Numero} não integrada após {Tentativas} tentativas: {Mensagem}",
            nota.Numero, MaxTentativas, motivoFinal);
        return new ResultadoIntegracaoNota(
            nota.Numero, transporte, Integrada: false, MaxTentativas,
            EstadoIntegracao.NaoIntegrada, motivoFinal, historico)
        {
            IdTransacao = idTransacao,
            IdDocumento = idDocumento,
            ProdutosProvisorios = produtosProvisorios,
            DuracaoWmsMs = wmsMs,
        };
    }

    /// <summary>Extrai a transação e o primeiro idDocumento não vazio (apenas para o relatório).</summary>
    private static (string Transacao, string IdDocumento) Capturar(RespostaWms resposta)
    {
        var doc = resposta.Retornos?
            .Select(r => r.IdDocumento)
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)) ?? string.Empty;
        return (resposta.Transacao ?? string.Empty, doc);
    }

    private static string Descrever(RespostaWms resposta, string mensagem)
    {
        var texto = string.IsNullOrWhiteSpace(mensagem) ? "(sem mensagem)" : mensagem;
        return $"sucesso={resposta.Sucesso} {texto}".Trim();
    }
}
