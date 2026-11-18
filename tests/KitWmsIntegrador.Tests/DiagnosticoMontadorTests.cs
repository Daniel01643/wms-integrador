using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Diagnostico;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>Montador puro do diagnóstico: agregações a partir de eventos semeados.</summary>
public sealed class DiagnosticoMontadorTests : IDisposable
{
    private static readonly DateTimeOffset Inicio = AmbienteLocal(11, 14, 0);
    private static readonly DateTimeOffset Fim = AmbienteLocal(12, 14, 0);
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "wms-diag-mont-" + Guid.NewGuid().ToString("N"));

    public DiagnosticoMontadorTests() => Directory.CreateDirectory(_pasta);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_pasta, true);
        }
        catch (IOException)
        {
        }
    }

    private static DateTimeOffset AmbienteLocal(int dia, int h, int m)
    {
        var local = new DateTime(2026, 11, dia, h, m, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static EntradaDiagnostico Entrada(
        IReadOnlyList<EventoDiagnostico> eventos, ModeloRelatorioI60? i60 = null, int linhas = 100, int grupos = 10,
        IReadOnlyList<LinhaUsuarioDevolucao>? devolucao = null, DadosSaude? saude = null) =>
        new(Fim, Inicio, Fim, eventos,
            AgregadorTelemetria.Agregar(Array.Empty<RegistroTelemetria>(), Array.Empty<EventoServico>(), Inicio, Fim),
            i60, true, devolucao ?? Array.Empty<LinhaUsuarioDevolucao>(),
            saude ?? new DadosSaude("1.0", null, 0, null, null, null, null, null), linhas, grupos);

    private static EventoDiagnostico Token(int h, int m, string backend, bool ok, int? status = null, string? msg = null) =>
        new() { Tipo = "gettoken", QuandoLocal = AmbienteLocal(12, h, m), Backend = backend, Ok = ok, Status = status, Mensagem = msg };

    [Fact]
    public void Login_AgregaPorClasseEPorBackend()
    {
        var m = MontadorDiagnostico.Montar(Entrada(new[]
        {
            Token(9, 0, "A", true), Token(9, 1, "A", false, 401), Token(9, 2, "A", false, null, "timeout"),
            Token(9, 3, "B", true), Token(9, 4, "B", false, 503), Token(9, 5, "B", false, 403), Token(9, 6, "B", false, null, "rede"),
        }));

        Assert.Equal(7, m.Login.Total);
        Assert.Equal(2, m.Login.Sucessos);
        Assert.Equal(5, m.Login.Falhas);
        Assert.Equal(1, m.Login.FalhasPorClasse.Single(c => c.Rotulo.StartsWith("401")).Quantidade);
        Assert.Equal(1, m.Login.FalhasPorClasse.Single(c => c.Rotulo.StartsWith("403")).Quantidade);
        Assert.Equal(1, m.Login.FalhasPorClasse.Single(c => c.Rotulo.StartsWith("5xx")).Quantidade);
        Assert.Equal(1, m.Login.FalhasPorClasse.Single(c => c.Rotulo == "timeout").Quantidade);
        Assert.Equal(1, m.Login.FalhasPorClasse.Single(c => c.Rotulo == "rede/outros").Quantidade);

        var b = m.Login.PorBackend;
        Assert.Equal("B", b[0].Backend);
        Assert.Equal(4, b[0].Tentativas);
        Assert.Equal(3, b[0].Falhas);
        Assert.Equal(75d, b[0].PercentualFalha, 3);
        Assert.Equal("A", b[1].Backend);
        Assert.Equal(2, b[1].Falhas);
    }

    [Fact]
    public void Login_JanelasDeQueda_FechadaEmAndamentoEInicioAntesDaJanela()
    {
        var m = MontadorDiagnostico.Montar(Entrada(new[]
        {
            new EventoDiagnostico { Tipo = "auth-recuperada", QuandoLocal = AmbienteLocal(11, 15, 0) },
            new EventoDiagnostico { Tipo = "auth-queda", QuandoLocal = AmbienteLocal(12, 9, 48), Subtipo = "inicio", Status = 401, Segundos = 60 },
            new EventoDiagnostico { Tipo = "auth-queda", QuandoLocal = AmbienteLocal(12, 9, 50), Subtipo = "escalada", Status = 401, Segundos = 120 },
            new EventoDiagnostico { Tipo = "auth-pacote-interrompido", QuandoLocal = AmbienteLocal(12, 9, 51), Subtipo = "cooldown", Quantidade = 14 },
            new EventoDiagnostico { Tipo = "auth-recuperada", QuandoLocal = AmbienteLocal(12, 9, 56) },
            new EventoDiagnostico { Tipo = "auth-queda", QuandoLocal = AmbienteLocal(12, 13, 0), Subtipo = "inicio", Status = 503, Segundos = 60 },
            new EventoDiagnostico { Tipo = "auth-pacote-interrompido", QuandoLocal = AmbienteLocal(12, 13, 1), Subtipo = "falha", Quantidade = 3 },
        }));

        var q = m.Login.Quedas;
        Assert.Equal(3, q.Count);
        Assert.Equal("início antes da janela", q[0].Situacao);
        Assert.Null(q[0].Inicio);
        Assert.Equal("recuperada", q[1].Situacao);
        Assert.Equal(AmbienteLocal(12, 9, 48), q[1].Inicio);
        Assert.Equal(TimeSpan.FromMinutes(8), q[1].Duracao);
        Assert.Equal("em andamento", q[2].Situacao);
        Assert.Null(q[2].Fim);
        Assert.Equal(3, m.Login.AtivacoesCooldown);
        Assert.Equal(2, m.Login.PacotesInterrompidos);
        Assert.Equal(17, m.Login.NotasAguardando);
    }

    [Fact]
    public void Cadastros_GatilhosAgrupadosPorMensagem_TopEOutros_ErrosProprios()
    {
        var eventos = new List<EventoDiagnostico>();
        for (var i = 0; i < 5; i++)
        {
            eventos.Add(new EventoDiagnostico { Tipo = "cadastro-gatilho", QuandoLocal = AmbienteLocal(12, 8, i), Subtipo = "I30", Nota = (100 + i).ToString(), Mensagem = "Produto A não cadastrado" });
        }

        eventos.Add(new EventoDiagnostico { Tipo = "cadastro-gatilho", QuandoLocal = AmbienteLocal(12, 8, 10), Subtipo = "I30", Nota = "100", Mensagem = "Produto B não cadastrado" });
        eventos.Add(new EventoDiagnostico { Tipo = "cadastro-gatilho", QuandoLocal = AmbienteLocal(12, 8, 11), Subtipo = "I30", Nota = "200", Mensagem = "Produto C não cadastrado" });
        eventos.Add(new EventoDiagnostico { Tipo = "cadastro-gatilho", QuandoLocal = AmbienteLocal(12, 8, 12), Subtipo = "I10", Nota = "300", Mensagem = "Empresa X não localizada" });
        eventos.Add(new EventoDiagnostico { Tipo = "cadastro-chamada", QuandoLocal = AmbienteLocal(12, 8, 20), Subtipo = "I30", Ok = true });
        eventos.Add(new EventoDiagnostico { Tipo = "cadastro-chamada", QuandoLocal = AmbienteLocal(12, 8, 21), Subtipo = "I30", Ok = true, Mensagem = "ja-existe" });
        eventos.Add(new EventoDiagnostico { Tipo = "cadastro-chamada", QuandoLocal = AmbienteLocal(12, 8, 22), Subtipo = "I30", Ok = false, Mensagem = "Unidade inválida" });
        eventos.Add(new EventoDiagnostico { Tipo = "cadastro-chamada", QuandoLocal = AmbienteLocal(12, 8, 23), Subtipo = "I30", Ok = false, Mensagem = "Unidade inválida" });
        eventos.Add(new EventoDiagnostico { Tipo = "cadastro-chamada", QuandoLocal = AmbienteLocal(12, 8, 24), Subtipo = "I10", Ok = false, Mensagem = "TimeoutException" });

        var m = MontadorDiagnostico.Montar(Entrada(eventos, grupos: 2));

        var i30 = m.Cadastros.Interfaces.Single(i => i.Interface == "I30");
        Assert.Equal(7, i30.Gatilhos);
        Assert.Equal(6, i30.NotasDistintas);
        Assert.Equal(3, i30.GatilhosPorMensagem.Count);
        Assert.Equal(new ContagemRotulo("Produto A não cadastrado", 5), i30.GatilhosPorMensagem[0]);
        Assert.Equal("outros", i30.GatilhosPorMensagem[2].Rotulo);
        Assert.Equal(1, i30.GatilhosPorMensagem[2].Quantidade);
        Assert.Equal(4, i30.Chamadas);
        Assert.Equal(1, i30.Sucessos);
        Assert.Equal(1, i30.JaExistia);
        Assert.Equal(2, i30.Erros);
        Assert.Equal(new ContagemRotulo("Unidade inválida", 2), Assert.Single(i30.ErrosPorMensagem));

        var i10 = m.Cadastros.Interfaces.Single(i => i.Interface == "I10");
        Assert.Equal(1, i10.Gatilhos);
        Assert.Equal(1, i10.Erros);
        Assert.Equal("TimeoutException", i10.ErrosPorMensagem[0].Rotulo);
    }

    [Fact]
    public void Separacao_ContagensDeI51I60EListaDeNaoAssociadas()
    {
        var problemas = new SecaoI60<LinhaNotaI60>(new[] { Linha("10", "Rejeitada", "Já associada") }, 1);
        var atrasadas = new SecaoI60<LinhaNotaI60>(new[] { Linha("20", string.Empty, string.Empty), Linha("21", string.Empty, string.Empty) }, 5);
        var i60 = new ModeloRelatorioI60
        {
            Aguardando = 4, Atrasadas = 5, Problemas = problemas, ListaAtrasadas = atrasadas,
            Ajustes = new SecaoI60<LinhaAjusteI60>(new[] { new LinhaAjusteI60("10", 1, "P", 10, 8, -2) }, 1),
        };
        var eventos = new[]
        {
            new EventoDiagnostico { Tipo = "i51-consulta", QuandoLocal = AmbienteLocal(12, 9, 0), Subtipo = "TODOS", Ok = true },
            new EventoDiagnostico { Tipo = "i51-consulta", QuandoLocal = AmbienteLocal(12, 9, 15), Subtipo = "TODOS", Ok = true, Mensagem = "vazio" },
            new EventoDiagnostico { Tipo = "i51-consulta", QuandoLocal = AmbienteLocal(12, 9, 30), Subtipo = "TODOS", Ok = false, Status = 502 },
            new EventoDiagnostico { Tipo = "i51-consulta", QuandoLocal = AmbienteLocal(12, 9, 45), Subtipo = "UNICO", Ok = false, Mensagem = "fora-do-padrao" },
            new EventoDiagnostico { Tipo = "i51-ciclo", QuandoLocal = AmbienteLocal(12, 10, 0), Quantidade = 3, Quantidade2 = 1 },
            new EventoDiagnostico { Tipo = "i51-ciclo", QuandoLocal = AmbienteLocal(12, 11, 0), Quantidade = 2, Quantidade2 = 0 },
            new EventoDiagnostico { Tipo = "i60-desfecho", QuandoLocal = AmbienteLocal(12, 10, 5), Subtipo = "associada", Nota = "1" },
            new EventoDiagnostico { Tipo = "i60-desfecho", QuandoLocal = AmbienteLocal(12, 10, 6), Subtipo = "associada", Nota = "2" },
            new EventoDiagnostico { Tipo = "i60-desfecho", QuandoLocal = AmbienteLocal(12, 10, 7), Subtipo = "imp", Nota = "3" },
            new EventoDiagnostico { Tipo = "i60-alerta", QuandoLocal = AmbienteLocal(12, 10, 30), Subtipo = "Atrasada", Quantidade = 2, Ok = true },
            new EventoDiagnostico { Tipo = "i60-alerta", QuandoLocal = AmbienteLocal(12, 12, 30), Subtipo = "Atrasada", Quantidade = 1, Ok = true },
        };

        var s = MontadorDiagnostico.Montar(Entrada(eventos, i60)).Separacao;

        Assert.True(s.I51Habilitada);
        Assert.Equal(4, s.Consultas);
        Assert.Equal(1, s.ConsultasOk);
        Assert.Equal(1, s.ConsultasVazias);
        Assert.Contains(new ContagemRotulo("HTTP 502", 1), s.ConsultasFalhas);
        Assert.Contains(new ContagemRotulo("fora-do-padrao", 1), s.ConsultasFalhas);
        Assert.Equal(5, s.NovasSeparadas);
        Assert.Equal(1, s.NovasAtrasadas);
        Assert.Equal(new ContagemRotulo("associada", 2), s.I60PorDesfecho[0]);
        Assert.Equal(new ContagemRotulo("Atrasada", 3), Assert.Single(s.AlertasPorEvento));
        Assert.Equal(1, s.AjustesQuantidade);
        Assert.Equal(4, s.Aguardando);
        Assert.Equal(5, s.Atrasadas);
        Assert.Equal(6, s.NaoAssociadas.Total);
        Assert.Equal(3, s.NaoAssociadas.Linhas.Count);
        Assert.Equal(3, s.NaoAssociadas.NaoExibidas);
        Assert.Equal("10", s.NaoAssociadas.Linhas[0].Nota);
        Assert.Equal("Rejeitada", s.NaoAssociadas.Linhas[0].Motivo);
        Assert.Equal("atrasada", s.NaoAssociadas.Linhas[1].Motivo);
    }

    [Fact]
    public void Separacao_SemI51_MarcaDesligada()
    {
        var s = MontadorDiagnostico.Montar(Entrada(Array.Empty<EventoDiagnostico>())).Separacao;

        Assert.False(s.I51Habilitada);
        Assert.Equal(0, s.NaoAssociadas.Total);
    }

    [Fact]
    public void ListaDeNaoAssociadas_RespeitaOTetoEContaOsNaoExibidos()
    {
        var muitas = Enumerable.Range(1, 30).Select(i => Linha(i.ToString(), "Rejeitada", "x")).ToList();
        var i60 = new ModeloRelatorioI60 { Problemas = new SecaoI60<LinhaNotaI60>(muitas, 30) };

        var s = MontadorDiagnostico.Montar(Entrada(Array.Empty<EventoDiagnostico>(), i60, linhas: 10)).Separacao;

        Assert.Equal(10, s.NaoAssociadas.Linhas.Count);
        Assert.Equal(20, s.NaoAssociadas.NaoExibidas);
    }

    [Fact]
    public void Mensagens_SaoSanitizadasETruncadasNoMontador()
    {
        var m = MontadorDiagnostico.Montar(Entrada(new[]
        {
            new EventoDiagnostico { Tipo = "cadastro-gatilho", QuandoLocal = AmbienteLocal(12, 8, 0), Subtipo = "I10", Nota = "1", Mensagem = "falha Bearer abc.def.ghi " + new string('x', 400) },
        }));

        var rotulo = m.Cadastros.Interfaces.Single(i => i.Interface == "I10").GatilhosPorMensagem.Single().Rotulo;
        Assert.DoesNotContain("abc.def.ghi", rotulo);
        Assert.True(rotulo.Length <= 203);
    }

    [Fact]
    public void Saude_UsaDadosELimpezaMaisRecenteDaJanela()
    {
        var saude = new DadosSaude("2026.11.18-1", AmbienteLocal(12, 7, 0), 2, 100, 200, 300, 400, 500);
        var m = MontadorDiagnostico.Montar(Entrada(new[]
        {
            new EventoDiagnostico { Tipo = "limpeza", QuandoLocal = AmbienteLocal(11, 20, 0), Quantidade = 1, Quantidade2 = 1, Ok = true },
            new EventoDiagnostico { Tipo = "limpeza", QuandoLocal = AmbienteLocal(12, 3, 0), Quantidade = 148, Quantidade2 = 12, Ok = false },
        }, saude: saude)).Saude;

        Assert.Equal("2026.11.18-1", m.Versao);
        Assert.Equal(2, m.Reinicios);
        Assert.Equal(100, m.EspacoLivreEntrada);
        Assert.Equal(500, m.TamanhoEstado);
        Assert.Equal(148, m.Limpeza!.Arquivos);
        Assert.False(m.Limpeza.SemErros);
        Assert.Equal("não coletado", m.LogWarningError);
    }

    [Fact]
    public void Pacotes_SomaArquivosIgnoradosEMarcaLotesDivididosComoNaoColetado()
    {
        var m = MontadorDiagnostico.Montar(Entrada(new[]
        {
            new EventoDiagnostico { Tipo = "arquivos-ignorados", QuandoLocal = AmbienteLocal(12, 8, 0), Quantidade = 3 },
            new EventoDiagnostico { Tipo = "arquivos-ignorados", QuandoLocal = AmbienteLocal(12, 9, 0), Quantidade = 2 },
        }));

        Assert.Equal(5, m.Pacotes.ArquivosIgnorados);
        Assert.Equal("não coletado", m.Pacotes.LotesDivididos);
    }

    [Fact]
    public void Devolucao_AuditoriaComLinhasDe7E8Campos_ContaPorUsuarioSoNaJanela()
    {
        var pasta = Path.Combine(_pasta, "aud");
        Directory.CreateDirectory(pasta);
        File.WriteAllLines(Path.Combine(pasta, "devolucao_20261112.log"), new[]
        {
            // 8 campos (com usuário)
            "2026-11-12 09:00:00;10.0.0.1;maria;CHAVE1;100;Enviada;T1;ok",
            "2026-11-12 09:01:00;10.0.0.1;maria;CHAVE2;101;ErroWms;T1;recusada pelo WMS",
            "2026-11-12 09:02:00;10.0.0.2;joao;CHAVE3;102;Enviada;T2;ok",
            // 7 campos (legado, sem usuário)
            "2026-11-12 10:00:00;10.0.0.3;CHAVE4;103;Enviada;T3;ok",
            // fora da janela (depois do fim às 14:00)
            "2026-11-12 15:00:00;10.0.0.1;maria;CHAVE5;104;Enviada;T4;ok",
            "linha inválida",
        });
        File.WriteAllLines(Path.Combine(pasta, "devolucao_20261111.log"), new[]
        {
            "2026-11-11 08:00:00;10.0.0.1;maria;CHAVE6;105;Enviada;T5;fora da janela por ser antes do início",
            "2026-11-11 16:00:00;10.0.0.1;joao;CHAVE7;106;Enviada;T6;ok",
        });
        var leitor = new LeitorAuditoriaDevolucao(
            Options.Create(new OpcoesPasta { Entrada = _pasta }), Options.Create(new OpcoesDevolucao { PastaAuditoria = pasta }),
            NullLogger<LeitorAuditoriaDevolucao>.Instance);

        var porUsuario = leitor.Ler(Inicio, Fim);
        var m = MontadorDiagnostico.Montar(Entrada(Array.Empty<EventoDiagnostico>(), devolucao: porUsuario)).Devolucao;

        Assert.Equal(5, m.Total);
        Assert.Equal(4, m.Sucessos);
        Assert.Equal(1, m.Falhas);
        var joao = m.PorUsuario.Single(u => u.Usuario == "joao");
        Assert.Equal((2, 2, 0), (joao.Total, joao.Sucessos, joao.Falhas));
        var maria = m.PorUsuario.Single(u => u.Usuario == "maria");
        Assert.Equal((2, 1, 1), (maria.Total, maria.Sucessos, maria.Falhas));
        Assert.Equal(1, m.PorUsuario.Single(u => u.Usuario == LeitorAuditoriaDevolucao.UsuarioNaoInformado).Total);
    }

    [Fact]
    public void Devolucao_SemArquivo_VazioENuncaLanca()
    {
        var leitor = new LeitorAuditoriaDevolucao(
            Options.Create(new OpcoesPasta { Entrada = _pasta }), Options.Create(new OpcoesDevolucao { PastaAuditoria = Path.Combine(_pasta, "inexistente") }),
            NullLogger<LeitorAuditoriaDevolucao>.Instance);

        Assert.Empty(leitor.Ler(Inicio, Fim));
    }

    [Fact]
    public void ProvedorSaudeReal_SomaPastaEToleraCaminhoInvalido()
    {
        var pasta = Path.Combine(_pasta, "tam");
        Directory.CreateDirectory(Path.Combine(pasta, "sub"));
        File.WriteAllBytes(Path.Combine(pasta, "a.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(pasta, "sub", "b.bin"), new byte[50]);
        var provedor = new ProvedorSaudeSistema();

        Assert.Equal(150, provedor.TamanhoPasta(pasta));
        Assert.Null(provedor.TamanhoPasta(Path.Combine(_pasta, "nao-existe")));
        Assert.Null(provedor.TamanhoPasta("\0inválido"));
        Assert.True(provedor.EspacoLivre(_pasta) > 0);
        Assert.Null(provedor.EspacoLivre(string.Empty));
    }

    private static LinhaNotaI60 Linha(string nota, string motivo, string mensagem) =>
        new(nota, "6", "PUXADA", "T1", string.Empty, "pac", "Problema", "1 h", "agora", "INE", motivo, mensagem, string.Empty, 0);
}
