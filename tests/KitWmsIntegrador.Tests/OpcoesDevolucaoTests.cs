using KitWmsIntegrador.Core.Configuracao;
using Microsoft.Extensions.Configuration;

namespace KitWmsIntegrador.Tests;

public class OpcoesDevolucaoTests
{
    [Fact]
    public void Defaults_SaoOsDecididos()
    {
        var o = new OpcoesDevolucao();

        Assert.False(o.Habilitada);
        Assert.Equal("ICD", o.TipoOperacaoNota);
        Assert.Equal("DE", o.TipoNota);
        Assert.Equal(string.Empty, o.NomeMotorista);
        Assert.False(o.InverterPartes);
        Assert.Equal("PR", o.FormaTransporte);
        Assert.Equal("040", o.CodSituacaoTributaria);
        Assert.Null(o.Porta);
        Assert.Equal("Devolucao", OpcoesDevolucao.Secao);
    }

    [Fact]
    public void Bind_LeSecaoDevolucao()
    {
        var configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("Devolucao:InverterPartes", "true"),
                new KeyValuePair<string, string?>("Devolucao:NomeMotorista", "FULANO"),
                new KeyValuePair<string, string?>("Devolucao:TipoOperacaoNota", "IMC"),
                new KeyValuePair<string, string?>("Devolucao:Porta", "8085"),
            })
            .Build();

        var o = configuracao.GetSection(OpcoesDevolucao.Secao).Get<OpcoesDevolucao>();

        Assert.NotNull(o);
        Assert.True(o!.InverterPartes);
        Assert.Equal("FULANO", o.NomeMotorista);
        Assert.Equal("IMC", o.TipoOperacaoNota);
        Assert.Equal(8085, o.Porta);
    }
}
