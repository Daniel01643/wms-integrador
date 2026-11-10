using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Worker;

/// <summary>
/// Envio MANUAL de diagnóstico: lê um XML de NF-e, monta a I60 com o mesmo
/// <see cref="ConstrutorI60"/> e a mesma configuração do Worker (seções <c>Operacional</c> e
/// <c>I60</c>, inclusive overrides por variável de ambiente) e envia SOMENTE a I60.
/// Não envia I50, não move o arquivo e não gera relatório.
/// Uso: <c>--enviar-i60 &lt;caminho-do-xml&gt;</c>, com <c>WMS__Usuario</c>/<c>WMS__Senha</c>.
/// Serve para testar a associação quando o DocCarga já existe no WMS.
/// </summary>
public static class EnvioManualI60
{
    /// <summary>Argumento que aciona o envio manual da I60.</summary>
    public const string Argumento = "--enviar-i60";

    public static async Task<int> ExecutarAsync(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        var logger = loggerFactory.CreateLogger("EnvioManualI60");

        var posicao = Array.IndexOf(args, Argumento);
        var caminho = posicao >= 0 && posicao + 1 < args.Length ? args[posicao + 1] : string.Empty;
        if (string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho))
        {
            logger.LogError("Informe o XML: {Argumento} <caminho-do-xml>. Recebido: '{Caminho}'.", Argumento, caminho);
            return 2;
        }

        // Base no diretório do executável: funciona de qualquer pasta em que o comando for chamado.
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var wms = new OpcoesWms();
        config.GetSection(OpcoesWms.Secao).Bind(wms);
        var operacional = new OpcoesOperacionais();
        config.GetSection(OpcoesOperacionais.Secao).Bind(operacional);
        var i60 = new OpcoesI60();
        config.GetSection(OpcoesI60.Secao).Bind(i60);

        if (string.IsNullOrWhiteSpace(wms.Usuario) || string.IsNullOrWhiteSpace(wms.Senha))
        {
            logger.LogError("Credenciais ausentes. Defina WMS__Usuario e WMS__Senha no ambiente.");
            return 2;
        }

        try
        {
            var nota = new LeitorNotaFiscalXml().ParseArquivo(caminho);
            var construtor = new ConstrutorI60(Options.Create(operacional), Options.Create(i60));
            var payload = construtor.Construir(nota);
            var nv = payload.NotasVenda[0];

            logger.LogInformation(
                "I60 manual: nota={Nota} pedidos={Pedidos} tipoOperacaoNota={Tipo} qtdeVolume={Volume} itens={Itens} destino={Url}",
                nv.NumeroNota, string.Join(",", nv.Pedidos), nv.TipoOperacaoNota, nv.QtdeVolume,
                nv.ItemNotaVenda.Count, wms.BaseUrl);

            var baseUrl = wms.BaseUrl.EndsWith('/') ? wms.BaseUrl : wms.BaseUrl + "/";
            using var http = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(wms.TimeoutSegundos),
            };
            var cliente = new ClienteWmsHttp(
                http, Options.Create(wms), loggerFactory.CreateLogger<ClienteWmsHttp>(), new SystemClock());

            var resposta = await cliente.EnviarAsync(InterfaceWms.I60, payload);
            logger.LogInformation(
                "Resposta I60: transacao={Transacao} sucesso={Sucesso}", resposta.Transacao, resposta.Sucesso);
            foreach (var r in resposta.Retornos)
            {
                logger.LogInformation(
                    "Retorno: documento={Documento} sucesso={Sucesso} mensagem={Mensagem}",
                    r.Documento, r.Sucesso, r.Mensagem);
            }

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Envio manual da I60 falhou.");
            return 1;
        }
    }
}
