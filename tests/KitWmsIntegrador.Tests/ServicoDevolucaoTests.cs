using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Construtores.Dtos;
using KitWmsIntegrador.Core.Devolucao;
using KitWmsIntegrador.Tests.Infra;

namespace KitWmsIntegrador.Tests;

public sealed class ServicoDevolucaoTests : IDisposable
{
    private readonly AmbienteDevolucao _amb = new();

    public void Dispose() => _amb.Dispose();

    private static SolicitacaoDevolucao Pedido(IEnumerable<string> chaves) =>
        new() { Chaves = chaves.ToList(), Origem = "10.0.0.5" };

    private NotaFiscalI40 NotaEnviada(int envio = 0, int indice = 0) =>
        ((PayloadI40)_amb.Cliente.Envios[envio].Payload).NotaFiscal[indice];

    [Fact]
    public async Task Enviar_MontaIMPSemPlacaNemMotorista_EMarcaEnviada()
    {
        _amb.CopiarXml(AmbienteDevolucao.Chave1);
        _amb.Cliente.Responder(InterfaceWms.I40,
            AmbienteDevolucao.Resposta("4042207", (AmbienteDevolucao.Nota1, true, "Registrado com sucesso")));

        var r = await _amb.Servico.EnviarAsync(Pedido(new[] { AmbienteDevolucao.Chave1 }));

        Assert.Equal(1, _amb.Cliente.Contar(InterfaceWms.I40));
        var nf = NotaEnviada();
        Assert.Equal("IMP", nf.TipoOperacaoNota);
        Assert.Equal(string.Empty, nf.PlacaVeiculoPrevistoNota);
        Assert.Equal(string.Empty, nf.NomeMotorista);
        Assert.Equal("DE", nf.TipoNota);
        Assert.Equal(1, r.Total);
        Assert.Equal(1, r.Enviadas);
        Assert.Equal("4042207", r.Transacao);
        var item = Assert.Single(r.Resultados);
        Assert.Equal(StatusChaveDevolucao.Enviada, item.Status);
        Assert.Equal(AmbienteDevolucao.Nota1, item.Nota);
        Assert.Equal("Registrado com sucesso", item.Mensagem);
    }

    [Fact]
    public async Task Enviar_ListaVazia_ELimiteDeChaves_LancamSolicitacaoInvalida()
    {
        await Assert.ThrowsAsync<SolicitacaoDevolucaoInvalidaException>(() =>
            _amb.Servico.EnviarAsync(Pedido(new[] { "", "  " })));

        var trinta1 = Enumerable.Range(0, 31)
            .Select(i => AmbienteDevolucao.ChaveComDv(AmbienteDevolucao.Chave1[..30] + i.ToString("D13"))).ToList();
        var ex = await Assert.ThrowsAsync<SolicitacaoDevolucaoInvalidaException>(() =>
            _amb.Servico.EnviarAsync(Pedido(trinta1)));
        Assert.Contains("30", ex.Message);
        Assert.Empty(_amb.Cliente.Envios);
    }

    [Fact]
    public async Task Enviar_Exatamente30Chaves_EhAceito_ComoLoteUnico()
    {
        var chaves = Enumerable.Range(0, 30)
            .Select(i => AmbienteDevolucao.ChaveComDv(AmbienteDevolucao.Chave1[..30] + i.ToString("D13"))).ToList();

        var r = await _amb.Servico.EnviarAsync(Pedido(chaves));

        Assert.Equal(30, r.Total);
        Assert.All(r.Resultados, x => Assert.Equal(StatusChaveDevolucao.XmlNaoEncontrado, x.Status));
        Assert.Empty(_amb.Cliente.Envios);
    }

    [Fact]
    public async Task Enviar_Duplicadas_SaoRemovidasEContadasUmaVez()
    {
        _amb.CopiarXml(AmbienteDevolucao.Chave1);
        _amb.Cliente.Responder(InterfaceWms.I40,
            AmbienteDevolucao.Resposta("9", (AmbienteDevolucao.Nota1, true, "ok")));

        var r = await _amb.Servico.EnviarAsync(Pedido(new[]
        {
            AmbienteDevolucao.Chave1, AmbienteDevolucao.Chave1, " " + AmbienteDevolucao.Chave1,
        }));

        Assert.Equal(1, r.Total);
        Assert.Single(((PayloadI40)_amb.Cliente.Envios[0].Payload).NotaFiscal);
    }

