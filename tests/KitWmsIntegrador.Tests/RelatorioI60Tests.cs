using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Relatorio;
using KitWmsIntegrador.Core.Separacao;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class MontadorRelatorioI60Tests
{
    [Fact]
    public void CartoesEIdades()
    {
        var m = ExemplosRelatorioI60.ModeloFicticio();
        Assert.Equal(3, m.Aguardando);
        Assert.Equal(1, m.Atrasadas);
        Assert.Equal(2, m.SeparadasPendentesI60); // inclui a nota com resposta não reconhecida (5012)
        Assert.Equal(2, m.AssociadasNaJanela);
        Assert.Equal(1, m.ImpNaJanela);
        Assert.Equal(1, m.EnviadasAguardandoConfirmacao);
        Assert.Equal(2, m.Rejeitadas);
        Assert.False(m.SemAtividade);
        Assert.Equal("3 h 0 min", m.ListaAguardando.Linhas[0].Idade);
        Assert.Equal("2 d 5 h", m.ListaAtrasadas.Linhas[0].Idade);
        Assert.Equal(1, m.OrfaosNaJanela);
    }

    [Fact]
    public void ProblemasIncluemRejeitadasEImp_ECandidatasI40()
    {
        var m = ExemplosRelatorioI60.ModeloFicticio();
        Assert.Equal(4, m.Problemas.Total); // 2 rejeitadas + IMP + resposta não reconhecida
        Assert.Contains(m.Problemas.Linhas, l => l.Motivo == "DocCargaEncerrado" && l.Operacao == "IMP");
        Assert.Contains(m.Problemas.Linhas, l => l.Motivo == "QuantidadeDiferente" && l.Ressalvas.Contains("PROD-A"));
        Assert.Equal(3, m.CandidatasI40.Total);
        Assert.True(m.DevolucaoDesligada);
        Assert.False(ExemplosRelatorioI60.ModeloFicticio(devolucaoHabilitada: true).DevolucaoDesligada);
    }

    [Fact]
    public void AjustesItemAItem_ComDiferenca()
    {
        var m = ExemplosRelatorioI60.ModeloFicticio();
        // Notas 5006 e 5009 têm diferença no PROD-A (separadas na janela de 24 h).
        Assert.Contains(m.Ajustes.Linhas, a => a.Nota == "5006" && a.Produto == "PROD-A" && a.Original == 24 && a.Separada == 20 && a.Diferenca == -4);
        Assert.Contains(m.Ajustes.Linhas, a => a.Nota == "5009" && a.Diferenca == -6);
    }

    [Fact]
    public void VisaoPorPacote_TspComOnda()
    {
        var m = ExemplosRelatorioI60.ModeloFicticio();
        var tsp = m.PorPacote.Linhas.Single(p => p.Pacote == "pacote_20261112_090000");
        Assert.Equal("TSP", tsp.Tipo);
        Assert.Equal("TSP-20261112-01-SJC", tsp.Ondas);
        Assert.Equal(1, tsp.Aguardando);
        var p1 = m.PorPacote.Linhas.Single(p => p.Pacote == "pacote_20261111_150000");
        Assert.Equal(1, p1.Associadas);
        Assert.Equal(1, p1.Imp);
        Assert.Equal(1, p1.Rejeitadas);
    }

    [Fact]
    public void TetoDeLinhas_ContaOQueNaoFoiExibido()
    {
        var m = ExemplosRelatorioI60.ModeloFicticio(maxLinhas: 10);
        // Com teto 10 nada estoura (11 notas, seções menores); força com um teto mínimo permitido e muitas notas.
        var notas = Enumerable.Range(0, 25).Select(i => new NotaSeparacao
        {
            Chave = "K" + i,
            Numero = i.ToString(),
            RegistradaEm = ExemplosRelatorioI60.Agora.AddHours(-1),
            Situacao = SituacaoNota.AguardandoSeparacao,
        }).ToList();
        var grande = MontadorRelatorioI60.Montar(
            new EntradaRelatorioI60(notas, new SaudeConsultaI51(), 0, 0, ExemplosRelatorioI60.Agora, ExemplosRelatorioI60.Agora.AddHours(-24), false),
            new OpcoesRelatorioI60 { MaxLinhasPorSecao = 10 }, new OpcoesI60());
        Assert.Equal(25, grande.ListaAguardando.Total);
        Assert.Equal(10, grande.ListaAguardando.Linhas.Count);
        Assert.Equal(15, grande.ListaAguardando.NaoExibidas);
        Assert.NotNull(m);
    }

    [Fact]
    public void SemNada_SemAtividade()
    {
        var m = MontadorRelatorioI60.Montar(
            new EntradaRelatorioI60(Array.Empty<NotaSeparacao>(), new SaudeConsultaI51(), 0, 0, ExemplosRelatorioI60.Agora, ExemplosRelatorioI60.Agora.AddHours(-24), false),
            new OpcoesRelatorioI60(), new OpcoesI60());
        Assert.True(m.SemAtividade);
    }

    [Fact]
    public void AlertaDeComunicacao_ContaNotasAcimaDoLimite()
    {
        var notas = new[]
        {
            new NotaSeparacao { Chave = "K", Numero = "1", Situacao = SituacaoNota.Separada, RegistradaEm = ExemplosRelatorioI60.Agora, FalhasComunicacao = 10 },
        };
        var m = MontadorRelatorioI60.Montar(
            new EntradaRelatorioI60(notas, new SaudeConsultaI51(), 0, 0, ExemplosRelatorioI60.Agora, ExemplosRelatorioI60.Agora.AddHours(-24), false),
            new OpcoesRelatorioI60(), new OpcoesI60 { MaxFalhasComunicacaoAlerta = 10 });
        Assert.Equal(1, m.NotasComAlertaComunicacao);
    }
}

