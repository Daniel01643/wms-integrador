using System.Text;

namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>Resumo de mensagens longas para a coluna "Mensagem" do relatório.</summary>
public static class ResumoMensagem
{
    private const string Reticencias = "...";

    /// <summary>
    /// Normaliza espaços/quebras de linha e corta no <paramref name="limite"/> acrescentando
    /// "..." (sem partir palavra quando possível). Resultado: no máximo limite + 3 caracteres.
    /// </summary>
    public static string Resumir(string? texto, int limite = 160)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(texto.Length);
        var espaco = false;
        foreach (var c in texto.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                espaco = true;
                continue;
            }

            if (espaco && sb.Length > 0)
            {
                sb.Append(' ');
            }

            espaco = false;
            sb.Append(c);
        }

        var normalizado = sb.ToString();
        if (normalizado.Length <= limite)
        {
            return normalizado;
        }

        var corte = normalizado.LastIndexOf(' ', limite);
        // Só recua até o último espaço se isso não descartar mais da metade do limite.
        if (corte < limite / 2)
        {
            corte = limite;
        }

        return normalizado[..corte].TrimEnd() + Reticencias;
    }
}
