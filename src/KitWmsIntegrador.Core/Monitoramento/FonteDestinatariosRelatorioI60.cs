using KitWmsIntegrador.Core.Configuracao;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Destinatários do relatório diário da I51/I60: o txt de <c>RelatorioI60:CaminhoDestinatarios</c>
/// (padrão <c>&lt;telemetria&gt;/destinatarios-i60.txt</c>), relido a cada envio; vazio cai na lista do
/// resumo semanal.
/// </summary>
public sealed class FonteDestinatariosRelatorioI60
{
    /// <summary>Nome padrão do arquivo.</summary>
    public const string NomeArquivoPadrao = "destinatarios-i60.txt";

    private readonly RegistradorTelemetria _telemetria;
    private readonly FonteDestinatariosResumo _resumo;
    private readonly OpcoesRelatorioI60 _opcoes;

    /// <summary>Cria a fonte.</summary>
    public FonteDestinatariosRelatorioI60(
        RegistradorTelemetria telemetria, FonteDestinatariosResumo resumo, IOptions<OpcoesRelatorioI60> opcoes)
    {
        _telemetria = telemetria ?? throw new ArgumentNullException(nameof(telemetria));
        _resumo = resumo ?? throw new ArgumentNullException(nameof(resumo));
        _opcoes = opcoes?.Value ?? throw new ArgumentNullException(nameof(opcoes));
    }

    /// <summary>Caminho do arquivo.</summary>
    public string Caminho =>
        string.IsNullOrWhiteSpace(_opcoes.CaminhoDestinatarios)
            ? Path.Combine(_telemetria.PastaBase, NomeArquivoPadrao)
            : _opcoes.CaminhoDestinatarios;

    /// <summary>Lê os destinatários (relê o arquivo a cada chamada).</summary>
    public IReadOnlyList<string> Ler()
    {
        var emails = LeitorListaEmails.Ler(Caminho);
        return emails.Count > 0 ? emails : _resumo.Ler();
    }
}
