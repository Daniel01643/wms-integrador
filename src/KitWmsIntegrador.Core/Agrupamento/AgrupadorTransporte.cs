using KitWmsIntegrador.Core.Modelos;

namespace KitWmsIntegrador.Core.Agrupamento;

/// <summary>
/// Agrupa notas fiscais por número de transporte, formando ondas. Notas sem
/// transporte são separadas numa lista de pendências.
/// </summary>
public sealed class AgrupadorTransporte
{
    /// <summary>
    /// Agrupa <paramref name="notas"/> por <see cref="NotaFiscal.NumeroTransporte"/>.
    /// Enumera a sequência uma única vez (compatível com a varredura em streaming),
    /// retendo apenas o modelo de domínio leve — não o XML bruto.
    /// </summary>
    public ResultadoAgrupamento Agrupar(IEnumerable<NotaFiscal> notas)
    {
        ArgumentNullException.ThrowIfNull(notas);

        var porTransporte = new Dictionary<string, List<NotaFiscal>>(StringComparer.Ordinal);
        var semTransporte = new List<NotaFiscal>();
        var total = 0;

        foreach (var nota in notas)
        {
            total++;

            if (string.IsNullOrEmpty(nota.NumeroTransporte))
            {
                semTransporte.Add(nota);
                continue;
            }

            if (!porTransporte.TryGetValue(nota.NumeroTransporte, out var lista))
            {
                lista = new List<NotaFiscal>();
                porTransporte[nota.NumeroTransporte] = lista;
            }

            lista.Add(nota);
        }

        var ondas = porTransporte
            .Select(par => new Onda { NumeroTransporte = par.Key, Notas = par.Value })
            .ToList();

        return new ResultadoAgrupamento
        {
            Ondas = ondas,
            SemTransporte = semTransporte,
            TotalNotas = total,
        };
    }
}
