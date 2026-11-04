namespace KitWmsIntegrador.Tests.Stress;

/// <summary>
/// Fact que só executa com a variável de ambiente <c>STRESS=1</c>. Sem ela o teste aparece como
/// ignorado e o <c>dotnet test</c> padrão não fica lento.
/// </summary>
public sealed class StressFactAttribute : FactAttribute
{
    public StressFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("STRESS") != "1")
        {
            Skip = "Teste de esforço: defina STRESS=1 para executar.";
        }
    }
}
