using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Orquestrador;
using KitWmsIntegrador.Core.Separacao;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Combinações das chaves I51/I60 com o pacote REAL (ProcessadorLote + Orquestrador real + registrador real), I50 pelo
/// FakeClienteWms e I51/I60 pelo FakeWmsI51. Garante: I51 desligada = comportamento antigo; I51 ligada = I60 só depois do retorno.
/// </summary>
public sealed class FluxoI51I60Tests : IDisposable
{
    private readonly AmbientePacote _pacote = new();
    private readonly FakeClienteWms _wmsI50 = new();
    private readonly ColetorLogs _logs = new();
    private readonly AmbienteI51 _i51;
    private OpcoesI60 _opcoesI60 => _i51.I60;
    private readonly OpcoesI51 _opcoesI51;

    public FluxoI51I60Tests()
    {
        var fabrica = LoggerFactory.Create(b => b.AddProvider(_logs));
        _i51 = new AmbienteI51(fabrica, _pacote.Pastas);
        _opcoesI51 = _i51.I51;
        _opcoesI51.Habilitada = false;
        _wmsI50.Responder(InterfaceWms.I50, new RespostaWms
        {
            Sucesso = "S",
            Retornos = new[] { new RetornoWms { Sucesso = "S", Mensagem = "Registrado com sucesso." } },
        });
        _wmsI50.Responder(InterfaceWms.I60, FakeWmsI51.Sucesso());
        _pacote.RegistradorSeparacao = _i51.Registrador;
    }

    public void Dispose()
    {
        _pacote.Dispose();
        _i51.Dispose();
    }

    private Orquestrador OrquestradorReal()
    {
        var op = Options.Create(_i51.Operacional);
        return new Orquestrador(
            _wmsI50,
            new ConstrutorI50(op, Options.Create(new OpcoesI50())),
            new ConstrutorI10(op),
            new ConstrutorI30(op),
            new ConstrutorI60(op, Options.Create(_opcoesI60)),
            new ClassificadorRetorno(),
            Options.Create(_opcoesI60),
            _pacote.Clock,
            NullLogger<Orquestrador>.Instance,
            Options.Create(_opcoesI51));
    }

    private async Task ProcessarPacoteAsync(int primeiro, params string?[] transportes)
    {
        _pacote.EscreverZipBrf("Puxada", "lote.zip", primeiro, transportes);
        var g = _pacote.CriarGerenciador(OrquestradorReal());
        await _pacote.RodarAteOciosoAsync(g);
    }

    [Fact]
    public async Task I51Desligada_I60Ligada_SaiJuntoDaI50_ComoAntes_ENaoCriaEstadoI51()
    {
        _opcoesI60.Habilitada = true;
        await ProcessarPacoteAsync(1000, "1000000001");

        Assert.Equal(1, _wmsI50.Contar(InterfaceWms.I50));
        Assert.Equal(1, _wmsI50.Contar(InterfaceWms.I60));
        var i60 = (KitWmsIntegrador.Core.Construtores.Dtos.PayloadI60)_wmsI50.Envios.Single(e => e.Identificador == InterfaceWms.I60).Payload;
        Assert.Equal("INE", i60.NotasVenda[0].TipoOperacaoNota);
        Assert.Equal("I", i60.NotasVenda[0].Tpacao);
        Assert.False(Directory.Exists(_i51.Caminhos.Base)); // registrador REAL, chave desligada: nada em disco
    }

    [Fact]
    public async Task I51Ligada_I60Ligada_NaoSaiJuntoDaI50_ERegistraANota()
    {
        _opcoesI51.Habilitada = true;
        _opcoesI60.Habilitada = true;
        await ProcessarPacoteAsync(1000, "1000000001");

        Assert.Equal(1, _wmsI50.Contar(InterfaceWms.I50));
        Assert.Equal(0, _wmsI50.Contar(InterfaceWms.I60));
        var n = Assert.Single(_i51.Repositorio.Listar());
        Assert.Equal(SituacaoNota.AguardandoSeparacao, n.Situacao);
        Assert.Equal("PUXADA", n.Tipo);
        Assert.True(n.XmlCopiado);
        Assert.True(File.Exists(Path.Combine(_i51.Caminhos.Base, n.CaminhoXml)));
        Assert.Equal("1000", n.Numero);
        // O XML continua indo para Processados (a tela I40 depende disso).
        Assert.NotEmpty(_pacote.Arquivos("Processados"));
    }

