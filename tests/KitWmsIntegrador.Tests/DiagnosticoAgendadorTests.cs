using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Diagnostico;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Relatorio;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>Agendamento (14:00 todo dia), marca de slot, destinatários e conteúdo do e-mail do diagnóstico.</summary>
public sealed class DiagnosticoAgendadorTests : IDisposable
{
    private readonly AmbienteDiagnostico _a = new();

    public void Dispose() => _a.Dispose();

    private Task Verificar() => _a.Servico.VerificarEEnviarAsync(CancellationToken.None);

    [Fact]
    public async Task AntesDas14_NaoEnvia()
    {
        _a.Ir(12, 13, 59);

        await Verificar();

        Assert.Empty(_a.Email.Enviadas);
        Assert.Empty(_a.Htmls);
        Assert.Null(_a.Marca.Ler());
    }

    [Fact]
    public async Task Slot14h_EnviaUmaVez_ProximoCicloNaoReenvia()
    {
        _a.Ir(12, 14, 0);
        await Verificar();
        _a.Ir(12, 14, 1);
        await Verificar();
        _a.Ir(12, 18, 30);
        await Verificar();

        Assert.Single(_a.Email.Enviadas);
        Assert.Single(_a.Htmls);
        Assert.Equal(AmbienteDiagnostico.Local(12, 14, 0), _a.Marca.Ler());
    }

    [Fact]
    public async Task Reinicio_ComSlotDeHojeMarcado_NaoReenvia()
    {
        _a.Ir(12, 14, 0);
        await Verificar();

        _a.NovoServico();
        _a.Ir(12, 15, 0);
        await Verificar();

        Assert.Single(_a.Email.Enviadas);
    }

    [Fact]
    public async Task SlotPerdido_ServidorLigadoAs16h_EnviaUmaVezSo()
    {
        _a.Ir(12, 16, 0);
        await Verificar();
        _a.Ir(12, 16, 1);
        await Verificar();

        Assert.Single(_a.Email.Enviadas);
        Assert.Equal(AmbienteDiagnostico.Local(12, 14, 0), _a.Marca.Ler());
    }

    [Fact]
    public async Task ProximoDia_ReabreSoNoSlotDeHoje()
    {
        _a.Ir(12, 14, 0);
        await Verificar();
        _a.Ir(16, 9, 0);
        await Verificar();
        _a.Ir(16, 14, 0);
        await Verificar();

        Assert.Equal(2, _a.Email.Enviadas.Count);
    }

    [Fact]
    public async Task SabadoEDomingo_Enviam()
    {
        _a.Ir(14, 14, 5);
        Assert.Equal(DayOfWeek.Saturday, AmbienteDiagnostico.Local(14, 14, 5).DayOfWeek);
        await Verificar();
        _a.Ir(15, 14, 5);
        Assert.Equal(DayOfWeek.Sunday, AmbienteDiagnostico.Local(15, 14, 5).DayOfWeek);
        await Verificar();

        Assert.Equal(2, _a.Email.Enviadas.Count);
    }

    [Fact]
    public async Task HabilitadoFalse_NaoGeraNemEnvia()
    {
        _a.Opcoes.Habilitado = false;
        _a.Ir(12, 14, 30);

        await Verificar();

        Assert.Empty(_a.Email.Enviadas);
        Assert.Empty(_a.Htmls);
        Assert.Null(_a.Marca.Ler());
    }

    [Fact]
    public async Task EmailDesabilitado_GravaHtmlEMarca_SemEnviar()
    {
        _a.OpcoesEmail.Habilitado = false;
        _a.Ir(12, 14, 0);

        await Verificar();

        Assert.Empty(_a.Email.Enviadas);
        Assert.Single(_a.Htmls);
        Assert.NotNull(_a.Marca.Ler());
    }

    [Fact]
    public async Task FalhaDeEmail_AindaMarcaOSlot()
    {
        _a.Email.Resultado = ResultadoEnvioEmail.Falhou("smtp fora");
        _a.Ir(12, 14, 0);

        await Verificar();
        _a.Ir(12, 14, 1);
        await Verificar();

        Assert.Single(_a.Email.Enviadas);
        Assert.Single(_a.Htmls);
        Assert.NotNull(_a.Marca.Ler());
    }

    [Fact]
    public async Task FalhaDeDisco_NaoMarcaESeguirTentando()
    {
        // O "diretório" de saída é um arquivo: criar a pasta de saída falha.
        var bloqueio = Path.Combine(_a.Amb.Raiz, "bloqueio");
        File.WriteAllText(bloqueio, "x");
        var enviador = new EnviadorDiagnostico(
            _a.Email, new GeradorDiagnostico(Options.Create(new OpcoesRelatorio())), _a.Destinatarios, Options.Create(_a.Opcoes),
            Options.Create(new OpcoesRelatorio { PastaSaida = Path.Combine(bloqueio, "dentro") }), Options.Create(_a.OpcoesEmail),
            NullLogger<EnviadorDiagnostico>.Instance);
        var servico = new ServicoDiagnostico(
            _a.Clock, _a.Coletor, _a.Marca, enviador, Options.Create(_a.Opcoes), NullLogger<ServicoDiagnostico>.Instance);
        _a.Ir(12, 14, 0);

        await servico.VerificarEEnviarAsync(CancellationToken.None);

        Assert.Null(_a.Marca.Ler());
        Assert.Empty(_a.Email.Enviadas);
    }