public class RelatorioI60RenderTests
{
    private readonly GeradorRelatorioI60 _gerador = new(Options.Create(new OpcoesRelatorio()));

    [Fact]
    public void Html_SemScriptNemCssExterno_ComIdentidadeVisual()
    {
        var html = _gerador.GerarHtml(ExemplosRelatorioI60.ModeloFicticio(), ModoLogoRelatorio.DataUri).Html;
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(System.Net.WebUtility.HtmlEncode("Relatório de Integração"), html);
        Assert.Contains("data:image/png;base64,", html);
        Assert.Contains("DocCargaEncerrado", html);
        Assert.Contains("pacote_20261112_090000", html);
        Assert.Contains(System.Net.WebUtility.HtmlEncode("última alternativa"), html);
        Assert.Contains("Devolucao:Habilitada=false", html);
    }

    [Fact]
    public void Html_NoEmailUsaCid_ESaiComImagemInline()
    {
        var (html, imagem) = _gerador.GerarHtml(ExemplosRelatorioI60.ModeloFicticio(), ModoLogoRelatorio.Cid);
        Assert.Contains("cid:", html);
        Assert.DoesNotContain("data:image", html);
        Assert.NotNull(imagem);
    }

    [Fact]
    public void Html_TextoDinamicoEHtmlEncoded()
    {
        var notas = new[]
        {
            new NotaSeparacao
            {
                Chave = "K", Numero = "1<script>alert(1)</script>", Situacao = SituacaoNota.Rejeitada, Motivo = MotivoI60.Outro,
                Mensagem = "<b>x</b> & \"y\"", RegistradaEm = ExemplosRelatorioI60.Agora, FinalizadaEm = ExemplosRelatorioI60.Agora,
            },
        };
        var modelo = MontadorRelatorioI60.Montar(
            new EntradaRelatorioI60(notas, new SaudeConsultaI51(), 0, 0, ExemplosRelatorioI60.Agora, ExemplosRelatorioI60.Agora.AddHours(-24), false),
            new OpcoesRelatorioI60(), new OpcoesI60());
        var html = _gerador.GerarHtml(modelo, ModoLogoRelatorio.Cid).Html;
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;b&gt;x&lt;/b&gt; &amp; &quot;y&quot;", html);
    }

    [Fact]
    public void Pdf_ELegivel_ComSecoesETextos()
    {
        var pdf = _gerador.GerarPdf(ExemplosRelatorioI60.ModeloFicticio());
        Assert.StartsWith("%PDF", System.Text.Encoding.Latin1.GetString(pdf, 0, 4));
        var texto = PdfTextoHelper.Texto(pdf);
        Assert.Contains("Relatório de Integração", texto);
        Assert.Contains("Aguardando separação", texto);
        Assert.Contains("Visão por pacote", texto);
        Assert.Contains("DocCargaEncerrado", texto);
        Assert.Contains("5006", texto);
    }

    [Fact]
    public void Pdf_ComMuitasLinhas_PaginaSemFalhar()
    {
        var notas = Enumerable.Range(0, 400).Select(i => new NotaSeparacao
        {
            Chave = "K" + i,
            Numero = (7000 + i).ToString(),
            Tipo = "PUXADA",
            IdentificadorPacote = "pacote_x" + (i % 9),
            RegistradaEm = ExemplosRelatorioI60.Agora.AddHours(-2),
            Situacao = SituacaoNota.AguardandoSeparacao,
        }).ToList();
        var modelo = MontadorRelatorioI60.Montar(
            new EntradaRelatorioI60(notas, new SaudeConsultaI51(), 0, 0, ExemplosRelatorioI60.Agora, ExemplosRelatorioI60.Agora.AddHours(-24), false),
            new OpcoesRelatorioI60 { MaxLinhasPorSecao = 300 }, new OpcoesI60());
        var pdf = _gerador.GerarPdf(modelo);
        Assert.NotEmpty(pdf);
        Assert.Contains("e mais 100", PdfTextoHelper.Texto(pdf));
    }