    [Fact]
    public async Task Enviar_Mistura_UmOk_UmSemXml_UmInvalido_GeraUmEnvioComUmaNota()
    {
        _amb.CopiarXml(AmbienteDevolucao.Chave1);
        _amb.Cliente.Responder(InterfaceWms.I40,
            AmbienteDevolucao.Resposta("77", (AmbienteDevolucao.Nota1, true, "Registrado com sucesso")));
        var dvErrado = AmbienteDevolucao.Chave2[..43] + (AmbienteDevolucao.Chave2[43] == '0' ? '1' : '0');
        var semXml = AmbienteDevolucao.ChaveComDv(AmbienteDevolucao.Chave1[..30] + "0000000000001");

        var r = await _amb.Servico.EnviarAsync(Pedido(new[] { dvErrado, AmbienteDevolucao.Chave1, semXml, "123" }));

        Assert.Equal(1, _amb.Cliente.Contar(InterfaceWms.I40));
        Assert.Single(((PayloadI40)_amb.Cliente.Envios[0].Payload).NotaFiscal);
        Assert.Equal(4, r.Total);
        Assert.Equal(1, r.Enviadas);
        Assert.Equal(
            new[] { StatusChaveDevolucao.ChaveInvalida, StatusChaveDevolucao.Enviada, StatusChaveDevolucao.XmlNaoEncontrado, StatusChaveDevolucao.ChaveInvalida },
            r.Resultados.Select(x => x.Status).ToArray());
        Assert.Contains("60 dias", r.Resultados[2].Mensagem);
    }

    [Fact]
    public async Task Enviar_DuasNotas_MapeiaRespostaPorDocumento_SucessoEErro()
    {
        _amb.CopiarXml(AmbienteDevolucao.Chave1);
        _amb.CopiarXml(AmbienteDevolucao.Chave2);
        // Retornos fora de ordem: o mapeamento deve ser por documento, não por posição.
        _amb.Cliente.Responder(InterfaceWms.I40, AmbienteDevolucao.Resposta("5",
            (AmbienteDevolucao.Nota2, false, "Produto 123 nao cadastrado"),
            (AmbienteDevolucao.Nota1, true, "Registrado com sucesso")));

        var r = await _amb.Servico.EnviarAsync(Pedido(new[] { AmbienteDevolucao.Chave1, AmbienteDevolucao.Chave2 }));

        Assert.Equal(1, _amb.Cliente.Contar(InterfaceWms.I40));
        Assert.Equal(2, ((PayloadI40)_amb.Cliente.Envios[0].Payload).NotaFiscal.Count);
        Assert.Equal(StatusChaveDevolucao.Enviada, r.Resultados[0].Status);
        Assert.Equal("Registrado com sucesso", r.Resultados[0].Mensagem);
        Assert.Equal(StatusChaveDevolucao.ErroWms, r.Resultados[1].Status);
        Assert.Equal("Produto 123 nao cadastrado", r.Resultados[1].Mensagem);
        Assert.Equal(1, r.Enviadas);
    }

    [Fact]
    public async Task Enviar_RetornoUnicoSemDocumento_AplicaAoLote()
    {
        _amb.CopiarXml(AmbienteDevolucao.Chave1);
        _amb.CopiarXml(AmbienteDevolucao.Chave2);
        _amb.Cliente.Responder(InterfaceWms.I40, AmbienteDevolucao.Resposta("6", ("", false, "Falha geral no lote")));

        var r = await _amb.Servico.EnviarAsync(Pedido(new[] { AmbienteDevolucao.Chave1, AmbienteDevolucao.Chave2 }));

        Assert.All(r.Resultados, x =>
        {
            Assert.Equal(StatusChaveDevolucao.ErroWms, x.Status);
            Assert.Equal("Falha geral no lote", x.Mensagem);
        });
        Assert.Equal(0, r.Enviadas);
    }

    [Fact]
    public async Task Enviar_SemRetornos_UsaOSucessoGeral()
    {
        _amb.CopiarXml(AmbienteDevolucao.Chave1);
        _amb.Cliente.Responder(InterfaceWms.I40, new RespostaWms { Transacao = "8", Sucesso = "S" });

        var r = await _amb.Servico.EnviarAsync(Pedido(new[] { AmbienteDevolucao.Chave1 }));

        Assert.Equal(StatusChaveDevolucao.Enviada, r.Resultados[0].Status);
    }

