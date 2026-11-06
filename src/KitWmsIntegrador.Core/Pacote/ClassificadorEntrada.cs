using KitWmsIntegrador.Core.Configuracao;

namespace KitWmsIntegrador.Core.Pacote;

/// <summary>
/// Separa os arquivos top-level da entrada em dois grupos de ação: os XML (que seguem o fluxo
/// normal de integração) e os não-XML definitivos (que viram <see cref="CategoriaErro.ExtensaoInvalida"/>).
/// Arquivos temporários/em-cópia (<see cref="OpcoesPasta.EhExtensaoTemporaria"/>) são IGNORADOS: não
/// entram em nenhum dos dois grupos e permanecem na entrada (podem virar .xml ao fim da gravação).
/// </summary>
/// <param name="XmlOuAguardando">Arquivos .xml (case-insensitive), fluxo atual de detecção/processamento.</param>
/// <param name="NaoXmlDefinitivo">Arquivos não-XML a tratar como ExtensaoInvalida (apenas quando habilitado).</param>
public sealed record ResultadoClassificacaoEntrada(
    IReadOnlyList<string> XmlOuAguardando,
    IReadOnlyList<string> NaoXmlDefinitivo);

/// <summary>Classifica os arquivos da entrada em XML / temporário (ignorado) / não-XML definitivo.</summary>
public static class ClassificadorEntrada
{
    /// <summary>Extensão de XML (case-insensitive).</summary>
    public const string ExtensaoXml = ".xml";

    /// <summary>
    /// Classifica os <paramref name="caminhos"/> top-level conforme as <paramref name="opcoes"/>.
    /// Regra: .xml → XML; senão, temporário → ignorado; senão → não-XML definitivo (somente quando
    /// <see cref="OpcoesPasta.TratarNaoXmlComoErro"/> é verdadeiro; caso contrário, também ignora).
    /// </summary>
    public static ResultadoClassificacaoEntrada Classificar(
        IEnumerable<string> caminhos, OpcoesPasta opcoes)
    {
        ArgumentNullException.ThrowIfNull(caminhos);
        ArgumentNullException.ThrowIfNull(opcoes);

        var xml = new List<string>();
        var naoXml = new List<string>();

        foreach (var caminho in caminhos)
        {
            if (string.Equals(Path.GetExtension(caminho), ExtensaoXml, StringComparison.OrdinalIgnoreCase))
            {
                xml.Add(caminho);
                continue;
            }

            if (opcoes.EhExtensaoTemporaria(caminho))
            {
                continue;
            }

            if (opcoes.TratarNaoXmlComoErro)
            {
                naoXml.Add(caminho);
            }
        }

        return new ResultadoClassificacaoEntrada(xml, naoXml);
    }
}
