using KitWmsIntegrador.Core.Agrupamento;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Worker;
using KitWmsIntegrador.Worker.Configuracao;

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

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
