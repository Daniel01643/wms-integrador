using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Worker;

/// <summary>
/// Registro do cliente WMS como UMA instância única (singleton): Orquestrador, consulta da I51 e devolução
/// compartilham o mesmo token, o mesmo lock de renovação e o mesmo retry. O 401 transitório do
/// <c>GetToken</c> vem de concorrência de sessão no WMS; instâncias separadas com tokens separados o agravariam.
/// </summary>
public static class RegistroClienteWms
{
    /// <summary>Nome do <c>HttpClient</c> nomeado do WMS.</summary>
    public const string NomeHttpClient = "wms";

    /// <summary>Registra o <see cref="ClienteWmsHttp"/> único e o expõe como <see cref="IClienteWms"/> e <see cref="IClienteWmsBruto"/>.</summary>
    public static IServiceCollection AdicionarClienteWms(this IServiceCollection services)
    {
        services
            .AddHttpClient(NomeHttpClient, (sp, http) =>
            {
                var opcoes = sp.GetRequiredService<IOptions<OpcoesWms>>().Value;
                if (!string.IsNullOrWhiteSpace(opcoes.BaseUrl))
                {
                    // BaseAddress precisa terminar em "/" para compor caminhos relativos.
                    var baseUrl = opcoes.BaseUrl.EndsWith('/') ? opcoes.BaseUrl : opcoes.BaseUrl + "/";
                    http.BaseAddress = new Uri(baseUrl);
                }

                http.Timeout = TimeSpan.FromSeconds(opcoes.TimeoutSegundos);
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
                // Windows Server 2012 R2: o WMS de produção exige TLS 1.2. Forçamos o protocolo no handler para
                // não depender do SChannel/registro do SO (que negociava de forma intermitente, causando 401 no
                // GetToken). TLS 1.2 e TLS 1.3 quando disponível. PooledConnectionLifetime renova o DNS/conexões
                // porque o HttpClient agora vive o tempo todo do processo (singleton).
                new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                    {
                        EnabledSslProtocols =
                            System.Security.Authentication.SslProtocols.Tls12 |
                            System.Security.Authentication.SslProtocols.Tls13,
                    },
                });

        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<ClienteWmsHttp>(
            sp, sp.GetRequiredService<IHttpClientFactory>().CreateClient(NomeHttpClient)));
        services.AddSingleton<IClienteWms>(sp => sp.GetRequiredService<ClienteWmsHttp>());
        services.AddSingleton<IClienteWmsBruto>(sp => sp.GetRequiredService<ClienteWmsHttp>());
        return services;
    }
}
