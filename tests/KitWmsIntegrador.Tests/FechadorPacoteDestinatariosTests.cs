using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Tests.Infra;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Roteamento dos destinatários do e-mail de FECHAMENTO (via <see cref="FonteDestinatariosFechamento"/>
/// e <c>DestinatariosOverride</c>): txt editável a quente com precedência sobre
/// <c>Email:Destinatarios</c>, fallback para ele e não-envio quando ambos estão vazios.
/// </summary>
public sealed class FechadorPacoteDestinatariosTests : IDisposable
{
    private readonly AmbientePacote _amb = new();
    private readonly OrquestradorFalso _orq = new();
    private readonly string _txt;

    public FechadorPacoteDestinatariosTests()
    {
        // O txt vive dentro da raiz temporária; aponta-se Email:CaminhoDestinatarios para ele.
        _txt = Path.Combine(_amb.Raiz, "destinatarios-fechamento.txt");
        _amb.OpcoesDeEmail.CaminhoDestinatarios = _txt;
    }

    public void Dispose() => _amb.Dispose();

    [Fact]
    public async Task Txt_ComEmails_EmailVaiParaEles_PrecedeEmailDestinatarios()
    {
        _amb.OpcoesDeEmail.Destinatarios = "fallback@exemplo.com";
        File.WriteAllText(_txt, "fechamento1@exemplo.com\nfechamento2@exemplo.com\n");

        var g = _amb.CriarGerenciador(_orq);
        _amb.EscreverXml("a.xml", "100", "70001");
        await _amb.RodarAteOciosoAsync(g);

        var email = Assert.Single(_amb.Email.Enviadas);
        Assert.Equal(new[] { "fechamento1@exemplo.com", "fechamento2@exemplo.com" }, email.DestinatariosOverride!);
    }

    [Fact]
    public async Task TxtAusente_FallbackParaEmailDestinatarios()
    {
        _amb.OpcoesDeEmail.Destinatarios = "a@exemplo.com; b@exemplo.com";
        // Sem criar o txt.

        var g = _amb.CriarGerenciador(_orq);
        _amb.EscreverXml("a.xml", "100", "70001");
        await _amb.RodarAteOciosoAsync(g);

        var email = Assert.Single(_amb.Email.Enviadas);
        Assert.Equal(new[] { "a@exemplo.com", "b@exemplo.com" }, email.DestinatariosOverride!);
    }

    [Fact]
    public async Task AmbosVazios_NaoEnviaMasConcluiOFechamento()
    {
        _amb.OpcoesDeEmail.Destinatarios = string.Empty;
        // Sem criar o txt.

        var g = _amb.CriarGerenciador(_orq);
        _amb.EscreverXml("a.xml", "100", "70001");
        await _amb.RodarAteOciosoAsync(g);

        // Nada enviado, mas o fechamento concluiu: pacote voltou a Ocioso e o relatório foi gerado.
        Assert.Empty(_amb.Email.Enviadas);
        Assert.Equal(EstadoPacote.Ocioso, g.Estado);
        Assert.Single(_amb.RelatoriosGerados(".pdf"));
        Assert.Single(_amb.RelatoriosGerados(".html"));
    }

    [Fact]
    public async Task ReleAArquivoACadaEnvio_EdicaoEntreDoisPacotesRefleteNoSegundo()
    {
        File.WriteAllText(_txt, "primeiro@exemplo.com\n");

        var g = _amb.CriarGerenciador(_orq);
        _amb.EscreverXml("a.xml", "100", "70001");
        await _amb.RodarAteOciosoAsync(g);

        var primeiro = Assert.Single(_amb.Email.Enviadas);
        Assert.Equal(new[] { "primeiro@exemplo.com" }, primeiro.DestinatariosOverride!);

        // Edita o txt e fecha um segundo pacote (carimbo diferente).
        File.WriteAllText(_txt, "segundo@exemplo.com\n");
        _amb.Clock.Avancar(TimeSpan.FromSeconds(30));
        _amb.EscreverXml("b.xml", "200", "70002");
        await _amb.RodarAteOciosoAsync(g);

        Assert.Equal(2, _amb.Email.Enviadas.Count);
        Assert.Equal(new[] { "segundo@exemplo.com" }, _amb.Email.Enviadas[1].DestinatariosOverride!);
    }
}
