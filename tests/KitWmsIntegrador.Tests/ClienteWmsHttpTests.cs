using System.Net;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class ClienteWmsHttpTests
{
    // JSON reais dos 3 cenários (TESTE-COMUNICACAO.md).
    private const string JsonSucesso =
        "{\"transacao\":\"4019663\",\"tipoEDI\":\"I50\",\"sucesso\":\"S\"," +
        "\"retornos\":[{\"documento\":\"DOC1\",\"idDocumento\":\"10\",\"sucesso\":\"S\",\"mensagem\":\"Importado com sucesso.\"}]}";

    private const string JsonPendente =
        "{\"transacao\":\"4019664\",\"tipoEDI\":\"I50\",\"sucesso\":\"N\"," +
        "\"retornos\":[{\"documento\":\"\",\"idDocumento\":\"\",\"sucesso\":\"N\"," +
        "\"mensagem\":\"Foi importado e esta pendente na interface. Ressalvas : {Destinataria - Empresa não localizada, sigla não informada.}\"}]}";

    private const string JsonRejeitado =
        "{\"transacao\":\"4019665\",\"tipoEDI\":\"I50\",\"sucesso\":\"N\"," +
        "\"retornos\":[{\"documento\":\"\",\"idDocumento\":\"\",\"sucesso\":\"N\"," +
        "\"mensagem\":\"Mensagem EDI com estrutura mal-formada. Processo Abortado.\"}]}";

    private const string BaseUrl = "https://friozem.assistsoftware.com.br:8888/wmsapi/api";

    private static OpcoesWms OpcoesPadrao() => new()
    {
        BaseUrl = BaseUrl,
        Requisitante = "02.559.193/0001-40",
        Destino = "14.605.065/0001-75",
        CnpjRequisitante = "",
        CnpjDestino = "",
        Usuario = "BLUEFOOD",
        Senha = "senha-de-teste",
        TimeoutSegundos = 30,
        MaxTentativas = 3,
        MargemRenovacaoTokenSegundos = 60,
    };

    private static (ClienteWmsHttp Cliente, FakeHttpMessageHandler Handler) CriarCliente(
        Func<HttpRequestMessage, int, HttpResponseMessage> responder,
        OpcoesWms? opcoes = null,
        FakeClock? clock = null)
    {
        opcoes ??= OpcoesPadrao();
        var handler = new FakeHttpMessageHandler(responder);
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri(opcoes.BaseUrl + "/"),
            Timeout = TimeSpan.FromSeconds(opcoes.TimeoutSegundos),
        };
        var cliente = new ClienteWmsHttp(
            http,
            Options.Create(opcoes),
            NullLogger<ClienteWmsHttp>.Instance,
            clock ?? new FakeClock(DateTimeOffset.UtcNow));
        return (cliente, handler);
    }

    private static HttpResponseMessage TokenOk(string jwt) =>
        FakeHttpMessageHandler.Json(HttpStatusCode.OK, $"{{\"token\":\"{jwt}\"}}");

    private static bool EhGetToken(HttpRequestMessage req) =>
        req.RequestUri!.AbsolutePath.EndsWith("Integracao/GetToken", StringComparison.OrdinalIgnoreCase);

    // ---- Desserialização + classificação dos 3 cenários ----

    [Fact]
    public async Task Enviar_RespostaSucesso_DesserializaEClassificaComoSucesso()
    {
        var jwt = JwtFake.ComExpiracao(DateTimeOffset.UtcNow.AddHours(1));
        var (cliente, _) = CriarCliente((req, _) =>
            EhGetToken(req) ? TokenOk(jwt) : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso));

        var resposta = await cliente.EnviarAsync(InterfaceWms.I50, "{}");

        Assert.Equal("4019663", resposta.Transacao);
        Assert.Equal("I50", resposta.TipoEDI);
        Assert.Equal("S", resposta.Sucesso);
        var retorno = Assert.Single(resposta.Retornos);
        Assert.Equal("DOC1", retorno.Documento);
        Assert.Equal(ResultadoWms.Sucesso, cliente.Classificar(retorno));
    }

    [Fact]
    public async Task Enviar_RespostaPendente_DesserializaEClassificaComoPendente()
    {
        var jwt = JwtFake.ComExpiracao(DateTimeOffset.UtcNow.AddHours(1));
        var (cliente, handler) = CriarCliente((req, _) =>
            EhGetToken(req) ? TokenOk(jwt) : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonPendente));

        var resposta = await cliente.EnviarAsync(InterfaceWms.I50, "{}");

        var retorno = Assert.Single(resposta.Retornos);
        Assert.Equal(ResultadoWms.Pendente, cliente.Classificar(retorno));
        Assert.Contains("pendente na interface", retorno.Mensagem, StringComparison.OrdinalIgnoreCase);
        // Pendência é resposta de negócio — handler chamado só p/ token + POST (sem retry).
        Assert.Equal(2, handler.Requisicoes.Count);
    }

    [Fact]
    public async Task Enviar_RespostaRejeitada_DesserializaEClassificaComoRejeitado()
    {
        var jwt = JwtFake.ComExpiracao(DateTimeOffset.UtcNow.AddHours(1));
        var (cliente, handler) = CriarCliente((req, _) =>
            EhGetToken(req) ? TokenOk(jwt) : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonRejeitado));

        var resposta = await cliente.EnviarAsync(InterfaceWms.I50, "{}");

        var retorno = Assert.Single(resposta.Retornos);
        Assert.Equal(ResultadoWms.Rejeitado, cliente.Classificar(retorno));
        // Rejeição de negócio NÃO é retry: 1 token + 1 POST.
        Assert.Equal(2, handler.Requisicoes.Count);
    }

    // ---- Query string ----

    [Theory]
    [InlineData(InterfaceWms.I50, "WEBAPI-I50-Json")]
    [InlineData(InterfaceWms.I60, "WEBAPI-I60-Json")]
    [InlineData(InterfaceWms.I10, "WEBAPI-I10-Json")]
    public async Task Enviar_MontaQueryStringComOs5Parametros(InterfaceWms interfaceWms, string identificadorEsperado)
    {
        var jwt = JwtFake.ComExpiracao(DateTimeOffset.UtcNow.AddHours(1));
        var (cliente, handler) = CriarCliente((req, _) =>
            EhGetToken(req) ? TokenOk(jwt) : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso));

        await cliente.EnviarAsync(interfaceWms, "{}");

        var post = handler.Requisicoes.Single(r => r.Metodo == HttpMethod.Post);
        var query = post.Uri.Query;

        Assert.Contains($"IDENTIFICADOR={identificadorEsperado}", query);
        Assert.Contains("REQUISITANTE=02.559.193%2F0001-40", query);
        Assert.Contains("DESTINO=14.605.065%2F0001-75", query);
        // Os 2 parâmetros vazios precisam estar presentes como chave sem valor.
        Assert.Matches(@"[?&]CNPJREQUISITANTE(&|$)", query);
        Assert.Matches(@"[?&]CNPJDESTINO(&|$)", query);
    }

    [Fact]
    public async Task Enviar_TodosOsCincoParametrosNaOrdemEsperada()
    {
        var jwt = JwtFake.ComExpiracao(DateTimeOffset.UtcNow.AddHours(1));
        var (cliente, handler) = CriarCliente((req, _) =>
            EhGetToken(req) ? TokenOk(jwt) : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso));

        await cliente.EnviarAsync(InterfaceWms.I50, "{}");

        var post = handler.Requisicoes.Single(r => r.Metodo == HttpMethod.Post);
        Assert.Equal(
            "?IDENTIFICADOR=WEBAPI-I50-Json&REQUISITANTE=02.559.193%2F0001-40&CNPJREQUISITANTE&DESTINO=14.605.065%2F0001-75&CNPJDESTINO",
            post.Uri.Query);
    }

    // ---- Headers ----

    [Fact]
    public async Task Enviar_UsaBasicNoGetTokenEBearerNoPost()
    {
        var jwt = JwtFake.ComExpiracao(DateTimeOffset.UtcNow.AddHours(1));
        var (cliente, handler) = CriarCliente((req, _) =>
            EhGetToken(req) ? TokenOk(jwt) : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso));

        await cliente.EnviarAsync(InterfaceWms.I50, "{}");

        var getToken = handler.Requisicoes.Single(r => r.Metodo == HttpMethod.Get);
        Assert.Equal("Basic", getToken.AuthScheme);
        var esperadoBasic = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("BLUEFOOD:senha-de-teste"));
        Assert.Equal(esperadoBasic, getToken.AuthParametro);

        var post = handler.Requisicoes.Single(r => r.Metodo == HttpMethod.Post);
        Assert.Equal("Bearer", post.AuthScheme);
        Assert.Equal(jwt, post.AuthParametro);
        Assert.Equal("application/json", post.ContentType);
    }

    // ---- 401 renova token + repete uma vez ----

    [Fact]
    public async Task Enviar_401_RenovaTokenERepetePostUmaVez()
    {
        var jwt1 = JwtFake.ComExpiracao(DateTimeOffset.UtcNow.AddHours(1));
        var jwt2 = JwtFake.ComExpiracao(DateTimeOffset.UtcNow.AddHours(2));
        var getTokenCount = 0;
        var postCount = 0;

        var (cliente, handler) = CriarCliente((req, _) =>
        {
            if (EhGetToken(req))
            {
                getTokenCount++;
                return TokenOk(getTokenCount == 1 ? jwt1 : jwt2);
            }

            postCount++;
            return postCount == 1
                ? FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}")
                : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso);
        });

        var resposta = await cliente.EnviarAsync(InterfaceWms.I50, "{}");

        Assert.Equal("S", resposta.Sucesso);
        Assert.Equal(2, getTokenCount); // token inicial + renovação por 401
        Assert.Equal(2, postCount);     // POST falho (401) + POST repetido

        // O segundo POST usou o novo token.
        var posts = handler.Requisicoes.Where(r => r.Metodo == HttpMethod.Post).ToList();
        Assert.Equal(jwt1, posts[0].AuthParametro);
        Assert.Equal(jwt2, posts[1].AuthParametro);
    }

    [Fact]
    public async Task Enviar_401Persistente_NaoRepeteMaisQueUmaVez()
    {
        var jwt = JwtFake.ComExpiracao(DateTimeOffset.UtcNow.AddHours(1));
        var postCount = 0;

        var (cliente, _) = CriarCliente((req, _) =>
        {
            if (EhGetToken(req))
            {
                return TokenOk(jwt);
            }

            postCount++;
            return FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}");
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => cliente.EnviarAsync(InterfaceWms.I50, "{}"));
        Assert.Equal(2, postCount); // POST original + 1 repetição; depois desiste
    }

    // ---- 4xx (≠401) não faz retry ----

    [Fact]
    public async Task Enviar_400_NaoFazRetryELancaExcecao()
    {
        var jwt = JwtFake.ComExpiracao(DateTimeOffset.UtcNow.AddHours(1));
        var postCount = 0;

        var (cliente, _) = CriarCliente((req, _) =>
        {
            if (EhGetToken(req))
            {
                return TokenOk(jwt);
            }

            postCount++;
            return FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, "{}");
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => cliente.EnviarAsync(InterfaceWms.I50, "{}"));
        Assert.Equal(1, postCount);
    }

    // ---- 5xx faz retry com backoff até MaxTentativas ----

    [Fact]
    public async Task Enviar_500_FazRetryAteMaxTentativas()
    {
        var jwt = JwtFake.ComExpiracao(DateTimeOffset.UtcNow.AddHours(1));
        var postCount = 0;
        var opcoes = OpcoesPadrao();
        opcoes.MaxTentativas = 3;

        var (cliente, _) = CriarCliente((req, _) =>
        {
            if (EhGetToken(req))
            {
                return TokenOk(jwt);
            }

            postCount++;
            return FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}");
        }, opcoes);

        await Assert.ThrowsAsync<HttpRequestException>(() => cliente.EnviarAsync(InterfaceWms.I50, "{}"));
        Assert.Equal(3, postCount);
    }

    [Fact]
    public async Task Enviar_500DepoisSucesso_RetornaSucesso()
    {
        var jwt = JwtFake.ComExpiracao(DateTimeOffset.UtcNow.AddHours(1));
        var postCount = 0;

        var (cliente, _) = CriarCliente((req, _) =>
        {
            if (EhGetToken(req))
            {
                return TokenOk(jwt);
            }

            postCount++;
            return postCount == 1
                ? FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}")
                : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso);
        });

        var resposta = await cliente.EnviarAsync(InterfaceWms.I50, "{}");
        Assert.Equal("S", resposta.Sucesso);
        Assert.Equal(2, postCount);
    }

    // ---- Cache de token com relógio fake ----

    [Fact]
    public async Task Enviar_TokenValido_NaoBuscaTokenNovamente()
    {
        var agora = DateTimeOffset.UtcNow;
        var clock = new FakeClock(agora);
        var jwt = JwtFake.ComExpiracao(agora.AddHours(1));
        var getTokenCount = 0;

        var (cliente, _) = CriarCliente((req, _) =>
        {
            if (EhGetToken(req))
            {
                getTokenCount++;
                return TokenOk(jwt);
            }

            return FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso);
        }, clock: clock);

        await cliente.EnviarAsync(InterfaceWms.I50, "{}");
        await cliente.EnviarAsync(InterfaceWms.I60, "{}");

        Assert.Equal(1, getTokenCount); // token reaproveitado do cache
    }

    [Fact]
    public async Task Enviar_TokenExpiradoAposMargem_RenovaToken()
    {
        var agora = DateTimeOffset.UtcNow;
        var clock = new FakeClock(agora);
        var getTokenCount = 0;

        var (cliente, _) = CriarCliente((req, _) =>
        {
            if (EhGetToken(req))
            {
                getTokenCount++;
                // cada token expira 10 min após ser emitido no "agora" do clock
                return TokenOk(JwtFake.ComExpiracao(clock.UtcNow.AddMinutes(10)));
            }

            return FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso);
        }, clock: clock);

        await cliente.EnviarAsync(InterfaceWms.I50, "{}");
        Assert.Equal(1, getTokenCount);

        // Avança além de (exp - margem): 10min - 60s = 9min. Avançar 10min expira.
        clock.Avancar(TimeSpan.FromMinutes(10));
        await cliente.EnviarAsync(InterfaceWms.I50, "{}");
        Assert.Equal(2, getTokenCount);
    }

    // ---- Sobrecarga de objeto serializa o payload ----

    [Fact]
    public async Task Enviar_Objeto_SerializaComoJson()
    {
        var jwt = JwtFake.ComExpiracao(DateTimeOffset.UtcNow.AddHours(1));
        var (cliente, handler) = CriarCliente((req, _) =>
            EhGetToken(req) ? TokenOk(jwt) : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso));

        await cliente.EnviarAsync(InterfaceWms.I50, new { numeroPedido = "ABC", quantidade = 3 });

        var post = handler.Requisicoes.Single(r => r.Metodo == HttpMethod.Post);
        Assert.Contains("\"numeroPedido\":\"ABC\"", post.Corpo);
        Assert.Contains("\"quantidade\":3", post.Corpo);
    }
}
