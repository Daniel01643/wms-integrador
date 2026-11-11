using System.Reflection;
using System.Text.Json;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Devolucao;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KitWmsIntegrador.Worker.TelaDevolucao;

/// <summary>Corpo do POST /api/devolucao/enviar.</summary>
public sealed class EnvioDevolucaoRequest
{
    public List<string?>? Chaves { get; set; }
}

/// <summary>
/// Monta o <see cref="WebApplication"/> (Kestrel) da tela de devolução: <c>GET /</c> (página única),
/// <c>GET /api/config</c>, <c>GET /api/saude</c>, <c>GET /api/devolucao/buscar?numero=&amp;serie=</c>
/// (resolve número + série em chave(s)) e <c>POST /api/devolucao/enviar</c>. Sem login.
/// É usado pelo <see cref="ServidorTelaDevolucao"/> e pelos testes de integração do endpoint.
/// </summary>
public static class TelaDevolucaoApp
{
    private const long LimiteCorpoBytes = 64 * 1024;
    private static readonly Lazy<string> Pagina = new(CarregarPagina);

    /// <param name="urls">Ex.: <c>http://0.0.0.0:8085</c> (testes: <c>http://127.0.0.1:0</c>).</param>
    public static WebApplication Criar(
        IServicoDevolucao servico, OpcoesDevolucao opcoes, ILoggerFactory loggerFactory, string urls)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.WebHost.UseUrls(urls);
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.AddServerHeader = false;
            k.Limits.MaxRequestBodySize = LimiteCorpoBytes;
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(loggerFactory);
        // O ciclo de vida (parada) é do host principal do Worker; este host não trata Ctrl+C.
        builder.Services.AddSingleton<IHostLifetime, SemCicloDeVida>();

        var app = builder.Build();
        var logger = loggerFactory.CreateLogger("TelaDevolucao");
        var max = opcoes.MaxChavesPorEnvio > 0 ? opcoes.MaxChavesPorEnvio : OpcoesDevolucao.MaxChavesPorEnvioPadrao;

        app.Use(async (ctx, next) =>
        {
            var h = ctx.Response.Headers;
            h["Cache-Control"] = "no-store";
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Content-Security-Policy"] =
                "default-src 'self'; img-src data:; style-src 'unsafe-inline'; script-src 'unsafe-inline'; frame-ancestors 'none'";
            await next();
        });

        app.MapGet("/", () => Results.Content(Pagina.Value, "text/html; charset=utf-8"));

        app.MapGet("/api/saude", () => Results.Json(new { status = "ok" }));

        app.MapGet("/api/config", () => Results.Json(new
        {
            maxChaves = max,
        }));

        app.MapGet("/api/devolucao/buscar", (string? numero, string? serie) =>
        {
            try
            {
                var busca = servico.Buscar(numero, serie);
                if (busca.Opcoes.Count == 0)
                {
                    return Results.Json(
                        new { erro = "Nota não encontrada na retenção de 60 dias.", numero = busca.Numero, serie = busca.Serie },
                        statusCode: 404);
                }

                return Results.Json(busca);
            }
            catch (SolicitacaoDevolucaoInvalidaException ex)
            {
                return Results.Json(new { erro = ex.Message }, statusCode: 400);
            }
            catch (ConfiguracaoDevolucaoException ex)
            {
                logger.LogWarning("Devolução: configuração insuficiente na busca: {Mensagem}", ex.Message);
                return Results.Json(new { erro = ex.Message }, statusCode: 503);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Devolução: erro inesperado no endpoint de busca");
                return Results.Json(new { erro = "Erro interno ao buscar a nota. Veja o log do serviço." }, statusCode: 500);
            }
        });

        app.MapPost("/api/devolucao/enviar", async (HttpContext ctx) =>
        {
            try
            {
                EnvioDevolucaoRequest? req;
                try
                {
                    req = await ctx.Request.ReadFromJsonAsync<EnvioDevolucaoRequest>(ctx.RequestAborted);
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or BadHttpRequestException)
                {
                    return Results.Json(new { erro = "Pedido inválido: envie JSON com a lista de chaves." }, statusCode: 400);
                }

                if (req is null)
                {
                    return Results.Json(new { erro = "Pedido inválido: corpo vazio." }, statusCode: 400);
                }

                var resultado = await servico.EnviarAsync(new SolicitacaoDevolucao
                {
                    Chaves = (IReadOnlyList<string>)(req.Chaves ?? new List<string?>()).Select(c => c ?? string.Empty).ToList(),
                    Origem = ctx.Connection.RemoteIpAddress?.ToString(),
                }, ctx.RequestAborted);

                return Results.Json(resultado);
            }
            catch (SolicitacaoDevolucaoInvalidaException ex)
            {
                return Results.Json(new { erro = ex.Message }, statusCode: 400);
            }
            catch (ConfiguracaoDevolucaoException ex)
            {
                logger.LogWarning("Devolução: configuração insuficiente: {Mensagem}", ex.Message);
                return Results.Json(new { erro = ex.Message }, statusCode: 503);
            }
            catch (OperationCanceledException)
            {
                return Results.StatusCode(499);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Devolução: erro inesperado no endpoint de envio");
                return Results.Json(new { erro = "Erro interno ao processar o envio. Veja o log do serviço." }, statusCode: 500);
            }
        });

        return app;
    }

    private sealed class SemCicloDeVida : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static string CarregarPagina()
    {
        var html = LerRecurso("PaginaDevolucao.html");
        var logo = LerRecurso("logo-friozem.b64.txt").Trim();
        return html.Replace("{{LOGO_BASE64}}", logo, StringComparison.Ordinal);
    }

    private static string LerRecurso(string nome)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(nome)
            ?? throw new InvalidOperationException($"Recurso embutido não encontrado: {nome}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
