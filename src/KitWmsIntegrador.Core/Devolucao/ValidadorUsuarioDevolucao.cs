using System.Text.RegularExpressions;

namespace KitWmsIntegrador.Core.Devolucao;

/// <summary>
/// Regras do campo "usuário do WMS" da tela de devolução. É só um nome declarado para registrar
/// quem enviou (não há autenticação). Letras, números e <c>. _ - @</c> e espaço; 1 a 60 caracteres.
/// </summary>
public static class ValidadorUsuarioDevolucao
{
    public const int TamanhoMaximo = 60;

    private static readonly Regex Permitidos = new(@"^[\p{L}\p{N}._@ \-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Valida e normaliza (trim). Devolve a mensagem de erro em pt-BR, ou <c>null</c> se válido.
    /// </summary>
    public static string? Validar(string? usuario, out string normalizado)
    {
        normalizado = usuario?.Trim() ?? string.Empty;
        if (normalizado.Length == 0)
        {
            return "Informe o usuário do WMS.";
        }

        if (normalizado.Length > TamanhoMaximo)
        {
            return $"O usuário do WMS pode ter no máximo {TamanhoMaximo} caracteres.";
        }

        if (!Permitidos.IsMatch(normalizado))
        {
            return "O usuário do WMS tem caracteres não permitidos. Use letras, números, ponto, hífen, sublinhado, @ e espaço.";
        }

        return null;
    }

    /// <summary>Texto seguro para linha de auditoria/log: sem ';' nem quebras de linha, limitado a 60; "-" se vazio.</summary>
    public static string Sanitizar(string? usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario))
        {
            return "-";
        }

        var limpo = usuario.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();
        return limpo.Length > TamanhoMaximo ? limpo[..TamanhoMaximo] : limpo;
    }
}
