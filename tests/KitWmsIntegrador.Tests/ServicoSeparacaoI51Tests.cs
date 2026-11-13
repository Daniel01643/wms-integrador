using System.Net.Http;
using KitWmsIntegrador.Core.Separacao;
using KitWmsIntegrador.Tests.Infra;

namespace KitWmsIntegrador.Tests;

public class ServicoSeparacaoI51Tests : IDisposable
{
    private readonly AmbienteI51 _amb = new();

    public void Dispose() => _amb.Dispose();

    private static FakeWmsI51.ItemDoc It(int seq, string cod, decimal q, decimal sep) => new(seq, cod, q, sep);

    private FakeWmsI51.Doc Separar(int numero, params FakeWmsI51.ItemDoc[] itens) =>
        _amb.Wms.Separar(numero.ToString(), AmbienteI51.CnpjDestinatario, itens);

    private Task<ResultadoCicloI51> Ciclo() => _amb.Servico.CicloAsync(CancellationToken.None);

    [Fact]
    public async Task Todos_EConsumivel_ASegundaConsultaNaoRepeteENaoDuplica()
    {
        _amb.RegistrarNota(100, new[] { ("A", 10m) });
        _amb.RegistrarNota(101, new[] { ("A", 10m) });
        Separar(100, It(1, "A", 10, 10));

        var c1 = await Ciclo();
        Assert.True(c1.TodosConsultado);
        Assert.Equal(SituacaoNota.Associada, _amb.Nota(100).Situacao);
        Assert.Equal(SituacaoNota.AguardandoSeparacao, _amb.Nota(101).Situacao);

        // A 101 ainda aguarda: o TODOS roda de novo e o WMS não devolve o que já foi puxado.
        var c2 = await Ciclo();
        Assert.True(c2.TodosConsultado);
        Assert.Equal(0, c2.NotasSeparadas);
        Assert.Single(_amb.Wms.EnviosI60);
        Assert.Equal(SituacaoNota.Associada, _amb.Nota(100).Situacao);
        Assert.Single(Directory.GetFiles(_amb.Caminhos.BrutosProcessados, "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RespostaBruta_EGravadaAntesDeProcessar_EOCicloSeguinteProcessaSemChamarOWms()
    {
        _amb.RegistrarNota(100, new[] { ("A", 10m) });
        Separar(100, It(1, "A", 10, 10));
        // Falha proposital na gravação do estado: um DIRETÓRIO no lugar do .tmp da nota.
        var bloqueio = Path.Combine(_amb.Caminhos.Notas, AmbienteI51.Chave(100) + ".json.tmp");
        Directory.CreateDirectory(bloqueio);

        await Ciclo();

        Assert.Equal(SituacaoNota.AguardandoSeparacao, _amb.Nota(100).Situacao);
        var pendente = Assert.Single(_amb.Brutos.ListarPendentes());
        Assert.Contains("\"Numero\":\"100\"", File.ReadAllText(pendente));
        Assert.Single(_amb.Wms.Consultas);

        Directory.Delete(bloqueio);
        await Ciclo();

        Assert.Single(_amb.Wms.Consultas); // não consultou de novo
        Assert.Equal(SituacaoNota.Associada, _amb.Nota(100).Situacao);
        Assert.Empty(_amb.Brutos.ListarPendentes());
    }

    [Fact]
    public async Task BrutoQueNuncaProcessa_VaiParaFalhosAposAsTentativas()
    {
        _amb.Wms.CorposForcados.Enqueue("{ isto nao e json");
        _amb.RegistrarNota(100, new[] { ("A", 10m) });

        for (var i = 0; i < _amb.I51.MaxTentativasProcessarBruto; i++)
        {
            await Ciclo();
        }

        Assert.Empty(_amb.Brutos.ListarPendentes());
        Assert.Equal(1, _amb.Brutos.ContarFalhos());
    }

    [Fact]
    public async Task Unico_ERepetivel_ERecuperaNotaAtrasada()
    {
        _amb.RegistrarNota(100, new[] { ("A", 10m) });
        var doc = Separar(100, It(1, "A", 10, 7));
        // O TODOS foi consumido em outro lugar (bruto perdido): o UNICO é o caminho de recuperação.
        await _amb.Consulta.ConsultarTodosAsync(CancellationToken.None);
        _amb.Wms.Consultas.Clear();

        _amb.Clock.Avancar(TimeSpan.FromHours(25));
        var c = await Ciclo();

        Assert.Equal(1, c.NotasAtrasadas);
        Assert.Equal(1, c.UnicosConsultados);
        var unico = Assert.Single(_amb.Wms.Consultas, q => q.Tipo == "UNICO");
        Assert.Equal("000000100", unico.Numero);
        Assert.Equal(SituacaoNota.Associada, _amb.Nota(100).Situacao);
        Assert.Equal(7m, _amb.Nota(100).Itens[0].QtdeSeparada);
        Assert.Equal(7m, _amb.Wms.EnviosI60[0].NotasVenda[0].ItemNotaVenda[0].QtdeProduto);

        // O mesmo UNICO pode ser repetido quantas vezes forem necessárias.
        var a = await _amb.Consulta.ConsultarUnicoAsync("100", CancellationToken.None);
        var b = await _amb.Consulta.ConsultarUnicoAsync("100", CancellationToken.None);
        Assert.Equal(a, b);
        Assert.NotNull(doc);
    }

    [Fact]
    public async Task SucessoN_NaoEErro_NaoGravaBruto_NaoMudaEstado()
    {
        _amb.RegistrarNota(100, new[] { ("A", 10m) });

        var c = await Ciclo();

        Assert.True(c.TodosConsultado);
        Assert.Empty(_amb.Brutos.ListarPendentes());
        Assert.False(Directory.Exists(_amb.Caminhos.BrutosProcessados));
        Assert.Equal(0, _amb.Brutos.LerSaude().FalhasConsecutivas);
        Assert.NotNull(_amb.Brutos.LerSaude().UltimaConsultaOkEm);
        Assert.Equal(SituacaoNota.AguardandoSeparacao, _amb.Nota(100).Situacao);
    }

    [Fact]
    public async Task PadroesSemRetorno_ForaDaLista_ContaComoFalhaDeConsulta()
    {
        _amb.I51.PadroesSemRetorno = "nenhum documento";
        _amb.RegistrarNota(100, new[] { ("A", 10m) });

        await Ciclo(); // mensagem "Nenhum documento disponivel..." casa: ok
        Assert.Equal(0, _amb.Brutos.LerSaude().FalhasConsecutivas);

        _amb.Wms.CorposForcados.Enqueue("{\"sucesso\":\"N\",\"mensagem\":\"EDI nao configurado para o CNPJ\"}");
        await Ciclo();
        Assert.Equal(1, _amb.Brutos.LerSaude().FalhasConsecutivas);
    }

    [Fact]
    public async Task FalhaDeRede_ContaFalhaESegueOCiclo()
    {
        _amb.RegistrarNota(100, new[] { ("A", 10m) });
        _amb.Wms.FalhasConsulta.Enqueue(new HttpRequestException("falha", null, System.Net.HttpStatusCode.BadGateway));

        var c = await Ciclo();

        Assert.True(c.TodosConsultado);
        Assert.Equal(1, _amb.Brutos.LerSaude().FalhasConsecutivas);
        Assert.Equal(SituacaoNota.AguardandoSeparacao, _amb.Nota(100).Situacao);
    }

    [Fact]
    public async Task Unico_FalhaDeRede_NaoERepetidoACadaCiclo()
    {
        _amb.RegistrarNota(100, new[] { ("A", 10m) });
        _amb.Clock.Avancar(TimeSpan.FromHours(25));
        _amb.Wms.FalhasConsulta.Enqueue(new HttpRequestException("TODOS cai"));
        _amb.Wms.FalhasConsulta.Enqueue(new HttpRequestException("UNICO cai"));

        await Ciclo();
        Assert.Equal(1, _amb.Wms.Consultas.Count(q => q.Tipo == "UNICO"));
        Assert.Equal(1, _amb.Repositorio.Obter(AmbienteI51.Chave(100))!.TentativasUnico);

        _amb.Clock.Avancar(TimeSpan.FromMinutes(5));
        await Ciclo();
        Assert.Equal(1, _amb.Wms.Consultas.Count(q => q.Tipo == "UNICO")); // ainda dentro do intervalo

        _amb.Clock.Avancar(TimeSpan.FromMinutes(61));
        await Ciclo();
        Assert.Equal(2, _amb.Wms.Consultas.Count(q => q.Tipo == "UNICO"));
    }

    [Fact]
    public async Task NotaMuitoAntiga_NaoLiberaOTodos_MasAindaUsaOUnico()
    {
        _amb.RegistrarNota(100, new[] { ("A", 10m) });
        _amb.Clock.Avancar(TimeSpan.FromDays(15));

        var c = await Ciclo();

        Assert.False(c.TodosConsultado);
        Assert.DoesNotContain(_amb.Wms.Consultas, q => q.Tipo == "TODOS");
        Assert.Contains(_amb.Wms.Consultas, q => q.Tipo == "UNICO");
    }

    [Fact]
    public async Task Atrasada_ContinuaCasandoComOTodos()
    {
        _amb.RegistrarNota(100, new[] { ("A", 10m) });
        _amb.Clock.Avancar(TimeSpan.FromHours(30));
        Separar(100, It(1, "A", 10, 10));

        await Ciclo();

        Assert.Equal(SituacaoNota.Associada, _amb.Nota(100).Situacao);
        Assert.NotNull(_amb.Nota(100).AtrasadaEm);
    }

    [Fact]
    public async Task OndaTsp_PercorreOFluxoAteAI60()
    {
        _amb.RegistrarNota(200, new[] { ("A", 6m) }, tipo: "TSP", onda: "TSP-20261112-01-SJC");
        Separar(200, It(1, "A", 6, 6));

        await Ciclo();

        var n = _amb.Nota(200);
        Assert.Equal("TSP", n.Tipo);
        Assert.Equal("TSP-20261112-01-SJC", n.Onda);
        Assert.Equal(SituacaoNota.Associada, n.Situacao);
        Assert.Single(_amb.Wms.EnviosI60);
    }

    [Fact]
    public async Task Reinicio_NoMeioDoFluxo_RetomaDeOndeParou()
    {
        _amb.RegistrarNota(100, new[] { ("A", 10m) });
        _amb.Reiniciar();
        Assert.Equal(SituacaoNota.AguardandoSeparacao, _amb.Nota(100).Situacao);

        // I60 desligada: a nota para em Separada; reinicia e liga a I60.
        _amb.I60.Habilitada = false;
        Separar(100, It(1, "A", 10, 9));
        await Ciclo();
        Assert.Equal(SituacaoNota.Separada, _amb.Nota(100).Situacao);
        _amb.Reiniciar();
        Assert.Equal(SituacaoNota.Separada, _amb.Nota(100).Situacao);

        _amb.I60.Habilitada = true;
        await Ciclo();
        Assert.Equal(SituacaoNota.Associada, _amb.Nota(100).Situacao);

        _amb.Reiniciar();
        Assert.Equal(SituacaoNota.Associada, _amb.Nota(100).Situacao);
        Assert.Equal(1, _amb.Wms.ChamadasI60);
    }

    [Fact]
    public async Task I51Desligada_NaoFazNada_NemTocaODisco()
    {
        _amb.I51.Habilitada = false;
        _amb.RegistrarNota(100, new[] { ("A", 10m) });

        var c = await Ciclo();

        Assert.False(c.TodosConsultado);
        Assert.Empty(_amb.Wms.Consultas);
        Assert.False(Directory.Exists(_amb.Caminhos.Base));
    }

    [Fact]
    public async Task ReenfileirarVindoDeOutroProcesso_EAplicadoPeloWorkerNoCicloSeguinte()
    {
        _amb.RegistrarNota(100, new[] { ("A", 10m) });
        Separar(100, It(1, "A", 10, 10));
        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.Recusa("mensagem desconhecida do wms");
        _amb.I60.MaxTentativasOutro = 1;
        await Ciclo();
        Assert.Equal(SituacaoNota.Rejeitada, _amb.Nota(100).Situacao);

        // "Outro processo": só conhece a pasta, escreve o pedido e não toca nas notas.
        var cli = new ComandosI51(new CaminhosI51(Microsoft.Extensions.Options.Options.Create(_amb.Pastas)), _amb.Clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ComandosI51>.Instance);
        Assert.True(cli.GravarReenfileirar(null, "Outro"));
        Assert.Equal(SituacaoNota.Rejeitada, _amb.Nota(100).Situacao); // o Worker (repositório em memória) ainda não viu

        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.Sucesso();
        await Ciclo();

        Assert.Equal(SituacaoNota.Associada, _amb.Nota(100).Situacao);
        Assert.Empty(Directory.GetFiles(_amb.Caminhos.Comandos, "reenfileirar_*.json"));
        Assert.Equal(2, _amb.Wms.ChamadasI60);
    }
}
