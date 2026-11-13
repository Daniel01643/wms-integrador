using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Relatorio;
using KitWmsIntegrador.Core.Separacao;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class AlertaI60Tests : IDisposable
{
    private const string TextoEnc = "Doccarga encerrado, nao e possivel associar a nota (status ENC).";
    private const string TextoQtde = "Quantidade caixa da nota não confere com a quantidade separada.";

    private readonly ColetorLogs _logs = new();
    private readonly AmbienteI51 _amb;

    public AlertaI60Tests()
    {
        _amb = new AmbienteI51(LoggerFactory.Create(b => b.AddProvider(_logs)));
    }

    public void Dispose() => _amb.Dispose();

    private static string Op(Core.Construtores.Dtos.PayloadI60 p) => p.NotasVenda[0].TipoOperacaoNota;

    private static string Pedido(Core.Construtores.Dtos.PayloadI60 p) => p.NotasVenda[0].Pedidos[0];

    // Registra a nota e a deixa "separada" no WMS falso (o ciclo seguinte lê a separação e envia a I60).
    private void Preparar(int numero, decimal qtde = 10m)
    {
        _amb.RegistrarNota(numero, new[] { ("A", qtde) });
        _amb.Wms.Separar(numero.ToString(), AmbienteI51.CnpjDestinatario, new[] { new FakeWmsI51.ItemDoc(1, "A", qtde, qtde) }, 1);
    }

    private Task Ciclo() => _amb.Servico.CicloAsync(CancellationToken.None);

    private string[] LinhasArquivo()
    {
        if (!Directory.Exists(_amb.Caminhos.Alertas))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(_amb.Caminhos.Alertas, "*.log")
            .SelectMany(f => File.ReadAllText(f).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            .ToArray();
    }

    [Fact]
    public async Task Rejeitada_AlertaUmaVez_ComAssuntoETexto_ECiclosSeguintesNaoRepetem()
    {
        Preparar(100);
        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.Recusa(TextoQtde);

        await Ciclo();

        var email = Assert.Single(_amb.Email.Enviadas);
        Assert.Equal("[KitWms] I60 precisa de atenção: 1 nota(s)", email.Assunto);
        Assert.Contains("100", email.CorpoHtml);
        Assert.Contains("quantidade separada", email.CorpoHtml);
        Assert.Contains("PadroesDocCargaEncerrado", email.CorpoHtml);
        Assert.Equal(new[] { "ti@teste.local" }, email.DestinatariosOverride);
        Assert.NotNull(_amb.Nota(100).AlertaI60Em);

        _amb.Clock.Avancar(TimeSpan.FromMinutes(10));
        await Ciclo();
        await Ciclo();
        Assert.Single(_amb.Email.Enviadas);
    }

    [Fact]
    public async Task VariasNotasNoMesmoCiclo_VaoNumEmailSo_AssociadaNaoAlerta()
    {
        Preparar(100);
        Preparar(101);
        Preparar(102);
        _amb.Wms.ResponderI60 = (p, _) => Pedido(p) switch
        {
            "100" => FakeWmsI51.Recusa(TextoQtde),
            "101" => Op(p) == "INE" ? FakeWmsI51.Recusa(TextoEnc) : FakeWmsI51.Pendente(),
            _ => FakeWmsI51.Sucesso(),
        };

        await Ciclo();

        Assert.Equal(SituacaoNota.Associada, _amb.Nota(102).Situacao);
        Assert.Null(_amb.Nota(102).AlertaI60Em);
        var email = Assert.Single(_amb.Email.Enviadas);
        Assert.Equal("[KitWms] I60 precisa de atenção: 2 nota(s)", email.Assunto);
        Assert.Equal(2, LinhasArquivo().Length);
    }

    [Fact]
    public async Task EncViraImp_Alerta_ComTextoDoWmsEDica()
    {
        Preparar(100);
        _amb.Wms.ResponderI60 = (p, _) => Op(p) == "INE" ? FakeWmsI51.Recusa(TextoEnc) : FakeWmsI51.Pendente();

        await Ciclo();

        Assert.Equal(SituacaoNota.I60Enviada, _amb.Nota(100).Situacao);
        var email = Assert.Single(_amb.Email.Enviadas);
        Assert.Contains("ENC/IMP", email.CorpoHtml);
        Assert.Contains("encerrado", email.CorpoHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("I60:PadroesDocCargaEncerrado", email.CorpoHtml);
    }

    [Fact]
    public async Task RespostaNaoReconhecida_AindaEmTentativas_Alerta()
    {
        Preparar(100);
        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.Recusa("Texto totalmente novo do WMS.");

        await Ciclo();

        var n = _amb.Nota(100);
        Assert.Equal(SituacaoNota.Separada, n.Situacao);
        Assert.True(n.TentativasOutro >= 1);
        var email = Assert.Single(_amb.Email.Enviadas);
        Assert.Contains("NaoReconhecida", email.CorpoHtml);
        Assert.Contains("Texto totalmente novo do WMS.", email.CorpoHtml);

        // Ao esgotar as tentativas a nota vira Rejeitada: o alerta já foi dado (não repete).
        for (var i = 0; i < 4; i++)
        {
            _amb.Clock.Avancar(TimeSpan.FromHours(2));
            await Ciclo();
        }

        Assert.Equal(SituacaoNota.Rejeitada, _amb.Nota(100).Situacao);
        Assert.Single(_amb.Email.Enviadas);
    }

    [Fact]
    public async Task Atrasada_AlertaUmaVez()
    {
        _amb.RegistrarNota(100, new[] { ("A", 10m) });
        _amb.Clock.Avancar(TimeSpan.FromHours(_amb.I51.HorasParaAtraso + 1));

        await Ciclo();

        Assert.Equal(SituacaoNota.Atrasada, _amb.Nota(100).Situacao);
        var email = Assert.Single(_amb.Email.Enviadas);
        Assert.Contains("Atrasada", email.CorpoHtml);
        Assert.NotNull(_amb.Nota(100).AlertaAtrasoEm);
        Assert.Null(_amb.Nota(100).AlertaI60Em);

        _amb.Clock.Avancar(TimeSpan.FromHours(3));
        await Ciclo();
        Assert.Single(_amb.Email.Enviadas);
    }

    [Fact]
    public async Task Reinicio_NaoRealerta()
    {
        Preparar(100);
        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.Recusa(TextoQtde);
        await Ciclo();
        Assert.Single(_amb.Email.Enviadas);

        _amb.Reiniciar();
        await Ciclo();

        Assert.Single(_amb.Email.Enviadas);
        Assert.Single(LinhasArquivo());
    }

    [Fact]
    public async Task AlertaImediatoFalse_NaoEnviaNemGravaArquivoNemLoga()
    {
        _amb.I60.AlertaImediato = false;
        Preparar(100);
        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.Recusa(TextoQtde);

        await Ciclo();

        Assert.Equal(SituacaoNota.Rejeitada, _amb.Nota(100).Situacao);
        Assert.Empty(_amb.Email.Enviadas);
        Assert.False(Directory.Exists(_amb.Caminhos.Alertas));
        Assert.DoesNotContain(_logs.Linhas, l => l.Contains("I60 precisa de atencao", StringComparison.Ordinal));
        Assert.Null(_amb.Nota(100).AlertaI60Em);
    }

    [Fact]
    public async Task EmailDesabilitado_GravaArquivoELog_NaoEnviaECarimba()
    {
        _amb.EmailOpcoes.Habilitado = false;
        Preparar(100);
        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.Recusa(TextoQtde);

        await Ciclo();

        Assert.Empty(_amb.Email.Enviadas);
        Assert.Single(LinhasArquivo());
        Assert.NotNull(_amb.Nota(100).AlertaI60Em);
        Assert.Contains(_logs.Linhas, l => l.StartsWith("Warning: I60 precisa de atencao: 1 nota(s) neste ciclo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FalhaDeEmail_CarimbaELogaWarning()
    {
        _amb.Email.Resultado = ResultadoEnvioEmail.Falhou("smtp fora");
        Preparar(100);
        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.Recusa(TextoQtde);

        await Ciclo();
        await Ciclo();

        Assert.Single(_amb.Email.Enviadas);
        Assert.NotNull(_amb.Nota(100).AlertaI60Em);
        Assert.Single(LinhasArquivo());
        Assert.Contains(_logs.Linhas, l => l.StartsWith("Warning: I60 precisa de atencao: 1 nota(s)", StringComparison.Ordinal) && l.Contains("Falhou"));
        Assert.Contains(_logs.Linhas, l => l.StartsWith("Warning: I60 atencao: nota 100 serie 6 situacao Rejeitada motivo QuantidadeDiferente.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Arquivo_UmaLinhaPorEvento_TextoEmUmaLinhaTruncado()
    {
        Preparar(100);
        var longo = TextoQtde + "\r\nsegunda linha\tcom tab " + new string('x', 2000);
        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.Recusa(longo);

        await Ciclo();

        var linha = Assert.Single(LinhasArquivo()).TrimEnd('\r');
        Assert.DoesNotContain('\r', linha);
        var colunas = linha.Split('\t');
        Assert.Equal(9, colunas.Length);
        Assert.Equal("Rejeitada", colunas[1]);
        Assert.Equal("100", colunas[2]);
        Assert.Equal("6", colunas[3]);
        Assert.Equal("Rejeitada", colunas[6]);
        Assert.Equal("QuantidadeDiferente", colunas[7]);
        Assert.True(colunas[8].Length <= 500);
        Assert.StartsWith("Quantidade caixa", colunas[8]);
    }

    [Fact]
    public async Task Corpo_EncodaHtml_ESemSegredos()
    {
        Preparar(100);
        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.Recusa("<script>alert(1)</script> " + TextoQtde);

        await Ciclo();

        var html = Assert.Single(_amb.Email.Enviadas).CorpoHtml;
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("senha", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reenfileirar_LiberaNovoAlerta()
    {
        Preparar(100);
        _amb.Wms.ResponderI60 = (_, _) => FakeWmsI51.Recusa(TextoQtde);
        await Ciclo();
        Assert.Single(_amb.Email.Enviadas);

        Assert.Equal(1, _amb.Repositorio.Reenfileirar(AmbienteI51.Chave(100), null, _amb.Clock.UtcNow));
        Assert.Null(_amb.Nota(100).AlertaI60Em);
        await Ciclo();

        Assert.Equal(2, _amb.Email.Enviadas.Count);
        Assert.Equal(2, LinhasArquivo().Length);
    }

    [Fact]
    public async Task Limpeza_ApagaLogsDeAlertaVencidos()
    {
        Directory.CreateDirectory(_amb.Caminhos.Alertas);
        var velho = Path.Combine(_amb.Caminhos.Alertas, "2020-01-01.log");
        File.WriteAllText(velho, "x");
        File.SetLastWriteTimeUtc(velho, _amb.Clock.UtcNow.UtcDateTime.AddDays(-_amb.I51.DiasRetencaoEstado - 2));
        var recente = Path.Combine(_amb.Caminhos.Alertas, "2026-11-12.log");
        File.WriteAllText(recente, "x");
        File.SetLastWriteTimeUtc(recente, _amb.Clock.UtcNow.UtcDateTime);

        await Ciclo();

        Assert.False(File.Exists(velho));
        Assert.True(File.Exists(recente));
    }

    [Fact]
    public void Destinatarios_VemDoTxtDoRelatorio_ESenaoDoFallback()
    {
        var monit = Options.Create(new OpcoesMonitoramento());
        var telemetria = new RegistradorTelemetria(Options.Create(_amb.Pastas), monit, NullLogger<RegistradorTelemetria>.Instance);
        var resumo = new FonteDestinatariosResumo(telemetria, monit, NullLogger<FonteDestinatariosResumo>.Instance);
        var opcoes = new OpcoesRelatorioI60 { CaminhoDestinatarios = Path.Combine(_amb.Raiz, "destinatarios-i60.txt") };
        IDestinatariosAlertaI60 fonte = new FonteDestinatariosRelatorioI60(telemetria, resumo, Options.Create(opcoes));

        Assert.NotEmpty(fonte.Ler()); // fallback seguro

        File.WriteAllText(opcoes.CaminhoDestinatarios, "a@teste.local\nb@teste.local\n");
        Assert.Equal(new[] { "a@teste.local", "b@teste.local" }, fonte.Ler());
    }

    [Fact]
    public void RelatorioDiario_MostraBlocoAtencaoNoTopo_HtmlEPdf()
    {
        var modelo = ExemplosRelatorioI60.ModeloFicticio();
        Assert.True(modelo.AtencaoTotal > 0);
        Assert.Equal(1, modelo.AtencaoNaoReconhecidas);
        Assert.Contains(modelo.Problemas.Linhas, l => l.Motivo == "RespostaNaoReconhecida");

        var gerador = new GeradorRelatorioI60(Options.Create(new OpcoesRelatorio()));
        var html = gerador.GerarHtml(modelo, ModoLogoRelatorio.DataUri).Html;
        var atencao = html.IndexOf(">" + System.Net.WebUtility.HtmlEncode("Atenção") + "<", StringComparison.Ordinal);
        var saude = html.IndexOf(">" + System.Net.WebUtility.HtmlEncode("Saúde da consulta") + "<", StringComparison.Ordinal);
        Assert.True(atencao > 0 && atencao < saude, "O bloco Atenção deve vir antes de Saúde da consulta.");

        var pdf = gerador.GerarPdf(modelo);
        Assert.True(pdf.Length > 1000);
    }

    [Fact]
    public void Exemplos_GeraHtmlParaEvidencia()
    {
        if (Environment.GetEnvironmentVariable("GERAR_EXEMPLOS") != "1")
        {
            return;
        }

        var pasta = Path.Combine(Path.GetTempPath(), "kitwms-evidencias-1311");
        Directory.CreateDirectory(pasta);
        var gerador = new GeradorRelatorioI60(Options.Create(new OpcoesRelatorio()));
        File.WriteAllText(
            Path.Combine(pasta, "relatorio-i60-enc-nao-reconhecida.html"),
            gerador.GerarHtml(ExemplosRelatorioI60.ModeloFicticio(), ModoLogoRelatorio.DataUri).Html, new System.Text.UTF8Encoding(true));

        var linhas = new[]
        {
            new LinhaAlertaI60("ENC/IMP", "5009", "6", "1000003", string.Empty, "I60Enviada", "DocCargaEncerrado",
                "Doccarga encerrado (ENC): I60 enviada como IMP (tpacao=UPD), sem associação automática."),
            new LinhaAlertaI60("NaoReconhecida", "5012", "6", "1000005", string.Empty, "Separada", "RespostaNaoReconhecida",
                "Mensagem nova do WMS que o integrador ainda não conhece (exemplo fictício)."),
            new LinhaAlertaI60("Rejeitada", "5010", "6", "1000003", string.Empty, "Rejeitada", "QuantidadeDiferente",
                "Quantidade caixa da nota 36 não confere com a quantidade separada 30. | Quantidade divergente no item PROD-A"),
            new LinhaAlertaI60("Atrasada", "5004", "6", "1000003", string.Empty, "Atrasada", "SemRetornoDaSeparacao", string.Empty),
        };
        File.WriteAllText(
            Path.Combine(pasta, "alerta-i60-email.html"),
            CorpoEmailAlertaI60.Montar(linhas, ExemplosRelatorioI60.Agora, IdentidadeVisual.ObterLogo(null), ModoLogoRelatorio.DataUri).Html,
            new System.Text.UTF8Encoding(true));
    }
}
