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
        Assert.Empty(m.AlternateViews);
    }

    [Fact]
    public void MontarMensagem_ComDestinatariosOverride_UsaListaDoOverride()
    {
        var msg = new MensagemEmail(
            "Resumo", "<p>oi</p>", Array.Empty<AnexoEmail>(), ImagemEmbutida: null,
            DestinatariosOverride: new[] { "resumo@exemplo.com" });

        using var m = EnviadorEmailSmtp.MontarMensagem(msg, Configurada());

        Assert.Single(m.To);
        Assert.Equal("resumo@exemplo.com", m.To[0].Address);
    }

    [Fact]
    public void MontarMensagem_SemOverride_MantemDestinatariosDeOpcoesEmail()
    {
        using var m = EnviadorEmailSmtp.MontarMensagem(Msg(), Configurada());

        Assert.Equal(2, m.To.Count);
        Assert.Equal("a@exemplo.com", m.To[0].Address);
        Assert.Equal("b@exemplo.com", m.To[1].Address);
    }

    [Fact]
    public void MontarMensagem_ComImagemEmbutida_MontaAlternateViewComLinkedResourceCid()
    {
        var msg = new MensagemEmail(
            "Fechamento",
            "<p><img src=\"cid:logo-friozem\" /></p>",
            new[] { new AnexoEmail("rel.pdf", new byte[] { 1, 2, 3 }) },
            new ImagemInline("logo-friozem", new byte[] { 0x89, 0x50, 0x4E, 0x47 }));

        using var m = EnviadorEmailSmtp.MontarMensagem(msg, Configurada());

        var view = Assert.Single(m.AlternateViews);
        var recurso = Assert.Single(view.LinkedResources);
        Assert.Equal("logo-friozem", recurso.ContentId);
        Assert.Equal("image/png", recurso.ContentType.MediaType);
        // PDF continua como anexo normal.
        Assert.Single(m.Attachments);
        Assert.Equal("rel.pdf", m.Attachments[0].Name);
    }
}
