using System.Text.Json;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Separacao;

/// <summary>Contagens de um ciclo da I51 (para log e testes).</summary>
public sealed record ResultadoCicloI51
{
    /// <summary>O TODOS foi consultado.</summary>
    public bool TodosConsultado { get; init; }

    /// <summary>Consultas UNICO de recuperação feitas.</summary>
    public int UnicosConsultados { get; init; }

    /// <summary>Notas que passaram a Separada.</summary>
    public int NotasSeparadas { get; init; }

    /// <summary>Notas que passaram a Atrasada.</summary>
    public int NotasAtrasadas { get; init; }

    /// <summary>Documentos sem nota correspondente.</summary>
    public int Orfaos { get; init; }

    /// <summary>Documentos de outro proprietário.</summary>
    public int OutroProprietario { get; init; }

    /// <summary>Resumo do passo da I60.</summary>
    public ResumoI60 I60 { get; init; } = ResumoI60.Vazio;
}

/// <summary>
/// Ciclo da consulta periódica da I51: atrasos, brutos pendentes, TODOS (só se há nota aguardando
/// recente), UNICO de recuperação das atrasadas, I60 das separadas e limpeza. A resposta bruta é
/// gravada em disco ANTES de processar (o TODOS não repete). Com <c>I51:Habilitada=false</c> não faz nada.
/// </summary>
public sealed class ServicoSeparacaoI51
{
    private static readonly TimeSpan IntervaloLimpeza = TimeSpan.FromHours(24);

    private readonly IRepositorioSeparacao _repositorio;
    private readonly ArmazenamentoBrutoI51 _brutos;
    private readonly ClienteI51 _cliente;
    private readonly CasadorDocumentoNota _casador;
    private readonly RepositorioOrfaosI51 _orfaos;
    private readonly ProcessadorI60Separacao _i60;
    private readonly ComandosI51 _comandos;
    private readonly AlertaI60 _alerta;
    private readonly OpcoesI51 _opcoes;
    private readonly IClock _clock;
    private readonly ILogger<ServicoSeparacaoI51> _logger;
    private readonly Dictionary<string, int> _tentativasBruto = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _ultimaLimpeza = DateTimeOffset.MinValue;

