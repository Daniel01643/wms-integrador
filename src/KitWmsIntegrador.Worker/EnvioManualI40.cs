using System.Text;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Devolucao;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Core.Tempo;
using KitWmsIntegrador.Worker.Configuracao;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Worker;

/// <summary>
/// Envio MANUAL de teste da I40 (devolução, Plano B fase 1). Localiza o XML da venda pela
/// chave (ou usa <c>--xml</c>), monta a I40 com o <see cref="ConstrutorI40"/> e a mesma
/// configuração do Worker (seções <c>Operacional</c>, <c>Pastas</c> e <c>Devolucao</c>, com
/// overrides por ambiente, ex.: <c>Devolucao__InverterPartes</c>) e envia SOMENTE a I40.
/// Uso: <c>--enviar-i40 --chave &lt;44 dígitos&gt; [--xml &lt;caminho&gt;]</c> ou
/// <c>--enviar-i40 --numero &lt;n&gt; --serie &lt;s&gt; [--xml &lt;caminho&gt;]</c>, com
/// <c>WMS__Usuario</c>/<c>WMS__Senha</c>. Por número + série a busca é feita na retenção (mesma regra
/// da tela); se houver mais de uma chave distinta, lista as opções e NÃO envia. A I40 vai como IMP
/// (<c>Devolucao:TipoOperacaoNota</c>), sem placa nem motorista.
/// </summary>
public static class EnvioManualI40
{
    /// <summary>Argumento que aciona o envio manual da I40.</summary>
    public const string Argumento = "--enviar-i40";

    public static async Task<int> ExecutarAsync(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        var logger = loggerFactory.CreateLogger("EnvioManualI40");

        var chave = ValorDe(args, "--chave");
        var numero = ValorDe(args, "--numero");
        var serie = ValorDe(args, "--serie");
        var xml = ValorDe(args, "--xml");
        const string uso = "Uso: --enviar-i40 --chave <44 digitos> [--xml <caminho>] ou --enviar-i40 --numero <n> --serie <s> [--xml <caminho>].";

        if (chave is not null)
        {
            var erroChave = LocalizadorXmlDevolucao.ValidarChave(chave);
            if (erroChave is not null)
            {
                logger.LogError("{Erro} {Uso}", erroChave, uso);
                return 2;
            }
        }
        else
        {
            var erroNumero = ValidadorChavesDevolucao.ValidarNumeroSerie(numero, serie, out _, out _);
            if (erroNumero is not null)
            {
                logger.LogError("Informe --chave ou --numero e --serie. {Erro} {Uso}", erroNumero, uso);
                return 2;
            }
        }

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var wms = new OpcoesWms();
        config.GetSection(OpcoesWms.Secao).Bind(wms);
        var operacional = new OpcoesOperacionais();
        config.GetSection(OpcoesOperacionais.Secao).Bind(operacional);
        var devolucao = new OpcoesDevolucao();
        config.GetSection(OpcoesDevolucao.Secao).Bind(devolucao);
        var pastas = new OpcoesPasta();
        config.GetSection(OpcoesPasta.Secao).Bind(pastas);
        if (string.IsNullOrWhiteSpace(pastas.Entrada))
        {
            pastas.Entrada = config.GetSection(OpcoesEntrada.Secao)["PastaEntrada"] ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(wms.Usuario) || string.IsNullOrWhiteSpace(wms.Senha))
        {
            logger.LogError("Credenciais ausentes. Defina WMS__Usuario e WMS__Senha no ambiente.");
            return 2;
        }

        try
        {
            string caminho;
            if (chave is null && string.IsNullOrWhiteSpace(xml))
            {
                if (string.IsNullOrWhiteSpace(pastas.Entrada))
                {
                    logger.LogError("Pastas:Entrada não configurada; informe --xml <caminho> ou configure a pasta.");
                    return 2;
                }

                ValidadorChavesDevolucao.ValidarNumeroSerie(numero, serie, out var numeroInt, out var serieInt);
                var opcoes = new LocalizadorXmlDevolucao(pastas.CaminhoProcessados)
                    .BuscarPorNumeroSerie(numeroInt, serieInt, operacional.CnpjDepositante);
                if (opcoes.Count == 0)
                {
                    logger.LogError(
                        "Nota {Numero} série {Serie} não encontrada na retenção de 30 dias em {Pasta}.",
                        numeroInt, serieInt, pastas.CaminhoProcessados);
                    return 2;
                }

                if (opcoes.Count > 1)
                {
                    logger.LogError(
                        "Nota {Numero} série {Serie} tem {Qtde} chaves distintas; nada foi enviado. Rode de novo com --chave <44 digitos> de uma delas:",
                        numeroInt, serieInt, opcoes.Count);
                    foreach (var o in opcoes)
                    {
                        logger.LogError("  chave={Chave} arquivo={Data} pacote={Pacote}", o.Chave, o.DataArquivo, o.Pacote);
                    }

                    return 2;
                }

                chave = opcoes[0].Chave;
                logger.LogInformation("Nota {Numero} série {Serie} resolvida para a chave {Chave}.", numeroInt, serieInt, chave);
            }

            if (!string.IsNullOrWhiteSpace(xml))
            {
                if (!File.Exists(xml))
                {
                    logger.LogError("XML informado em --xml não existe: '{Caminho}'.", xml);
                    return 2;
                }

                caminho = xml;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(pastas.Entrada))
                {
                    logger.LogError("Pastas:Entrada não configurada; informe --xml <caminho> ou configure a pasta.");
                    return 2;
                }

                var achado = new LocalizadorXmlDevolucao(pastas.CaminhoProcessados).Localizar(chave!);
                if (achado is null)
                {
                    logger.LogError(
                        "XML da chave {Chave} não encontrado em {Pasta} (retenção de 30 dias; Erro não é pesquisada).",
                        chave, pastas.CaminhoProcessados);
                    return 2;
                }

                caminho = achado;
            }

            var nota = new LeitorNotaFiscalXml().ParseArquivo(caminho);
            if (chave is not null && !string.Equals(nota.ChaveNFe, chave.Trim(), StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "A chave do XML ({ChaveXml}) difere da informada ({Chave}).", nota.ChaveNFe, chave);
            }

            var construtor = new ConstrutorI40(Options.Create(operacional), Options.Create(devolucao));
            var payload = construtor.Construir(nota);
            var nf = payload.NotaFiscal[0];
            var json = construtor.ConstruirJson(nota);

            logger.LogInformation("JSON da I40:{NovaLinha}{Json}", Environment.NewLine, json);
            logger.LogInformation(
                "I40 manual: nota={Nota} tipoNota={TipoNota} tipoOperacaoNota={Tipo} inverter={Inverter} emitente={Emitente} destinatario={Destinatario} qtdeVolume={Volume} itens={Itens} destino={Url}",
                nf.NumeroNota, nf.TipoNota, nf.TipoOperacaoNota, devolucao.InverterPartes,
                nf.CnpjEmitente, nf.CnpjDestinatario, nf.QtdeVolume, nf.ItemNotaFiscal.Count, wms.BaseUrl);

            var baseUrl = wms.BaseUrl.EndsWith('/') ? wms.BaseUrl : wms.BaseUrl + "/";
            using var http = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(wms.TimeoutSegundos),
            };
            var cliente = new ClienteWmsHttp(
                http, Options.Create(wms), loggerFactory.CreateLogger<ClienteWmsHttp>(), new SystemClock());

