using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Diagnostico;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Relatorio;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Corpo do e-mail enxuto (só resumo e problemas) e PDF anexo completo: relatório diário da I51/I60 (12:00) e
/// Diagnóstico agente integrador (14:00).
/// </summary>
public sealed class CorpoEmailEnxutoTests : IDisposable
{
    private readonly AmbienteDiagnostico _a = new();

    public void Dispose() => _a.Dispose();

    private static LinhaNotaI60 Linha(string numero, string motivo = "", string mensagem = "") =>
        new(numero, "6", "PUXADA", "1000001", string.Empty, "pacote_20261112_080000", "Situacao", "1 h", "12/11 10:00", "INE", motivo, mensagem, string.Empty, 0);

    private static SecaoI60<LinhaNotaI60> Secao(string prefixo, int quantidade) =>
        new(Enumerable.Range(1, quantidade).Select(i => Linha(prefixo + i.ToString("D3"))).ToList(), quantidade);

    /// <summary>Modelo com listas grandes do que deu certo e com problemas identificáveis.</summary>
    private static ModeloRelatorioI60 Modelo(int associadas = 80) => new()
    {
        GeradoEm = ExemplosRelatorioI60.Agora,
        JanelaInicio = ExemplosRelatorioI60.Agora.AddHours(-24),
        JanelaFim = ExemplosRelatorioI60.Agora,
        Aguardando = 30,
        Atrasadas = 2,
        AssociadasNaJanela = associadas,
        Rejeitadas = 1,
        AtencaoRejeitadas = 1,
        AtencaoAtrasadas = 2,
        ListaAguardando = Secao("AGUARD", 30),
        SeparadasNaJanela = Secao("SEPARA", 12),
        PendentesI60 = Secao("PENDEN", 5),
        Associadas = Secao("ASSOC", associadas),
        AguardandoConfirmacao = Secao("CONFIR", 3),
        Problemas = new SecaoI60<LinhaNotaI60>(new[] { Linha("REJEIT1", "Rejeitada", "Documento já associado a outro transporte") }, 1),
        ListaAtrasadas = new SecaoI60<LinhaNotaI60>(new[] { Linha("ATRAS01"), Linha("ATRAS02") }, 2),
        Ajustes = new SecaoI60<LinhaAjusteI60>(new[] { new LinhaAjusteI60("AJUST01", 1, "PROD-A", 24, 20, -4) }, 1),
        PorPacote = new SecaoI60<LinhaPacoteI60>(new[] { new LinhaPacoteI60("PACOTEVISAO1", "PUXADA", string.Empty, 90, 30, 2, 5, 1, 80, 1, 1) }, 1),
    };

    private static GeradorRelatorioI60 GeradorI60() => new(Options.Create(new OpcoesRelatorio()));

    private static int Paginas(byte[] pdf) =>
        System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b").Count;

    // ---- Relatório diário I51/I60 ----

    [Fact]
    public void I60_CorpoEnxuto_NaoTemAListaDeAssociadasNemOutrasListasDoQueDeuCerto()
    {
        var html = System.Net.WebUtility.HtmlDecode(GeradorI60().GerarHtml(Modelo(), ModoLogoRelatorio.Cid, enxuto: true).Html);

        Assert.DoesNotContain("ASSOC001", html);
        Assert.DoesNotContain("ASSOC080", html);
        Assert.DoesNotContain("I60 associadas (80)", html);
        Assert.DoesNotContain("AGUARD001", html);
        Assert.DoesNotContain("SEPARA001", html);
        Assert.DoesNotContain("PENDEN001", html);
        Assert.DoesNotContain("CONFIR001", html);
        Assert.DoesNotContain("Visão por pacote", html);
        Assert.DoesNotContain("PACOTEVISAO1", html);
    }

    [Fact]
    public void I60_CorpoEnxuto_TemResumoSaudeProblemasEALinhaDoPdf()
    {
        var html = System.Net.WebUtility.HtmlDecode(GeradorI60().GerarHtml(Modelo(), ModoLogoRelatorio.Cid, enxuto: true).Html);

        Assert.Contains("Detalhes completos no PDF anexo", html);
        Assert.Contains("Atenção", html);
        Assert.Contains("Saúde da consulta", html);
        Assert.Contains("Resumo", html);
        Assert.Contains("I60 associadas (janela)", html);   // contagem por situação continua nos cartões
        Assert.Contains("I60 com problema (1)", html);
        Assert.Contains("REJEIT1", html);
        Assert.Contains("Documento já associado a outro transporte", html);
        Assert.Contains("Ajustes de quantidade (1)", html);
        Assert.Contains("AJUST01", html);
        Assert.Contains("Atrasadas (2)", html);
        Assert.Contains("ATRAS01", html);
    }