    /// <summary>Cria o serviço.</summary>
    public ServicoSeparacaoI51(
        IRepositorioSeparacao repositorio, ArmazenamentoBrutoI51 brutos, ClienteI51 cliente, CasadorDocumentoNota casador,
        RepositorioOrfaosI51 orfaos, ProcessadorI60Separacao i60, ComandosI51 comandos, AlertaI60 alerta, IOptions<OpcoesI51> opcoes,
        IClock clock, ILogger<ServicoSeparacaoI51> logger)
    {
        _repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        _brutos = brutos ?? throw new ArgumentNullException(nameof(brutos));
        _cliente = cliente ?? throw new ArgumentNullException(nameof(cliente));
        _casador = casador ?? throw new ArgumentNullException(nameof(casador));
        _orfaos = orfaos ?? throw new ArgumentNullException(nameof(orfaos));
        _i60 = i60 ?? throw new ArgumentNullException(nameof(i60));
        _comandos = comandos ?? throw new ArgumentNullException(nameof(comandos));
        _alerta = alerta ?? throw new ArgumentNullException(nameof(alerta));
        _opcoes = opcoes?.Value ?? throw new ArgumentNullException(nameof(opcoes));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Executa um ciclo. Exceção de consulta é tratada dentro (só cancelamento propaga).</summary>
    public async Task<ResultadoCicloI51> CicloAsync(CancellationToken ct)
    {
        if (!_opcoes.Habilitada)
        {
            return new ResultadoCicloI51();
        }

        var resultado = new ResultadoCicloI51();

        // 0) Pedidos de reenfileiramento vindos de outro processo (CLI).
        _comandos.Consumir(_repositorio);

        // 1) Atrasos.
        var atrasadas = MarcarAtrasadas();

        // 2) Órfãos (documento chegou antes da nota) e brutos que sobraram de um ciclo anterior.
        var separadas = 0;
        var orfaosNovos = 0;
        try
        {
            separadas += _casador.ReprocessarOrfaos().NotasSeparadas;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Falha ao reavaliar os documentos órfãos da I51.");
        }

        foreach (var pendente in _brutos.ListarPendentes())
        {
            ct.ThrowIfCancellationRequested();
            var r = ProcessarBruto(pendente);
            separadas += r?.NotasSeparadas ?? 0;
        }

        // 3) TODOS: só se há nota aguardando recente (senão consumiria documentos que não temos como casar).
        var todos = false;
        var outroProp = 0;
        if (HaNotaAguardandoRecente())
        {
            todos = true;
            var r = await ConsultarAsync(ClienteI51.TipoTodos, null, () => _cliente.ConsultarTodosAsync(ct), ct).ConfigureAwait(false);
            separadas += r?.NotasSeparadas ?? 0;
            orfaosNovos += r?.Orfaos ?? 0;
            outroProp += r?.OutroProprietario ?? 0;
        }

        // 4) UNICO de recuperação das atrasadas.
        var unicos = 0;
        foreach (var nota in EscolherParaUnico())
        {
            ct.ThrowIfCancellationRequested();
            // Gravado ANTES da chamada: falha de rede não repete a cada ciclo.
            var agora = _clock.UtcNow;
            if (!_repositorio.Atualizar(nota.Chave, n => n with { UltimoUnicoEm = agora, TentativasUnico = n.TentativasUnico + 1 }))
            {
                continue;
            }

            unicos++;
            var numero = nota.Numero.Trim().TrimStart('0');
            var erro = _cliente.ValidarNumeroUnico(numero);
            if (erro is not null)
            {
                _repositorio.Atualizar(nota.Chave, n => n with { Observacao = Juntar(n.Observacao, "UNICO não consultado: " + erro) });
                continue;
            }

            var r = await ConsultarAsync(ClienteI51.TipoUnico, numero, () => _cliente.ConsultarUnicoAsync(numero, ct), ct).ConfigureAwait(false);
            separadas += r?.NotasSeparadas ?? 0;
            orfaosNovos += r?.Orfaos ?? 0;
        }

        // 5) I60 das separadas.
        var resumoI60 = ResumoI60.Vazio;
        try
        {
            resumoI60 = await _i60.ProcessarPendentesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Falha no passo da I60 após a separação (tentará no próximo ciclo).");
        }

        // 5b) Alerta imediato da I60 (varredura por estado; só cancelamento propaga).
        try
        {
            await _alerta.ProcessarAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Falha no alerta imediato da I60 (tentará no próximo ciclo).");
        }

        // 6) Limpeza a cada 24 h.
        Limpar();

        resultado = resultado with
        {
            TodosConsultado = todos,
            UnicosConsultados = unicos,
            NotasSeparadas = separadas,
            NotasAtrasadas = atrasadas,
            Orfaos = orfaosNovos,
            OutroProprietario = outroProp,
            I60 = resumoI60,
        };
        LogarResumo(resultado);
        return resultado;
    }

    private int MarcarAtrasadas()
    {
        var limite = _clock.UtcNow - TimeSpan.FromHours(_opcoes.HorasParaAtraso);
        var total = 0;
        foreach (var nota in _repositorio.ListarPorSituacao(SituacaoNota.AguardandoSeparacao).Where(n => n.RegistradaEm < limite))
        {
            var agora = _clock.UtcNow;
            if (_repositorio.Atualizar(nota.Chave, n => n with { Situacao = SituacaoNota.Atrasada, AtrasadaEm = agora }))
            {
                total++;
                _logger.LogWarning("Nota {Numero} sem retorno da separação há mais de {Horas} h; marcada como atrasada.", nota.Numero, _opcoes.HorasParaAtraso);
            }
        }

        return total;
    }

    private bool HaNotaAguardandoRecente()
    {
        var corte = _clock.UtcNow - TimeSpan.FromDays(_opcoes.DiasMaxTodos);
        return _repositorio.ListarPorSituacao(SituacaoNota.AguardandoSeparacao, SituacaoNota.Atrasada)
            .Any(n => n.RegistradaEm >= corte);
    }

    private IEnumerable<NotaSeparacao> EscolherParaUnico()
    {
        if (_opcoes.MaxUnicoPorCiclo <= 0)
        {
            return Array.Empty<NotaSeparacao>();
        }

        var limite = _clock.UtcNow - TimeSpan.FromMinutes(_opcoes.IntervaloUnicoMinutos);
        return _repositorio.ListarPorSituacao(SituacaoNota.Atrasada)
            .Where(n => n.UltimoUnicoEm is null || n.UltimoUnicoEm <= limite)
            .OrderBy(n => n.UltimoUnicoEm ?? DateTimeOffset.MinValue)
            .ThenBy(n => n.RegistradaEm)
            .Take(_opcoes.MaxUnicoPorCiclo)
            .ToList();
    }

    // Consulta o WMS, grava o bruto ANTES de processar e processa. Falha de comunicação só conta/loga.
    private async Task<ResultadoCasamento?> ConsultarAsync(
        string tipo, string? numero, Func<Task<string>> consulta, CancellationToken ct)
    {
        var saude = _brutos.LerSaude();
        var agora = _clock.UtcNow;
        string corpo;
        try
        {
            corpo = await consulta().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var falhas = saude.FalhasConsecutivas + 1;
            var status = ex is HttpRequestException { StatusCode: { } sc } ? $" (HTTP {(int)sc})" : string.Empty;
            if (falhas >= 3)
            {
                _logger.LogError("Consulta {Tipo} da I51 falhou {Falhas} vez(es) seguida(s): {Erro}{Status}.", tipo, falhas, ex.GetType().Name, status);
            }
            else
            {
                _logger.LogWarning("Consulta {Tipo} da I51 falhou: {Erro}{Status}.", tipo, ex.GetType().Name, status);
            }

            _brutos.GravarSaude(saude with { UltimaTentativaEm = agora, FalhasConsecutivas = falhas });
            return null;
        }

        // "Nada a retornar" (sucesso=N / documentos vazio) não é erro e não gera arquivo.
        if (ParserI51.NadaARetornar(corpo, out var sucesso, out var mensagem))
        {
            var padroes = TextoNormalizado.Lista(_opcoes.PadroesSemRetorno);
            var estranho = padroes.Count > 0
                           && string.Equals(sucesso.Trim(), "N", StringComparison.OrdinalIgnoreCase)
                           && !TextoNormalizado.Casa(mensagem, padroes);
            if (estranho)
            {
                var falhas = saude.FalhasConsecutivas + 1;
                _logger.LogWarning(
                    "Consulta {Tipo} da I51 devolveu 'N' sem documentos com mensagem fora da lista esperada: {Mensagem}",
                    tipo, TextoNormalizado.Truncar(mensagem, 200));
                _brutos.GravarSaude(saude with { UltimaTentativaEm = agora, FalhasConsecutivas = falhas });
                return null;
            }

            var curta = TextoNormalizado.Truncar(mensagem, 200);
            if (!string.Equals(curta, saude.UltimaMensagemVazia, StringComparison.Ordinal) && curta.Length > 0)
            {
                _logger.LogInformation("Consulta {Tipo} da I51 sem retorno: {Mensagem}", tipo, curta);
            }
            else
            {
                _logger.LogDebug("Consulta {Tipo} da I51 sem retorno.", tipo);
            }

            _brutos.GravarSaude(saude with { UltimaTentativaEm = agora, UltimaConsultaOkEm = agora, FalhasConsecutivas = 0, UltimaMensagemVazia = curta });
            return null;
        }

        // Grava ANTES de processar. Se não gravar, ainda processa em memória (UNICO recupera depois).
        var caminho = _brutos.GravarPendente(tipo, numero, corpo);
        _brutos.GravarSaude(saude with { UltimaTentativaEm = agora, UltimaConsultaOkEm = agora, FalhasConsecutivas = 0 });
        if (caminho is not null)
        {
            return ProcessarBruto(caminho);
        }

        _logger.LogError("Não foi possível gravar a resposta bruta da I51; processando só em memória.");
        return ProcessarCorpo(corpo, "(memoria)");
    }

    private ResultadoCasamento? ProcessarBruto(string caminho)
    {
        var corpo = _brutos.Ler(caminho);
        if (corpo is null)
        {
            return null;
        }

        var resultado = ProcessarCorpo(corpo, Path.GetFileName(caminho));
        var ok = resultado is not null;
        if (ok)
        {
            _tentativasBruto.Remove(caminho);
            _brutos.MoverProcessado(caminho);
            return resultado;
        }

        var tentativas = _tentativasBruto.GetValueOrDefault(caminho) + 1;
        _tentativasBruto[caminho] = tentativas;
        if (tentativas >= _opcoes.MaxTentativasProcessarBruto)
        {
            _logger.LogError("Bruto da I51 {Arquivo} esgotou {Max} tentativa(s) de processamento; movido para falhos.", Path.GetFileName(caminho), tentativas);
            _tentativasBruto.Remove(caminho);
            _brutos.MoverFalho(caminho);
        }
        else
        {
            _logger.LogWarning("Bruto da I51 {Arquivo} não processado (tentativa {Tentativa}/{Max}); será repetido no próximo ciclo.", Path.GetFileName(caminho), tentativas, _opcoes.MaxTentativasProcessarBruto);
        }

        return null;
    }

    // null = falha (ilegível, documento inválido ou gravação que falhou): o bruto permanece pendente.
    private ResultadoCasamento? ProcessarCorpo(string corpo, string origem)
    {
        RespostaI51 resposta;
        try
        {
            resposta = ParserI51.Ler(corpo);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            _logger.LogWarning("Resposta da I51 ilegível ({Erro}).", ex.GetType().Name);
            return null;
        }

        var r = _casador.Processar(resposta, origem);
        if (resposta.Invalidos > 0)
        {
            _logger.LogWarning("{Quantidade} documento(s) inválido(s) na resposta da I51 ({Origem}).", resposta.Invalidos, origem);
        }

        return resposta.Invalidos > 0 || r.Falhas > 0 ? null : r;
    }

    private void Limpar()
    {
        var agora = _clock.UtcNow;
        if (agora - _ultimaLimpeza < IntervaloLimpeza)
        {
            return;
        }

        _ultimaLimpeza = agora;
        var notas = _repositorio.Limpar(agora);
        var orfaos = _orfaos.Limpar(agora, _opcoes.DiasRetencaoEstado);
        var brutos = _brutos.Limpar(_opcoes.DiasRetencaoEstado);
        _alerta.Limpar(_opcoes.DiasRetencaoEstado);
        if (notas + orfaos + brutos > 0)
        {
            _logger.LogInformation("Limpeza do estado I51: {Notas} nota(s), {Orfaos} órfão(s), {Brutos} pasta(s) de brutos.", notas, orfaos, brutos);
        }
    }

    private void LogarResumo(ResultadoCicloI51 r)
    {
        var todas = _repositorio.Listar();
        int Conta(SituacaoNota s) => todas.Count(n => n.Situacao == s);
        _logger.LogInformation(
            "Ciclo I51: aguardando={Aguardando} atrasadas={Atrasadas} separadas={Separadas} i60Enviadas={Enviadas} associadas={Associadas} rejeitadas={Rejeitadas} | " +
            "TODOS={Todos} UNICO={Unicos} novasSeparadas={Novas} orfaos={Orfaos} outroProprietario={Outro} | I60: enviadas={IEnviadas} imp={Imp} rejeitadas={IRej} falhasComunicacao={Falhas}",
            Conta(SituacaoNota.AguardandoSeparacao), Conta(SituacaoNota.Atrasada), Conta(SituacaoNota.Separada), Conta(SituacaoNota.I60Enviada),
            Conta(SituacaoNota.Associada), Conta(SituacaoNota.Rejeitada), r.TodosConsultado, r.UnicosConsultados, r.NotasSeparadas, r.Orfaos,
            r.OutroProprietario, r.I60.Enviadas, r.I60.ImportadasImp, r.I60.Rejeitadas, r.I60.FalhasComunicacao);
    }

    private static string Juntar(string a, string b) => string.IsNullOrEmpty(a) ? b : a + "; " + b;
}
