using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.ClienteWms;

/// <summary>
/// Implementação HTTP do <see cref="IClienteWms"/> sobre <see cref="HttpClient"/>.
///
/// Responsabilidades: obter/cachear o token JWT (Basic Auth → Bearer), montar a
/// query string obrigatória com os 5 parâmetros (inclusive os 2 vazios), enviar o
/// POST e desserializar a resposta, com resiliência que distingue erro transitório
/// (retry com backoff) de 401-token-expirado (renova + repete uma vez) e de
/// rejeição de negócio (sem retry).
///
/// NUNCA loga token, header Authorization, credenciais ou o corpo integral da nota.
/// </summary>
public sealed class ClienteWmsHttp : IClienteWms, IClienteWmsBruto
{
    // Política de serialização compartilhada com os construtores de payload (ver JsonWms).
    private static readonly JsonSerializerOptions JsonOpcoes = JsonWms.Opcoes;

    private readonly HttpClient _http;
    private readonly OpcoesWms _opcoes;
    private readonly ILogger<ClienteWmsHttp> _logger;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private readonly GuardaAutenticacaoWms _guarda;
    private readonly IAlertaAutenticacaoWms? _alerta;

    private string? _token;
    private DateTimeOffset? _tokenExpiraEm;
    private int? _ultimoStatusToken;
    private DateTimeOffset _tokenObtidoEm;

    /// <summary>
    /// Cria o cliente. <paramref name="guarda"/> e <paramref name="alerta"/> são opcionais: sem guarda, usa uma
    /// própria (cooldown por instância); sem alerta, só loga.
    /// </summary>
    public ClienteWmsHttp(
        HttpClient http,
        IOptions<OpcoesWms> opcoes,
        ILogger<ClienteWmsHttp> logger,
        IClock clock,
        GuardaAutenticacaoWms? guarda = null,
        IAlertaAutenticacaoWms? alerta = null)
    {
        _http = http;
        _opcoes = opcoes.Value;
        _logger = logger;
        _clock = clock;
        _guarda = guarda ?? new GuardaAutenticacaoWms(clock, opcoes);
        _alerta = alerta;
    }

