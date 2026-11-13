using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Separacao;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class RepositorioSeparacaoTests : IDisposable
{
    private readonly AmbienteI51 _amb = new();

    public void Dispose() => _amb.Dispose();

    private static NotaSeparacao Nova(string chave = "K1", string numero = "100") => new()
    {
        Chave = chave,
        Numero = numero,
        RegistradaEm = new DateTimeOffset(2026, 11, 12, 15, 0, 0, TimeSpan.Zero),
        Itens = new[] { new ItemSeparacao { Sequencia = 1, CodProduto = "A", QtdeOriginal = 5m } },
    };

    [Fact]
    public void Construir_NaoCriaPasta()
    {
        Assert.False(Directory.Exists(_amb.Caminhos.Base));
        _ = _amb.Repositorio;
        Assert.False(Directory.Exists(_amb.Caminhos.Base));
    }

    [Fact]
    public void Registrar_GravaArquivoAtomico_SemTmp()
    {
        Assert.True(_amb.Repositorio.Registrar(Nova()));
        Assert.Single(Directory.GetFiles(_amb.Caminhos.Notas, "*.json"));
        Assert.Empty(Directory.GetFiles(_amb.Caminhos.Notas, "*.tmp"));
    }

    [Fact]
    public void Registrar_EIdempotentePorChave()
    {
        Assert.True(_amb.Repositorio.Registrar(Nova()));
        Assert.False(_amb.Repositorio.Registrar(Nova() with { Numero = "999" }));
        Assert.Equal("100", _amb.Repositorio.Obter("K1")!.Numero);
    }

    [Fact]
    public void Estado_SobreviveAoReinicio()
    {
        _amb.Repositorio.Registrar(Nova());
        _amb.Repositorio.Atualizar("K1", n => n with { Situacao = SituacaoNota.Separada, SeparadaEm = _amb.Clock.UtcNow });
        _amb.Reiniciar();
        Assert.Equal(SituacaoNota.Separada, _amb.Repositorio.Obter("K1")!.Situacao);
        Assert.Equal(5m, _amb.Repositorio.Obter("K1")!.Itens[0].QtdeOriginal);
    }

    [Fact]
    public void ArquivoCorrompido_EPreservado_ENaoDerrubaACarga()
    {
        _amb.Repositorio.Registrar(Nova("K2", "200"));
        var ruim = Path.Combine(_amb.Caminhos.Notas, "ruim.json");
        File.WriteAllText(ruim, "{nao e json");
        _amb.Reiniciar();
        Assert.Single(_amb.Repositorio.Listar());
        Assert.False(File.Exists(ruim));
        Assert.Single(Directory.GetFiles(_amb.Caminhos.Notas, "ruim.json.corrompido-*"));
    }

    [Fact]
    public void TransicaoInvalida_Lanca_ENaoAlteraOIndice()
    {
        _amb.Repositorio.Registrar(Nova());
        Assert.Throws<InvalidOperationException>(() =>
            _amb.Repositorio.Atualizar("K1", n => n with { Situacao = SituacaoNota.Associada }));
        Assert.Equal(SituacaoNota.AguardandoSeparacao, _amb.Repositorio.Obter("K1")!.Situacao);
    }

    [Fact]
    public void AtualizarGenerico_RecusaRejeitadaParaSeparada()
    {
        _amb.Repositorio.Registrar(Nova());
        _amb.Repositorio.Atualizar("K1", n => n with { Situacao = SituacaoNota.Separada });
        _amb.Repositorio.Atualizar("K1", n => n with { Situacao = SituacaoNota.Rejeitada, Motivo = MotivoI60.Outro });
        Assert.Throws<InvalidOperationException>(() =>
            _amb.Repositorio.Atualizar("K1", n => n with { Situacao = SituacaoNota.Separada }));
    }

    [Fact]
    public void Reenfileirar_E_AUnicaSaidaDeRejeitada_ELimpaContadores()
    {
        _amb.Repositorio.Registrar(Nova());
        _amb.Repositorio.Atualizar("K1", n => n with { Situacao = SituacaoNota.Separada });
        _amb.Repositorio.Atualizar("K1", n => n with
        {
            Situacao = SituacaoNota.Rejeitada, Motivo = MotivoI60.Outro, TentativasI60 = 3, TentativasOutro = 3, Mensagem = "x",
        });

        Assert.Equal(0, _amb.Repositorio.Reenfileirar("outra", null, _amb.Clock.UtcNow));
        Assert.Equal(1, _amb.Repositorio.Reenfileirar(null, MotivoI60.Outro, _amb.Clock.UtcNow));

        var n = _amb.Repositorio.Obter("K1")!;
        Assert.Equal(SituacaoNota.Separada, n.Situacao);
        Assert.Equal(0, n.TentativasI60);
        Assert.Equal(0, n.TentativasOutro);
        Assert.Equal(MotivoI60.Nenhum, n.Motivo);
        Assert.Contains("reenfileirada", n.Observacao);
    }

    [Fact]
    public void FalhaDeDisco_NaoLanca_EMantemOIndice()
    {
        _amb.Repositorio.Registrar(Nova());
        // Um DIRETÓRIO no lugar do arquivo .tmp faz a gravação falhar.
        var tmp = Path.Combine(_amb.Caminhos.Notas, "K1.json.tmp");
        Directory.CreateDirectory(tmp);

        var ok = _amb.Repositorio.Atualizar("K1", n => n with { Situacao = SituacaoNota.Separada });

        Assert.False(ok);
        Assert.Equal(SituacaoNota.AguardandoSeparacao, _amb.Repositorio.Obter("K1")!.Situacao);
    }

    [Fact]
    public void Limpar_SoApagaTerminaisVencidos_ENuncaAsAguardando()
    {
        _amb.Repositorio.Registrar(Nova("K1", "1"));
        _amb.Repositorio.Registrar(Nova("K2", "2"));
        _amb.Repositorio.Atualizar("K2", n => n with { Situacao = SituacaoNota.Separada });
        _amb.Repositorio.Atualizar("K2", n => n with { Situacao = SituacaoNota.I60Enviada, FinalizadaEm = _amb.Clock.UtcNow });

        Assert.Equal(0, _amb.Repositorio.Limpar(_amb.Clock.UtcNow));
        Assert.Equal(1, _amb.Repositorio.Limpar(_amb.Clock.UtcNow.AddDays(61)));
        Assert.Null(_amb.Repositorio.Obter("K2"));
        Assert.NotNull(_amb.Repositorio.Obter("K1"));
    }

    [Fact]
    public void Registrar_ConcorrenteNaoPerdeNotas()
    {
        Parallel.For(0, 40, i => _amb.Repositorio.Registrar(Nova("C" + i, i.ToString())));
        Assert.Equal(40, _amb.Repositorio.Listar().Count);
        Assert.Equal(40, Directory.GetFiles(_amb.Caminhos.Notas, "*.json").Length);
    }
}

