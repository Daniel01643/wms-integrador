using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Diagnostico;
using KitWmsIntegrador.Tests.Infra;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>Diário de diagnóstico em JSONL: gravação, leitura por janela, retenção e sanitização.</summary>
public sealed class DiagnosticoRegistroTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "kitwms-diag-" + Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    private RegistroDiagnosticoArquivo Criar(string? entrada = null, OpcoesLimpeza? limpeza = null) =>
        new(
            _clock,
            Options.Create(new OpcoesPasta { Entrada = entrada ?? _raiz }),
            Options.Create(limpeza ?? new OpcoesLimpeza()),
            NullLogger<RegistroDiagnosticoArquivo>.Instance);

    private static DateTimeOffset Local(int dia, int hora, int minuto)
    {
        var dt = new DateTime(2026, 11, dia, hora, minuto, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
    }

    private static string NomeArquivo(int dia) => $"202611{dia:D2}.jsonl";

    [Fact]
    public void GravaEmUmArquivoPorDiaLocal_ELePelaJanelaDeDoisDias()
    {
        var r = Criar();
        r.Registrar(new EventoDiagnostico { Tipo = "gettoken", QuandoLocal = Local(17, 23, 50), Ok = true });
        r.Registrar(new EventoDiagnostico { Tipo = "gettoken", QuandoLocal = Local(18, 0, 10), Ok = false, Status = 401 });
        r.Registrar(new EventoDiagnostico { Tipo = "limpeza", QuandoLocal = Local(18, 9, 0), Quantidade = 3 });

        Assert.True(File.Exists(Path.Combine(r.PastaBase, NomeArquivo(17))));
        Assert.True(File.Exists(Path.Combine(r.PastaBase, NomeArquivo(18))));

        var janela = r.Ler(Local(17, 23, 0), Local(18, 12, 0));
        Assert.Equal(3, janela.Count);
        Assert.Equal(new[] { "gettoken", "gettoken", "limpeza" }, janela.Select(e => e.Tipo));
        Assert.Equal(401, janela[1].Status);
        Assert.False(janela[1].Ok);
        Assert.Equal(3, janela[2].Quantidade);

        // Janela menor: filtra pelo horário do evento, não só pelo arquivo.
        var so18 = r.Ler(Local(18, 0, 0), Local(18, 12, 0));
        Assert.Equal(2, so18.Count);
    }

    [Fact]
    public void SemQuandoLocal_UsaORelogioInjetado()
    {
        var r = Criar();
        r.Registrar(new EventoDiagnostico { Tipo = "auth-recuperada" });

        var e = Assert.Single(r.Ler(_clock.UtcNow.AddMinutes(-1), _clock.UtcNow.AddMinutes(1)));
        Assert.Equal(_clock.UtcNow, e.QuandoLocal.ToUniversalTime());
    }

    [Fact]
    public void LeituraAbreSoOsDiasDaJanela()
    {
        var r = Criar();
        r.Registrar(new EventoDiagnostico { Tipo = "a", QuandoLocal = Local(16, 10, 0) });
        r.Registrar(new EventoDiagnostico { Tipo = "b", QuandoLocal = Local(18, 10, 0) });

        // O arquivo do dia 16 fica travado para leitura: se a janela do dia 18 o abrisse, a leitura falharia.
        using var trava = new FileStream(
            Path.Combine(r.PastaBase, NomeArquivo(16)), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var lidos = r.Ler(Local(18, 0, 0), Local(18, 23, 0));

        Assert.Equal("b", Assert.Single(lidos).Tipo);
    }

    [Fact]
    public void LinhaTruncadaOuInvalida_EhIgnorada()
    {
        var r = Criar();
        r.Registrar(new EventoDiagnostico { Tipo = "ok1", QuandoLocal = Local(18, 9, 0) });
        File.AppendAllText(Path.Combine(r.PastaBase, NomeArquivo(18)), "{\"tipo\":\"truncada\",\"quandoLo\n\nlixo\n");
        r.Registrar(new EventoDiagnostico { Tipo = "ok2", QuandoLocal = Local(18, 10, 0) });

        var lidos = r.Ler(Local(18, 0, 0), Local(18, 23, 0));

        Assert.Equal(new[] { "ok1", "ok2" }, lidos.Select(e => e.Tipo));
    }

    [Fact]
    public void CaminhoInvalido_NuncaLanca()
    {
        // Entrada é um ARQUIVO: criar Estado\diagnostico sob ele falha com IOException.
        Directory.CreateDirectory(_raiz);
        var arquivo = Path.Combine(_raiz, "e-um-arquivo");
        File.WriteAllText(arquivo, "x");
        var r = Criar(arquivo);

        var ex = Record.Exception(() =>
        {
            r.Registrar(new EventoDiagnostico { Tipo = "gettoken", Ok = true });
            r.Registrar(new EventoDiagnostico { Tipo = "gettoken", Ok = true });
            Assert.Empty(r.Ler(_clock.UtcNow.AddDays(-1), _clock.UtcNow.AddDays(1)));
            Assert.Equal(0, r.Limpar(7));
        });

        Assert.Null(ex);
    }

    [Fact]
    public void PastaSomenteLeitura_NaoLanca()
    {
        var r = Criar();
        r.Registrar(new EventoDiagnostico { Tipo = "primeiro", QuandoLocal = Local(18, 9, 0) });
        var caminho = Path.Combine(r.PastaBase, NomeArquivo(18));
        File.SetAttributes(caminho, FileAttributes.ReadOnly);
        try
        {
            var ex = Record.Exception(() => r.Registrar(new EventoDiagnostico { Tipo = "segundo", QuandoLocal = Local(18, 9, 5) }));

            Assert.Null(ex);
            Assert.Equal("primeiro", Assert.Single(r.Ler(Local(18, 0, 0), Local(18, 23, 0))).Tipo);
        }
        finally
        {
            File.SetAttributes(caminho, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Retencao_ApagaSoJsonlAntigos()
    {
        var r = Criar();
        Directory.CreateDirectory(r.PastaBase);
        foreach (var nome in new[] { "20261101.jsonl", "20261110.jsonl", "20261111.jsonl", "20261118.jsonl", "observacao.txt" })
        {
            File.WriteAllText(Path.Combine(r.PastaBase, nome), "{}");
        }

        // Hoje = 18/11 (15:00 UTC); retenção de 7 dias => corte em 11/11 (mantém o 11).
        var apagados = r.Limpar(7);

        Assert.Equal(2, apagados);
        var restantes = Directory.GetFiles(r.PastaBase).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "20261111.jsonl", "20261118.jsonl", "observacao.txt" }, restantes);
    }

    [Fact]
    public void Sanitizacao_NuncaGravaSegredos()
    {
        const string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjMifQ.c2lnbmF0dXJlMTIz";
        var r = Criar();
        r.Registrar(new EventoDiagnostico
        {
            Tipo = "cadastro-chamada",
            QuandoLocal = Local(18, 9, 0),
            Mensagem = "falhou Bearer abc.def.ghi e Basic dXNlcjpwYXNz depois senha=123 e " + jwt + " e Authorization: Bearer zzz999",
            Backend = "BIG#a1b2 token=segredo-backend",
        });
        r.Registrar(new EventoDiagnostico
        {
            Tipo = "gettoken",
            QuandoLocal = Local(18, 9, 1),
            Mensagem = "password: hunter2 token: tok-12345",
        });

        var texto = File.ReadAllText(Path.Combine(r.PastaBase, NomeArquivo(18)));

        foreach (var segredo in new[]
                 {
                     "abc.def.ghi", "dXNlcjpwYXNz", "senha=123", "=123", jwt, "eyJhbGci", "zzz999", "segredo-backend", "hunter2", "tok-12345",
                 })
        {
            Assert.DoesNotContain(segredo, texto);
        }

        Assert.Contains("falhou", texto);
        Assert.Contains("BIG#a1b2", texto);
        var lidos = r.Ler(Local(18, 0, 0), Local(18, 23, 0));
        Assert.Equal(2, lidos.Count);
    }

    [Fact]
    public void Sanitizador_RemoveControlesETrunca()
    {
        Assert.Null(SanitizadorDiagnostico.Texto(null));
        Assert.Null(SanitizadorDiagnostico.Texto("   "));
        Assert.Equal("a  b c", SanitizadorDiagnostico.Texto("a\r\nb\tc"));

        var longo = SanitizadorDiagnostico.Texto(new string('x', 500))!;
        Assert.Equal(203, longo.Length);
        Assert.EndsWith("...", longo);
        Assert.Equal("Erro de cadastro normal", SanitizadorDiagnostico.Texto("Erro de cadastro normal"));
    }
}