    [Fact]
    public void SemAtividade_GeraAviso()
    {
        var modelo = MontadorRelatorioI60.Montar(
            new EntradaRelatorioI60(Array.Empty<NotaSeparacao>(), new SaudeConsultaI51(), 0, 0, ExemplosRelatorioI60.Agora, ExemplosRelatorioI60.Agora.AddHours(-24), false),
            new OpcoesRelatorioI60(), new OpcoesI60());
        Assert.Contains("Nenhuma nota em acompanhamento", _gerador.GerarHtml(modelo, ModoLogoRelatorio.Cid).Html);
        Assert.NotEmpty(_gerador.GerarPdf(modelo));
    }
}

public class AgendadorRelatorioDiarioTests
{
    private static DateTimeOffset T(int dia, int h, int min = 0) => new(2026, 11, dia, h, min, 0, TimeSpan.FromHours(-3));

    private static readonly TimeOnly Meio = new(12, 0);

    [Fact]
    public void AntesDasDoze_NaoEnvia()
    {
        Assert.False(AgendadorRelatorioDiario.DeveEnviar(T(12, 11, 59), null, Meio).Deve);
    }

    [Fact]
    public void DepoisDasDoze_Envia_ENaoRepete()
    {
        var (deve, slot) = AgendadorRelatorioDiario.DeveEnviar(T(12, 12, 0), null, Meio);
        Assert.True(deve);
        Assert.Equal(T(12, 12), slot);
        Assert.False(AgendadorRelatorioDiario.DeveEnviar(T(12, 18), slot, Meio).Deve);
    }

    [Fact]
    public void SlotPerdidoNoMesmoDia_ERecuperado()
    {
        Assert.True(AgendadorRelatorioDiario.DeveEnviar(T(12, 19), T(11, 12), Meio).Deve);
    }

    [Fact]
    public void ParadaDeVariosDias_GeraUmUnicoEnvio()
    {
        var (deve, slot) = AgendadorRelatorioDiario.DeveEnviar(T(20, 13), T(12, 12), Meio);
        Assert.True(deve);
        Assert.Equal(T(20, 12), slot);
        Assert.False(AgendadorRelatorioDiario.DeveEnviar(T(20, 14), slot, Meio).Deve);
    }

    [Fact]
    public void DiaSeguinte_Reabre()
    {
        Assert.False(AgendadorRelatorioDiario.DeveEnviar(T(13, 11), T(12, 12), Meio).Deve);
        Assert.True(AgendadorRelatorioDiario.DeveEnviar(T(13, 12), T(12, 12), Meio).Deve);
    }
}

public class ServicoRelatorioDiarioI60Tests : IDisposable
{
    private readonly AmbienteI51 _amb = new();
    private readonly GravadorEmail _email = new();
    private readonly OpcoesRelatorioI60 _opcoes = new();
    private readonly OpcoesEmail _opcoesEmail = new();
    private readonly string _saida;
    private readonly MarcaUltimoRelatorioI60 _marca;
    private readonly ServicoRelatorioDiarioI60 _servico;

    public ServicoRelatorioDiarioI60Tests()
    {
        _saida = Path.Combine(_amb.Raiz, "relatorios");
        var monit = Options.Create(new OpcoesMonitoramento());
        var telemetria = new RegistradorTelemetria(Options.Create(_amb.Pastas), monit, NullLogger<RegistradorTelemetria>.Instance);
        _marca = new MarcaUltimoRelatorioI60(telemetria, NullLogger<MarcaUltimoRelatorioI60>.Instance);
        var destinatarios = new FonteDestinatariosRelatorioI60(
            telemetria, new FonteDestinatariosResumo(telemetria, monit, NullLogger<FonteDestinatariosResumo>.Instance),
            Options.Create(_opcoes));
        var enviador = new EnviadorRelatorioDiarioI60(
            _email, new GeradorRelatorioI60(Options.Create(new OpcoesRelatorio())), destinatarios, Options.Create(_opcoes),
            Options.Create(new OpcoesRelatorio { PastaSaida = _saida }), Options.Create(_opcoesEmail),
            NullLogger<EnviadorRelatorioDiarioI60>.Instance);
        _servico = new ServicoRelatorioDiarioI60(
            _amb.Clock, _amb.Repositorio, _amb.Brutos, _amb.Orfaos, _marca, enviador, Options.Create(_amb.I51), Options.Create(_amb.I60),
            Options.Create(_opcoes), Options.Create(new OpcoesDevolucao()), NullLogger<ServicoRelatorioDiarioI60>.Instance);
    }

