using KitWmsIntegrador.Core.Configuracao;
using Microsoft.Extensions.Configuration;

namespace KitWmsIntegrador.Tests;

public class OpcoesI60Tests
{
    [Fact]
    public void Habilitada_DefaultEhFalse()
    {
        // A I60 nasce desligada: o envio só ocorre se a flag for ligada explicitamente.
        Assert.False(new OpcoesI60().Habilitada);
    }

    [Fact]
    public void Bind_LeSecaoI60_HabilitadaTrue()
    {
        var configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("I60:Habilitada", "true"),
            })
            .Build();

        var opcoes = configuracao.GetSection(OpcoesI60.Secao).Get<OpcoesI60>();

        Assert.NotNull(opcoes);
        Assert.True(opcoes!.Habilitada);
    }

    [Fact]
    public void TipoOperacaoNota_DefaultEIne()
    {
        Assert.Equal("INE", new OpcoesI60().TipoOperacaoNota);
    }

    [Fact]
    public void Bind_LeTipoOperacaoNota()
    {
        var configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("I60:TipoOperacaoNota", "INV"),
            })
            .Build();

        var opcoes = configuracao.GetSection(OpcoesI60.Secao).Get<OpcoesI60>();

        Assert.Equal("INV", opcoes!.TipoOperacaoNota);
    }
}