    [Fact]
    public async Task I51Ligada_I60Desligada_NotaChegaASeparada_ENenhumaI60Sai()
    {
        _opcoesI51.Habilitada = true;
        _opcoesI60.Habilitada = false;
        await ProcessarPacoteAsync(1000, "1000000001");
        _i51.Wms.Separar("1000", AmbienteI51.CnpjDestinatario, new[] { new FakeWmsI51.ItemDoc(1, "ABC1000", 1, 1) });

        await _i51.Servico.CicloAsync(CancellationToken.None);

        Assert.Equal(SituacaoNota.Separada, _i51.Repositorio.Listar().Single().Situacao);
        Assert.Equal(0, _i51.Wms.ChamadasI60);
        Assert.Equal(0, _wmsI50.Contar(InterfaceWms.I60));
    }

    [Fact]
    public async Task I51Ligada_I60Ligada_FluxoCompleto_I50_Registro_Todos_I60Ine_Associada()
    {
        _opcoesI51.Habilitada = true;
        _opcoesI60.Habilitada = true;
        await ProcessarPacoteAsync(1000, "1000000001");
        Assert.Equal(0, _i51.Wms.ChamadasI60);

        _i51.Wms.Separar("1000", AmbienteI51.CnpjDestinatario, new[] { new FakeWmsI51.ItemDoc(1, "ABC1000", 1, 1) });
        await _i51.Servico.CicloAsync(CancellationToken.None);

        var n = _i51.Repositorio.Listar().Single();
        Assert.Equal(SituacaoNota.Associada, n.Situacao);
        Assert.Equal("INE", n.OperacaoI60);
        Assert.Equal(1, _i51.Wms.ChamadasI60);
        Assert.Equal(0, _wmsI50.Contar(InterfaceWms.I60));
    }

    [Fact]
    public async Task OrquestradorRegistraNoHistorico_QueAI60FoiAdiada()
    {
        _opcoesI51.Habilitada = true;
        _opcoesI60.Habilitada = true;
        var nota = new KitWmsIntegrador.Core.Leitura.LeitorNotaFiscalXml().Parse(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(AmbientePacote.XmlBrf(1000, "1000000001"))), "x.xml");

        var r = (await OrquestradorReal().IntegrarAsync(new[] { nota }, "1000000001")).Single();

        Assert.Contains(r.Historico, h => h.Contains("I60 adiada"));
        Assert.Equal(0, _wmsI50.Contar(InterfaceWms.I60));
    }

    [Fact]
    public async Task OndaTsp_RegistraTipoEOnda_EPercorreOFluxoAteAI60()
    {
        _opcoesI51.Habilitada = true;
        _opcoesI60.Habilitada = true;
        _pacote.EscreverZipBrf("PuxadaTSP", "XMLS SJC.zip", 2001, "1000000001", "1000000002");
        var g = _pacote.CriarGerenciador(OrquestradorReal());
        await _pacote.RodarAteOciosoAsync(g);

        var notas = _i51.Repositorio.Listar();
        Assert.Equal(2, notas.Count);
        Assert.All(notas, n =>
        {
            Assert.Equal("TSP", n.Tipo);
            Assert.StartsWith("TSP-", n.Onda);
        });

        foreach (var n in notas)
        {
            _i51.Wms.Separar(n.Numero, AmbienteI51.CnpjDestinatario, new[] { new FakeWmsI51.ItemDoc(1, "ABC" + n.Numero, 1, 1) });
        }

        await _i51.Servico.CicloAsync(CancellationToken.None);
        Assert.All(_i51.Repositorio.Listar(), n => Assert.Equal(SituacaoNota.Associada, n.Situacao));
        Assert.Equal(2, _i51.Wms.ChamadasI60);
    }

    [Fact]
    public async Task NenhumLogContemTokenSenhaOuTrechoDoXml()
    {
        _opcoesI51.Habilitada = true;
        _opcoesI60.Habilitada = true;
        await ProcessarPacoteAsync(1000, "1000000001");
        _i51.Wms.Separar("1000", AmbienteI51.CnpjDestinatario, new[] { new FakeWmsI51.ItemDoc(1, "ABC1000", 1, 1) });
        await _i51.Servico.CicloAsync(CancellationToken.None);

        string[] proibidos =
        {
            "Bearer", "senha", "Authorization", "<nfeProc", "<infNFe", "EMITENTE SINTETICO", "DESTINATARIO SINTETICO", "\"documentos\"", "CnpjProprietario",
        };
        string[] linhas;
        lock (_logs.Linhas)
        {
            linhas = _logs.Linhas.ToArray();
        }

        Assert.NotEmpty(linhas);
        foreach (var linha in linhas)
        {
            foreach (var p in proibidos)
            {
                Assert.DoesNotContain(p, linha, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
