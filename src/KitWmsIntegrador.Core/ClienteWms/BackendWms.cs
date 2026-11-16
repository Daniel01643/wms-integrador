using System.Security.Cryptography;
using System.Text;

namespace KitWmsIntegrador.Core.ClienteWms;

/// <summary>
/// Identificação do servidor (nó do balanceador) que respondeu, extraída só dos cabeçalhos da resposta.
/// </summary>
/// <param name="Chave">Chave de contagem: nome e hash curto do cookie de afinidade (ou o cabeçalho <c>Server</c>).</param>
/// <param name="Detalhe">Texto de diagnóstico para log (nomes de cookies com hash, Server, cabeçalhos X-*). Sem segredos.</param>
/// <param name="CookieHeader">Valor do cabeçalho <c>Cookie</c> a repetir no POST do mesmo envio (afinidade). NUNCA logar.</param>
public sealed record BackendWms(string Chave, string Detalhe, string? CookieHeader)
{
    /// <summary>Sem resposta (timeout/rede) ou sem nenhum identificador.</summary>
    public static readonly BackendWms Desconhecido = new("(sem identificador)", "(sem identificador)", null);
}

/// <summary>
/// Extrai o identificador do servidor da resposta do WMS. Os cookies são tratados à mão (o handler do HttpClient fica com
/// <c>UseCookies=false</c>) para o GetToken e o POST de um envio ficarem no mesmo servidor sem prender envios diferentes.
/// </summary>
public static class IdentificadorBackendWms
{
    private static readonly string[] PalavrasSensiveis = { "token", "auth", "key", "secret", "pass", "cred" };

    /// <summary>Hash curto (8 hex de SHA-256) de um valor; serve para comparar servidores sem expor o valor.</summary>
    public static string HashCurto(string valor) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor)))[..8].ToLowerInvariant();

    /// <summary>Lê os cabeçalhos da resposta e devolve o identificador do servidor.</summary>
    public static BackendWms De(HttpResponseMessage? resposta)
    {
        if (resposta is null)
        {
            return BackendWms.Desconhecido;
        }

        var cookies = new List<(string Nome, string Valor)>();
        if (resposta.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var sc in setCookies)
            {
                var par = sc.Split(';', 2)[0];
                var i = par.IndexOf('=');
                if (i <= 0)
                {
                    continue;
                }

                var nome = par[..i].Trim();
                var valor = par[(i + 1)..].Trim();
                cookies.RemoveAll(c => string.Equals(c.Nome, nome, StringComparison.OrdinalIgnoreCase));
                cookies.Add((nome, valor));
            }
        }

        var server = resposta.Headers.TryGetValues("Server", out var s) ? Limpar(string.Join(",", s)) : string.Empty;

        // Cookie de sessão muda a cada GetToken e não identifica o servidor: fica de fora da chave (mas segue no POST).
        var afinidade = cookies.Where(c => !c.Nome.Contains("session", StringComparison.OrdinalIgnoreCase)).ToList();
        string chave;
        if (afinidade.Count > 0)
        {
            chave = string.Join("+", afinidade.Select(c => c.Nome + "#" + HashCurto(c.Valor)));
        }
        else if (server.Length > 0)
        {
            chave = "Server=" + server;
        }
        else
        {
            chave = BackendWms.Desconhecido.Chave;
        }

        var partes = new List<string>();
        if (cookies.Count > 0)
        {
            partes.Add("cookies=" + string.Join("+", cookies.Select(c => c.Nome + "#" + HashCurto(c.Valor))));
        }

        if (server.Length > 0)
        {
            partes.Add("server=" + server);
        }

        foreach (var h in resposta.Headers)
        {
            if (h.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase)
                && !PalavrasSensiveis.Any(p => h.Key.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                partes.Add(h.Key + "=" + Limpar(string.Join(",", h.Value)));
            }
        }

        var cookieHeader = cookies.Count > 0 ? string.Join("; ", cookies.Select(c => c.Nome + "=" + c.Valor)) : null;
        return new BackendWms(chave, partes.Count > 0 ? string.Join(" ", partes) : BackendWms.Desconhecido.Detalhe, cookieHeader);
    }

    private static string Limpar(string texto)
    {
        var limpo = new string(texto.Select(c => char.IsControl(c) || c == ' ' ? '_' : c).ToArray()).Trim();
        return limpo.Length <= 60 ? limpo : limpo[..60] + "...";
    }
}
