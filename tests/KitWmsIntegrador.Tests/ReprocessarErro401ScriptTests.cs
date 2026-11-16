using System.Diagnostics;
using System.Text;

namespace KitWmsIntegrador.Tests;

/// <summary>[Fact] que só roda se houver PowerShell no ambiente (senão fica ignorado).</summary>
public sealed class FactComPowerShellAttribute : FactAttribute
{
    public FactComPowerShellAttribute()
    {
        if (ReprocessarErro401ScriptTests.Interpretador() is null)
        {
            Skip = "PowerShell não encontrado.";
        }
    }
}

/// <summary>
/// deploy\reprocessar-erro-401.ps1 em pasta temporária e com XMLs sintéticos: -WhatIf não move nada, só o
/// sidecar com 401 volta (para a origem registrada ou o destino padrão), a segunda execução não faz nada,
/// colisão de nome gera nome único e o conteúdo das notas nunca aparece na saída nem no log.
/// </summary>
public sealed class ReprocessarErro401ScriptTests : IDisposable
{
    private const string Conteudo = "<x>CONTEUDO-SINTETICO-DA-NOTA</x>";
    private readonly string _entrada = Path.Combine(Path.GetTempPath(), "kitwms-repro-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_entrada, true);
        }
        catch (IOException)
        {
        }
    }

    private static readonly Lazy<string?> Cache = new(Descobrir);

    internal static string? Interpretador() => Cache.Value;

    private static string? Descobrir()
    {
        foreach (var nome in new[] { "powershell", "pwsh" })
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(nome, "-NoProfile -Command exit 0")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                });
                p!.WaitForExit(15000);
                if (p.ExitCode == 0)
                {
                    return nome;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        return null;
    }

    private static string Script()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidato = Path.Combine(dir.FullName, "deploy", "reprocessar-erro-401.ps1");
            if (File.Exists(candidato))
            {
                return candidato;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("deploy\\reprocessar-erro-401.ps1 não encontrado.");
    }

    private string Erro(params string[] partes) => Path.Combine(new[] { _entrada, "Erro" }.Concat(partes).ToArray());

    private void Nota(string pacote, string nome, string mensagem, string? origem, bool comXml = true, string nota = "1234")
    {
        var pasta = Erro(pacote, "Outro");
        Directory.CreateDirectory(pasta);
        if (comXml)
        {
            File.WriteAllText(Path.Combine(pasta, nome), Conteudo);
        }

        var sidecar = $"Nota: {nota}\nTransporte: 1000000001\nTentativas: 1\nData: 2026-11-15 17:30:00\nMensagem: {mensagem}" +
                      (origem is null ? string.Empty : $"\nOrigem: {origem}");
        File.WriteAllText(Path.Combine(pasta, nome + ".motivo.txt"), sidecar);
    }

    private void Preparar()
    {
        Nota("pacote_1", "a.xml", "Excecao ao integrar: Response status code does not indicate success: 401 (Unauthorized).", "PuxadaTSP");
        Nota("pacote_1", "antigo.xml", "Excecao ao integrar: Response status code does not indicate success: 401 (Unauthorized).", null);
        Nota("pacote_1", "outro.xml", "Produto nao cadastrado", "Puxada", nota: "401");
        Nota("pacote_1", "semxml.xml", "HTTP 401", "Puxada", comXml: false);
    }

    private (int Codigo, string Saida) Rodar(params string[] extra)
    {
        var args = new StringBuilder("-NoProfile -ExecutionPolicy Bypass -File \"" + Script() + "\" -Entrada \"" + _entrada + "\"");
        foreach (var e in extra)
        {
            args.Append(' ').Append(e);
        }

        using var p = Process.Start(new ProcessStartInfo(Interpretador()!, args.ToString())
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        })!;
        p.StandardInput.Close();
        var saida = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(120000);
        return (p.ExitCode, saida);
    }

    private string[] Arquivos(string pasta) =>
        Directory.Exists(pasta)
            ? Directory.GetFiles(pasta, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(_entrada, f)).OrderBy(f => f).ToArray()
            : Array.Empty<string>();

    [FactComPowerShell]
    public void WhatIf_NaoMoveNemGravaNada()
    {
        Preparar();
        var antes = Arquivos(_entrada);

        var (codigo, saida) = Rodar("-WhatIf");

        Assert.Equal(0, codigo);
        Assert.Equal(antes, Arquivos(_entrada));
        Assert.False(Directory.Exists(Path.Combine(_entrada, "Estado")));
        Assert.DoesNotContain("CONTEUDO-SINTETICO", saida);
    }

    [FactComPowerShell]
    public void Execucao_MoveSoOsDe401_ParaAOrigemOuODestinoPadrao_ESegundaExecucaoNaoFazNada()
    {
        Preparar();

        var (codigo, saida) = Rodar("-Force");

        Assert.Equal(0, codigo);
        Assert.Equal(Conteudo, File.ReadAllText(Path.Combine(_entrada, "PuxadaTSP", "a.xml")));   // Origem registrada
        Assert.True(File.Exists(Path.Combine(_entrada, "Puxada", "antigo.xml")));                 // sem Origem: destino padrão
        Assert.True(File.Exists(Erro("pacote_1", "Outro", "outro.xml")));                          // outro motivo: fica
        Assert.True(File.Exists(Erro("pacote_1", "Outro", "outro.xml.motivo.txt")));
        Assert.True(File.Exists(Erro("pacote_1", "Outro", "semxml.xml.motivo.txt")));              // 401 sem XML: fica
        Assert.False(File.Exists(Erro("pacote_1", "Outro", "a.xml")));
        Assert.Single(Directory.GetFiles(Erro("pacote_1", "Outro"), "a.xml.motivo.txt.reprocessado-*"));
        Assert.DoesNotContain("CONTEUDO-SINTETICO", saida);

        var log = Directory.GetFiles(Path.Combine(_entrada, "Estado", "logs"), "reprocessar-erro-401-*.log").Single();
        var textoLog = File.ReadAllText(log);
        Assert.Contains("movidos=2", textoLog);
        Assert.DoesNotContain("CONTEUDO-SINTETICO", textoLog);

        var depois = Arquivos(_entrada);
        var (codigo2, saida2) = Rodar("-Force");

        Assert.Equal(0, codigo2);
        Assert.Contains("Nada a fazer", saida2);
        Assert.Equal(depois.Where(f => !f.StartsWith("Estado", StringComparison.Ordinal)),
            Arquivos(_entrada).Where(f => !f.StartsWith("Estado", StringComparison.Ordinal)));
    }

    [FactComPowerShell]
    public void ColisaoDeNome_NaoSobrescreve_GravaNomeUnico()
    {
        Preparar();
        Directory.CreateDirectory(Path.Combine(_entrada, "PuxadaTSP"));
        File.WriteAllText(Path.Combine(_entrada, "PuxadaTSP", "a.xml"), "existente");

        var (codigo, _) = Rodar("-Force");

        Assert.Equal(0, codigo);
        Assert.Equal("existente", File.ReadAllText(Path.Combine(_entrada, "PuxadaTSP", "a.xml")));
        Assert.Equal(Conteudo, File.ReadAllText(Path.Combine(_entrada, "PuxadaTSP", "a_1.xml")));
    }

    [FactComPowerShell]
    public void OrigemRaiz_VoltaParaAPastaDeEntrada_EDestinoPadraoPodeSerTsp()
    {
        Nota("pacote_2", "r.xml", "HTTP 401", "raiz");
        Nota("pacote_2", "velho.xml", "HTTP 401", null);

        var (codigo, _) = Rodar("-Force", "-DestinoPadrao", "PuxadaTSP");

        Assert.Equal(0, codigo);
        Assert.True(File.Exists(Path.Combine(_entrada, "r.xml")));
        Assert.True(File.Exists(Path.Combine(_entrada, "PuxadaTSP", "velho.xml")));
    }

    [FactComPowerShell]
    public void SemPastaDeErro_InformaESaiComZero()
    {
        Directory.CreateDirectory(_entrada);

        var (codigo, saida) = Rodar("-Force");

        Assert.Equal(0, codigo);
        Assert.Contains("Nada a reprocessar", saida);
    }
}
