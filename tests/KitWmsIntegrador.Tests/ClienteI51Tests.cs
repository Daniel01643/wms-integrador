using System.Net;
using System.Text.Json;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Separacao;
using KitWmsIntegrador.Tests.Infra;
using KitWmsIntegrador.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class ClienteWmsBrutoTests
{
    private const string BaseUrl = "https://wms.exemplo.local:8888/wmsapi/api";

    private static (ClienteWmsHttp Cliente, FakeHttpMessageHandler Handler) Criar(Func<HttpRequestMessage, int, HttpResponseMessage> responder)
    {
        var opcoes = new OpcoesWms
        {
            BaseUrl = BaseUrl,
            Requisitante = "01.838.723/0098-50",
            Destino = "14.605.065/0001-75",
            Usuario = "USUARIO",
            Senha = "senha-de-teste",
            MaxTentativas = 3,
        };
        var handler = new FakeHttpMessageHandler(responder);
        var http = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl + "/") };
        return (new ClienteWmsHttp(http, Options.Create(opcoes), NullLogger<ClienteWmsHttp>.Instance, new FakeClock(DateTimeOffset.UtcNow)), handler);
    }

    private static bool EhToken(HttpRequestMessage r) => r.RequestUri!.AbsolutePath.EndsWith("GetToken", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task PostBruto_UsaOIdentificadorConfiguradoEDevolveOCorpoBruto()
    {
        var jwt = JwtFake.ComExpiracao(DateTimeOffset.UtcNow.AddHours(1));
        var (cliente, handler) = Criar((req, _) => EhToken(req)
            ? FakeHttpMessageHandler.Json(HttpStatusCode.OK, $"{{\"token\":\"{jwt}\"}}")
            : FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"qualquer\":1}"));

        var corpo = await cliente.PostBrutoAsync("WEBAPI-I51-X", "{\"CONSULTA\":{}}");

        Assert.Equal("{\"qualquer\":1}", corpo);
        var post = handler.Requisicoes.Last();
        Assert.Contains("IDENTIFICADOR=WEBAPI-I51-X", post.Uri.Query);
        Assert.Contains("REQUISITANTE=", post.Uri.Query);
        Assert.Contains("CNPJREQUISITANTE", post.Uri.Query);
        Assert.Contains("CNPJDESTINO", post.Uri.Query);
        Assert.Equal("{\"CONSULTA\":{}}", post.Corpo);
        Assert.Equal("Bearer", post.AuthScheme);
    }

    [Fact]
    public async Task PostBruto_ReaproveitaORetryDoToken()
    {
        var jwt = JwtFake.ComExpiracao(DateTimeOffset.UtcNow.AddHours(1));
        var tokens = 0;
        var (cliente, _) = Criar((req, _) =>
        {
            if (EhToken(req))
            {
                return ++tokens == 1
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    : FakeHttpMessageHandler.Json(HttpStatusCode.OK, $"{{\"token\":\"{jwt}\"}}");
            }

            return FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}");
        });

        Assert.Equal("{}", await cliente.PostBrutoAsync("WEBAPI-I51", "{}"));
        Assert.Equal(2, tokens);
    }
}

public class RegistroClienteWmsTests
{
    [Fact]
    public void UmaSoInstancia_ParaTodosOsConsumidores()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<KitWmsIntegrador.Core.Tempo.IClock, KitWmsIntegrador.Core.Tempo.SystemClock>();
        services.AddOptions<OpcoesWms>().Configure(o => { o.BaseUrl = "https://exemplo.local/api"; o.TimeoutSegundos = 5; });
        services.AdicionarClienteWms();
        using var provider = services.BuildServiceProvider();

        var tipado = provider.GetRequiredService<IClienteWms>();
        var bruto = provider.GetRequiredService<IClienteWmsBruto>();
        var http = provider.GetRequiredService<ClienteWmsHttp>();

        Assert.Same(http, tipado);
        Assert.Same(http, bruto);
        Assert.Same(provider.GetRequiredService<IClienteWms>(), tipado);
    }

    [Fact]
    public void ProgramNaoRegistraOutroClienteWms()
    {
        var raiz = AppContext.BaseDirectory;
        while (raiz is not null && !File.Exists(Path.Combine(raiz, "KitWmsIntegrador.sln")))
        {
            raiz = Path.GetDirectoryName(raiz);
        }

        var program = File.ReadAllText(Path.Combine(raiz!, "src", "KitWmsIntegrador.Worker", "Program.cs"));
        Assert.DoesNotContain("AddHttpClient<IClienteWms", program);
        Assert.Contains("AdicionarClienteWms()", program);
    }
}

public class ClienteI51Tests
{
    private static ClienteI51 Criar(OpcoesI51? i51 = null, FakeWmsI51? wms = null) =>
        new(wms ?? new FakeWmsI51(), Options.Create(i51 ?? new OpcoesI51()),
            Options.Create(new OpcoesOperacionais { CnpjDepositante = "01.838.723/0098-50" }));

    private static JsonElement Consulta(string corpo) => JsonDocument.Parse(corpo).RootElement.GetProperty("CONSULTA");

    [Fact]
    public void Todos_UsaOsValoresNeutrosEOCnpjComMascara()
    {
        var c = Consulta(Criar().MontarCorpo(ClienteI51.TipoTodos, null));
        Assert.Equal("TODOS", c.GetProperty("TIPO").GetString());
        Assert.Equal("01.838.723/0098-50", c.GetProperty("CNPJ").GetString());
        Assert.Equal("0", c.GetProperty("numero").GetString());
        Assert.Equal("0", c.GetProperty("serie").GetString());
        Assert.Equal(string.Empty, c.GetProperty("chaveNFe").GetString());
        Assert.Equal("0", c.GetProperty("nroDocumento").GetString());
    }

