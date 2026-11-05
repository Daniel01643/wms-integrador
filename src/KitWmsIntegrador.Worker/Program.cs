using KitWmsIntegrador.Core.Agrupamento;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Orquestrador;
using KitWmsIntegrador.Core.Pacote;
using Microsoft.Extensions.Options;
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

// Config de e-mail dedicada (opcional, ignorada pelo git): email.settings.json ao lado do exe.
builder.Configuration.AddJsonFile("email.settings.json", optional: true, reloadOnChange: true);

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
builder.Services.AddSingleton<IConstrutorI30, ConstrutorI30>();

// Classificador de retorno do WMS (por texto). Peça pura, sem rede — consumida pelo
// Orquestrador (FEAT-002) para decidir pré-condições e tentativas.
builder.Services.AddSingleton<ClassificadorRetorno>();

// Orquestrador reativo (FEAT-002): coordena I50 → pré-condições (I10/I30) → reenvio,
// respeitando a flag da I60 e o limite de 3 tentativas por nota. Sem rede direta —
// usa o IClienteWms. Quem o aciona é o ProcessadorLote, chamado pelo GerenciadorPacote a cada ciclo do Worker.
builder.Services.AddSingleton<IOrquestrador, Orquestrador>();

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

// Etapa 7: watcher de pasta + pacote + movimentação + limpeza + e-mail.
// Pastas: "Pastas:Entrada" vazia herda o legado "Entrada:PastaEntrada".
builder.Services
    .AddOptions<OpcoesPasta>()
    .Bind(builder.Configuration.GetSection(OpcoesPasta.Secao))
    .PostConfigure<IOptions<OpcoesEntrada>>((pastas, legado) =>
    {
        if (string.IsNullOrWhiteSpace(pastas.Entrada))
        {
            pastas.Entrada = legado.Value.PastaEntrada;
        }
    });
builder.Services
    .AddOptions<OpcoesPacote>()
    .Bind(builder.Configuration.GetSection(OpcoesPacote.Secao));
builder.Services
    .AddOptions<OpcoesLimpeza>()
    .Bind(builder.Configuration.GetSection(OpcoesLimpeza.Secao));
// E-mail: a senha NUNCA vai no appsettings (env Email__Senha ou email.settings.json ignorado pelo git).
builder.Services
    .AddOptions<OpcoesEmail>()
    .Bind(builder.Configuration.GetSection(OpcoesEmail.Secao));
builder.Services.AddSingleton<IEnviadorEmail, EnviadorEmailSmtp>();

// Monitoramento/telemetria (seção "Monitoramento"). A LISTA de destinatários do resumo NÃO
// fica aqui (vive em CaminhoDestinatarios, um txt fora do git). SMTP é reaproveitado do bloco Email.
builder.Services
    .AddOptions<OpcoesMonitoramento>()
    .Bind(builder.Configuration.GetSection(OpcoesMonitoramento.Secao));
builder.Services.AddSingleton<RegistradorTelemetria>();

builder.Services.AddSingleton<ClassificadorCategoriaErro>();
builder.Services.AddSingleton<OrganizadorArquivos>();
builder.Services.AddSingleton<LimpadorPastas>();
builder.Services.AddSingleton<DetectorArquivoPronto>();
builder.Services.AddSingleton<DiarioPacote>();
builder.Services.AddSingleton<ProcessadorLote>();
builder.Services.AddSingleton<FechadorPacote>();
builder.Services.AddSingleton<GerenciadorPacote>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

return 0;
