using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Orquestrador;

namespace KitWmsIntegrador.Tests;

public class ClassificadorRetornoTests
{
    private static readonly ClassificadorRetorno Classificador = new();

    private static RespostaWms Resposta(string sucesso, params string[] mensagens) => new()
    {
        Sucesso = sucesso,
        Retornos = mensagens.Select(m => new RetornoWms { Mensagem = m }).ToArray(),
    };

    [Fact]
    public void EhSucesso_QuandoSucessoS()
    {
        var resposta = Resposta("S", "Registrado com sucesso");

        Assert.True(Classificador.EhSucesso(resposta));
    }

    [Fact]
    public void EhSucesso_QuandoMensagemRegistradoComSucesso()
    {
        var resposta = Resposta("N", "Produto 175894 Registrado com sucesso.");

        Assert.True(Classificador.EhSucesso(resposta));
    }

    [Fact]
    public void EhEmpresaNaoLocalizada_TextoReal()
    {
        var resposta = Resposta("N", "Destinataria - Empresa não localizada, sigla não informada.");

        Assert.True(Classificador.EhEmpresaNaoLocalizada(resposta));
        Assert.False(Classificador.EhSucesso(resposta));
        Assert.False(Classificador.EhProdutoNaoCadastrado(resposta));
    }

    [Fact]
    public void EhProdutoNaoCadastrado_TextoReal()
    {
        var resposta = Resposta(
            "N",
            "Produto Cod\\EAN: \"175886\" da empresa \"3509308-01.838.723/0098-50\" não ativo ou faltando ou não cadastrado!");

        Assert.True(Classificador.EhProdutoNaoCadastrado(resposta));
        Assert.False(Classificador.EhEmpresaNaoLocalizada(resposta));
    }

    [Fact]
    public void EhPendencia_TextoReal()
    {
        var resposta = Resposta("N", "Foi importado e esta pendente na interface.");

        Assert.True(Classificador.EhPendencia(resposta));
    }

    [Fact]
    public void PreCondicaoOk_JaExiste_I10()
    {
        var resposta = Resposta(
            "N",
            "CodEmpresa: -28.288.914/0001-19- já existe no cadastro. Utilizar o comando de alteracao(U).");

        Assert.True(Classificador.PreCondicaoOk(resposta));
    }

    [Fact]
    public void PreCondicaoOk_JaExiste_I30()
    {
        var resposta = Resposta("N", "Produto 175894 já existe.");

        Assert.True(Classificador.PreCondicaoOk(resposta));
    }

    [Fact]
    public void Classificacao_Rejeitado_EstruturaMalFormada()
    {
        var resposta = Resposta("N", "Mensagem EDI com estrutura mal-formada.");

        Assert.False(Classificador.EhSucesso(resposta));
        Assert.False(Classificador.EhEmpresaNaoLocalizada(resposta));
        Assert.False(Classificador.EhProdutoNaoCadastrado(resposta));
        Assert.False(Classificador.EhPendencia(resposta));
        Assert.False(Classificador.PreCondicaoOk(resposta));
    }

    [Fact]
    public void PrimeiraMensagem_RetornaPrimeiraNaoVazia()
    {
        var resposta = Resposta("N", "", "mensagem relevante");

        Assert.Equal("mensagem relevante", Classificador.PrimeiraMensagem(resposta));
    }
}
