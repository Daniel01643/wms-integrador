using System.Net;

namespace KitWmsIntegrador.Tests.Infra;

/// <summary>
/// <see cref="HttpMessageHandler"/> fake: responde a partir de uma função
/// programável e captura todas as requisições enviadas (método, URI, headers e
/// corpo) para asserts, sem nenhum tráfego de rede real.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _responder;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder)
        => _responder = responder;

    /// <summary>Requisições capturadas, na ordem em que foram enviadas.</summary>
    public List<RequisicaoCapturada> Requisicoes { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? corpo = null;
        if (request.Content is not null)
        {
            corpo = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        var autorizacao = request.Headers.Authorization;
        var capturada = new RequisicaoCapturada(
            request.Method,
            request.RequestUri!,
            autorizacao?.Scheme,
            autorizacao?.Parameter,
            corpo,
            request.Content?.Headers.ContentType?.MediaType);

        Requisicoes.Add(capturada);

        return _responder(request, Requisicoes.Count);
    }

    /// <summary>Cria uma resposta JSON com o status indicado.</summary>
    public static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };
}

/// <summary>Snapshot imutável de uma requisição enviada.</summary>
public sealed record RequisicaoCapturada(
    HttpMethod Metodo,
    Uri Uri,
    string? AuthScheme,
    string? AuthParametro,
    string? Corpo,
    string? ContentType);
