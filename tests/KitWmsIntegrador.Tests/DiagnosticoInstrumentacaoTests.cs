using System.Net;
using System.Text.Json;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Diagnostico;
using KitWmsIntegrador.Core.Modelos;
using KitWmsIntegrador.Core.Orquestrador;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>Os pontos de instrumentação gravam os eventos do diário de diagnóstico (tabela 3.2 do plano), sem segredos.</summary>
public sealed class DiagnosticoInstrumentacaoTests : IDisposable
{
    private const string JsonSucesso =
        "{\"transacao\":\"1\",\"tipoEDI\":\"I50\",\"sucesso\":\"S\"," +
        "\"retornos\":[{\"documento\":\"D\",\"idDocumento\":\"1\",\"sucesso\":\"S\",\"mensagem\":\"Registrado com sucesso.\"}]}";

    private const string SenhaDeTeste = "senha-secreta-de-teste";
    private static readonly DateTimeOffset Inicio = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly RegistroDiagnosticoMemoria _diario = new();
    private readonly List<IDisposable> _descartaveis = new();

    public void Dispose()
    {
        foreach (var d in _descartaveis)
        {
            d.Dispose();
        }
    }

    // ---- ClienteWmsHttp: gettoken, auth-queda, auth-recuperada ----

    private static OpcoesWms OpcoesWmsTeste(bool tokenPorEnvio = false, int tentativasImediatas = 1) => new()
    {
        BaseUrl = "https://wms.exemplo.test/wmsapi/api",
        Requisitante = "01.838.723/0098-50",
        Destino = "14.605.065/0001-75",
        Usuario = "USUARIO-TESTE",
        Senha = SenhaDeTeste,
        TimeoutSegundos = 30,
        MaxTentativas = 3,
        MargemRenovacaoTokenSegundos = 60,
        TokenPorEnvio = tokenPorEnvio,
        TokenTentativasImediatas = tentativasImediatas,
        CooldownTokenSegundos = "60;120;300;600;900",
    };

    private static bool EhGetToken(HttpRequestMessage req) =>
        req.RequestUri!.AbsolutePath.EndsWith("Integracao/GetToken", StringComparison.OrdinalIgnoreCase);

    private (ClienteWmsHttp Cliente, FakeClock Clock) CriarCliente(
        Func<HttpRequestMessage, int, HttpResponseMessage> responder, OpcoesWms? opcoes = null)
    {
        opcoes ??= OpcoesWmsTeste();
        var clock = new FakeClock(Inicio);
        var http = new HttpClient(new FakeHttpMessageHandler(responder)) { BaseAddress = new Uri(opcoes.BaseUrl + "/") };
        _descartaveis.Add(http);
        var guarda = new GuardaAutenticacaoWms(clock, Options.Create(opcoes));
        var cliente = new ClienteWmsHttp(
            http, Options.Create(opcoes), NullLogger<ClienteWmsHttp>.Instance, clock, guarda, null, null, _diario);
        return (cliente, clock);
    }

