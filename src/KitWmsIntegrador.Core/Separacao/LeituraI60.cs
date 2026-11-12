using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Orquestrador;

namespace KitWmsIntegrador.Core.Separacao;

/// <summary>
/// Leitura de uma resposta da I60: mensagens, ressalvas (bloco <c>Ressalvas : {...}</c>) e os
/// sinais de sucesso/pendência. O casamento de padrões usa <see cref="TextoNormalizado"/> sobre
/// mensagens E ressalvas.
/// </summary>
public sealed record LeituraI60(
    IReadOnlyList<string> Mensagens, IReadOnlyList<string> Ressalvas, bool Sucesso, bool Pendencia,
    string Transacao, string IdDocumento)
{
    /// <summary>Lê a resposta do WMS.</summary>
    public static LeituraI60 Ler(RespostaWms resposta, ClassificadorRetorno classificador)
    {
        ArgumentNullException.ThrowIfNull(resposta);
        ArgumentNullException.ThrowIfNull(classificador);
        var mensagens = resposta.Retornos
            .Select(r => r.Mensagem)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToList();
        var ressalvas = mensagens.SelectMany(m => ParserRessalvas.Extrair(m)).ToList();
        var doc = resposta.Retornos.Select(r => r.IdDocumento).FirstOrDefault(i => !string.IsNullOrWhiteSpace(i)) ?? string.Empty;
        return new LeituraI60(
            mensagens, ressalvas, classificador.EhSucesso(resposta), classificador.EhPendencia(resposta),
            resposta.Transacao ?? string.Empty, doc);
    }

    /// <summary>Primeira mensagem (ou vazio).</summary>
    public string Primeira => Mensagens.Count > 0 ? Mensagens[0] : string.Empty;

    /// <summary>Todas as mensagens juntas (truncáveis pelo chamador).</summary>
    public string Resumo => string.Join(" | ", Mensagens);

    /// <summary>Algum padrão casa mensagem ou ressalva.</summary>
    public bool Casa(IEnumerable<string> padroes)
    {
        var lista = padroes as IReadOnlyList<string> ?? padroes.ToList();
        return TextoNormalizado.CasaAlgum(Mensagens, lista) || TextoNormalizado.CasaAlgum(Ressalvas, lista);
    }

    /// <summary>Alguma RESSALVA casa o padrão (ressalva não bloqueante não casa).</summary>
    public bool RessalvaCasa(IEnumerable<string> padroes) => TextoNormalizado.CasaAlgum(Ressalvas, padroes.ToList());
}