    [Fact]
    public async Task Email_AssuntoComPrefixoEData_ESoParaDestinatariosDeDiagnostico()
    {
        _a.Opcoes.AssuntoPrefixo = "[TESTE]";
        _a.Ir(12, 14, 0);

        await Verificar();

        var msg = Assert.Single(_a.Email.Enviadas);
        Assert.Equal("[TESTE] Diagnóstico agente integrador — 12/11/2026", msg.Assunto);
        Assert.Equal(new[] { "ti1@exemplo.com", "ti2@exemplo.com" }, msg.DestinatariosOverride);
        Assert.Empty(msg.Anexos);
        Assert.Contains("agente integrador", msg.CorpoHtml);
    }

    [Fact]
    public async Task Email_SemTxt_UsaOpcaoDeDestinatarios()
    {
        File.Delete(_a.Destinatarios.Caminho);
        _a.Ir(12, 14, 0);

        await Verificar();

        Assert.Equal(new[] { "opcao@exemplo.com" }, Assert.Single(_a.Email.Enviadas).DestinatariosOverride);
    }

    [Fact]
    public async Task GerarAgora_NaoTocaAMarca_EUsaJanelaDe24hAteAgora()
    {
        _a.Ir(12, 10, 0);
        _a.Diario.Registrar(new EventoDiagnostico { Tipo = "arquivos-ignorados", QuandoLocal = AmbienteDiagnostico.Local(11, 9, 0), Quantidade = 99 });
        _a.Diario.Registrar(new EventoDiagnostico { Tipo = "arquivos-ignorados", QuandoLocal = AmbienteDiagnostico.Local(11, 11, 0), Quantidade = 4 });

        var (html, enviado) = await _a.Servico.GerarAgoraAsync(enviarEmail: true, CancellationToken.None);

        Assert.True(File.Exists(html));
        Assert.True(enviado);
        Assert.Null(_a.Marca.Ler());
        var modelo = _a.Coletor.Coletar(AmbienteDiagnostico.Local(12, 10, 0), AmbienteDiagnostico.Local(11, 10, 0), AmbienteDiagnostico.Local(12, 10, 0));
        Assert.Equal(4, modelo.Pacotes.ArquivosIgnorados);
    }

    [Fact]
    public async Task GerarAgora_SemEnviar_NaoMandaEmail()
    {
        _a.Ir(12, 10, 0);

        var (_, enviado) = await _a.Servico.GerarAgoraAsync(enviarEmail: false, CancellationToken.None);

        Assert.False(enviado);
        Assert.Empty(_a.Email.Enviadas);
        Assert.Single(_a.Htmls);
    }

    [Fact]
    public async Task SegredosSemeados_NaoAparecemNoJsonlNemNoHtmlNemNoEmail()
    {
        const string Jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4In0.assinaturaFicticia";
        var segredos = new[] { "abc.def.ghi", "dXNlcjpwYXNz", "senha123", Jwt, "tokenSecreto99" };
        var texto = "Bearer abc.def.ghi | Basic dXNlcjpwYXNz | senha=senha123 | " + Jwt + " | token: tokenSecreto99";
        var quando = AmbienteDiagnostico.Local(12, 9, 0);
        _a.Diario.Registrar(new EventoDiagnostico { Tipo = "cadastro-gatilho", QuandoLocal = quando, Subtipo = "I30", Nota = "1", Mensagem = texto });
        _a.Diario.Registrar(new EventoDiagnostico { Tipo = "cadastro-chamada", QuandoLocal = quando, Subtipo = "I30", Ok = false, Nota = "1", Mensagem = texto });
        _a.Diario.Registrar(new EventoDiagnostico { Tipo = "gettoken", QuandoLocal = quando, Ok = false, Status = 401, Backend = "BIGip#x " + texto });
        _a.Ir(12, 14, 0);

        await Verificar();

        var jsonl = string.Concat(Directory.GetFiles(_a.Diario.PastaBase, "*.jsonl").Select(File.ReadAllText));
        var html = File.ReadAllText(Assert.Single(_a.Htmls));
        var corpo = Assert.Single(_a.Email.Enviadas).CorpoHtml;
        Assert.Contains("cadastro-gatilho", jsonl);
        foreach (var segredo in segredos)
        {
            Assert.DoesNotContain(segredo, jsonl);
            Assert.DoesNotContain(segredo, html);
            Assert.DoesNotContain(segredo, corpo);
        }
    }

    [Fact]
    public void Coletor_UsaProvedorDeSaudeEAsPastasDeTrabalho()
    {
        _a.Saude.Livre = 12345;
        _a.Saude.Tamanho = 678;
        _a.Ir(12, 14, 0);

        var modelo = _a.Coletor.Coletar(AmbienteDiagnostico.Local(12, 14, 0), AmbienteDiagnostico.Local(11, 14, 0), AmbienteDiagnostico.Local(12, 14, 0));

        Assert.Equal(12345, modelo.Saude.EspacoLivreEntrada);
        Assert.Equal(12345, modelo.Saude.EspacoLivreAplicacao);
        Assert.Equal(678, modelo.Saude.TamanhoProcessados);
        Assert.Equal(678, modelo.Saude.TamanhoErro);
        Assert.Equal(678, modelo.Saude.TamanhoEstado);
        Assert.Contains("tamanho:" + _a.Amb.Pastas.CaminhoProcessados, _a.Saude.Consultados);
        Assert.Contains("tamanho:" + _a.Amb.Pastas.CaminhoErro, _a.Saude.Consultados);
        Assert.Contains("tamanho:" + _a.Amb.Pastas.CaminhoEstado, _a.Saude.Consultados);
        Assert.Contains("livre:" + _a.Amb.Pastas.CaminhoEntrada, _a.Saude.Consultados);
        Assert.Equal("não coletado", modelo.Saude.LogWarningError);
    }
}