    private static HttpResponseMessage Resposta401ComAfinidade()
    {
        var r = FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}");
        r.Headers.TryAddWithoutValidation("Set-Cookie", "BIGipServerWMS=valor-do-balanceador; path=/");
        return r;
    }

    [Fact]
    public async Task GetToken401ComAfinidade_GravaGettokenComBackend_QuedaInicio_ERecuperada()
    {
        var falhar = true;
        var jwt = JwtFake.ComExpiracao(Inicio.AddHours(1));
        var (cliente, clock) = CriarCliente((req, _) => EhGetToken(req)
            ? (falhar
                ? Resposta401ComAfinidade()
                : FakeHttpMessageHandler.Json(HttpStatusCode.OK, $"{{\"token\":\"{jwt}\"}}"))
            : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso));

        await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(() => cliente.EnviarAsync(InterfaceWms.I50, "{}"));

        var falha = Assert.Single(_diario.De("gettoken"));
        Assert.False(falha.Ok);
        Assert.Equal(401, falha.Status);
        Assert.Equal(1, falha.Quantidade);
        Assert.StartsWith("BIGipServerWMS#", falha.Backend);
        Assert.DoesNotContain("valor-do-balanceador", falha.Backend);
        Assert.Null(falha.Mensagem);
        Assert.NotNull(falha.DuracaoMs);

        var queda = Assert.Single(_diario.De("auth-queda"));
        Assert.Equal("inicio", queda.Subtipo);
        Assert.Equal(401, queda.Status);
        Assert.Equal(60, queda.Segundos);
        Assert.Equal(1, queda.Quantidade);
        Assert.Empty(_diario.De("auth-recuperada"));

        falhar = false;
        clock.Avancar(TimeSpan.FromSeconds(61));
        await cliente.EnviarAsync(InterfaceWms.I50, "{}");

        Assert.Equal(2, _diario.De("gettoken").Count);
        Assert.True(_diario.De("gettoken")[1].Ok);
        Assert.Single(_diario.De("auth-recuperada"));
        Assert.Single(_diario.De("auth-queda"));

        // Nenhum segredo (senha, JWT, Authorization) vai para o diário.
        var json = JsonSerializer.Serialize(_diario.Eventos);
        Assert.DoesNotContain(SenhaDeTeste, json);
        Assert.DoesNotContain(jwt, json);
        Assert.DoesNotContain("Basic", json);
        Assert.DoesNotContain("Bearer", json);
    }

    [Fact]
    public async Task SegundaFalhaAposOCooldown_GravaQuedaEscalada()
    {
        var (cliente, clock) = CriarCliente((req, _) => FakeHttpMessageHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"));

        await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(() => cliente.EnviarAsync(InterfaceWms.I50, "{}"));
        clock.Avancar(TimeSpan.FromSeconds(61));
        await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(() => cliente.EnviarAsync(InterfaceWms.I50, "{}"));

        var quedas = _diario.De("auth-queda");
        Assert.Equal(new[] { "inicio", "escalada" }, quedas.Select(q => q.Subtipo));
        Assert.Equal(new[] { 60, 120 }, quedas.Select(q => q.Segundos!.Value));
        Assert.Equal(new[] { 1, 2 }, quedas.Select(q => q.Quantidade!.Value));
        Assert.All(quedas, q => Assert.Equal(503, q.Status));
    }

    [Fact]
    public async Task GetTokenComMotivo_TimeoutRedeESemToken_UmEventoPorTentativa()
    {
        var timeout = CriarCliente((req, _) => throw new TaskCanceledException("timeout simulado"), OpcoesWmsTeste(tentativasImediatas: 2));
        await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(() => timeout.Cliente.EnviarAsync(InterfaceWms.I50, "{}"));

        var porTentativa = _diario.De("gettoken");
        Assert.Equal(2, porTentativa.Count);
        Assert.Equal(new[] { 1, 2 }, porTentativa.Select(e => e.Quantidade!.Value));
        Assert.All(porTentativa, e =>
        {
            Assert.False(e.Ok);
            Assert.Null(e.Status);
            Assert.Equal("timeout", e.Mensagem);
        });

        var rede = CriarCliente((req, _) => throw new HttpRequestException("rede simulada"));
        await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(() => rede.Cliente.EnviarAsync(InterfaceWms.I50, "{}"));
        Assert.Equal("rede", _diario.De("gettoken")[^1].Mensagem);

        var semToken = CriarCliente((req, _) => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"outro\":1}"));
        await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(() => semToken.Cliente.EnviarAsync(InterfaceWms.I50, "{}"));
        var ultimo = _diario.De("gettoken")[^1];
        Assert.Equal("sem-token", ultimo.Mensagem);
        Assert.Equal(200, ultimo.Status);
    }

    [Fact]
    public async Task TokenPorEnvio_TambemGravaGettokenEQueda()
    {
        var (cliente, _) = CriarCliente(
            (req, _) => EhGetToken(req) ? Resposta401ComAfinidade() : FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSucesso),
            OpcoesWmsTeste(tokenPorEnvio: true));

        await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(() => cliente.EnviarAsync(InterfaceWms.I50, "{}"));

        Assert.Single(_diario.De("gettoken"));
        Assert.Equal("inicio", Assert.Single(_diario.De("auth-queda")).Subtipo);
    }

    [Fact]
    public async Task TestarToken_NaoGravaEventos()
    {
        var (cliente, _) = CriarCliente((req, _) => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}"));

        var r = await cliente.TestarTokenDetalhadoAsync(false);

        Assert.False(r.Ok);
        Assert.Empty(_diario.Eventos);
    }

    // ---- Orquestrador: cadastro-gatilho e cadastro-chamada ----

    private static RespostaWms Resp(string sucesso, string mensagem) => new()
    {
        TipoEDI = "I50",
        Sucesso = sucesso,
        Retornos = new[] { new RetornoWms { Sucesso = sucesso, Mensagem = mensagem } },
    };

    private const string TextoEmpresa = "Destinataria - Empresa não localizada, sigla não informada.";
    private const string TextoProduto = "Produto Cod\\EAN: \"175886\" da empresa \"3509308\" não ativo ou faltando ou não cadastrado!";

    private static NotaFiscal Nota(string numero = "15665807") => new()
    {
        Numero = numero,
        Serie = "6",
        DestinatarioCnpj = "14.605.065/0001-75",
        NumeroTransporte = "30288",
        Itens = new[]
        {
            new ItemNota { CodigoProduto = "00175880", Descricao = "PRODUTO 0", Unidade = "CX", Quantidade = 1, Cfop = "5102" },
        },
    };

    private Orquestrador CriarOrquestrador(IClienteWms cliente)
    {
        var op = Options.Create(new OpcoesOperacionais
        {
            Deposito = "JD",
            CnpjArmazem = "14.605.065/0001-75",
            CnpjDepositante = "01.838.723/0098-50",
            CodigoDepositante = "3509308",
        });
        return new Orquestrador(
            cliente,
            new ConstrutorI50(op, Options.Create(new OpcoesI50())),
            new ConstrutorI10(op),
            new ConstrutorI30(op),
            new ConstrutorI60(op),
            new ClassificadorRetorno(),
            Options.Create(new OpcoesI60 { Habilitada = false }),
            new FakeClock(Inicio),
            NullLogger<Orquestrador>.Instance,
            null,
            _diario);
    }

    [Fact]
    public async Task I10_GatilhoComMensagemDaI50_ChamadaOk()
    {
        var wms = new FakeClienteWms()
            .Responder(InterfaceWms.I50, Resp("N", TextoEmpresa), Resp("S", "Registrado com sucesso."))
            .Responder(InterfaceWms.I10, Resp("S", "Registrado com sucesso."));

        await CriarOrquestrador(wms).IntegrarAsync(new[] { Nota() }, "30288");

        var gatilho = Assert.Single(_diario.De("cadastro-gatilho"));
        Assert.Equal("I10", gatilho.Subtipo);
        Assert.Equal(TextoEmpresa, gatilho.Mensagem);
        Assert.Equal("15665807", gatilho.Nota);

        var chamada = Assert.Single(_diario.De("cadastro-chamada"));
        Assert.Equal("I10", chamada.Subtipo);
        Assert.True(chamada.Ok);
        Assert.Null(chamada.Mensagem);
        Assert.Equal("15665807", chamada.Nota);
        Assert.NotNull(chamada.DuracaoMs);

        // O gatilho vem antes da chamada.
        var tipos = _diario.Eventos.Select(e => e.Tipo).ToList();
        Assert.True(tipos.IndexOf("cadastro-gatilho") < tipos.IndexOf("cadastro-chamada"));
    }

    [Fact]
    public async Task I30_JaExiste_GravaOkComMensagemJaExiste()
    {
        var wms = new FakeClienteWms()
            .Responder(InterfaceWms.I50, Resp("N", TextoProduto), Resp("S", "Registrado com sucesso."))
            .Responder(InterfaceWms.I30, Resp("N", "Registro já existe no cadastro."));

        await CriarOrquestrador(wms).IntegrarAsync(new[] { Nota() }, "30288");

        Assert.Equal("I30", Assert.Single(_diario.De("cadastro-gatilho")).Subtipo);
        var chamada = Assert.Single(_diario.De("cadastro-chamada"));
        Assert.Equal("I30", chamada.Subtipo);
        Assert.True(chamada.Ok);
        Assert.Equal("ja-existe", chamada.Mensagem);
    }

    [Fact]
    public async Task I30_Erro_GravaOkFalsoComOTextoDoWms()
    {
        var wms = new FakeClienteWms()
            .Responder(InterfaceWms.I50, Resp("N", TextoProduto))
            .Responder(InterfaceWms.I30, Resp("N", "Unidade de medida inválida para o produto."));

        await CriarOrquestrador(wms).IntegrarAsync(new[] { Nota() }, "30288");

        var chamada = _diario.De("cadastro-chamada").First();
        Assert.Equal("I30", chamada.Subtipo);
        Assert.False(chamada.Ok);
        Assert.Equal("Unidade de medida inválida para o produto.", chamada.Mensagem);
        Assert.Equal(_diario.De("cadastro-gatilho").Count, _diario.De("cadastro-chamada").Count);
    }

    [Fact]
    public async Task ExcecaoNaChamadaDeCadastro_GravaOTipoEPropaga_MasAutenticacaoNaoEErroDoCadastro()
    {
        // I10 sem resposta programada: o FakeClienteWms lança InvalidOperationException.
        var semI10 = new FakeClienteWms().Responder(InterfaceWms.I50, Resp("N", TextoEmpresa));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CriarOrquestrador(semI10).IntegrarAsync(new[] { Nota() }, "30288"));

        var chamada = Assert.Single(_diario.De("cadastro-chamada"));
        Assert.False(chamada.Ok);
        Assert.Equal(nameof(InvalidOperationException), chamada.Mensagem);

        var antes = _diario.De("cadastro-chamada").Count;
        var auth = new ClienteQueFalhaNoCadastro(
            Resp("N", TextoEmpresa), new AutenticacaoWmsIndisponivelException("cooldown", 401, true, null));
        await Assert.ThrowsAsync<AutenticacaoWmsIndisponivelException>(
            () => CriarOrquestrador(auth).IntegrarAsync(new[] { Nota() }, "30288"));

        Assert.Equal(antes, _diario.De("cadastro-chamada").Count);
        Assert.Equal(2, _diario.De("cadastro-gatilho").Count); // o gatilho antecede a chamada nos dois casos
    }

    private sealed class ClienteQueFalhaNoCadastro : IClienteWms
    {
        private readonly RespostaWms _i50;
        private readonly Exception _falha;

        public ClienteQueFalhaNoCadastro(RespostaWms i50, Exception falha)
        {
            _i50 = i50;
            _falha = falha;
        }

        public Task<RespostaWms> EnviarAsync(InterfaceWms identificador, object payload, CancellationToken cancellationToken = default) =>
            identificador == InterfaceWms.I50 ? Task.FromResult(_i50) : throw _falha;

        public Task<RespostaWms> EnviarAsync(InterfaceWms identificador, string jsonBruto, CancellationToken cancellationToken = default) =>
            EnviarAsync(identificador, (object)jsonBruto, cancellationToken);

        public ResultadoWms Classificar(RetornoWms retorno) => ResultadoWms.Rejeitado;
    }

    // ---- ProcessadorLote: auth-pacote-interrompido ----

    [Fact]
    public async Task PacoteInterrompidoPorAutenticacao_GravaQuantidadeDeNotasRestantesContandoAAtual()
    {
        var amb = new AmbientePacote { Diagnostico = _diario };
        _descartaveis.Add(amb);
        var baseTempo = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 5; i++)
        {
            var caminho = amb.EscreverXml($"n{i + 1}.xml", (1000 + i).ToString(), "70001");
            File.SetCreationTimeUtc(caminho, baseTempo.AddMinutes(i));
        }

        var chamadas = 0;
        var orq = new OrquestradorFalso
        {
            Comportamento = (nota, transporte) =>
            {
                if (++chamadas == 3)
                {
                    throw new AutenticacaoWmsIndisponivelException("GetToken falhou", 401, false, null);
                }

                return Task.FromResult(OrquestradorFalso.Sucesso(nota.Numero, transporte));
            },
        };

        var g = amb.CriarGerenciador(orq);
        await amb.RodarAteOciosoAsync(g);

        var e = Assert.Single(_diario.De("auth-pacote-interrompido"));
        Assert.Equal(3, e.Quantidade); // 5 notas, 2 tratadas: a atual e as 2 seguintes
        Assert.Equal(401, e.Status);
        Assert.Equal("falha", e.Subtipo);
    }

    // ---- GerenciadorPacote: arquivos-ignorados ----

    [Fact]
    public async Task ArquivosNaoXml_GravamArquivosIgnorados()
    {
        var amb = new AmbientePacote { Diagnostico = _diario };
        _descartaveis.Add(amb);
        amb.EscreverNaoXml("leia-me.txt");
        amb.EscreverNaoXml("planilha.pdf");

        var g = amb.CriarGerenciador(new OrquestradorFalso());
        await amb.RodarAteOciosoAsync(g);

        var e = Assert.Single(_diario.De("arquivos-ignorados"));
        Assert.Equal(2, e.Quantidade);
    }

    [Fact]
    public async Task PacoteSoComXml_NaoGravaArquivosIgnorados()
    {
        var amb = new AmbientePacote { Diagnostico = _diario };
        _descartaveis.Add(amb);
        amb.EscreverXml("n1.xml", "1000", "70001");

        var g = amb.CriarGerenciador(new OrquestradorFalso());
        await amb.RodarAteOciosoAsync(g);

        Assert.Empty(_diario.De("arquivos-ignorados"));
    }

    // ---- I51 e I60 ----

    private static FakeWmsI51.ItemDoc Item(decimal qtde, decimal sep) => new(1, "A", qtde, sep);

    [Fact]
    public async Task I51_ConsultaOk_Ciclo_EDesfechoDaI60()
    {
        var amb = new AmbienteI51(diagnostico: _diario);
        _descartaveis.Add(amb);
        amb.RegistrarNota(100, new[] { ("A", 10m) });
        amb.Wms.Separar("100", AmbienteI51.CnpjDestinatario, new[] { Item(10, 10) });

        await amb.Servico.CicloAsync(CancellationToken.None);

        var consulta = Assert.Single(_diario.De("i51-consulta"));
        Assert.Equal("TODOS", consulta.Subtipo);
        Assert.True(consulta.Ok);

        var ciclo = Assert.Single(_diario.De("i51-ciclo"));
        Assert.Equal(1, ciclo.Quantidade);
        Assert.Equal(0, ciclo.Quantidade2);

        var desfecho = Assert.Single(_diario.De("i60-desfecho"));
        Assert.Equal("associada", desfecho.Subtipo);
        Assert.Equal("100", desfecho.Nota);
        Assert.Equal("6", desfecho.Serie);
    }

    [Fact]
    public async Task I51_Vazio_ForaDoPadrao_EFalhaComStatusHttp()
    {
        var amb = new AmbienteI51(diagnostico: _diario);
        _descartaveis.Add(amb);
        amb.I51.PadroesSemRetorno = "nenhum documento";
        amb.RegistrarNota(100, new[] { ("A", 10m) });

        await amb.Servico.CicloAsync(CancellationToken.None); // "Nenhum documento disponivel..."
        amb.Wms.CorposForcados.Enqueue("{\"sucesso\":\"N\",\"mensagem\":\"EDI nao configurado para o CNPJ\"}");
        await amb.Servico.CicloAsync(CancellationToken.None);
        amb.Wms.FalhasConsulta.Enqueue(new HttpRequestException("falha", null, HttpStatusCode.BadGateway));
        await amb.Servico.CicloAsync(CancellationToken.None);

        var consultas = _diario.De("i51-consulta");
        Assert.Equal(3, consultas.Count);
        Assert.Equal("vazio", consultas[0].Mensagem);
        Assert.True(consultas[0].Ok);
        Assert.Equal("fora-do-padrao", consultas[1].Mensagem);
        Assert.False(consultas[1].Ok);
        Assert.False(consultas[2].Ok);
        Assert.Equal(502, consultas[2].Status);
        Assert.Equal(nameof(HttpRequestException), consultas[2].Mensagem);
        Assert.Empty(_diario.De("i51-ciclo")); // sem separadas nem atrasadas
    }

    [Fact]
    public async Task I51_NotaAtrasada_GravaCicloComQuantidade2_EAlertaDaI60()
    {
        var amb = new AmbienteI51(diagnostico: _diario);
        _descartaveis.Add(amb);
        amb.RegistrarNota(100, new[] { ("A", 10m) });
        amb.Clock.Avancar(TimeSpan.FromHours(amb.I51.HorasParaAtraso + 1));

        await amb.Servico.CicloAsync(CancellationToken.None);

        var ciclo = Assert.Single(_diario.De("i51-ciclo"));
        Assert.Equal(0, ciclo.Quantidade);
        Assert.Equal(1, ciclo.Quantidade2);

        var alerta = Assert.Single(_diario.De("i60-alerta"));
        Assert.Equal("Atrasada", alerta.Subtipo);
        Assert.Equal(1, alerta.Quantidade);
        Assert.True(alerta.Ok); // o e-mail (falso) foi enviado
    }

    [Fact]
    public async Task I60Rejeitada_GravaDesfechoEAlertaPorEvento()
    {
        var amb = new AmbienteI51(diagnostico: _diario);
        _descartaveis.Add(amb);
        amb.RegistrarNota(100, new[] { ("A", 10m) });
        amb.Wms.Separar("100", AmbienteI51.CnpjDestinatario, new[] { Item(10, 10) });
        amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.Recusa("Quantidade caixa da nota não confere com a quantidade separada.");

        await amb.Servico.CicloAsync(CancellationToken.None);

        Assert.Equal("rejeitada", Assert.Single(_diario.De("i60-desfecho")).Subtipo);
        var alerta = Assert.Single(_diario.De("i60-alerta"));
        Assert.Equal("Rejeitada", alerta.Subtipo);
        Assert.True(alerta.Ok);
    }

    [Fact]
    public async Task I60Alerta_SemSmtp_GravaOkFalso()
    {
        var amb = new AmbienteI51(diagnostico: _diario);
        _descartaveis.Add(amb);
        amb.EmailOpcoes.Habilitado = false;
        amb.RegistrarNota(100, new[] { ("A", 10m) });
        amb.Wms.Separar("100", AmbienteI51.CnpjDestinatario, new[] { Item(10, 10) });
        amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.Recusa("Quantidade caixa da nota não confere com a quantidade separada.");

        await amb.Servico.CicloAsync(CancellationToken.None);

        Assert.False(Assert.Single(_diario.De("i60-alerta")).Ok);
    }
}