    [Fact]
    public async Task Enviar_ExcecaoDoCliente_ViraErroWmsParaAsNotasDoLote_SemLancar()
    {
        _amb.CopiarXml(AmbienteDevolucao.Chave1);
        var servico = new ServicoDevolucao(
            new ClienteQueFalha(), new KitWmsIntegrador.Core.Construtores.ConstrutorI40(
                Microsoft.Extensions.Options.Options.Create(new KitWmsIntegrador.Core.Configuracao.OpcoesOperacionais()),
                Microsoft.Extensions.Options.Options.Create(_amb.Devolucao)),
            new KitWmsIntegrador.Core.Leitura.LeitorNotaFiscalXml(),
            new AuditoriaDevolucao(new FakeClock(DateTimeOffset.UtcNow),
                Microsoft.Extensions.Options.Options.Create(_amb.Pastas),
                Microsoft.Extensions.Options.Options.Create(_amb.Devolucao),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditoriaDevolucao>.Instance),
            Microsoft.Extensions.Options.Options.Create(_amb.Pastas),
            Microsoft.Extensions.Options.Options.Create(_amb.Operacional),
            Microsoft.Extensions.Options.Options.Create(_amb.Devolucao),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ServicoDevolucao>.Instance);

        var r = await servico.EnviarAsync(Pedido(new[] { AmbienteDevolucao.Chave1 }));

        var item = Assert.Single(r.Resultados);
        Assert.Equal(StatusChaveDevolucao.ErroWms, item.Status);
        Assert.Contains("sem rede", item.Mensagem);
    }

    [Fact]
    public async Task Enviar_XmlIlegivel_ViraErroInterno_SemBloquearAsDemais()
    {
        _amb.CopiarXml(AmbienteDevolucao.Chave2);
        var pacote = Path.Combine(_amb.Processados, "pacote_20260101_100000");
        File.WriteAllText(Path.Combine(pacote, $"NFE{AmbienteDevolucao.Chave1}.XML"), "isto nao e xml");
        _amb.Cliente.Responder(InterfaceWms.I40,
            AmbienteDevolucao.Resposta("3", (AmbienteDevolucao.Nota2, true, "ok")));

        var r = await _amb.Servico.EnviarAsync(Pedido(new[] { AmbienteDevolucao.Chave1, AmbienteDevolucao.Chave2 }));

        Assert.Equal(StatusChaveDevolucao.ErroInterno, r.Resultados[0].Status);
        Assert.Equal(StatusChaveDevolucao.Enviada, r.Resultados[1].Status);
    }

    [Fact]
    public async Task Enviar_GravaAuditoriaSemSegredos()
    {
        _amb.CopiarXml(AmbienteDevolucao.Chave1);
        _amb.Cliente.Responder(InterfaceWms.I40,
            AmbienteDevolucao.Resposta("4042207", (AmbienteDevolucao.Nota1, true, "Registrado; com sucesso")));

        await _amb.Servico.EnviarAsync(Pedido(new[] { AmbienteDevolucao.Chave1 }));

        var arquivo = Assert.Single(Directory.GetFiles(_amb.Auditoria, "devolucao_*.log"));
        var linha = Assert.Single(File.ReadAllLines(arquivo));
        var campos = linha.Split(';');
        Assert.Equal(7, campos.Length);
        Assert.Equal("10.0.0.5", campos[1]);
        Assert.Equal(AmbienteDevolucao.Chave1, campos[2]);
        Assert.Equal(AmbienteDevolucao.Nota1, campos[3]);
        Assert.Equal("Enviada", campos[4]);
        Assert.Equal("4042207", campos[5]);
        Assert.Equal("Registrado, com sucesso", campos[6]);
        Assert.DoesNotContain("NOTA_FISCAL", linha);
        Assert.DoesNotContain("MOTORISTA", linha);
    }

    private sealed class ClienteQueFalha : IClienteWms
    {
        public Task<RespostaWms> EnviarAsync(InterfaceWms identificador, object payload, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("sem rede");

        public Task<RespostaWms> EnviarAsync(InterfaceWms identificador, string jsonBruto, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("sem rede");

        public ResultadoWms Classificar(RetornoWms retorno) => ResultadoWms.Rejeitado;
    }
}
