using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Relatorio;

namespace KitWmsIntegrador.Core.Email;

/// <summary>Decide se o e-mail de fechamento do pacote deve ser enviado.</summary>
public static class PoliticaEnvioEmail
{
    /// <summary>
    /// Aplica <see cref="OpcoesEmail.EnviarQuando"/> ao resumo do pacote. Quando o pacote não tem
    /// nenhuma nota real (<see cref="ResumoRelatorio.Total"/> == 0, ou seja, só arquivos ignorados
    /// não-XML), o e-mail NUNCA é enviado — nem com <see cref="EnviarQuando.Sempre"/> — pois não há
    /// fechamento de notas a comunicar.
    /// </summary>
    public static bool Deve(ResumoRelatorio resumo, int xmlsInvalidos, OpcoesEmail opcoes)
    {
        ArgumentNullException.ThrowIfNull(resumo);
        ArgumentNullException.ThrowIfNull(opcoes);

        if (resumo.Total == 0)
        {
            return false;
        }

        return opcoes.EnviarQuando switch
        {
            EnviarQuando.Sempre => true,
            EnviarQuando.Nunca => false,
            _ => resumo.NaoIntegradas > 0
                 || resumo.Pendentes > 0
                 || resumo.IntegradasRevisarProduto > 0
                 || xmlsInvalidos > 0
        };
    }
}