    public void Dispose() => _amb.Dispose();

    private void IrParaMeioDia()
    {
        var local = _amb.Clock.UtcNow.ToLocalTime();
        var alvo = new DateTimeOffset(local.Date.AddHours(12), local.Offset);
        _amb.Clock.Avancar(alvo.ToUniversalTime() - _amb.Clock.UtcNow);
    }

    private string[] Arquivos(string ext) =>
        Directory.Exists(Path.Combine(_saida, "i60")) ? Directory.GetFiles(Path.Combine(_saida, "i60"), "*" + ext) : Array.Empty<string>();

    [Fact]
    public async Task ComNotas_NoSlot_GravaHtmlEPdf_EMarca_SemRepetir()
    {
        _amb.RegistrarNota(100, new[] { ("A", 1m) });
        IrParaMeioDia();

        await _servico.VerificarEEnviarAsync(CancellationToken.None);

        Assert.Single(Arquivos(".html"));
        Assert.Single(Arquivos(".pdf"));
        Assert.NotNull(_marca.Ler());
        await _servico.VerificarEEnviarAsync(CancellationToken.None);
        Assert.Single(Arquivos(".html"));
    }

    [Fact]
    public async Task SmtpConfigurado_EnviaEmailComAnexoPdf()
    {
        _opcoesEmail.Habilitado = true;
        _opcoesEmail.Host = "smtp.exemplo.local";
        _opcoesEmail.Remetente = "kitwms@exemplo.local";
        _amb.RegistrarNota(100, new[] { ("A", 1m) });
        IrParaMeioDia();

        await _servico.VerificarEEnviarAsync(CancellationToken.None);

        var msg = Assert.Single(_email.Enviadas);
        Assert.Contains("I51/I60", msg.Assunto);
        Assert.Equal("relatorio-i60.pdf", Assert.Single(msg.Anexos).Nome);
        Assert.NotNull(msg.ImagemEmbutida);
    }

    [Fact]
    public async Task FalhaDoEmail_MantemOArquivo_EMarcaOSlot()
    {
        _opcoesEmail.Habilitado = true;
        _opcoesEmail.Host = "smtp.exemplo.local";
        _opcoesEmail.Remetente = "kitwms@exemplo.local";
        _email.Resultado = ResultadoEnvioEmail.Falhou("smtp fora");
        _amb.RegistrarNota(100, new[] { ("A", 1m) });
        IrParaMeioDia();

        await _servico.VerificarEEnviarAsync(CancellationToken.None);

        Assert.Single(Arquivos(".pdf"));
        Assert.NotNull(_marca.Ler());
    }

    [Fact]
    public async Task FalhaDeGeracao_NaoMarcaOSlot()
    {
        _amb.RegistrarNota(100, new[] { ("A", 1m) });
        // Um ARQUIVO no lugar da pasta de saída faz a gravação falhar.
        Directory.CreateDirectory(Path.GetDirectoryName(_saida)!);
        File.WriteAllText(_saida, "bloqueio");
        IrParaMeioDia();

        await _servico.VerificarEEnviarAsync(CancellationToken.None);

        Assert.Null(_marca.Ler());
    }

    [Fact]
    public async Task SemAtividade_MarcaOSlotSemGerar()
    {
        IrParaMeioDia();
        await _servico.VerificarEEnviarAsync(CancellationToken.None);
        Assert.Empty(Arquivos(".html"));
        Assert.NotNull(_marca.Ler());
    }

    [Fact]
    public async Task SemAtividade_ComEnviarQuandoVazio_Gera()
    {
        _opcoes.EnviarQuandoVazio = true;
        IrParaMeioDia();
        await _servico.VerificarEEnviarAsync(CancellationToken.None);
        Assert.Single(Arquivos(".html"));
    }

    [Fact]
    public async Task I51Desligada_NaoAgendaNada()
    {
        _amb.I51.Habilitada = false;
        IrParaMeioDia();
        await _servico.VerificarEEnviarAsync(CancellationToken.None);
        Assert.Empty(Arquivos(".html"));
        Assert.Null(_marca.Ler());
    }

    [Fact]
    public async Task AntesDoHorario_NaoFazNada()
    {
        _amb.RegistrarNota(100, new[] { ("A", 1m) });
        var local = _amb.Clock.UtcNow.ToLocalTime();
        var onze = new DateTimeOffset(local.Date.AddHours(11), local.Offset);
        _amb.Clock.Avancar(onze.ToUniversalTime() - _amb.Clock.UtcNow);
        await _servico.VerificarEEnviarAsync(CancellationToken.None);
        Assert.Empty(Arquivos(".html"));
    }
}
