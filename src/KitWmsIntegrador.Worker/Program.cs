using KitWmsIntegrador.Core.Agrupamento;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Core.Relatorio;
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

// Config operacional (CNPJs/depósito BRF) e parâmetros da I50 (tipo de carga/separação).
// Defaults BRF nas classes de opções; override por appsettings ("Operacional"/"I50")
// e env (ex.: Operacional__CnpjArmazem, I50__TipoCarga). Nada hardcoded nos construtores.
builder.Services
    .AddOptions<OpcoesOperacionais>()
    .Bind(builder.Configuration.GetSection(OpcoesOperacionais.Secao));
builder.Services
    .AddOptions<OpcoesI50>()
    .Bind(builder.Configuration.GetSection(OpcoesI50.Secao));

// Flag da I60 (seção "I60"). Desligada por ora (Habilitada=false): a I50 passou a
// carregar os dados fiscais. O código da I60 permanece e é reativável; o fluxo de
// envio da Etapa 5 (Orquestrador) deve respeitar esta flag.
builder.Services
    .AddOptions<OpcoesI60>()
    .Bind(builder.Configuration.GetSection(OpcoesI60.Secao));

// Relatório de erros/pendências (HTML + PDF). Pasta de saída vem da seção "Relatorio"
// (override por env Relatorio__PastaSaida). Sem rede — só monta e grava arquivos.
builder.Services
    .AddOptions<OpcoesRelatorio>()
    .Bind(builder.Configuration.GetSection(OpcoesRelatorio.Secao));
builder.Services.AddSingleton<IGeradorRelatorio, GeradorRelatorio>();

// Construtores de payload (I50/I60/I10). Apenas montam objetos/JSON — sem rede.
builder.Services.AddSingleton<IConstrutorI50, ConstrutorI50>();
builder.Services.AddSingleton<IConstrutorI60, ConstrutorI60>();
builder.Services.AddSingleton<IConstrutorI10, ConstrutorI10>();

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
