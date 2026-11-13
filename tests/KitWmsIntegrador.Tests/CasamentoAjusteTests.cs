using KitWmsIntegrador.Core.Separacao;
using KitWmsIntegrador.Tests.Infra;

namespace KitWmsIntegrador.Tests;

public class CalculadorAjusteTests
{
    private static ItemSeparacao Item(int seq, string cod, decimal q) => new() { Sequencia = seq, CodProduto = cod, QtdeOriginal = q };

    [Fact]
    public void QuantidadeMenor_RegistraOriginalSeparadaEDiferenca()
    {
        var r = CalculadorAjuste.Calcular(
            new[] { Item(1, "A", 10), Item(2, "B", 5) },
            new[] { new ItemI51(1, "A", 10, 8), new ItemI51(2, "B", 5, 5) }, omitirSemRetorno: false);

        Assert.Equal(13m, r.SomaSeparada);
        Assert.Equal(8m, r.Itens[0].QtdeSeparada);
        Assert.Equal(-2m, r.Itens[0].Diferenca);
        Assert.Equal(0m, r.Itens[1].Diferenca);
        Assert.False(r.SemQuantidadeSeparada);
    }

    [Fact]
    public void SequenciaDivergente_CaiParaOMesmoCodigoNaoUsado()
    {
        var r = CalculadorAjuste.Calcular(
            new[] { Item(1, "A", 4), Item(2, "A", 6) },
            new[] { new ItemI51(7, "a", 0, 3), new ItemI51(8, "A", 0, 6) }, false);
        Assert.Equal(3m, r.Itens[0].QtdeSeparada);
        Assert.Equal(6m, r.Itens[1].QtdeSeparada);
    }

    [Fact]
    public void ItemSemRetorno_ZeraOuOmite()
    {
        var itens = new[] { Item(1, "A", 10), Item(2, "B", 5) };
        var wms = new[] { new ItemI51(1, "A", 10, 10) };

        var zerar = CalculadorAjuste.Calcular(itens, wms, omitirSemRetorno: false);
        Assert.Equal(0m, zerar.Itens[1].QtdeSeparada);
        Assert.True(zerar.Itens[1].SemRetorno);
        Assert.Equal(-5m, zerar.Itens[1].Diferenca);

        var omitir = CalculadorAjuste.Calcular(itens, wms, omitirSemRetorno: true);
        Assert.Null(omitir.Itens[1].QtdeSeparada);
        Assert.True(omitir.Itens[1].SemRetorno);
    }

    [Fact]
    public void SomaZero_SemQuantidadeSeparada()
    {
        var r = CalculadorAjuste.Calcular(new[] { Item(1, "A", 10) }, new[] { new ItemI51(1, "A", 10, 0) }, false);
        Assert.True(r.SemQuantidadeSeparada);
    }

    [Fact]
    public void DocumentosRepetidos_SomamPorItem()
    {
        var r = CalculadorAjuste.Calcular(
            new[] { Item(1, "A", 10) },
            new[] { new ItemI51(1, "A", 10, 4), new ItemI51(1, "A", 10, 5) }, false);
        Assert.Equal(9m, r.Itens[0].QtdeSeparada);
    }
}

public class CasadorDocumentoNotaTests : IDisposable
{
    private readonly AmbienteI51 _amb = new();

    public void Dispose() => _amb.Dispose();

    private static FakeWmsI51.ItemDoc It(int seq, string cod, decimal q, decimal sep) => new(seq, cod, q, sep);

    private ResultadoCasamento Processar(params FakeWmsI51.Doc[] docs) =>
        _amb.Casador.Processar(ParserI51.Ler(FakeWmsI51.CorpoDe(docs)), "i51_TODOS_20261112_150000000.json");

    [Fact]
    public void ZerosAEsquerda_Casam()
    {
        _amb.RegistrarNota(12345, new[] { ("A", 10m) });
        var doc = new FakeWmsI51.Doc("000012345", AmbienteI51.CnpjProprietario, AmbienteI51.CnpjDestinatario, "PEDIDO", "CN", 2, new[] { It(1, "A", 10, 9) });

        var r = Processar(doc);

        Assert.Equal(1, r.NotasSeparadas);
        var n = _amb.Nota(12345);
        Assert.Equal(SituacaoNota.Separada, n.Situacao);
        Assert.Equal(2, n.QtdeVolumesI51);
        Assert.Equal(9m, n.Itens[0].QtdeSeparada);
        Assert.StartsWith("i51_TODOS_", n.BrutoOrigem);
    }

