using System.Net;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Devolucao;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>A tela de devolução (I40) continua pronta e funcionando com o fluxo I51/I60 ligado.</summary>
public sealed class RegressaoI40ComI51Tests : IDisposable
{
    private readonly AmbientePacote _pacote = new();

    public void Dispose() => _pacote.Dispose();

    [Fact]
    public async Task ComI51Ligada_XmlContinuaEmProcessados_EABuscaPorChaveENumeroSerieAcha()
    {
        using var i51 = new AmbienteI51(null, _pacote.Pastas);
        _pacote.RegistradorSeparacao = i51.Registrador;
        var orq = new OrquestradorFalso();
        _pacote.EscreverZipBrf("Puxada", "lote.zip", 4001, "1000000001");

        await _pacote.RodarAteOciosoAsync(_pacote.CriarGerenciador(orq));

        var chave = AmbientePacote.ChaveBrf(4001);
        var loc = new LocalizadorXmlDevolucao(_pacote.Pastas.CaminhoProcessados);
        Assert.NotNull(loc.Localizar(chave));
        var achadas = loc.BuscarPorNumeroSerie(4001, 6, "01838723009850");
        Assert.Single(achadas);
        Assert.True(i51.Repositorio.Obter(chave)!.XmlCopiado);
    }

    [Fact]
    public async Task I40_UsaIdentificadorImpSemPlacaNemMotorista_ComClienteHttpReal()
    {
        using var amb = new AmbienteDevolucao();
        amb.CopiarXml(AmbienteDevolucao.Chave1);
        var jwt = JwtFake.ComExpiracao(DateTimeOffset.UtcNow.AddHours(1));
        var handler = new FakeHttpMessageHandler((req, _) =>
            req.RequestUri!.AbsolutePath.EndsWith("GetToken", StringComparison.OrdinalIgnoreCase)
                ? FakeHttpMessageHandler.Json(HttpStatusCode.OK, $"{{\"token\":\"{jwt}\"}}")
                : FakeHttpMessageHandler.Json(HttpStatusCode.OK,
                    "{\"transacao\":\"1\",\"tipoEDI\":\"I40\",\"sucesso\":\"S\",\"retornos\":[{\"documento\":\"d\",\"sucesso\":\"S\",\"mensagem\":\"Registrado com sucesso.\"}]}"));
        var opcoes = new OpcoesWms { BaseUrl = "https://exemplo.local/api", Requisitante = "01.838.723/0098-50", Destino = "14.605.065/0001-75", Usuario = "u", Senha = "s" };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://exemplo.local/api/") };
        var cliente = new ClienteWmsHttp(http, Options.Create(opcoes), NullLogger<ClienteWmsHttp>.Instance, new FakeClock(DateTimeOffset.UtcNow));
        var servico = new ServicoDevolucao(
            cliente, new ConstrutorI40(Options.Create(amb.Operacional), Options.Create(amb.Devolucao)), new LeitorNotaFiscalXml(),
            new AuditoriaDevolucao(new FakeClock(DateTimeOffset.UtcNow), Options.Create(amb.Pastas), Options.Create(amb.Devolucao), NullLogger<AuditoriaDevolucao>.Instance),
            Options.Create(amb.Pastas), Options.Create(amb.Operacional), Options.Create(amb.Devolucao), NullLogger<ServicoDevolucao>.Instance);

        await servico.EnviarAsync(new SolicitacaoDevolucao { Chaves = new[] { AmbienteDevolucao.Chave1 }.ToList(), Origem = "10.0.0.5" });

        var post = handler.Requisicoes.Last(r => r.Metodo == HttpMethod.Post);
        Assert.Contains("IDENTIFICADOR=WEBAPI-I40-Json", post.Uri.Query);
        Assert.Contains("\"tipoOperacaoNota\":\"IMP\"", post.Corpo);
        Assert.Contains("\"placaVeiculoPrevistoNota\":\"\"", post.Corpo);
        Assert.Contains("\"nomeMotorista\":\"\"", post.Corpo);
    }
}