    [Fact]
    public void I60_HtmlCompleto_ContinuaComTodasAsListas()
    {
        var html = System.Net.WebUtility.HtmlDecode(GeradorI60().GerarHtml(Modelo(), ModoLogoRelatorio.DataUri).Html);

        Assert.Contains("ASSOC001", html);
        Assert.Contains("ASSOC080", html);
        Assert.Contains("I60 associadas (80)", html);
        Assert.Contains("PACOTEVISAO1", html);
        Assert.DoesNotContain("Detalhes completos no PDF anexo", html);
    }

    [Fact]
    public void I60_Pdf_ContemAListaDeAssociadasETudoMais()
    {
        var texto = PdfTexto.Extrair(GeradorI60().GerarPdf(Modelo()));

        Assert.Contains("ASSOC001", texto);
        Assert.Contains("ASSOC080", texto);
        Assert.Contains("AGUARD001", texto);
        Assert.Contains("SEPARA001", texto);
        Assert.Contains("CONFIR001", texto);
        Assert.Contains("PACOTEVISAO1", texto);
        Assert.Contains("REJEIT1", texto);
        Assert.Contains("AJUST01", texto);
        Assert.Contains("ATRAS01", texto);
    }

    [Fact]
    public async Task I60_Email_CorpoEnxuto_PdfAnexoCompleto_EArquivosEmDiscoCompletos()
    {
        var opcoes = new OpcoesRelatorioI60();
        var enviador = new EnviadorRelatorioDiarioI60(
            _a.Email, GeradorI60(),
            new FonteDestinatariosRelatorioI60(
                _a.Telemetria, new FonteDestinatariosResumo(_a.Telemetria, Options.Create(new OpcoesMonitoramento()), NullLogger<FonteDestinatariosResumo>.Instance),
                Options.Create(opcoes)),
            Options.Create(opcoes), Options.Create(new OpcoesRelatorio { PastaSaida = _a.Saida }), Options.Create(_a.OpcoesEmail),
            NullLogger<EnviadorRelatorioDiarioI60>.Instance);

        var (html, pdf, enviado) = await enviador.EnviarAsync(Modelo(), enviarEmail: true, CancellationToken.None);

        Assert.True(enviado);
        var msg = Assert.Single(_a.Email.Enviadas);
        var corpo = System.Net.WebUtility.HtmlDecode(msg.CorpoHtml);
        Assert.DoesNotContain("ASSOC001", corpo);
        Assert.Contains("Detalhes completos no PDF anexo", corpo);
        Assert.Contains("REJEIT1", corpo);

        var anexo = Assert.Single(msg.Anexos);
        Assert.Equal(EnviadorRelatorioDiarioI60.NomeAnexoPdf, anexo.Nome);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(anexo.Conteudo, 0, 4));
        Assert.Contains("ASSOC001", PdfTexto.Extrair(anexo.Conteudo));
        Assert.Contains("ASSOC080", PdfTexto.Extrair(anexo.Conteudo));

