using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Diagnostico;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>HTML do diagnóstico: cabeçalho, 6 seções, HTML-encode e ausência de segredos.</summary>
public sealed class DiagnosticoRenderTests
{
    private static string Html(EntradaDiagnostico entrada, ModoLogoRelatorio modo = ModoLogoRelatorio.DataUri) =>
        new GeradorDiagnostico(Options.Create(new OpcoesRelatorio()))
            .GerarHtml(MontadorDiagnostico.Montar(entrada), modo).Html;

    /// <summary>HtmlEncode transforma as vogais acentuadas em entidades numéricas; decodifica para comparar o texto.</summary>
    private static string Texto(string html) => System.Net.WebUtility.HtmlDecode(html);

    [Fact]
    public void Cabecalho_TemTituloDoRelatorioDeIntegracaoESubtituloDoDiagnostico()
    {
        var html = Texto(Html(ExemplosDiagnostico.EntradaFicticia()));

        Assert.Contains("Relatório de Integração", html);
        Assert.Contains("Diagnóstico agente integrador", html);
        Assert.Contains("<img", html);
        Assert.Contains("data:image/png;base64,", html);
    }

    [Fact]
    public void TemAsSeisSecoes()
    {
        var html = Texto(Html(ExemplosDiagnostico.EntradaFicticia()));

        foreach (var titulo in new[]
        {
            "1. Login do WMS", "2. Pacotes e notas", "3. Cadastros automáticos (I10 e I30)",
            "4. Separação (I51) e associação (I60)", "5. Devolução pela I40", "6. Saúde do serviço",
        })
        {
            Assert.Contains(titulo, html);
        }
    }

    [Fact]
    public void ConteudoFicticio_AparecePorSecao()
    {
        var html = Texto(Html(ExemplosDiagnostico.EntradaFicticia()));

        Assert.Contains(ExemplosDiagnostico.BackendB, html);
        Assert.Contains("recuperada", html);
        Assert.Contains("Produto 5006566 não cadastrado", html);
        Assert.Contains("Unidade de medida inválida", html);
        Assert.Contains("Notas não associadas", html);
        Assert.Contains("USUARIO.A", html);
        Assert.Contains("2026.11.18-1 (exemplo)", html);
        Assert.Contains("não coletado", html);
    }

    [Fact]
    public void ModoCid_UsaCidEDevolveImagem()
    {
        var gerador = new GeradorDiagnostico(Options.Create(new OpcoesRelatorio()));
        var (html, imagem) = gerador.GerarHtml(MontadorDiagnostico.Montar(ExemplosDiagnostico.EntradaFicticia()), ModoLogoRelatorio.Cid);

        Assert.Contains("cid:", html);
        Assert.NotNull(imagem);
        Assert.DoesNotContain("data:image/png", html);
    }

    [Fact]
    public void SemOcorrencias_MostraMensagensDeVazio()
    {
        var entrada = ExemplosDiagnostico.EntradaFicticia(Array.Empty<EventoDiagnostico>()) with
        {
            ModeloI60 = null,
            DevolucaoHabilitada = false,
            Devolucao = Array.Empty<LinhaUsuarioDevolucao>(),
        };

        var html = Texto(Html(entrada));

        Assert.Contains("Nenhuma tentativa de login no período.", html);
        Assert.Contains("Nenhuma queda de autenticação no período.", html);
        Assert.Contains("Nenhum disparo no período.", html);
        Assert.Contains("I51 desligada", html);
        Assert.Contains("Sem envios (tela de devolução desligada).", html);
    }

    [Fact]
    public void TextoLivre_EHtmlEncoded()
    {
        var entrada = ExemplosDiagnostico.EntradaFicticia(new[]
        {
            new EventoDiagnostico
            {
                Tipo = "cadastro-gatilho", QuandoLocal = ExemplosDiagnostico.Fim.AddHours(-2), Subtipo = "I30", Nota = "1",
                Mensagem = "<script>alert('x')</script> & <b>negrito</b>",
            },
        });

        var html = Html(entrada);

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<b>negrito</b>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&amp;", html);
    }

    [Fact]
    public void SegredosSemeadosNoMontador_NaoChegamAoHtml()
    {
        const string Jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4In0.assinaturaFicticia";
        var texto = "Bearer abc.def.ghi Basic dXNlcjpwYXNz senha=senha123 " + Jwt + " token=tokenSecreto99";
        var quando = ExemplosDiagnostico.Fim.AddHours(-3);
        var entrada = ExemplosDiagnostico.EntradaFicticia(new[]
        {
            new EventoDiagnostico { Tipo = "cadastro-gatilho", QuandoLocal = quando, Subtipo = "I10", Nota = "1", Mensagem = texto },
            new EventoDiagnostico { Tipo = "cadastro-chamada", QuandoLocal = quando, Subtipo = "I10", Ok = false, Nota = "1", Mensagem = texto },
            new EventoDiagnostico { Tipo = "gettoken", QuandoLocal = quando, Ok = false, Status = 401, Backend = "BIGip#x " + texto },
            new EventoDiagnostico { Tipo = "i51-consulta", QuandoLocal = quando, Subtipo = "TODOS", Ok = false, Mensagem = texto },
        });

        var html = Html(entrada);

        foreach (var segredo in new[] { "abc.def.ghi", "dXNlcjpwYXNz", "senha123", Jwt, "tokenSecreto99" })
        {
            Assert.DoesNotContain(segredo, html);
        }
    }

    [Fact]
    public void ExemploFicticio_NaoTemDadoReal()
    {
        var html = Texto(Html(ExemplosDiagnostico.EntradaFicticia()));

        Assert.DoesNotContain("friozem.assistsoftware", html);
        Assert.DoesNotContain("@", html);
    }
}
