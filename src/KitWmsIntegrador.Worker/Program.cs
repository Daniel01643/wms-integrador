using KitWmsIntegrador.Core.Agrupamento;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Core.Tempo;
using KitWmsIntegrador.Worker;
using KitWmsIntegrador.Worker.Configuracao;

// Smoke test manual do cliente WMS (fora do fluxo padrão, NÃO roda em CI/testes).
// Aciona um GetToken real + POST contra o homolog usando WMS__Usuario/WMS__Senha.
if (args.Contains(SmokeTestWms.Argumento))
{
    return await SmokeTestWms.ExecutarAsync(args);
}

var builder = Host.CreateApplicationBuilder(args);

// Configuração: seção "Entrada" do appsettings + variáveis de ambiente.
// O host genérico já adiciona env vars; o override segue o padrão
// "Entrada__PastaEntrada" (duplo underscore).
builder.Services
    .AddOptions<OpcoesEntrada>()
    .Bind(builder.Configuration.GetSection(OpcoesEntrada.Secao));

// Leitura/parsing/agrupamento (nenhuma comunicação de rede nesta fase).
builder.Services.AddSingleton<LeitorNotaFiscalXml>();
builder.Services.AddSingleton<VarredorPasta>();
builder.Services.AddSingleton<AgrupadorTransporte>();

// Cliente WMS (comunicação HTTP). Credenciais (Usuario/Senha) vêm de env
// (WMS__Usuario / WMS__Senha) — NUNCA do appsettings. BaseAddress/Timeout saem
// das opções da seção "Wms".
builder.Services
    .AddOptions<OpcoesWms>()
    .Bind(builder.Configuration.GetSection(OpcoesWms.Secao));

builder.Services.AddSingleton<IClock, SystemClock>();

builder.Services.AddHttpClient<IClienteWms, ClienteWmsHttp>((sp, http) =>
{
    var opcoes = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpcoesWms>>().Value;
    if (!string.IsNullOrWhiteSpace(opcoes.BaseUrl))
    {
        // BaseAddress precisa terminar em "/" para compor caminhos relativos.
        var baseUrl = opcoes.BaseUrl.EndsWith('/') ? opcoes.BaseUrl : opcoes.BaseUrl + "/";
        http.BaseAddress = new Uri(baseUrl);
    }

    http.Timeout = TimeSpan.FromSeconds(opcoes.TimeoutSegundos);
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

return 0;
