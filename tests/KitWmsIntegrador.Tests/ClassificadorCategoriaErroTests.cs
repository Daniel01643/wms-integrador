using KitWmsIntegrador.Core.Orquestrador;
using KitWmsIntegrador.Core.Pacote;

namespace KitWmsIntegrador.Tests;

public class ClassificadorCategoriaErroTests
{
    private readonly ClassificadorCategoriaErro _sut = new(new ClassificadorRetorno());

    private static ResultadoIntegracaoNota Res(
        string mensagem, bool integrada = false, int tentativas = 3,
        EstadoIntegracao estado = EstadoIntegracao.NaoIntegrada) =>
        new("1", "T1", integrada, tentativas, estado, mensagem, Array.Empty<string>());

    [Fact]
    public void XmlInvalido() =>
        Assert.Equal(CategoriaErro.XmlInvalido, _sut.ClassificarXmlInvalido());

    [Fact]
    public void SemTransporte() =>
        Assert.Equal(CategoriaErro.SemTransporte,
            _sut.Classificar(Res("Nota sem número de transporte (nroagrupseparacao) — não é possível montar a I50.", tentativas: 0)));

    [Fact]
    public void ProdutoNaoCadastrado() =>
        Assert.Equal(CategoriaErro.ProdutoNaoCadastrado,
            _sut.Classificar(Res("Produto X não ativo ou faltando ou não cadastrado")));

    [Fact]
    public void EmpresaNaoLocalizada() =>
        Assert.Equal(CategoriaErro.EmpresaNaoLocalizada,
            _sut.Classificar(Res("Empresa não localizada: 123")));

    [Fact]
    public void TentativasEsgotadas() =>
        Assert.Equal(CategoriaErro.TentativasEsgotadas,
            _sut.Classificar(Res("erro qualquer", tentativas: Orquestrador.MaxTentativas)));

    [Fact]
    public void Outro() =>
        Assert.Equal(CategoriaErro.Outro, _sut.Classificar(Res("erro qualquer", tentativas: 1)));

    [Fact]
    public void MensagemTemPrioridadeSobreTentativas() =>
        Assert.Equal(CategoriaErro.ProdutoNaoCadastrado,
            _sut.Classificar(Res("não ativo ou faltando ou não cadastrado", tentativas: Orquestrador.MaxTentativas)));

    [Fact]
    public void DestinoIntegradaVaiParaProcessados() =>
        Assert.Equal(DestinoArquivo.Processados,
            _sut.DestinoDe(Res("ok", integrada: true, tentativas: 1, estado: EstadoIntegracao.Integrada)));

    [Fact]
    public void DestinoHibridoVaiParaProcessados() =>
        Assert.Equal(DestinoArquivo.Processados,
            _sut.DestinoDe(Res("ok", integrada: true, tentativas: 1, estado: EstadoIntegracao.Pendente)));

    [Fact]
    public void DestinoPendenteVaiParaProcessados() =>
        Assert.Equal(DestinoArquivo.Processados,
            _sut.DestinoDe(Res("pendente na interface", estado: EstadoIntegracao.Pendente)));

    [Fact]
    public void DestinoJaExisteVaiParaProcessados() =>
        Assert.Equal(DestinoArquivo.Processados, _sut.DestinoDe(Res("Documento já existe")));

    [Fact]
    public void DestinoNaoIntegradaVaiParaErro() =>
        Assert.Equal(DestinoArquivo.Erro, _sut.DestinoDe(Res("falhou")));

    [Fact]
    public void OverloadsStringDoClassificadorRetorno()
    {
        var c = new ClassificadorRetorno();
        Assert.True(c.EhJaExiste("Já Existe"));
        Assert.False(c.EhJaExiste((string?)null));
        Assert.True(c.EhEmpresaNaoLocalizada("empresa não localizada"));
        Assert.True(c.EhProdutoNaoCadastrado("não ativo ou faltando ou não cadastrado"));
    }
}
