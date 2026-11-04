using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

public class EnviadorEmailSmtpTests
{
    private static OpcoesEmail Configurada() => new()
    {
        Habilitado = true,
        Host = "smtp.invalido.test",
        Remetente = "de@exemplo.com",
        Destinatarios = "a@exemplo.com; b@exemplo.com;;",
        AssuntoPrefixo = "[KitWms]"
    };

    private static MensagemEmail Msg() =>
        new("Fechamento", "<p>oi</p>", new[] { new AnexoEmail("rel.pdf", new byte[] { 1, 2, 3 }) });

    [Fact]
    public async Task NaoConfigurado_NaoTentaRede_E_NaoLanca()
    {
        var o = new OpcoesEmail { Habilitado = false, Host = "smtp.invalido.test" };
        var env = new EnviadorEmailSmtp(Options.Create(o), NullLogger<EnviadorEmailSmtp>.Instance);

        var r = await env.EnviarAsync(Msg(), CancellationToken.None);

        Assert.Equal(StatusEnvioEmail.NaoConfigurado, r.Status);
    }

    [Fact]
    public void CamposVazios_NaoEstaConfigurado()
    {
        Assert.False(new OpcoesEmail { Habilitado = true }.EstaConfigurado);
        Assert.True(Configurada().EstaConfigurado);
    }

    [Fact]
    public void MontarMensagem_AplicaPrefixoDestinatariosEAnexo()
    {
        using var m = EnviadorEmailSmtp.MontarMensagem(Msg(), Configurada());

        Assert.Equal("[KitWms] Fechamento", m.Subject);
        Assert.Equal(2, m.To.Count);
        Assert.Equal("b@exemplo.com", m.To[1].Address);
        Assert.Single(m.Attachments);
        Assert.Equal("rel.pdf", m.Attachments[0].Name);
        Assert.True(m.IsBodyHtml);
    }
}
