using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Pacote;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class FonteDestinatariosFechamentoTests : IDisposable
{
    private readonly string _pasta;
    private readonly string _caminho;

    public FonteDestinatariosFechamentoTests()
    {
        _pasta = Path.Combine(Path.GetTempPath(), "wms-destfech-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_pasta);
        _caminho = Path.Combine(_pasta, FonteDestinatariosFechamento.NomeArquivoPadrao);
    }

    private FonteDestinatariosFechamento Criar(OpcoesEmail opcoes)
    {
        // Telemetria com a pasta base = _pasta, para o caminho padrão cair dentro dela.
        var telemetria = new RegistradorTelemetria(
            Options.Create(new OpcoesPasta()),
            Options.Create(new OpcoesMonitoramento { CaminhoTelemetria = _pasta }),
            NullLogger<RegistradorTelemetria>.Instance);
        return new FonteDestinatariosFechamento(
            Options.Create(opcoes), telemetria, NullLogger<FonteDestinatariosFechamento>.Instance);
    }

    [Fact]
    public void Txt_ComEmails_TemPrecedenciaSobreOpcoesEmail()
    {
        File.WriteAllText(_caminho, "a@exemplo.com\nb@exemplo.com\n");
        var fonte = Criar(new OpcoesEmail { Destinatarios = "fallback@exemplo.com" });

        var lista = fonte.Ler();

        Assert.Equal(new[] { "a@exemplo.com", "b@exemplo.com" }, lista);
    }

    [Fact]
    public void TxtAusente_UsaOpcoesEmailComoFallback()
    {
        var fonte = Criar(new OpcoesEmail { Destinatarios = "x@exemplo.com; y@exemplo.com" });

        var lista = fonte.Ler();

        Assert.Equal(new[] { "x@exemplo.com", "y@exemplo.com" }, lista);
    }

    [Fact]
    public void TxtVazio_UsaOpcoesEmailComoFallback()
    {
        File.WriteAllText(_caminho, "# só comentário\n\n   \n");
        var fonte = Criar(new OpcoesEmail { Destinatarios = "z@exemplo.com" });

        var lista = fonte.Ler();

        Assert.Equal(new[] { "z@exemplo.com" }, lista);
    }

    [Fact]
    public void TxtAusente_EOpcoesEmailVazio_RetornaVazio()
    {
        var fonte = Criar(new OpcoesEmail());

        var lista = fonte.Ler();

        Assert.Empty(lista);
    }

    [Fact]
    public void Parsing_IgnoraComentariosEVazias_ComTrim()
    {
        File.WriteAllText(
            _caminho,
            "# comentario\n  a@exemplo.com \n\n#b@exemplo.com\n  c@exemplo.com\n");
        var fonte = Criar(new OpcoesEmail());

        var lista = fonte.Ler();

        Assert.Equal(new[] { "a@exemplo.com", "c@exemplo.com" }, lista);
    }

    [Fact]
    public void Recarrega_AposEditarOArquivo()
    {
        var fonte = Criar(new OpcoesEmail());

        File.WriteAllText(_caminho, "a@exemplo.com\n");
        Assert.Equal(new[] { "a@exemplo.com" }, fonte.Ler());

        File.WriteAllText(_caminho, "a@exemplo.com\nb@exemplo.com\n");
        Assert.Equal(new[] { "a@exemplo.com", "b@exemplo.com" }, fonte.Ler());
    }

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
        {
            Directory.Delete(_pasta, recursive: true);
        }
    }
}
