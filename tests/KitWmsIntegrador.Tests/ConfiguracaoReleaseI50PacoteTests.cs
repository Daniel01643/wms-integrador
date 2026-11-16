using System.Text.Json.Nodes;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Leitura;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests;

/// <summary>
/// Release 16/11/2026: tipos de separação da I50 (PIC;FEF;PLL / AGL;FEF;PLL) no cabeçalho e nos itens, espera de
/// coleta de 20 s e chaves de autenticação do WMS. Lê o appsettings.json REAL do Worker.
/// </summary>
public class ConfiguracaoReleaseI50PacoteTests
{
    private static IConfigurationRoot Config() =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
            .Build();

    [Fact]
    public void OpcoesI50_Padrao_TiposDeSeparacao()
    {
        var o = new OpcoesI50();

        Assert.Equal("PIC;FEF;PLL", o.TipoSeparacao);
        Assert.Equal("AGL;FEF;PLL", o.TipoSeparacao2);
    }

    [Fact]
    public void AppsettingsDoWorker_TiposDeSeparacao()
    {
        var o = new OpcoesI50();
        Config().GetSection(OpcoesI50.Secao).Bind(o);

        Assert.Equal("PIC;FEF;PLL", o.TipoSeparacao);
        Assert.Equal("AGL;FEF;PLL", o.TipoSeparacao2);
    }

    [Fact]
    public void PayloadI50_TemOsDoisTiposNoCabecalhoENosItens()
    {
        var op = Options.Create(new OpcoesOperacionais
        {
            Deposito = "JD",
            CnpjArmazem = "14.605.065/0001-75",
            CnpjDepositante = "01.838.723/0098-50",
            CodigoDepositante = "3509308",
        });
        var opcoesI50 = new OpcoesI50();
        Config().GetSection(OpcoesI50.Secao).Bind(opcoesI50);
        var nota = new LeitorNotaFiscalXml().ParseArquivo(EscreverXml());
        var json = new ConstrutorI50(op, Options.Create(opcoesI50)).ConstruirJson(nota, "1000000001");

        var raiz = JsonNode.Parse(json)!;
        var textoTodo = raiz.ToJsonString();
        Assert.DoesNotContain("RES;FEF;PLI", textoTodo);

        var tipos = new List<(string?, string?)>();
        Coletar(raiz, tipos);
        Assert.True(tipos.Count >= 2, "esperava cabeçalho e ao menos um item");
        Assert.All(tipos, t =>
        {
            Assert.Equal("PIC;FEF;PLL", t.Item1);
            Assert.Equal("AGL;FEF;PLL", t.Item2);
        });
    }

    private static void Coletar(JsonNode? no, List<(string?, string?)> achados)
    {
        switch (no)
        {
            case JsonObject obj:
                if (obj.ContainsKey("tipoSeparacao"))
                {
                    achados.Add((obj["tipoSeparacao"]?.GetValue<string>(), obj["tipoSeparacao2"]?.GetValue<string>()));
                }

                foreach (var filho in obj)
                {
                    Coletar(filho.Value, achados);
                }

                break;
            case JsonArray arr:
                foreach (var filho in arr)
                {
                    Coletar(filho, achados);
                }

                break;
        }
    }

    private static string EscreverXml()
    {
        var caminho = Path.Combine(Path.GetTempPath(), "kitwms-i50-" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(caminho, KitWmsIntegrador.Tests.Infra.AmbientePacote.XmlBrf(7001, "1000000001"));
        return caminho;
    }

    [Fact]
    public void Pacote_Padrao_E_Appsettings_Silencio20_Janela180_Estabilizacao3()
    {
        var padrao = new OpcoesPacote();
        Assert.Equal(20, padrao.SilencioSegundos);
        Assert.Equal(180, padrao.JanelaMaximaSegundos);
        Assert.Equal(3, padrao.EstabilizacaoSegundos);

        var o = new OpcoesPacote();
        Config().GetSection(OpcoesPacote.Secao).Bind(o);
        Assert.Equal(20, o.SilencioSegundos);
        Assert.Equal(180, o.JanelaMaximaSegundos);
        Assert.Equal(3, o.EstabilizacaoSegundos);
        Assert.Equal("Silencio", o.ModoJanela);
        Assert.Empty(o.Validar());
    }

    [Fact]
    public void ConfigExemploDeProducao_TemSilencio20_E_TiposDeSeparacao()
    {
        var caminho = Localizar("deploy", "config-exemplo", "appsettings.Production.json");
        var cfg = new ConfigurationBuilder().AddJsonFile(caminho, optional: false).Build();

        Assert.Equal(20, cfg.GetValue<int>("Pacote:SilencioSegundos"));
        Assert.Equal(180, cfg.GetValue<int>("Pacote:JanelaMaximaSegundos"));
        Assert.Equal("PIC;FEF;PLL", cfg["I50:TipoSeparacao"]);
        Assert.Equal("AGL;FEF;PLL", cfg["I50:TipoSeparacao2"]);
    }

    [Fact]
    public void Wms_AppsettingsTrazAsChavesDeAutenticacao()
    {
        var o = new OpcoesWms();
        Config().GetSection(OpcoesWms.Secao).Bind(o);

        Assert.Equal(2, o.TokenTentativasImediatas);
        Assert.Equal(3, o.ValidadeMaximaTokenMinutos);
        Assert.True(o.AlertaAutenticacao);
        Assert.Equal(new[] { 60, 120, 300, 600, 900 }, o.ListaCooldown.Select(t => (int)t.TotalSeconds).ToArray());
    }

    private static string Localizar(params string[] partes)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidato = Path.Combine(new[] { dir.FullName }.Concat(partes).ToArray());
            if (File.Exists(candidato))
            {
                return candidato;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join("\\", partes));
    }
}