            var resposta = await cliente.EnviarAsync(InterfaceWms.I40, payload);
            logger.LogInformation(
                "Resposta I40: transacao={Transacao} sucesso={Sucesso}", resposta.Transacao, resposta.Sucesso);
            foreach (var r in resposta.Retornos)
            {
                logger.LogInformation(
                    "Retorno: documento={Documento} sucesso={Sucesso} mensagem={Mensagem}",
                    r.Documento, r.Sucesso, r.Mensagem);
            }

            var pasta = Path.Combine(AppContext.BaseDirectory, "envio-manual-i40");
            Directory.CreateDirectory(pasta);
            var arquivo = Path.Combine(pasta, $"i40_{nf.NumeroNota}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            var texto = new StringBuilder()
                .AppendLine($"Data: {DateTime.Now:dd/MM/yyyy HH:mm:ss}")
                .AppendLine($"XML: {caminho}")
                .AppendLine($"Destino: {wms.BaseUrl}")
                .AppendLine($"InverterPartes={devolucao.InverterPartes} tipoNota={nf.TipoNota} tipoOperacaoNota={nf.TipoOperacaoNota} qtdeVolume={nf.QtdeVolume} qtdeTotalNota={nf.QtdeTotalNota}")
                .AppendLine()
                .AppendLine("JSON enviado:")
                .AppendLine(json)
                .AppendLine()
                .AppendLine($"Resposta: transacao={resposta.Transacao} sucesso={resposta.Sucesso}");
            foreach (var r in resposta.Retornos)
            {
                texto.AppendLine($"Retorno: documento={r.Documento} sucesso={r.Sucesso} mensagem={r.Mensagem}");
            }

            File.WriteAllText(arquivo, texto.ToString(), new UTF8Encoding(false));
            logger.LogInformation("Envio e resposta gravados em {Arquivo}", arquivo);

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Envio manual da I40 falhou: {Mensagem}", ex.Message);
            return 1;
        }
    }

    /// <summary>Valor que segue a opção <paramref name="nome"/> (ex.: <c>--chave</c>); null se ausente.</summary>
    private static string? ValorDe(string[] args, string nome)
    {
        var i = Array.IndexOf(args, nome);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
