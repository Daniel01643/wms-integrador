using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Separacao;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Worker;

/// <summary>
/// CLI de apoio ao homolog: <c>--consultar-i51 --tipo UNICO --cnpj &lt;cnpj&gt; --numero &lt;n&gt;</c>. SÓ o UNICO (repetível e
/// que não consome nada); o TODOS é recusado (saída 2) porque consome documentos e só roda pelo serviço. Grava o
/// bruto em <c>envio-manual-i51\</c> e imprime apenas contagens e números dos documentos. Saída: 0 ok, 1 erro, 2 parâmetro/configuração.
/// </summary>
public static class ConsultaManualI51
{
    /// <summary>Argumento que aciona a CLI.</summary>
    public const string Argumento = "--consultar-i51";

    /// <summary>Executa a CLI.</summary>
    public static async Task<int> ExecutarAsync(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        var logger = loggerFactory.CreateLogger("ConsultaManualI51");

        var tipo = ConfiguracaoManual.Argumento(args, "--tipo");
        if (!string.Equals(tipo, ClienteI51.TipoUnico, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError(
                "Só --tipo UNICO é aceito por esta CLI: o TODOS consome os documentos no WMS (não repete) e roda apenas pelo serviço.");
            return 2;
        }

        var cnpj = new string(ConfiguracaoManual.Argumento(args, "--cnpj").Where(char.IsDigit).ToArray());
        var numero = ConfiguracaoManual.Argumento(args, "--numero");
        if (cnpj.Length != 14)
        {
            logger.LogError("Informe --cnpj com 14 dígitos.");
            return 2;
        }

        var config = ConfiguracaoManual.Criar();
        var wms = new OpcoesWms();
        config.GetSection(OpcoesWms.Secao).Bind(wms);
        var i51 = new OpcoesI51();
        config.GetSection(OpcoesI51.Secao).Bind(i51);
        var operacional = new OpcoesOperacionais();
        config.GetSection(OpcoesOperacionais.Secao).Bind(operacional);
        if (string.IsNullOrWhiteSpace(wms.Usuario) || string.IsNullOrWhiteSpace(wms.Senha))
        {
            logger.LogError("Credenciais ausentes. Defina WMS__Usuario e WMS__Senha no ambiente.");
            return 2;
        }

        var problemas = i51.Validar();
        if (problemas.Count > 0)
        {
            foreach (var p in problemas)
            {
                logger.LogError("Configuração inválida: {Problema}", p);
            }

            return 2;
        }

        try
        {
            var baseUrl = wms.BaseUrl.EndsWith('/') ? wms.BaseUrl : wms.BaseUrl + "/";
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(wms.TimeoutSegundos) };
            var cliente = new ClienteWmsHttp(http, Options.Create(wms), loggerFactory.CreateLogger<ClienteWmsHttp>(), new SystemClock());
            var consulta = new ClienteI51(cliente, Options.Create(i51), Options.Create(operacional));

            var erro = consulta.ValidarNumeroUnico(numero);
            if (erro is not null)
            {
                logger.LogError("{Erro}", erro);
                return 2;
            }

            var corpo = await consulta.ConsultarUnicoAsync(cnpj, numero.Trim(), CancellationToken.None);
            var pasta = Path.Combine(AppContext.BaseDirectory, "envio-manual-i51");
            Directory.CreateDirectory(pasta);
            var arquivo = Path.Combine(pasta, $"i51_UNICO_{CaminhosI51.NomeSeguro(numero)}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            File.WriteAllText(arquivo, corpo, new System.Text.UTF8Encoding(false));

            if (ParserI51.NadaARetornar(corpo, out var sucesso, out var mensagem))
            {
                logger.LogInformation("Sem documentos. sucesso={Sucesso} mensagem={Mensagem}", sucesso, TextoNormalizado.Truncar(mensagem, 200));
            }
            else
            {
                var resposta = ParserI51.Ler(corpo);
                logger.LogInformation(
                    "Documentos: {Qtde} (inválidos: {Invalidos}). Números: {Numeros}",
                    resposta.Documentos.Count, resposta.Invalidos, string.Join(",", resposta.Documentos.Select(d => d.Numero)));
            }

            logger.LogInformation("Resposta bruta gravada em {Arquivo}", arquivo);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError("Consulta UNICO da I51 falhou: {Erro}", ex.GetType().Name);
            return 1;
        }
    }
}