    [Fact]
    public void CnpjDeConsultaDiferenteDoDepositante_AindaCasa_PeloCnpjProprietarioDoRetorno()
    {
        _amb.I51.CnpjConsulta = "12.345.678/0001-95";
        _amb.RegistrarNota(77, new[] { ("A", 3m) });
        var doc = new FakeWmsI51.Doc("77", AmbienteI51.CnpjProprietario, AmbienteI51.CnpjDestinatario, "PEDIDO", "CN", 1, new[] { It(1, "A", 3, 3) });
        Assert.Equal(1, Processar(doc).NotasSeparadas);
    }

    [Fact]
    public void OutroProprietario_EContadoEIgnorado_SemVirarOrfao()
    {
        _amb.RegistrarNota(88, new[] { ("A", 3m) });
        var doc = new FakeWmsI51.Doc("88", "12345678000195", AmbienteI51.CnpjDestinatario, "PEDIDO", "CN", 1, new[] { It(1, "A", 3, 3) });

        var r = Processar(doc);

        Assert.Equal(1, r.OutroProprietario);
        Assert.Equal(0, r.NotasSeparadas);
        Assert.Empty(_amb.Orfaos.Listar());
        Assert.Equal(SituacaoNota.AguardandoSeparacao, _amb.Nota(88).Situacao);
    }

    [Fact]
    public void DocumentoSemNota_ViraOrfao_ECasaQuandoANotaChega()
    {
        var doc = new FakeWmsI51.Doc("500", AmbienteI51.CnpjProprietario, AmbienteI51.CnpjDestinatario, "PEDIDO", "CN", 1, new[] { It(1, "A", 3, 2) });
        var r = Processar(doc);
        Assert.Equal(1, r.Orfaos);
        Assert.Single(_amb.Orfaos.Listar());

        _amb.RegistrarNota(500, new[] { ("A", 3m) }); // o Registrar confronta os órfãos

        Assert.Equal(SituacaoNota.Separada, _amb.Nota(500).Situacao);
        Assert.Equal(2m, _amb.Nota(500).Itens[0].QtdeSeparada);
        Assert.Empty(_amb.Orfaos.Listar());
    }

    [Fact]
    public void OrfaoDeNotaJaSeparada_EDescartado()
    {
        _amb.RegistrarNota(600, new[] { ("A", 3m) });
        var doc = new FakeWmsI51.Doc("600", AmbienteI51.CnpjProprietario, AmbienteI51.CnpjDestinatario, "PEDIDO", "CN", 1, new[] { It(1, "A", 3, 3) });
        Processar(doc);
        // O mesmo documento volta (ex.: UNICO repetido): a nota já está separada.
        var r = Processar(doc);
        Assert.Equal(1, r.Descartados);
        Assert.Empty(_amb.Orfaos.Listar());
    }

