using KitWmsIntegrador.Core.Agrupamento;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Construtores.Dtos;
using KitWmsIntegrador.Core.Modelos;
using KitWmsIntegrador.Core.Orquestrador;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class OrquestradorTests
{
    private const string Transporte = "30288";

    // Respostas com os textos reais do WMS (docs/TESTE-COMUNICACAO.md).
    private static RespostaWms Sucesso() => new()
    {
        TipoEDI = "I50",
        Sucesso = "S",
        Retornos = new[]
        {
            new RetornoWms { Sucesso = "S", Mensagem = "Registrado com sucesso." },
        },
    };

    private static RespostaWms EmpresaNaoLocalizada() => new()
    {
        TipoEDI = "I50",
        Sucesso = "N",
        Retornos = new[]
        {
            new RetornoWms
            {
                Sucesso = "N",
                Mensagem = "Destinataria - Empresa não localizada, sigla não informada.",
            },
        },
    };

    private static RespostaWms ProdutoNaoCadastrado() => new()
    {
        TipoEDI = "I50",
        Sucesso = "N",
        Retornos = new[]
        {
            new RetornoWms
            {
                Sucesso = "N",
                Mensagem = "Produto Cod\\EAN: \"175886\" da empresa \"3509308\" não ativo ou faltando ou não cadastrado!",
            },
        },
    };

    private static RespostaWms Pendencia() => new()
    {
        TipoEDI = "I50",
        Sucesso = "N",
        Retornos = new[]
        {
            new RetornoWms { Sucesso = "N", Mensagem = "Foi importado e esta pendente na interface." },
        },
    };

    private static RespostaWms JaExiste() => new()
    {
        Sucesso = "N",
        Retornos = new[]
        {
            new RetornoWms { Sucesso = "N", Mensagem = "Registro já existe no cadastro." },
        },
    };

    private static RespostaWms Rejeitado() => new()
    {
        TipoEDI = "I50",
        Sucesso = "N",
        Retornos = new[]
        {
            new RetornoWms { Sucesso = "N", Mensagem = "Mensagem EDI com estrutura mal-formada. Processo Abortado." },
        },
    };

    private static NotaFiscal Nota(string numero = "15665807", string? transporte = Transporte, int itens = 2)
    {
        var lista = new List<ItemNota>(itens);
        for (var i = 0; i < itens; i++)
        {
            lista.Add(new ItemNota
            {
                CodigoProduto = $"0017588{i}",
                Descricao = $"PRODUTO {i}",
                Unidade = "CX",
                Quantidade = 1,
                Cfop = "5102",
            });
        }

        return new NotaFiscal
        {
            Numero = numero,
            Serie = "6",
            DestinatarioCnpj = "14.605.065/0001-75",
            NumeroTransporte = transporte,
            Itens = lista,
        };
    }

    private static OpcoesOperacionais Operacional() => new()
    {
        Deposito = "JD",
        CnpjArmazem = "14.605.065/0001-75",
        CnpjDepositante = "01.838.723/0098-50",
        CodigoDepositante = "3509308",
    };

    private static OpcoesI50 I50Opcoes() => new();

    private static Orquestrador Criar(FakeClienteWms cliente, bool i60Habilitada = false)
    {
        var op = Options.Create(Operacional());
        var construtorI50 = new ConstrutorI50(op, Options.Create(I50Opcoes()));
        var construtorI10 = new ConstrutorI10(op);
        var construtorI30 = new ConstrutorI30(op);
        var construtorI60 = new ConstrutorI60(op);

        return new Orquestrador(
            cliente,
            construtorI50,
            construtorI10,
            construtorI30,
            construtorI60,
            new ClassificadorRetorno(),
            Options.Create(new OpcoesI60 { Habilitada = i60Habilitada }),
            new FakeClock(DateTimeOffset.UtcNow),
            NullLogger<Orquestrador>.Instance);
    }

    // (1) I50 sucesso -> integrada sem I60 (flag off), Tentativas==1.
    [Fact]
    public async Task I50Sucesso_NotaIntegrada_SemI60_QuandoFlagDesligada()
    {
        var cliente = new FakeClienteWms().Responder(InterfaceWms.I50, Sucesso());
        var orq = Criar(cliente, i60Habilitada: false);

        var resultado = Assert.Single(await orq.IntegrarAsync(new[] { Nota() }, Transporte));

        Assert.True(resultado.Integrada);
        Assert.Equal(EstadoIntegracao.Integrada, resultado.Estado);
        Assert.Equal(1, resultado.Tentativas);
        Assert.Equal(0, cliente.Contar(InterfaceWms.I60));
        Assert.Equal(1, cliente.Contar(InterfaceWms.I50));
    }

    // (2) Flag I60 on -> envia I60 após I50 sucesso.
    [Fact]
    public async Task I50Sucesso_ComFlagI60Ligada_EnviaI60()
    {
        var cliente = new FakeClienteWms()
            .Responder(InterfaceWms.I50, Sucesso())
            .Responder(InterfaceWms.I60, Sucesso());
        var orq = Criar(cliente, i60Habilitada: true);

        var resultado = Assert.Single(await orq.IntegrarAsync(new[] { Nota() }, Transporte));

        Assert.True(resultado.Integrada);
        Assert.Equal(1, cliente.Contar(InterfaceWms.I60));
        Assert.Equal(InterfaceWms.I50, cliente.Envios[0].Identificador);
        Assert.Equal(InterfaceWms.I60, cliente.Envios[1].Identificador);
    }

    // (3) Empresa não localizada -> I10 'já existe' -> reenvia I50 sucesso (Tentativas==2, ordem I50,I10,I50).
    [Fact]
    public async Task EmpresaNaoLocalizada_EnviaI10_DepoisReenviaI50_Integra()
    {
        var cliente = new FakeClienteWms()
            .Responder(InterfaceWms.I50, EmpresaNaoLocalizada(), Sucesso())
            .Responder(InterfaceWms.I10, JaExiste());
        var orq = Criar(cliente);

        var resultado = Assert.Single(await orq.IntegrarAsync(new[] { Nota() }, Transporte));

        Assert.True(resultado.Integrada);
        Assert.Equal(2, resultado.Tentativas);
        Assert.Collection(
            cliente.Envios,
            e => Assert.Equal(InterfaceWms.I50, e.Identificador),
            e => Assert.Equal(InterfaceWms.I10, e.Identificador),
            e => Assert.Equal(InterfaceWms.I50, e.Identificador));
    }

    // (4) Produto não cadastrado -> I30 com TODOS os itens -> reenvia I50 sucesso (Tentativas==2).
    [Fact]
    public async Task ProdutoNaoCadastrado_EnviaI30DeTodosOsItens_DepoisReenviaI50_Integra()
    {
        var cliente = new FakeClienteWms()
            .Responder(InterfaceWms.I50, ProdutoNaoCadastrado(), Sucesso())
            .Responder(InterfaceWms.I30, JaExiste());
        var nota = Nota(itens: 2);
        var orq = Criar(cliente);

        var resultado = Assert.Single(await orq.IntegrarAsync(new[] { nota }, Transporte));

        Assert.True(resultado.Integrada);
        Assert.Equal(2, resultado.Tentativas);
        Assert.Equal(1, cliente.Contar(InterfaceWms.I30));

        var envioI30 = cliente.Envios.Single(e => e.Identificador == InterfaceWms.I30);
        var payload = Assert.IsType<PayloadI30>(envioI30.Payload);
        Assert.Equal(nota.Itens.Count, payload.Produto.Count);
    }

    // (5) Produto não cadastrado registra pendência de revisão (mesmo integrando depois).
    [Fact]
    public async Task ProdutoNaoCadastrado_RegistraPendenciaDeRevisao()
    {
        var cliente = new FakeClienteWms()
            .Responder(InterfaceWms.I50, ProdutoNaoCadastrado(), Sucesso())
            .Responder(InterfaceWms.I30, JaExiste());
        var orq = Criar(cliente);

        var resultado = Assert.Single(await orq.IntegrarAsync(new[] { Nota() }, Transporte));

        Assert.True(resultado.Integrada);
        Assert.Equal(EstadoIntegracao.Pendente, resultado.Estado);
        Assert.Contains(resultado.Historico, h => h.Contains("revisão", StringComparison.OrdinalIgnoreCase));
    }

    // (6) Pendência na interface -> Estado=Pendente, não trava o lote.
    [Fact]
    public async Task Pendencia_NaInterface_TrataComoPendente_NaoTravaLote()
    {
        var cliente = new FakeClienteWms().Responder(InterfaceWms.I50, Pendencia());
        var orq = Criar(cliente);

        var resultado = Assert.Single(await orq.IntegrarAsync(new[] { Nota() }, Transporte));

        Assert.False(resultado.Integrada);
        Assert.Equal(EstadoIntegracao.Pendente, resultado.Estado);
        Assert.Equal(1, resultado.Tentativas);
    }

    // (7) 3 tentativas sem sucesso -> NaoIntegrada com motivo e Tentativas==3.
    [Fact]
    public async Task TresTentativasSemSucesso_NaoIntegrada_RegistraMotivoEContagem()
    {
        // Produto persiste não cadastrado nas 3 tentativas (fila esgota e repete a última).
        var cliente = new FakeClienteWms()
            .Responder(InterfaceWms.I50, ProdutoNaoCadastrado())
            .Responder(InterfaceWms.I30, JaExiste());
        var orq = Criar(cliente);

        var resultado = Assert.Single(await orq.IntegrarAsync(new[] { Nota() }, Transporte));

        Assert.False(resultado.Integrada);
        Assert.Equal(EstadoIntegracao.NaoIntegrada, resultado.Estado);
        Assert.Equal(Orquestrador.MaxTentativas, resultado.Tentativas);
        Assert.False(string.IsNullOrWhiteSpace(resultado.UltimaMensagem));
        Assert.Equal(3, cliente.Contar(InterfaceWms.I50));
    }

    // (8) Outro erro -> registra e segue (sem loop).
    [Fact]
    public async Task OutroErro_RegistraESegue()
    {
        var cliente = new FakeClienteWms().Responder(InterfaceWms.I50, Rejeitado());
        var orq = Criar(cliente);

        var resultado = Assert.Single(await orq.IntegrarAsync(new[] { Nota() }, Transporte));

        Assert.False(resultado.Integrada);
        Assert.Equal(EstadoIntegracao.NaoIntegrada, resultado.Estado);
        Assert.Equal(1, resultado.Tentativas);
        Assert.Equal(1, cliente.Contar(InterfaceWms.I50));
    }

    // (9) Lote com múltiplas notas: falha de uma não trava a outra; um resultado por nota.
    [Fact]
    public async Task LoteComMultiplasNotas_UmaFalhaNaoTravaAsOutras()
    {
        // 1ª nota: rejeitada na 1ª I50; 2ª nota: sucesso. Fila de I50 compartilhada
        // atende na ordem de processamento (nota1 -> rejeitado, nota2 -> sucesso).
        var cliente = new FakeClienteWms().Responder(InterfaceWms.I50, Rejeitado(), Sucesso());
        var onda = new Onda
        {
            NumeroTransporte = Transporte,
            Notas = new[] { Nota("111"), Nota("222") },
        };
        var orq = Criar(cliente);

        var resultados = await orq.IntegrarAsync(onda);

        Assert.Equal(2, resultados.Count);
        Assert.False(resultados[0].Integrada);
        Assert.True(resultados[1].Integrada);
    }

    // (10) Nenhuma chamada HTTP real — garantido pelo fake (nenhuma rede neste teste).
    [Fact]
    public async Task NaoEnviaHttpReal()
    {
        var cliente = new FakeClienteWms().Responder(InterfaceWms.I50, Sucesso());
        var orq = Criar(cliente);

        await orq.IntegrarAsync(new[] { Nota() }, Transporte);

        // Toda comunicação passou pelo fake (sem rede): há envios gravados e
        // nenhum deles saiu de um HttpClient real.
        Assert.NotEmpty(cliente.Envios);
        Assert.All(cliente.Envios, e => Assert.NotNull(e.Payload));
    }

    // (11) Caso B: nota sem transporte -> não-integrada registrada, sem lançar.
    [Fact]
    public async Task CasoB_NotaSemTransporte_NaoIntegradaRegistrada()
    {
        var cliente = new FakeClienteWms().Responder(InterfaceWms.I50, Sucesso());
        var orq = Criar(cliente);

        // Transporte vazio na onda e na nota -> Caso B.
        var resultado = Assert.Single(await orq.IntegrarAsync(new[] { Nota(transporte: null) }, string.Empty));

        Assert.False(resultado.Integrada);
        Assert.Equal(EstadoIntegracao.NaoIntegrada, resultado.Estado);
        Assert.Equal(0, cliente.Contar(InterfaceWms.I50));
        Assert.False(string.IsNullOrWhiteSpace(resultado.UltimaMensagem));
    }
}