        // cópias em disco continuam completas
        Assert.Contains("ASSOC001", System.Net.WebUtility.HtmlDecode(File.ReadAllText(html)));
        Assert.Contains("ASSOC001", PdfTexto.Extrair(File.ReadAllBytes(pdf)));
    }

    [Fact]
    public void I60_PdfComAssociadas_TemMaisPaginasQueSemAssociadas()
    {
        Assert.True(Paginas(GeradorI60().GerarPdf(Modelo(80))) > Paginas(GeradorI60().GerarPdf(Modelo(0))));
    }

    // ---- Diagnóstico agente integrador ----

    private static ModeloDiagnostico ModeloDiag() => MontadorDiagnostico.Montar(ExemplosDiagnostico.EntradaFicticia());

    private static GeradorDiagnostico GeradorDiag() => new(Options.Create(new OpcoesRelatorio()));

    [Fact]
    public void Diagnostico_CorpoEnxuto_TemResumoEProblemas_SemTabelasDeDetalhe()
    {
        var html = System.Net.WebUtility.HtmlDecode(GeradorDiag().GerarHtml(ModeloDiag(), ModoLogoRelatorio.Cid, enxuto: true).Html);

        Assert.Contains("Detalhes completos no PDF anexo", html);
        Assert.Contains("Falhas por classe", html);
        Assert.Contains("Janelas de indisponibilidade", html);
        Assert.Contains("Notas não associadas (4)", html);
        Assert.Contains("Unidade de medida inválida", html);   // erro da I30 (problema)
        Assert.Contains("7. Memória do integrador e do servidor", html);

        Assert.DoesNotContain("Por servidor do balanceador", html);
        Assert.DoesNotContain(ExemplosDiagnostico.BackendB, html);
        Assert.DoesNotContain("Mensagem da I50 que disparou", html);
        Assert.DoesNotContain("Produto 5006566 não cadastrado", html);
        Assert.DoesNotContain("I60 por desfecho", html);
        Assert.DoesNotContain("Usuário (declarado na tela)", html);
        Assert.DoesNotContain("Tamanho de Processados", html);
    }

    [Fact]
    public void Diagnostico_HtmlCompleto_ContinuaComOsDetalhes()
    {
        var html = System.Net.WebUtility.HtmlDecode(GeradorDiag().GerarHtml(ModeloDiag(), ModoLogoRelatorio.DataUri).Html);

        Assert.Contains("Por servidor do balanceador", html);
        Assert.Contains(ExemplosDiagnostico.BackendB, html);
        Assert.Contains("Mensagem da I50 que disparou", html);
        Assert.Contains("I60 por desfecho", html);
        Assert.Contains("USUARIO.A", html);
        Assert.Contains("Tamanho de Processados", html);
        Assert.DoesNotContain("Detalhes completos no PDF anexo", html);
    }

    [Fact]
    public void Diagnostico_Pdf_TemTodasAsSecoesEOsDetalhes()
    {
        var eventos = ExemplosDiagnostico.EventosFicticios().ToList();
        eventos.Add(new EventoDiagnostico
        {
            Tipo = "saude-amostra", QuandoLocal = ExemplosDiagnostico.Fim.AddHours(-3), ProcessoWsMb = 180, ProcessoPrivadoMb = 140, Handles = 500, Threads = 40,
            LivreMb = 420, TotalMb = 8192, PaginacaoUsadaMb = 7000, PaginacaoTotalMb = 12000,
            Consumidores = new[] { new ConsumidorMemoria("node", 1070), new ConsumidorMemoria("PowerBIGateway", 845) },
        });

        var texto = PdfTexto.Extrair(GeradorDiag().GerarPdf(MontadorDiagnostico.Montar(ExemplosDiagnostico.EntradaFicticia(eventos))));

        Assert.Contains("Diagnóstico agente integrador", texto);
        Assert.Contains("1. Login do WMS", texto);
        Assert.Contains(ExemplosDiagnostico.BackendB, texto);
        Assert.Contains("Mensagem da I50 que disparou", texto);
        Assert.Contains("Produto 5006566 não cadastrado", texto);
        Assert.Contains("Notas não associadas", texto);
        Assert.Contains("USUARIO.A", texto);
        Assert.Contains("Tamanho de Processados", texto);
        Assert.Contains("7. Memória do integrador e do servidor", texto);
        Assert.Contains("PowerBIGateway", texto);
        Assert.Contains("Página 1 de", texto);
    }

    [Fact]
    public async Task Diagnostico_Email_CorpoEnxuto_PdfAnexo_EHtmlEPdfEmDisco()
    {
        foreach (var e in ExemplosDiagnostico.EventosFicticios())
        {
            _a.Diario.Registrar(e);
        }

        _a.Ir(12, 14, 0);

        await _a.Servico.VerificarEEnviarAsync(CancellationToken.None);

        var msg = Assert.Single(_a.Email.Enviadas);
        var corpo = System.Net.WebUtility.HtmlDecode(msg.CorpoHtml);
        Assert.Contains("Detalhes completos no PDF anexo", corpo);
        Assert.DoesNotContain(ExemplosDiagnostico.BackendB, corpo);
        Assert.Contains("Falhas por classe", corpo);

        var anexo = Assert.Single(msg.Anexos);
        Assert.Equal(EnviadorDiagnostico.NomeAnexoPdf, anexo.Nome);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(anexo.Conteudo, 0, 4));
        Assert.Contains(ExemplosDiagnostico.BackendB, PdfTexto.Extrair(anexo.Conteudo));

        var html = Assert.Single(_a.Htmls);
        Assert.Contains(ExemplosDiagnostico.BackendB, System.Net.WebUtility.HtmlDecode(File.ReadAllText(html)));
        Assert.True(File.Exists(Path.ChangeExtension(html, ".pdf")));
    }

    [Fact]
    public async Task Diagnostico_SemEmail_GravaPdfEmDiscoAoLadoDoHtml()
    {
        _a.OpcoesEmail.Habilitado = false;
        _a.Ir(12, 14, 0);

        await _a.Servico.VerificarEEnviarAsync(CancellationToken.None);

        var html = Assert.Single(_a.Htmls);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(Path.ChangeExtension(html, ".pdf")), 0, 4));
        Assert.Empty(_a.Email.Enviadas);
    }
}