    [Fact]
    public void Ambiguidade_FiltraPorCnpjDestinatario_EDepoisPelaMaisAntiga()
    {
        _amb.RegistrarNota(900, new[] { ("A", 1m) }, cnpjDest: "22222222000122");
        _amb.Clock.Avancar(TimeSpan.FromMinutes(5));
        // Mesmo número em outro pacote, outra chave: precisa de XML com outra chave. Usa o registro direto.
        _amb.Repositorio.Registrar(new NotaSeparacao
        {
            Chave = "nf-900-6-33333333000133",
            Numero = "900",
            CnpjDestinatario = "33333333000133",
            RegistradaEm = _amb.Clock.UtcNow,
            Itens = new[] { new ItemSeparacao { Sequencia = 1, CodProduto = "A", QtdeOriginal = 1 } },
        });

        var doc = new FakeWmsI51.Doc("900", AmbienteI51.CnpjProprietario, "33333333000133", "PEDIDO", "CN", 1, new[] { It(1, "A", 1, 1) });
        Processar(doc);

        Assert.Equal(SituacaoNota.Separada, _amb.Repositorio.Obter("nf-900-6-33333333000133")!.Situacao);
        Assert.Equal(SituacaoNota.AguardandoSeparacao, _amb.Nota(900).Situacao);

        // Sem CNPJ que desempate: casa com a mais antiga e anota a ambiguidade.
        var doc2 = new FakeWmsI51.Doc("900", AmbienteI51.CnpjProprietario, "99999999000199", "PEDIDO", "CN", 1, new[] { It(1, "A", 1, 1) });
        _amb.Repositorio.Registrar(new NotaSeparacao
        {
            Chave = "nf-900-6-44444444000144",
            Numero = "900",
            RegistradaEm = _amb.Clock.UtcNow.AddMinutes(1),
            Itens = new[] { new ItemSeparacao { Sequencia = 1, CodProduto = "A", QtdeOriginal = 1 } },
        });
        Processar(doc2);
        Assert.Equal(SituacaoNota.Separada, _amb.Nota(900).Situacao);
        Assert.Contains("ambígua", _amb.Nota(900).Observacao);
        Assert.Equal(SituacaoNota.AguardandoSeparacao, _amb.Repositorio.Obter("nf-900-6-44444444000144")!.Situacao);
    }

    [Fact]
    public void MaisDeUmDocumentoParaAMesmaNota_SomaPorItem()
    {
        _amb.RegistrarNota(321, new[] { ("A", 10m) });
        var d1 = new FakeWmsI51.Doc("321", AmbienteI51.CnpjProprietario, AmbienteI51.CnpjDestinatario, "PEDIDO", "CN", 1, new[] { It(1, "A", 10, 4) });
        var d2 = new FakeWmsI51.Doc("321", AmbienteI51.CnpjProprietario, AmbienteI51.CnpjDestinatario, "PEDIDO", "CN", 2, new[] { It(1, "A", 10, 5) });
        Processar(d1, d2);
        var n = _amb.Nota(321);
        Assert.Equal(9m, n.Itens[0].QtdeSeparada);
        Assert.Equal(3, n.QtdeVolumesI51);
    }
}

public class ArmazenamentoBrutoI51Tests : IDisposable
{
    private readonly AmbienteI51 _amb = new();

    public void Dispose() => _amb.Dispose();

    [Fact]
    public void Pendente_Processado_Falho()
    {
        var p = _amb.Brutos.GravarPendente("TODOS", null, "{\"a\":1}");
        Assert.NotNull(p);
        Assert.Contains("pendentes", p);
        Assert.Single(_amb.Brutos.ListarPendentes());
        Assert.Equal("{\"a\":1}", _amb.Brutos.Ler(p!));

        _amb.Brutos.MoverProcessado(p!);
        Assert.Empty(_amb.Brutos.ListarPendentes());
        Assert.Single(Directory.GetFiles(_amb.Caminhos.BrutosProcessados, "*.json", SearchOption.AllDirectories));

        var p2 = _amb.Brutos.GravarPendente("UNICO", "5287", "{}");
        _amb.Brutos.MoverFalho(p2!);
        Assert.Equal(1, _amb.Brutos.ContarFalhos());
    }

    [Fact]
    public void Limpar_ApagaProcessadosVencidos()
    {
        var p = _amb.Brutos.GravarPendente("TODOS", null, "{}");
        _amb.Brutos.MoverProcessado(p!);
        Assert.Equal(0, _amb.Brutos.Limpar(60));
        _amb.Clock.Avancar(TimeSpan.FromDays(61));
        Assert.Equal(1, _amb.Brutos.Limpar(60));
    }

    [Fact]
    public void Saude_PersisteELeDeVolta()
    {
        _amb.Brutos.GravarSaude(new SaudeConsultaI51 { FalhasConsecutivas = 2, UltimaMensagemVazia = "nada" });
        _amb.Reiniciar();
        Assert.Equal(2, _amb.Brutos.LerSaude().FalhasConsecutivas);
        Assert.Equal("nada", _amb.Brutos.LerSaude().UltimaMensagemVazia);
    }
}
