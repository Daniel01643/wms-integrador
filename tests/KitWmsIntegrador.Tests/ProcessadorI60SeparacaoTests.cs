using System.Net;
using System.Net.Http;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Separacao;
using KitWmsIntegrador.Tests.Infra;

namespace KitWmsIntegrador.Tests;

public class ProcessadorI60SeparacaoTests : IDisposable
{
    private readonly AmbienteI51 _amb = new();

    public void Dispose() => _amb.Dispose();

    private static FakeWmsI51.ItemDoc It(int seq, string cod, decimal q, decimal sep) => new(seq, cod, q, sep);

    // Deixa a nota 100 Separada SEM enviar a I60 (para exercitar o processador isoladamente).
    private async Task<NotaSeparacao> SepararSemI60(
        (string Cod, decimal Qtde)[] itens, FakeWmsI51.ItemDoc[] separado, int numero = 100, int volumes = 1)
    {
        _amb.RegistrarNota(numero, itens);
        _amb.Wms.Separar(numero.ToString(), AmbienteI51.CnpjDestinatario, separado, volumes);
        var habilitada = _amb.I60.Habilitada;
        _amb.I60.Habilitada = false;
        await _amb.Servico.CicloAsync(CancellationToken.None);
        _amb.I60.Habilitada = habilitada;
        var nota = _amb.Nota(numero);
        Assert.Equal(SituacaoNota.Separada, nota.Situacao);
        return nota;
    }

    private Task<ResumoI60> Processar() => _amb.ProcessadorI60.ProcessarPendentesAsync(CancellationToken.None);

    private static string Op(Core.Construtores.Dtos.PayloadI60 p) => p.NotasVenda[0].TipoOperacaoNota;

    [Fact]
    public async Task AjustaAQuantidadeParaASeparada_NosTresCampos_ERecalculaTotalEVolume()
    {
        await SepararSemI60(new[] { ("A", 10m), ("B", 5m) }, new[] { It(1, "A", 10, 8), It(2, "B", 5, 5) });

        await Processar();

        var nv = Assert.Single(_amb.Wms.EnviosI60).NotasVenda[0];
        Assert.Equal(8m, nv.ItemNotaVenda[0].QtdeProduto);
        Assert.Equal(8m, nv.ItemNotaVenda[0].QtdeProdutoConfirmada);
        Assert.Equal(8m, nv.ItemNotaVenda[0].QtdeEmbalagem);
        Assert.Equal(13m, nv.QtdeTotalNota);
        Assert.Equal(13m, nv.QtdeVolume);
        Assert.Equal("INE", nv.TipoOperacaoNota);
        Assert.Equal("INE", nv.Tpsit);
        Assert.Equal("I", nv.Tpacao);
        Assert.Equal(new[] { "100" }, nv.Pedidos);

        var n = _amb.Nota(100);
        Assert.Equal(10m, n.Itens[0].QtdeOriginal);
        Assert.Equal(8m, n.Itens[0].QtdeSeparada);
        Assert.Equal(-2m, n.Itens[0].Diferenca);
    }

    [Fact]
    public async Task OperacaoNormal_ESempreIne_NuncaImb()
    {
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        await Processar();
        Assert.All(_amb.Wms.EnviosI60, p => Assert.NotEqual("IMB", Op(p)));
        Assert.Equal("INE", Op(_amb.Wms.EnviosI60[0]));
    }

    [Fact]
    public async Task ItemSemRetorno_OmitirRemoveDaI60_ZerarEnviaZero()
    {
        _amb.I51.ItemSemRetorno = "Omitir";
        await SepararSemI60(new[] { ("A", 10m), ("B", 5m) }, new[] { It(1, "A", 10, 10) });
        await Processar();
        var itens = _amb.Wms.EnviosI60[0].NotasVenda[0].ItemNotaVenda;
        Assert.Single(itens);
        Assert.Equal(1, itens[0].NumeroSequencialItem);
        Assert.Equal(10m, _amb.Wms.EnviosI60[0].NotasVenda[0].QtdeTotalNota);
    }