public class RepositorioOrfaosTests : IDisposable
{
    private readonly AmbienteI51 _amb = new();

    public void Dispose() => _amb.Dispose();

    [Fact]
    public void Salvar_ListarERemover()
    {
        var o = new OrfaoI51 { Numero = "55", CnpjDestinatario = "11111111000111", BrutoOrigem = "i51_TODOS_20261112_120000000.json", Tipo = "PEDIDO", SubTipo = "CN" };
        Assert.True(_amb.Orfaos.Salvar(o));
        var (arquivo, lido) = Assert.Single(_amb.Orfaos.Listar());
        Assert.Equal("55", lido.Numero);
        Assert.Equal("PEDIDO", lido.Tipo);
        Assert.Contains("i51_TODOS_20261112_120000000", Path.GetFileName(arquivo));
        _amb.Orfaos.Remover(arquivo);
        Assert.Empty(_amb.Orfaos.Listar());
    }

    [Fact]
    public void Limpar_ExpiraPelaRetencao()
    {
        _amb.Orfaos.Salvar(new OrfaoI51 { Numero = "1", BrutoOrigem = "a.json" });
        Assert.Equal(0, _amb.Orfaos.Limpar(_amb.Clock.UtcNow, 60));
        Assert.Equal(1, _amb.Orfaos.Limpar(_amb.Clock.UtcNow.AddDays(61), 60));
    }
}