    [Fact]
    public void Unico_PadraoColocaONumeroCom9DigitosEmNumero()
    {
        var c = Consulta(Criar().MontarCorpo(ClienteI51.TipoUnico, "5287"));
        Assert.Equal("UNICO", c.GetProperty("TIPO").GetString());
        Assert.Equal("000005287", c.GetProperty("numero").GetString());
        Assert.Equal("0", c.GetProperty("nroDocumento").GetString());
    }

    [Fact]
    public void Unico_CampoNroDocumento_ColocaONumeroEmNroDocumento()
    {
        var c = Consulta(Criar(new OpcoesI51 { CampoUnico = "NroDocumento" }).MontarCorpo(ClienteI51.TipoUnico, "5287"));
        Assert.Equal("5287", c.GetProperty("nroDocumento").GetString());
        Assert.Equal("0", c.GetProperty("numero").GetString());
    }

    [Fact]
    public void Unico_CnpjExplicitoRecebeMascara()
    {
        var c = Consulta(Criar().MontarCorpo(ClienteI51.TipoUnico, "1", "12345678000195"));
        Assert.Equal("12.345.678/0001-95", c.GetProperty("CNPJ").GetString());
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("12a", false)]
    [InlineData("1234567890", false)]
    [InlineData("123456789", true)]
    public void ValidarNumeroUnico_Numero(string numero, bool valido)
    {
        Assert.Equal(valido, Criar().ValidarNumeroUnico(numero) is null);
    }

    [Fact]
    public void ValidarNumeroUnico_NroDocumentoAceitaAte20()
    {
        var c = Criar(new OpcoesI51 { CampoUnico = "NroDocumento" });
        Assert.Null(c.ValidarNumeroUnico(new string('1', 20)));
        Assert.NotNull(c.ValidarNumeroUnico(new string('1', 21)));
    }

    [Fact]
    public async Task Consultar_UsaOIdentificadorConfiguradoNoWms()
    {
        var wms = new FakeWmsI51();
        var c = Criar(new OpcoesI51 { Identificador = "WEBAPI-I52" }, wms);
        await c.ConsultarTodosAsync(CancellationToken.None);
        await c.ConsultarUnicoAsync("5287", CancellationToken.None);
        Assert.All(wms.Consultas, q => Assert.Equal("WEBAPI-I52", q.Identificador));
        Assert.Equal(new[] { "TODOS", "UNICO" }, wms.Consultas.Select(q => q.Tipo));
    }

    [Fact]
    public async Task ConsultarUnico_NumeroInvalido_Lanca()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Criar().ConsultarUnicoAsync("abc", CancellationToken.None));
    }
}

public class ParserI51Tests
{
    private static string Doc(string numero, string sep = "24.0000", string extra = "") =>
        "{\"transacao\":\"1\",\"sucesso\":\"S\",\"mensagem\":\"OK\",\"documentos\":[{\"CnpjProprietario\":\"01.838.723/0098-50\",\"CnpjDestinatario\":\"11.111.111/0001-11\"," +
        $"\"Numero\":\"{numero}\",\"Tipo\":\"PEDIDO\",\"SubTipo\":\"CN\",\"CampoNovo\":123{extra},\"itens\":[{{\"numeroSequencialItem\":\"1\",\"codProduto\":\"ABC\",\"qtdeProduto\":\"24.0000\",\"qtdeProdutoSep\":\"{sep}\"}}],\"volumes\":[{{\"a\":1}},{{\"a\":2}}]}}]}}";

    [Fact]
    public void Ler_QuantidadesComPontoEContagemDeVolumes()
    {
        var r = ParserI51.Ler(Doc("000005287", "20.5000"));
        var d = Assert.Single(r.Documentos);
        Assert.Equal("5287", d.Numero);
        Assert.Equal("01838723009850", d.CnpjProprietario);
        Assert.Equal(2, d.QtdeVolumes);
        Assert.Equal(20.5m, d.Itens[0].QtdeProdutoSep);
        Assert.Equal(24m, d.Itens[0].QtdeProduto);
        Assert.Equal(1, d.Itens[0].Sequencia);
    }

    [Fact]
    public void Ler_QuantidadeInvalida_IsolaODocumento()
    {
        var r = ParserI51.Ler(Doc("1", "abc"));
        Assert.Empty(r.Documentos);
        Assert.Equal(1, r.Invalidos);
    }

    [Fact]
    public void Ler_NumeroZerado_EInvalido()
    {
        var r = ParserI51.Ler(Doc("000000000"));
        Assert.Empty(r.Documentos);
        Assert.Equal(1, r.Invalidos);
    }

    [Fact]
    public void Ler_JsonInvalido_Lanca()
    {
        Assert.ThrowsAny<Exception>(() => ParserI51.Ler("{nao e json"));
        Assert.Throws<InvalidDataException>(() => ParserI51.Ler(new string('x', ParserI51.LimiteCorpo + 1)));
    }

    [Fact]
    public void NadaARetornar_SemDocumentos_QualquerSucesso()
    {
        Assert.True(ParserI51.NadaARetornar("{\"sucesso\":\"N\",\"mensagem\":\"Nada\"}", out var s, out var m));
        Assert.Equal("N", s);
        Assert.Equal("Nada", m);
        Assert.True(ParserI51.NadaARetornar("{\"sucesso\":\"S\",\"documentos\":[]}", out _, out _));
        Assert.False(ParserI51.NadaARetornar(Doc("1"), out _, out _));
        Assert.False(ParserI51.NadaARetornar("isto nao e json", out _, out _));
    }
}
