using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Monitoramento;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>Precedência dos destinatários técnicos: txt, depois Diagnostico:Destinatarios, depois ti@friozem.com.br.</summary>
public sealed class FonteDestinatariosDiagnosticoTests : IDisposable
{
    private readonly string _pasta;

    public FonteDestinatariosDiagnosticoTests()
    {
        _pasta = Path.Combine(Path.GetTempPath(), "wms-destdiag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_pasta);
    }

    private FonteDestinatariosDiagnostico Criar(OpcoesDiagnostico opcoes)
    {
        var telemetria = new RegistradorTelemetria(
            Options.Create(new OpcoesPasta()),
            Options.Create(new OpcoesMonitoramento { CaminhoTelemetria = _pasta }),
            NullLogger<RegistradorTelemetria>.Instance);
        return new FonteDestinatariosDiagnostico(
            telemetria, Options.Create(opcoes), NullLogger<FonteDestinatariosDiagnostico>.Instance);
    }

    [Fact]
    public void CaminhoPadrao_EDentroDaPastaDeTelemetria()
    {
        var fonte = Criar(new OpcoesDiagnostico());

        Assert.Equal(Path.Combine(_pasta, "destinatarios-diagnostico.txt"), fonte.Caminho);
    }

    [Fact]
    public void Txt_VenceAOpcao()
    {
        var fonte = Criar(new OpcoesDiagnostico { Destinatarios = "opcao@exemplo.com" });
        File.WriteAllText(fonte.Caminho, "# TI\n  a@exemplo.com \n\n#b@exemplo.com\nc@exemplo.com\n");

        Assert.Equal(new[] { "a@exemplo.com", "c@exemplo.com" }, fonte.Ler());
    }

    [Fact]
    public void TxtConfiguradoPorCaminho_VenceAOpcao()
    {
        var outro = Path.Combine(_pasta, "outro.txt");
        File.WriteAllText(outro, "ti2@exemplo.com\n");
        var fonte = Criar(new OpcoesDiagnostico { CaminhoDestinatarios = outro, Destinatarios = "opcao@exemplo.com" });

        Assert.Equal(new[] { "ti2@exemplo.com" }, fonte.Ler());
    }

    [Fact]
    public void TxtAusenteOuVazio_UsaAOpcaoSeparadaPorPontoEVirgula()
    {
        var fonte = Criar(new OpcoesDiagnostico { Destinatarios = " a@exemplo.com ; b@exemplo.com;;" });

        Assert.Equal(new[] { "a@exemplo.com", "b@exemplo.com" }, fonte.Ler());

        File.WriteAllText(fonte.Caminho, "# só comentário\n\n   \n");
        Assert.Equal(new[] { "a@exemplo.com", "b@exemplo.com" }, fonte.Ler());
    }

    [Fact]
    public void OpcaoVazia_CaiNaConstanteDaTi()
    {
        var fonte = Criar(new OpcoesDiagnostico { Destinatarios = " ; " });

        Assert.Equal(new[] { "ti@friozem.com.br" }, fonte.Ler());
        Assert.Equal("ti@friozem.com.br", FonteDestinatariosDiagnostico.DestinatarioPadrao);
    }

    [Fact]
    public void Recarrega_AposEditarOArquivo()
    {
        var fonte = Criar(new OpcoesDiagnostico());
        File.WriteAllText(fonte.Caminho, "a@exemplo.com\n");
        Assert.Equal(new[] { "a@exemplo.com" }, fonte.Ler());

        File.WriteAllText(fonte.Caminho, "a@exemplo.com\nb@exemplo.com\n");
        Assert.Equal(new[] { "a@exemplo.com", "b@exemplo.com" }, fonte.Ler());

        File.Delete(fonte.Caminho);
        Assert.Equal(new[] { "ti@friozem.com.br" }, fonte.Ler());
    }

    [Fact]
    public void OpcoesDiagnostico_PadroesEValidacao()
    {
        var o = new OpcoesDiagnostico();

        Assert.True(o.Habilitado);
        Assert.Equal(new TimeOnly(14, 0), o.Horario);
        Assert.Equal("[KitWms]", o.AssuntoPrefixo);
        Assert.Equal(100, o.MaxLinhasPorSecao);
        Assert.Equal(10, o.MaxGruposMensagem);
        Assert.Empty(o.Validar());

        var ruim = new OpcoesDiagnostico { HoraDoDia = "25:99", MaxLinhasPorSecao = 5 };
        Assert.Equal(new TimeOnly(14, 0), ruim.Horario);
        Assert.Equal(2, ruim.Validar().Count);
        Assert.Equal(new TimeOnly(9, 30), new OpcoesDiagnostico { HoraDoDia = "09:30" }.Horario);
    }

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
        {
            Directory.Delete(_pasta, recursive: true);
        }
    }
}
