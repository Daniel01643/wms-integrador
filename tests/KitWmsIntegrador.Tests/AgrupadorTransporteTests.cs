using KitWmsIntegrador.Core.Agrupamento;
using KitWmsIntegrador.Core.Modelos;

namespace KitWmsIntegrador.Tests;

public class AgrupadorTransporteTests
{
    private static NotaFiscal Nota(string numero, string? transporte) => new()
    {
        Numero = numero,
        NumeroTransporte = transporte,
    };

    [Fact]
    public void Agrupar_MesmoTransporte_FormaUmaOndaComVariasNotas()
    {
        var notas = new[]
        {
            Nota("1", "0130693549"),
            Nota("2", "0130693549"),
            Nota("3", "0130693967"),
        };

        var resultado = new AgrupadorTransporte().Agrupar(notas);

        Assert.Equal(3, resultado.TotalNotas);
        Assert.Equal(2, resultado.Ondas.Count);
        Assert.Empty(resultado.SemTransporte);

        var onda1 = resultado.Ondas.Single(o => o.NumeroTransporte == "0130693549");
        Assert.Equal(2, onda1.Notas.Count);

        var onda2 = resultado.Ondas.Single(o => o.NumeroTransporte == "0130693967");
        Assert.Single(onda2.Notas);
    }

    [Fact]
    public void Agrupar_NotaSemTransporte_VaiParaPendencias()
    {
        var notas = new[]
        {
            Nota("1", "0130693549"),
            Nota("2", null),
            Nota("3", ""),
        };

        var resultado = new AgrupadorTransporte().Agrupar(notas);

        Assert.Equal(3, resultado.TotalNotas);
        Assert.Single(resultado.Ondas);
        Assert.Equal(2, resultado.SemTransporte.Count);
    }

    [Fact]
    public void Agrupar_PreservaZerosEsquerdaComoChave()
    {
        var notas = new[] { Nota("1", "0130693549") };

        var resultado = new AgrupadorTransporte().Agrupar(notas);

        Assert.Equal("0130693549", resultado.Ondas.Single().NumeroTransporte);
    }
}
