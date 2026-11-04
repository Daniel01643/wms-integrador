using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Modelos;
using KitWmsIntegrador.Core.Orquestrador;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>Captura de transação, idDocumento e produtos provisórios (Etapa 8), sem mudar decisões.</summary>
public class OrquestradorCapturaTests
{
    private static RespostaWms Resp(string sucesso, string msg, string transacao, string idDoc = "") => new()
    {
        TipoEDI = "I50",
        Sucesso = sucesso,
        Transacao = transacao,
        Retornos = new[] { new RetornoWms { Sucesso = sucesso, Mensagem = msg, IdDocumento = idDoc } },
    };

    private static NotaFiscal Nota() => new()
    {
        Numero = "15665807",
        Serie = "6",
        DestinatarioCnpj = "14.605.065/0001-75",
        NumeroTransporte = "30288",
        Itens = new[]
        {
            new ItemNota { CodigoProduto = "A1", Descricao = "P1", Unidade = "CX", Quantidade = 1, Cfop = "5102" },
            new ItemNota { CodigoProduto = "B2", Descricao = "P2", Unidade = "CX", Quantidade = 1, Cfop = "5102" },
            new ItemNota { CodigoProduto = "A1", Descricao = "P1 de novo", Unidade = "CX", Quantidade = 1, Cfop = "5102" },
        },
    };

    private static Orquestrador Criar(FakeClienteWms cliente)
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
            new FakeClock(DateTimeOffset.UtcNow),
            NullLogger<Orquestrador>.Instance);
    }

    [Fact]
    public async Task Sucesso_CapturaTransacaoEIdDocumentoDaUltimaI50()
    {
        var cliente = new FakeClienteWms().Responder(InterfaceWms.I50, Resp("S", "Registrado com sucesso.", "4019964", "1427353"));

        var r = Assert.Single(await Criar(cliente).IntegrarAsync(new[] { Nota() }, "30288"));

        Assert.Equal("4019964", r.IdTransacao);
        Assert.Equal("1427353", r.IdDocumento);
        Assert.Empty(r.ProdutosProvisorios);
        Assert.False(r.RevisaoProdutoPendente);
    }

    [Fact]
    public async Task NaoIntegrada_CapturaIdsDaUltimaChamada()
    {
        var cliente = new FakeClienteWms().Responder(InterfaceWms.I50,
            Resp("N", "Mensagem EDI com estrutura mal-formada. Processo Abortado.", "4019980"));

        var r = Assert.Single(await Criar(cliente).IntegrarAsync(new[] { Nota() }, "30288"));

        Assert.False(r.Integrada);
        Assert.Equal(EstadoIntegracao.NaoIntegrada, r.Estado);
        Assert.Equal("4019980", r.IdTransacao);
        Assert.Equal(string.Empty, r.IdDocumento);
    }

    [Fact]
    public async Task ProdutoNaoCadastrado_I30_Sucesso_GeraProdutosProvisoriosDistintos()
    {
        var cliente = new FakeClienteWms()
            .Responder(InterfaceWms.I50,
                Resp("N", "Produto Cod\\EAN: \"A1\" da empresa \"3509308\" não ativo ou faltando ou não cadastrado!", "1"),
                Resp("S", "Registrado com sucesso.", "2", "99"))
            .Responder(InterfaceWms.I30, Resp("S", "Registrado com sucesso.", "30"));

        var r = Assert.Single(await Criar(cliente).IntegrarAsync(new[] { Nota() }, "30288"));

        Assert.True(r.Integrada);
        Assert.Equal(EstadoIntegracao.Pendente, r.Estado);
        Assert.Equal(new[] { "A1", "B2" }, r.ProdutosProvisorios);
        Assert.True(r.RevisaoProdutoPendente);
        Assert.Equal("2", r.IdTransacao);
        Assert.Equal("99", r.IdDocumento);
    }
}