    [Fact]
    public async Task ItemSemRetorno_Zerar()
    {
        _amb.I51.ItemSemRetorno = "Zerar";
        await SepararSemI60(new[] { ("A", 10m), ("B", 5m) }, new[] { It(1, "A", 10, 10) });
        await Processar();
        var itens = _amb.Wms.EnviosI60[0].NotasVenda[0].ItemNotaVenda;
        Assert.Equal(2, itens.Count);
        Assert.Equal(0m, itens[1].QtdeProduto);
        Assert.True(_amb.Nota(100).Itens[1].SemRetorno);
    }

    [Fact]
    public async Task SomaSeparadaZero_NaoEnvia_ERejeita()
    {
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 0) });
        await Processar();
        Assert.Equal(0, _amb.Wms.ChamadasI60);
        var n = _amb.Nota(100);
        Assert.Equal(SituacaoNota.Rejeitada, n.Situacao);
        Assert.Equal(MotivoI60.SemQuantidadeSeparada, n.Motivo);
    }

    // ---- ENC -> IMP ----

    [Fact]
    public async Task Enc_RecusaDaIne_ReenviaComoImp_ERegistraOErro()
    {
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 9) });
        _amb.Wms.ResponderI60 = (p, _) => Op(p) == "INE"
            ? FakeWmsI51.Recusa("Doccarga encerrado, nao e possivel associar a nota (status ENC).")
            : FakeWmsI51.Pendente();

        var resumo = await Processar();

        Assert.Equal(2, _amb.Wms.ChamadasI60);
        var imp = _amb.Wms.EnviosI60[1].NotasVenda[0];
        Assert.Equal("IMP", imp.TipoOperacaoNota);
        Assert.Equal("IMP", imp.Tpsit);
        Assert.Equal("UPD", imp.Tpacao);
        Assert.Equal(9m, imp.ItemNotaVenda[0].QtdeProduto); // mesma NF-e ajustada
        var n = _amb.Nota(100);
        Assert.Equal(SituacaoNota.I60Enviada, n.Situacao);
        Assert.Equal("IMP", n.OperacaoI60);
        Assert.Equal("UPD", n.TpAcaoI60);
        Assert.Equal(MotivoI60.DocCargaEncerrado, n.Motivo);
        Assert.Contains("encerrado", n.Mensagem, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, resumo.ImportadasImp);
    }

    [Fact]
    public async Task Enc_ComoRessalvaDeRespostaPendente_TambemReenviaComoImp()
    {
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        _amb.Wms.ResponderI60 = (p, _) => Op(p) == "INE"
            ? FakeWmsI51.PendenteComRessalva("Doccarga encerrado")
            : FakeWmsI51.Sucesso();

        await Processar();

        Assert.Equal("IMP", Op(_amb.Wms.EnviosI60[1]));
        var n = _amb.Nota(100);
        Assert.Equal(SituacaoNota.I60Enviada, n.Situacao); // IMP nunca vira Associada
        Assert.Equal(MotivoI60.DocCargaEncerrado, n.Motivo);
    }

    [Fact]
    public async Task Enc_TpAcaoDoReenvioEConfiguravel_ComI_PedidoDeUpdGeraUmReenvioUpd()
    {
        _amb.I60.TpAcaoReenvioImp = "I";
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        _amb.Wms.ResponderI60 = (p, n) =>
        {
            var nv = p.NotasVenda[0];
            if (nv.TipoOperacaoNota == "INE")
            {
                return FakeWmsI51.Recusa("Doccarga encerrado");
            }

            return nv.Tpacao == "I" ? FakeWmsI51.Recusa("Nota [x] já existe, TpAcao precisa ser UPD!") : FakeWmsI51.Sucesso();
        };

        await Processar();

        Assert.Equal(new[] { ("INE", "I"), ("IMP", "I"), ("IMP", "UPD") },
            _amb.Wms.EnviosI60.Select(p => (Op(p), p.NotasVenda[0].Tpacao)));
        Assert.Equal(SituacaoNota.I60Enviada, _amb.Nota(100).Situacao);
        Assert.Equal("UPD", _amb.Nota(100).TpAcaoI60);
    }

    [Fact]
    public async Task Enc_ImpQueAindaPedeUpd_RejeitaComoJaExiste_SemReenvioInfinito()
    {
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        _amb.Wms.ResponderI60 = (p, _) => Op(p) == "INE"
            ? FakeWmsI51.Recusa("Doccarga encerrado")
            : FakeWmsI51.Recusa("Nota [x] já existe, TpAcao precisa ser UPD!");

        await Processar();

        Assert.Equal(2, _amb.Wms.ChamadasI60);
        Assert.Equal(MotivoI60.JaExiste, _amb.Nota(100).Motivo);
        Assert.Equal(SituacaoNota.Rejeitada, _amb.Nota(100).Situacao);
    }

    [Fact]
    public async Task NoMaximoQuatroChamadasPorNota()
    {
        _amb.I60.TpAcaoReenvioImp = "I";
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        _amb.Wms.ResponderI60 = (p, _) =>
        {
            if (Op(p) == "INE")
            {
                return p.NotasVenda[0].Tpacao == "I"
                    ? FakeWmsI51.Recusa("Nota [x] já existe, TpAcao precisa ser UPD!")
                    : FakeWmsI51.Recusa("Doccarga encerrado");
            }

            return FakeWmsI51.Recusa("Nota [x] já existe, TpAcao precisa ser UPD!");
        };

        await Processar();

        Assert.Equal(4, _amb.Wms.ChamadasI60);
        Assert.Equal(MotivoI60.JaExiste, _amb.Nota(100).Motivo);
    }

    // ---- Resposta real do homolog: pendente + UPD ----

    [Fact]
    public async Task PendenteMaisUpd_NuncaViraI60EnviadaSemOReenvioUpd()
    {
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        _amb.Wms.ResponderI60 = (p, _) => p.NotasVenda[0].Tpacao == "I"
            ? FakeWmsI51.PendenteComRessalva("Nota [123] já existe, TpAcao precisa ser UPD!")
            : FakeWmsI51.Pendente();

        await Processar();

        Assert.Equal(new[] { "I", "UPD" }, _amb.Wms.EnviosI60.Select(p => p.NotasVenda[0].Tpacao));
        var n = _amb.Nota(100);
        Assert.Equal(SituacaoNota.I60Enviada, n.Situacao);
        Assert.Equal(MotivoI60.PendenteNaInterface, n.Motivo);
        Assert.Equal("UPD", n.TpAcaoI60);
    }

    [Fact]
    public async Task ExigeUpd_ComReenvioDesligado_RejeitaComoJaExiste()
    {
        _amb.I60.ReenviarComUpdSeJaExiste = false;
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.PendenteComRessalva("Nota [1] já existe, TpAcao precisa ser UPD!");
        await Processar();
        Assert.Equal(1, _amb.Wms.ChamadasI60);
        Assert.Equal(MotivoI60.JaExiste, _amb.Nota(100).Motivo);
    }

    // ---- Classificação do resultado ----

    [Fact]
    public async Task SucessoReal_I60EnviadaEAssociada()
    {
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        await Processar();
        var n = _amb.Nota(100);
        Assert.Equal(SituacaoNota.Associada, n.Situacao);
        Assert.Equal(MotivoI60.Nenhum, n.Motivo);
        Assert.Equal("INE", n.OperacaoI60);
        Assert.NotNull(n.I60EnviadaEm);
        Assert.False(string.IsNullOrEmpty(n.TransacaoI60));
    }

    [Fact]
    public async Task PadroesAssociadaSucesso_ConfiguraOSinalDeAssociada()
    {
        _amb.I60.PadroesAssociadaSucesso = "Aceito pelo WMS";
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.Resp("N", "Aceito pelo WMS");
        await Processar();
        Assert.Equal(SituacaoNota.Associada, _amb.Nota(100).Situacao);
    }

    [Fact]
    public async Task PendenteSemRessalva_FicaI60Enviada_NaoAssociada()
    {
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.Pendente();
        await Processar();
        Assert.Equal(SituacaoNota.I60Enviada, _amb.Nota(100).Situacao);
        Assert.Equal(MotivoI60.PendenteNaInterface, _amb.Nota(100).Motivo);
    }

    [Fact]
    public async Task PendenteComRessalhaNaoBloqueante_FicaI60EnviadaComRessalva()
    {
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.PendenteComRessalva("Produto provisorio sem EAN");
        await Processar();
        var n = _amb.Nota(100);
        Assert.Equal(SituacaoNota.I60Enviada, n.Situacao);
        Assert.Equal(MotivoI60.PendenteComRessalva, n.Motivo);
        Assert.Equal("Produto provisorio sem EAN", Assert.Single(n.Ressalvas));
    }

    [Fact]
    public async Task PendenteComRessalvaBloqueante_Rejeita()
    {
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.PendenteComRessalva("Item nao confere com o pedido");
        await Processar();
        Assert.Equal(SituacaoNota.Rejeitada, _amb.Nota(100).Situacao);
        Assert.Equal(MotivoI60.Outro, _amb.Nota(100).Motivo);
    }

    [Fact]
    public async Task QuantidadeDiferente_Rejeita()
    {
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.Recusa("Quantidade caixa da nota 10 não confere com a quantidade separada 0");
        await Processar();
        Assert.Equal(MotivoI60.QuantidadeDiferente, _amb.Nota(100).Motivo);
        Assert.Equal(SituacaoNota.Rejeitada, _amb.Nota(100).Situacao);
    }

    [Fact]
    public async Task VolumeDiferente_Rejeita()
    {
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.Recusa("Total de volume(s) 3 não confere com a qtdvolume 10");
        await Processar();
        Assert.Equal(MotivoI60.VolumeDiferente, _amb.Nota(100).Motivo);
    }

    [Fact]
    public async Task PadroesConfiguraveis_TextoDiferenteMudaADeteccaoDoEnc()
    {
        _amb.I60.PadroesDocCargaEncerrado = "fechado definitivamente";
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        _amb.Wms.ResponderI60 = (p, _) => Op(p) == "INE" ? FakeWmsI51.Recusa("Doccarga FECHADO definitivamente") : FakeWmsI51.Pendente();
        await Processar();
        Assert.Equal("IMP", Op(_amb.Wms.EnviosI60[1]));
    }

    // ---- Comunicação, respostas desconhecidas, XML ----

    [Fact]
    public async Task FalhasDeComunicacao_NuncaRejeitam_ComBackoffExponencial()
    {
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        for (var i = 0; i < 5; i++)
        {
            _amb.Wms.FalhasI60.Enqueue(new HttpRequestException("x", null, i == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.BadGateway));
        }

        var esperados = new[] { 5, 10, 20, 40, 60 };
        for (var i = 0; i < 5; i++)
        {
            var antes = _amb.Clock.UtcNow;
            var resumo = await Processar();
            Assert.Equal(1, resumo.FalhasComunicacao);
            var n = _amb.Nota(100);
            Assert.Equal(SituacaoNota.Separada, n.Situacao);
            Assert.Equal(i + 1, n.FalhasComunicacao);
            Assert.Equal(TimeSpan.FromMinutes(esperados[i]), n.ProximaTentativaI60Em!.Value - antes);

            // Antes do backoff vencer, a nota não é reenviada.
            var chamadas = _amb.Wms.FalhasI60.Count;
            await Processar();
            Assert.Equal(chamadas, _amb.Wms.FalhasI60.Count);
            _amb.Clock.Avancar(TimeSpan.FromMinutes(esperados[i]));
        }

        await Processar(); // o WMS voltou
        Assert.Equal(SituacaoNota.Associada, _amb.Nota(100).Situacao);
        Assert.Equal(0, _amb.Nota(100).FalhasComunicacao);
        Assert.Equal(6, _amb.Nota(100).TentativasI60); // write-ahead conta cada envio
    }

    [Fact]
    public async Task Timeout_EJsonInvalido_TambemSaoComunicacao()
    {
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        _amb.Wms.FalhasI60.Enqueue(new TaskCanceledException("timeout"));
        await Processar();
        Assert.Equal(SituacaoNota.Separada, _amb.Nota(100).Situacao);
        Assert.Equal(1, _amb.Nota(100).FalhasComunicacao);
    }

    [Fact]
    public async Task RespostaDesconhecida_TentaAteOMaximo_DepoisRejeita_EReenfileirarCorrige()
    {
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.Recusa("texto desconhecido do wms");

        await Processar();
        Assert.Equal(SituacaoNota.Separada, _amb.Nota(100).Situacao);
        _amb.Clock.Avancar(TimeSpan.FromHours(2));
        await Processar();
        Assert.Equal(SituacaoNota.Separada, _amb.Nota(100).Situacao);
        _amb.Clock.Avancar(TimeSpan.FromHours(2));
        await Processar();
        Assert.Equal(SituacaoNota.Rejeitada, _amb.Nota(100).Situacao);
        Assert.Equal(MotivoI60.Outro, _amb.Nota(100).Motivo);

        // O texto era novo: ajusta o padrão e reenfileira.
        _amb.I60.PadroesAssociadaSucesso = "texto desconhecido";
        Assert.Equal(1, _amb.Repositorio.Reenfileirar(null, MotivoI60.Outro, _amb.Clock.UtcNow));
        await Processar();
        Assert.Equal(SituacaoNota.Associada, _amb.Nota(100).Situacao);
    }

    [Fact]
    public async Task XmlIndisponivel_RejeitaSemConsumirTentativaESemChamarOWms()
    {
        var n = await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        File.Delete(Path.Combine(_amb.Caminhos.Base, n.CaminhoXml));

        await Processar();

        Assert.Equal(0, _amb.Wms.ChamadasI60);
        var depois = _amb.Nota(100);
        Assert.Equal(MotivoI60.XmlIndisponivel, depois.Motivo);
        Assert.Equal(0, depois.TentativasI60);
    }

    [Fact]
    public async Task ChaveSintetica_SemXml_NaoLanca_ERejeita()
    {
        _amb.Repositorio.Registrar(new NotaSeparacao
        {
            Chave = "nf-5-6-11111111000111",
            Numero = "5",
            RegistradaEm = _amb.Clock.UtcNow,
            Situacao = SituacaoNota.Separada,
            SeparadaEm = _amb.Clock.UtcNow,
            Itens = new[] { new ItemSeparacao { Sequencia = 1, CodProduto = "A", QtdeOriginal = 1, QtdeSeparada = 1 } },
        });
        await Processar();
        Assert.Equal(MotivoI60.XmlIndisponivel, _amb.Repositorio.Obter("nf-5-6-11111111000111")!.Motivo);
    }

    [Fact]
    public async Task FallbackDoXml_AchaEmProcessadosPelaChave()
    {
        var n = await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        var origem = Path.Combine(_amb.Caminhos.Base, n.CaminhoXml);
        var pacote = Path.Combine(_amb.Pastas.CaminhoProcessados, "pacote_20261112_150000");
        Directory.CreateDirectory(pacote);
        File.Move(origem, Path.Combine(pacote, $"NFE{n.Chave}.XML"));

        await Processar();

        Assert.Equal(SituacaoNota.Associada, _amb.Nota(100).Situacao);
    }

    [Fact]
    public async Task I60Desligada_PulaOPasso_ANotaFicaSeparada()
    {
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 10) });
        _amb.I60.Habilitada = false;
        var r = await Processar();
        Assert.Equal(ResumoI60.Vazio, r);
        Assert.Equal(0, _amb.Wms.ChamadasI60);
        Assert.Equal(SituacaoNota.Separada, _amb.Nota(100).Situacao);
    }

    // ---- Volume ----

    [Fact]
    public async Task Volume_PadraoSomaCaixas()
    {
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 7) }, volumes: 3);
        await Processar();
        Assert.Equal(7m, _amb.Wms.EnviosI60[0].NotasVenda[0].QtdeVolume);
    }

    [Fact]
    public async Task Volume_VolumesI51_UsaAContagem()
    {
        _amb.I60.QtdeVolumeOrigem = "VolumesI51";
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 7) }, volumes: 3);
        await Processar();
        Assert.Equal(3m, _amb.Wms.EnviosI60[0].NotasVenda[0].QtdeVolume);
    }

    [Fact]
    public async Task Volume_VolumesI51ComContagemZero_CaiEmSomaCaixas()
    {
        _amb.I60.QtdeVolumeOrigem = "VolumesI51";
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 7) }, volumes: 0);
        await Processar();
        Assert.Equal(7m, _amb.Wms.EnviosI60[0].NotasVenda[0].QtdeVolume);
    }

    [Fact]
    public async Task Volume_QtdeVolumeZeroTemPrecedencia()
    {
        _amb.I60.QtdeVolumeZero = true;
        _amb.I60.QtdeVolumeOrigem = "VolumesI51";
        await SepararSemI60(new[] { ("A", 10m) }, new[] { It(1, "A", 10, 7) }, volumes: 3);
        await Processar();
        Assert.Equal(0m, _amb.Wms.EnviosI60[0].NotasVenda[0].QtdeVolume);
    }
}
