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
public sealed class ClienteWmsHttp : IClienteWms
{
    private static readonly JsonSerializerOptions JsonOpcoes = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly OpcoesWms _opcoes;
    private readonly ILogger<ClienteWmsHttp> _logger;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _token;
    private DateTimeOffset? _tokenExpiraEm;

    public ClienteWmsHttp(
        HttpClient http,
        IOptions<OpcoesWms> opcoes,
        ILogger<ClienteWmsHttp> logger,
        IClock clock)
    {
        _http = http;
        _opcoes = opcoes.Value;
        _logger = logger;
        _clock = clock;
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
        var relativo = $"integracao/Post{MontarQueryString(identificador)}";
        var inicio = Stopwatch.GetTimestamp();

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

                var corpo = await resposta.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var respostaWms = DesserializarResposta(corpo);

                var duracaoMs = Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;
                _logger.LogInformation(
                    "Envio WMS concluído: metodo=EnviarAsync identificador={Identificador} transacao={Transacao} sucesso={Sucesso} duracaoMs={DuracaoMs:F0}",
                    identificador, respostaWms.Transacao, respostaWms.Sucesso, duracaoMs);

                // Rejeição de negócio (sucesso="N") NÃO é retry: devolve a resposta tipada.
                return respostaWms;
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
    private string MontarQueryString(InterfaceWms identificador)
    {
        var sb = new StringBuilder();
        sb.Append("?IDENTIFICADOR=").Append(Uri.EscapeDataString(identificador.ParaIdentificador()));
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

        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Dupla verificação: outra thread pode ter renovado enquanto aguardávamos.
            if (TokenValido())
            {
                return _token!;
            }

            var novoToken = await ObterTokenAsync(cancellationToken).ConfigureAwait(false);
            _token = novoToken;
            _tokenExpiraEm = JwtHelper.ObterExpiracao(novoToken);
            return novoToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private bool TokenValido()
    {
        if (string.IsNullOrEmpty(_token))
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

    private void InvalidarToken()
    {
        _token = null;
        _tokenExpiraEm = null;
    }

    private async Task<string> ObterTokenAsync(CancellationToken cancellationToken)
    {
        var credenciais = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_opcoes.Usuario}:{_opcoes.Senha}"));

        using var requisicao = new HttpRequestMessage(HttpMethod.Get, "Integracao/GetToken");
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Basic", credenciais);

        using var resposta = await _http.SendAsync(requisicao, cancellationToken).ConfigureAwait(false);
        resposta.EnsureSuccessStatusCode();

        var corpo = await resposta.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var dto = JsonSerializer.Deserialize<TokenResposta>(corpo, JsonOpcoes);
        if (dto is null || string.IsNullOrWhiteSpace(dto.Token))
        {
            throw new InvalidOperationException("GetToken não retornou um token válido.");
        }

        return dto.Token;
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
