using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public sealed class EmailPorPacoteTests : IDisposable
{
    private readonly AmbientePacote _amb = new();
    private readonly OrquestradorFalso _orq = new();

    public void Dispose() => _amb.Dispose();

    [Fact]
    public async Task PoliticaSempre_EnviaUmEmailComPdfMesmoComTudoIntegrado()
    {
        _amb.OpcoesDeEmail.EnviarQuando = EnviarQuando.Sempre;
        var g = _amb.CriarGerenciador(_orq);
        _amb.EscreverXml("a.xml", "100", "70001");
        _amb.EscreverXml("b.xml", "101", "70002");

        await _amb.RodarAteOciosoAsync(g);

        var email = Assert.Single(_amb.Email.Enviadas);
        Assert.Contains("Fechamento do pacote", email.Assunto);
        Assert.Contains("2 nota(s)", email.Assunto);
        var anexo = Assert.Single(email.Anexos);
        Assert.EndsWith(".pdf", anexo.Nome);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(anexo.Conteudo, 0, 4));
        Assert.Contains("Total de notas: 2", email.CorpoHtml);
    }

    [Fact]
    public async Task PoliticaNunca_GeraRelatorioMasNaoEnvia()
    {
        _amb.OpcoesDeEmail.EnviarQuando = EnviarQuando.Nunca;
        var g = _amb.CriarGerenciador(_orq);
        _amb.EscreverXml("a.xml", "100", "70001");

        await _amb.RodarAteOciosoAsync(g);

        Assert.Empty(_amb.Email.Enviadas);
        Assert.Single(_amb.RelatoriosGerados(".pdf"));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task PoliticaComProblema_EnviaSoQuandoHaProblema(bool comFalha, int esperado)
    {
        _amb.OpcoesDeEmail.EnviarQuando = EnviarQuando.ComProblema;
        if (comFalha)
        {
            _orq.Comportamento = (n, t) => Task.FromResult(OrquestradorFalso.Falha(n.Numero, t, "erro"));
        }

        var g = _amb.CriarGerenciador(_orq);
        _amb.EscreverXml("a.xml", "100", "70001");

        await _amb.RodarAteOciosoAsync(g);

        Assert.Equal(esperado, _amb.Email.Enviadas.Count);
        Assert.Single(_amb.RelatoriosGerados(".pdf"));
    }

    [Fact]
    public async Task SmtpNaoConfigurado_NaoFalhaESalvaRelatorioEmDisco()
    {
        // Enviador SMTP real com Habilitado=false (padrão): devolve NaoConfigurado sem rede.
        var smtp = new EnviadorEmailSmtp(Options.Create(new OpcoesEmail()), NullLogger<EnviadorEmailSmtp>.Instance);
        var resultado = await smtp.EnviarAsync(new MensagemEmail("x", "<p>x</p>", Array.Empty<AnexoEmail>()), CancellationToken.None);
        Assert.Equal(StatusEnvioEmail.NaoConfigurado, resultado.Status);

        _amb.Email.Resultado = ResultadoEnvioEmail.NaoConfigurado();
        var g = _amb.CriarGerenciador(_orq);
        _amb.EscreverXml("a.xml", "100", "70001");

        await _amb.RodarAteOciosoAsync(g);

        Assert.Equal(Core.Pacote.EstadoPacote.Ocioso, g.Estado);
        Assert.Single(_amb.RelatoriosGerados(".pdf"));
        Assert.Single(_amb.RelatoriosGerados(".html"));
        Assert.Single(_amb.Arquivos("Processados"));
    }
}