    /// <inheritdoc />
    public Task<RespostaWms> EnviarAsync(InterfaceWms identificador, object payload, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpcoes);
        return EnviarAsync(identificador, json, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RespostaWms> EnviarAsync(InterfaceWms identificador, string jsonBruto, CancellationToken cancellationToken = default)
    {
        var inicio = Stopwatch.GetTimestamp();
        var corpo = await PostCoreAsync(identificador.ParaIdentificador(), jsonBruto, cancellationToken).ConfigureAwait(false);
        var respostaWms = DesserializarResposta(corpo);

        var duracaoMs = Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;
        _logger.LogInformation(
            "Envio WMS concluído: metodo=EnviarAsync identificador={Identificador} transacao={Transacao} sucesso={Sucesso} duracaoMs={DuracaoMs:F0}",
            identificador, respostaWms.Transacao, respostaWms.Sucesso, duracaoMs);

        // Rejeição de negócio (sucesso="N") NÃO é retry: devolve a resposta tipada.
        return respostaWms;
    }

    /// <inheritdoc />
    public async Task<string> PostBrutoAsync(string identificadorEdi, string jsonBruto, CancellationToken cancellationToken = default)
    {
        var inicio = Stopwatch.GetTimestamp();
        var corpo = await PostCoreAsync(identificadorEdi, jsonBruto, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Envio WMS concluído: metodo=PostBrutoAsync identificador={Identificador} duracaoMs={DuracaoMs:F0}",
            identificadorEdi, Stopwatch.GetElapsedTime(inicio).TotalMilliseconds);
        return corpo;
    }

    // Núcleo compartilhado: token, query string de 5 parâmetros, retry/backoff e 401. Devolve o
    // corpo bruto da resposta de sucesso HTTP. Nunca loga corpo, token nem credenciais.
    private async Task<string> PostCoreAsync(string identificador, string jsonBruto, CancellationToken cancellationToken)
    {
        var relativo = $"integracao/Post{MontarQueryString(identificador)}";

        var tentativa = 0;
        var jaRenovouPorToken = false;

        while (true)
        {
            tentativa++;
            cancellationToken.ThrowIfCancellationRequested();

            var token = await GarantirTokenAsync(cancellationToken).ConfigureAwait(false);

            HttpResponseMessage resposta;
            try
            {
                using var requisicao = new HttpRequestMessage(HttpMethod.Post, relativo)
                {
                    Content = new StringContent(jsonBruto, Encoding.UTF8, "application/json"),
                };
                requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                resposta = await _http.SendAsync(requisicao, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (tentativa < _opcoes.MaxTentativas)
            {
                _logger.LogWarning(ex,
                    "Falha de rede no envio WMS (identificador={Identificador}, tentativa={Tentativa}/{Max}); nova tentativa com backoff.",
                    identificador, tentativa, _opcoes.MaxTentativas);
                await EsperarBackoffAsync(tentativa, cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested && tentativa < _opcoes.MaxTentativas)
            {
                // TaskCanceledException sem cancelamento do chamador = timeout do HttpClient.
                _logger.LogWarning(ex,
                    "Timeout no envio WMS (identificador={Identificador}, tentativa={Tentativa}/{Max}); nova tentativa com backoff.",
                    identificador, tentativa, _opcoes.MaxTentativas);
                await EsperarBackoffAsync(tentativa, cancellationToken).ConfigureAwait(false);
                continue;
            }

            using (resposta)
            {
                // 401 por token expirado: renova e repete UMA vez, fora do contador de backoff.
                if (resposta.StatusCode == HttpStatusCode.Unauthorized && !jaRenovouPorToken)
                {
                    jaRenovouPorToken = true;
                    tentativa--; // não consome tentativa de backoff
                    InvalidarToken();
                    _logger.LogWarning(
                        "HTTP 401 no envio WMS (identificador={Identificador}); renovando token e repetindo uma vez.",
                        identificador);
                    continue;
                }

                // 5xx = transitório: retry com backoff.
                if ((int)resposta.StatusCode >= 500 && tentativa < _opcoes.MaxTentativas)
                {
                    _logger.LogWarning(
                        "HTTP {Status} no envio WMS (identificador={Identificador}, tentativa={Tentativa}/{Max}); nova tentativa com backoff.",
                        (int)resposta.StatusCode, identificador, tentativa, _opcoes.MaxTentativas);
                    await EsperarBackoffAsync(tentativa, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // 4xx (≠401) e 5xx esgotado: erro de transporte definitivo.
                resposta.EnsureSuccessStatusCode();

                return await resposta.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public ResultadoWms Classificar(RetornoWms retorno)
    {
        var mensagem = retorno.Mensagem ?? string.Empty;
        if (mensagem.Contains("pendente na interface", StringComparison.OrdinalIgnoreCase))
        {
            return ResultadoWms.Pendente;
        }

        if (string.Equals(retorno.Sucesso, "S", StringComparison.OrdinalIgnoreCase))
        {
            return ResultadoWms.Sucesso;
        }

        return ResultadoWms.Rejeitado;
    }

    /// <summary>
    /// Monta a query string obrigatória com os 5 parâmetros SEMPRE presentes,
    /// inclusive <c>CNPJREQUISITANTE</c> e <c>CNPJDESTINO</c> (vazios por ora). A
    /// ausência de qualquer um causava HTTP 404 no WMS.
    /// </summary>
    private string MontarQueryString(string identificador)
    {
        var sb = new StringBuilder();
        sb.Append("?IDENTIFICADOR=").Append(Uri.EscapeDataString(identificador));
        sb.Append("&REQUISITANTE=").Append(Uri.EscapeDataString(_opcoes.Requisitante));
        sb.Append("&CNPJREQUISITANTE").Append(ValorOpcional(_opcoes.CnpjRequisitante));
        sb.Append("&DESTINO=").Append(Uri.EscapeDataString(_opcoes.Destino));
        sb.Append("&CNPJDESTINO").Append(ValorOpcional(_opcoes.CnpjDestino));
        return sb.ToString();

        static string ValorOpcional(string valor) =>
            string.IsNullOrEmpty(valor) ? string.Empty : "=" + Uri.EscapeDataString(valor);
    }

    private async Task<string> GarantirTokenAsync(CancellationToken cancellationToken)
    {
        if (TokenValido())
        {
            return _token!;
        }

        // Alertas ficam FORA do _tokenLock: um SMTP lento não pode travar quem espera o token.
        var alertarIndisponivel = false;
        var alertarRecuperacao = false;
        int? statusAlerta = null;
        DateTimeOffset proximaAlerta = default;

        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                // Dupla verificação: outra thread pode ter renovado enquanto aguardávamos.
                if (TokenValido())
                {
                    return _token!;
                }

                // Cooldown global: zero chamadas ao WMS. Token ainda dentro do exp continua utilizável.
                if (_guarda.Bloqueada)
                {
                    if (TokenUtilizavel())
                    {
                        return _token!;
                    }

                    throw new AutenticacaoWmsIndisponivelException(
                        "GetToken do WMS em cooldown até " + _guarda.ProximaTentativaUtc?.ToString("O") + ".",
                        _ultimoStatusToken, true, _guarda.ProximaTentativaUtc);
                }

                var novoToken = await ObterTokenAsync(cancellationToken).ConfigureAwait(false);
                _token = novoToken;
                _tokenExpiraEm = JwtHelper.ObterExpiracao(novoToken);
                _tokenObtidoEm = _clock.UtcNow;
                _ultimoStatusToken = null;
                alertarRecuperacao = _guarda.RegistrarSucesso();
                return novoToken;
            }
            catch (AutenticacaoWmsIndisponivelException ex) when (!ex.EmCooldown)
            {
                _ultimoStatusToken = ex.Status;
                alertarIndisponivel = _guarda.RegistrarFalha(out var proxima);
                statusAlerta = ex.Status;
                proximaAlerta = proxima;
                _logger.LogError(
                    "GetToken do WMS indisponível (status={Status}); novas tentativas em cooldown até {Proxima:O}. Nenhuma nota será enviada para Erro.",
                    ex.Status, proxima);

                // Renovação proativa que falhou: o token atual ainda vale até o exp, segue com ele.
                if (TokenUtilizavel())
                {
                    return _token!;
                }

                throw new AutenticacaoWmsIndisponivelException(ex.Message, ex.Status, false, proxima);
            }
        }
        finally
        {
            _tokenLock.Release();
            if (alertarIndisponivel)
            {
                await DispararAlertaAsync(a => a.IndisponivelAsync(statusAlerta, proximaAlerta, CancellationToken.None)).ConfigureAwait(false);
            }

            if (alertarRecuperacao)
            {
                _logger.LogInformation("GetToken do WMS voltou a funcionar.");
                await DispararAlertaAsync(a => a.RecuperadoAsync(CancellationToken.None)).ConfigureAwait(false);
            }
        }
    }

    private async Task DispararAlertaAsync(Func<IAlertaAutenticacaoWms, Task> acao)
    {
        if (_alerta is null || !_opcoes.AlertaAutenticacao)
        {
            return;
        }

        try
        {
            await acao(_alerta).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao disparar o alerta de autenticação do WMS (ignorada).");
        }
    }

    private bool TokenValido()
    {
        if (string.IsNullOrEmpty(_token))
        {
            return false;
        }

        // Validade máxima configurável (default 3 min): o WMS pode invalidar tokens antigos; 0 = só o exp do JWT.
        if (_opcoes.ValidadeMaximaTokenMinutos > 0
            && _clock.UtcNow >= _tokenObtidoEm + TimeSpan.FromMinutes(_opcoes.ValidadeMaximaTokenMinutos))
        {
            return false;
        }

        if (_tokenExpiraEm is null)
        {
            // Sem exp conhecido: considera válido e renova sob 401.
            return true;
        }

        var margem = TimeSpan.FromSeconds(_opcoes.MargemRenovacaoTokenSegundos);
        return _clock.UtcNow < _tokenExpiraEm.Value - margem;
    }

    // Token ainda dentro do exp (mesmo na margem de renovação): pode ser usado enquanto o GetToken falha.
    private bool TokenUtilizavel() =>
        !string.IsNullOrEmpty(_token) && (_tokenExpiraEm is null || _clock.UtcNow < _tokenExpiraEm.Value);

    private void InvalidarToken()
    {
        _token = null;
        _tokenExpiraEm = null;
    }

    /// <summary>
    /// Diagnóstico: UM GetToken, sem cache nem cooldown (não altera o estado do cliente). Devolve o status HTTP
    /// (null em timeout/rede) e se veio um token válido. Nunca devolve nem loga o token.
    /// </summary>
    public async Task<(int? Status, bool Ok)> TestarTokenAsync(CancellationToken cancellationToken = default)
    {
        var r = await TentarGetTokenAsync(1, 1, cancellationToken).ConfigureAwait(false);
        return (r.Status, r.Token is not null);
    }

    private sealed record ResultadoGetToken(int? Status, string? Token);

    // Obtém o token com as tentativas imediatas; esgotadas, lança AutenticacaoWmsIndisponivelException
    // (a guarda global aplica o cooldown). Qualquer falha de GetToken é falha de autenticação, nunca da nota.
    private async Task<string> ObterTokenAsync(CancellationToken cancellationToken)
    {
        var max = Math.Max(1, _opcoes.TokenTentativasImediatas);
        ResultadoGetToken? ultimo = null;
        for (var tentativa = 1; tentativa <= max; tentativa++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ultimo = await TentarGetTokenAsync(tentativa, max, cancellationToken).ConfigureAwait(false);
            if (ultimo.Token is not null)
            {
                return ultimo.Token;
            }

            if (tentativa < max)
            {
                await EsperarBackoffAsync(tentativa, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new AutenticacaoWmsIndisponivelException(
            "GetToken do WMS falhou" + (ultimo?.Status is { } s ? " (HTTP " + s + ")." : " (sem resposta)."),
            ultimo?.Status, false, null);
    }

    private async Task<ResultadoGetToken> TentarGetTokenAsync(int tentativa, int max, CancellationToken cancellationToken)
    {
        var credenciais = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_opcoes.Usuario}:{_opcoes.Senha}"));
        try
        {
            using var requisicao = new HttpRequestMessage(HttpMethod.Get, "Integracao/GetToken");
            requisicao.Headers.Authorization = new AuthenticationHeaderValue("Basic", credenciais);
            using var resposta = await _http.SendAsync(requisicao, cancellationToken).ConfigureAwait(false);
            var status = (int)resposta.StatusCode;
            var corpo = await resposta.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!resposta.IsSuccessStatusCode)
            {
                LogarFalhaGetToken(resposta, corpo, tentativa, max);
                return new ResultadoGetToken(status, null);
            }

            string? token = null;
            try
            {
                token = JsonSerializer.Deserialize<TokenResposta>(corpo, JsonOpcoes)?.Token;
            }
            catch (JsonException)
            {
                // tratado abaixo como "sem token"
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning(
                    "GetToken do WMS respondeu HTTP {Status} sem token válido (tentativa={Tentativa}/{Max}, corpo de {Bytes} bytes).",
                    status, tentativa, max, corpo.Length);
                return new ResultadoGetToken(status, null);
            }

            return new ResultadoGetToken(status, token);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                "Falha de rede no GetToken (tentativa={Tentativa}/{Max}): {Tipo}.", tentativa, max, ex.GetType().Name);
            return new ResultadoGetToken(null, null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Timeout no GetToken (tentativa={Tentativa}/{Max}).", tentativa, max);
            return new ResultadoGetToken(null, null);
        }
    }

    // Diagnóstico do 401/403/5xx: status, WWW-Authenticate, corpo truncado e request id. Sem token,
    // Authorization, usuário nem senha (o corpo de erro do WMS não os contém e é truncado a 200 caracteres).
    private void LogarFalhaGetToken(HttpResponseMessage resposta, string corpo, int tentativa, int max)
    {
        var wwwAuth = string.Join(" | ", resposta.Headers.WwwAuthenticate.Select(h => h.ToString()));
        _logger.LogWarning(
            "GetToken do WMS falhou: status={Status} wwwAuthenticate={WwwAuthenticate} requestId={RequestId} " +
            "corpo={Corpo} tentativa={Tentativa}/{Max}",
            (int)resposta.StatusCode, Truncar(wwwAuth), IdRequisicao(resposta), Truncar(corpo), tentativa, max);
    }

    private static string IdRequisicao(HttpResponseMessage resposta)
    {
        foreach (var nome in new[] { "X-Request-Id", "Request-Id", "X-Correlation-Id", "traceparent" })
        {
            if (resposta.Headers.TryGetValues(nome, out var valores))
            {
                return Truncar(string.Join(",", valores));
            }
        }

        return "(ausente)";
    }

    private static string Truncar(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return string.Empty;
        }

        var limpo = new string(texto.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return limpo.Length <= 200 ? limpo : limpo[..200] + "...";
    }

    private static RespostaWms DesserializarResposta(string corpo)
    {
        var resposta = JsonSerializer.Deserialize<RespostaWms>(corpo, JsonOpcoes);
        return resposta ?? new RespostaWms();
    }

    private async Task EsperarBackoffAsync(int tentativa, CancellationToken cancellationToken)
    {
        // Backoff exponencial simples: 2^(tentativa-1) * 500ms (0,5s, 1s, 2s, ...).
        var atraso = TimeSpan.FromMilliseconds(500 * Math.Pow(2, tentativa - 1));
        await Task.Delay(atraso, cancellationToken).ConfigureAwait(false);
    }

    private sealed record TokenResposta
    {
        [JsonPropertyName("token")]
        public string Token { get; init; } = string.Empty;
    }
}
