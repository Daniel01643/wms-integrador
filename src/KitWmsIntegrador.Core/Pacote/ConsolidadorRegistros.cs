namespace KitWmsIntegrador.Core.Pacote;

/// <summary>
/// Deduplica registros por arquivo (rede de segurança para a retomada após queda e para o
/// fechamento). Regra: por arquivo (caminho, sem diferenciar maiúsculas) mantém o PRIMEIRO registro
/// com <see cref="RegistroDiario.Integrada"/> = verdadeiro; se nenhum foi integrado, mantém o
/// primeiro registrado. Assim um "integrada" nunca é substituído por um "já existia" posterior.
/// A ordem de primeira aparição dos arquivos é preservada.
/// </summary>
public static class ConsolidadorRegistros
{
    /// <summary>Devolve um registro por arquivo conforme a regra acima.</summary>
    public static List<RegistroDiario> Consolidar(IEnumerable<RegistroDiario> registros)
    {
        ArgumentNullException.ThrowIfNull(registros);
        return registros
            .GroupBy(r => r.Arquivo, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.FirstOrDefault(r => r.Integrada) ?? g.First())
            .ToList();
    }
}
