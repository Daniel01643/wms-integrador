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

/// <summary>
/// Arquivos candidatos das pastas de entrada (raiz, Puxada e PuxadaTSP), já com tipo e origem.
/// </summary>
/// <param name="Aceitos">XML e ZIP (na PuxadaTSP, só ZIP) que seguem o fluxo de integração.</param>
/// <param name="NaoXml">Arquivos definitivos que não podem ser integrados (viram ExtensaoInvalida).</param>
public sealed record ClassificacaoPastas(
    IReadOnlyList<EntradaArquivo> Aceitos,
    IReadOnlyList<EntradaArquivo> NaoXml);

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

    /// <summary>
    /// Lista (só o nível superior, nunca descendo em Processados/Erro/Estado) a raiz da entrada
    /// (quando <see cref="OpcoesPasta.AceitarRaiz"/>), a pasta Puxada e a pasta PuxadaTSP. O tipo
    /// vem só da pasta: PuxadaTSP sobe como TSP; Puxada e raiz sobem como PUXADA. Puxada e raiz
    /// aceitam .xml e .zip; PuxadaTSP aceita só .zip (qualquer outro arquivo definitivo vira
    /// não-XML). Temporários/em-cópia ficam onde estão.
    /// </summary>
    public static ClassificacaoPastas ClassificarPastas(OpcoesPasta opcoes)
    {
        ArgumentNullException.ThrowIfNull(opcoes);
        var aceitos = new List<EntradaArquivo>();
        var naoXml = new List<EntradaArquivo>();

        void Listar(string pasta, TipoNota tipo, string origem, bool soZip)
        {
            if (!Directory.Exists(pasta))
            {
                return;
            }

            foreach (var caminho in Directory.EnumerateFiles(pasta, "*", SearchOption.TopDirectoryOnly))
            {
                var entrada = new EntradaArquivo(caminho, tipo, origem);
                if (soZip ? entrada.EhZip : entrada.EhXml || entrada.EhZip)
                {
                    aceitos.Add(entrada);
                }
                else if (!opcoes.EhExtensaoTemporaria(caminho) && opcoes.TratarNaoXmlComoErro)
                {
                    naoXml.Add(entrada);
                }
            }
        }

        if (opcoes.AceitarRaiz)
        {
            Listar(opcoes.CaminhoEntrada, TipoNota.Puxada, TipoNotaExtensoes.OrigemRaiz, soZip: false);
        }

        Listar(opcoes.CaminhoPuxada, TipoNota.Puxada, opcoes.Puxada, soZip: false);
        Listar(opcoes.CaminhoPuxadaTsp, TipoNota.Tsp, opcoes.PuxadaTsp, soZip: true);
        return new ClassificacaoPastas(aceitos, naoXml);
    }
}