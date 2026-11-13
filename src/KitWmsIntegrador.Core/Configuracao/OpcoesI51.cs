using System.Text.RegularExpressions;

namespace KitWmsIntegrador.Core.Configuracao;

/// <summary>Valores "neutros" dos campos da CONSULTA da I51 que o integrador não usa.</summary>
public sealed class ValoresNeutrosI51
{
    /// <summary>Valor de <c>numero</c> quando não é o campo do UNICO.</summary>
    public string Numero { get; set; } = "0";

    /// <summary>Valor de <c>serie</c>.</summary>
    public string Serie { get; set; } = "0";

    /// <summary>Valor de <c>chaveNFe</c> (vazio ou 44 dígitos).</summary>
    public string ChaveNFe { get; set; } = string.Empty;

    /// <summary>Valor de <c>nroDocumento</c> quando não é o campo do UNICO.</summary>
    public string NroDocumento { get; set; } = "0";
}

/// <summary>
/// Opções do retorno de separação (I51), que passa a ser o gatilho da I60 (seção "I51").
/// Desligada por padrão: com <see cref="Habilitada"/> = false nada novo executa nem toca disco
/// e a I60 segue o comportamento anterior. Override por env: <c>I51__Habilitada</c> etc.
/// </summary>
public sealed class OpcoesI51
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string Secao = "I51";

    /// <summary>Campo da CONSULTA que recebe o número da nota no UNICO: número da nota.</summary>
    public const string CampoUnicoNumero = "Numero";

    /// <summary>Campo da CONSULTA que recebe o número da nota no UNICO: nroDocumento.</summary>
    public const string CampoUnicoNroDocumento = "NroDocumento";

    private static readonly Regex IdentificadorValido =
        new(@"^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Liga registro, consulta periódica e relatório diário. Padrão false.</summary>
    public bool Habilitada { get; set; } = false;

    /// <summary>IDENTIFICADOR EDI da I51 na query (o número pode mudar no WMS).</summary>
    public string Identificador { get; set; } = "WEBAPI-I51";

    /// <summary>Intervalo entre consultas, em minutos (1 a 1440).</summary>
    public int IntervaloMinutos { get; set; } = 5;

    /// <summary>Espera antes do 1º ciclo, em segundos (5 a 3600).</summary>
    public int AtrasoInicialSegundos { get; set; } = 20;

    /// <summary><c>Numero</c> ou <c>NroDocumento</c>: onde vai o número da nota no UNICO.</summary>
    public string CampoUnico { get; set; } = CampoUnicoNumero;

    /// <summary>
    /// Textos (separados por <c>;</c>) aceitos na mensagem de um <c>sucesso=N</c> sem documentos.
    /// Vazio = todo N sem documentos é "nada a retornar".
    /// </summary>
    public string PadroesSemRetorno { get; set; } = string.Empty;

    /// <summary>Horas sem retorno para a nota ser considerada atrasada (1 a 720).</summary>
    public int HorasParaAtraso { get; set; } = 24;

    /// <summary>CNPJ da CONSULTA; vazio usa <c>Operacional:CnpjDepositante</c>.</summary>
    public string CnpjConsulta { get; set; } = string.Empty;

    /// <summary>Mínimo entre dois UNICO da mesma nota atrasada, em minutos (5 a 1440).</summary>
    public int IntervaloUnicoMinutos { get; set; } = 60;

    /// <summary>Máximo de UNICO de recuperação por ciclo (0 a 200; 0 desliga).</summary>
    public int MaxUnicoPorCiclo { get; set; } = 10;

    /// <summary>Retenção do estado terminal, XML copiado e brutos processados, em dias (7 a 3650; padrão 30, ajustado de 60 em 13/11/2026).</summary>
    public int DiasRetencaoEstado { get; set; } = 30;

    /// <summary><c>Zerar</c> ou <c>Omitir</c>: item da nota que o WMS não devolveu.</summary>
    public string ItemSemRetorno { get; set; } = "Zerar";

    /// <summary>Tentativas de processar um bruto antes de movê-lo para falhos (1 a 20).</summary>
    public int MaxTentativasProcessarBruto { get; set; } = 3;

    /// <summary>
    /// O TODOS só roda se existir nota aguardando registrada há no máximo este número de dias
    /// (1 a 365). Evita consumir o backlog por causa de notas antigas esquecidas.
    /// </summary>
    public int DiasMaxTodos { get; set; } = 14;

    /// <summary>Valores dos campos da CONSULTA que não usamos.</summary>
    public ValoresNeutrosI51 ValoresNeutros { get; set; } = new();

    /// <summary>Verdadeiro se o item sem retorno deve ser omitido (senão, zerado).</summary>
    public bool OmitirItemSemRetorno =>
        string.Equals(ItemSemRetorno?.Trim(), "Omitir", StringComparison.OrdinalIgnoreCase);

    /// <summary>Verdadeiro se o UNICO usa o campo <c>nroDocumento</c>.</summary>
    public bool UnicoUsaNroDocumento =>
        string.Equals(CampoUnico?.Trim(), CampoUnicoNroDocumento, StringComparison.OrdinalIgnoreCase);

    /// <summary>Devolve a lista de problemas da configuração (vazia = válida).</summary>
    public IReadOnlyList<string> Validar()
    {
        var p = new List<string>();
        if (string.IsNullOrWhiteSpace(Identificador) || !IdentificadorValido.IsMatch(Identificador.Trim()))
        {
            p.Add("I51:Identificador deve ter de 1 a 64 caracteres [A-Za-z0-9_-].");
        }

        Faixa(p, "IntervaloMinutos", IntervaloMinutos, 1, 1440);
        Faixa(p, "AtrasoInicialSegundos", AtrasoInicialSegundos, 5, 3600);
        Faixa(p, "HorasParaAtraso", HorasParaAtraso, 1, 720);
        Faixa(p, "IntervaloUnicoMinutos", IntervaloUnicoMinutos, 5, 1440);
        Faixa(p, "MaxUnicoPorCiclo", MaxUnicoPorCiclo, 0, 200);
        Faixa(p, "DiasRetencaoEstado", DiasRetencaoEstado, 7, 3650);
        Faixa(p, "MaxTentativasProcessarBruto", MaxTentativasProcessarBruto, 1, 20);
        Faixa(p, "DiasMaxTodos", DiasMaxTodos, 1, 365);

        var campo = CampoUnico?.Trim() ?? string.Empty;
        if (!campo.Equals(CampoUnicoNumero, StringComparison.OrdinalIgnoreCase)
            && !campo.Equals(CampoUnicoNroDocumento, StringComparison.OrdinalIgnoreCase))
        {
            p.Add("I51:CampoUnico deve ser Numero ou NroDocumento.");
        }

        var item = ItemSemRetorno?.Trim() ?? string.Empty;
        if (!item.Equals("Zerar", StringComparison.OrdinalIgnoreCase)
            && !item.Equals("Omitir", StringComparison.OrdinalIgnoreCase))
        {
            p.Add("I51:ItemSemRetorno deve ser Zerar ou Omitir.");
        }

        if (!string.IsNullOrWhiteSpace(CnpjConsulta))
        {
            var digitos = new string(CnpjConsulta.Where(char.IsDigit).ToArray());
            if (digitos.Length != 14)
            {
                p.Add("I51:CnpjConsulta deve ter 14 dígitos (ou ficar vazio).");
            }
        }

        var chave = ValoresNeutros?.ChaveNFe ?? string.Empty;
        if (chave.Length != 0 && (chave.Length != 44 || !chave.All(char.IsDigit)))
        {
            p.Add("I51:ValoresNeutros:ChaveNFe deve ser vazio ou ter 44 dígitos.");
        }

        return p;
    }

    private static void Faixa(List<string> p, string nome, int valor, int min, int max)
    {
        if (valor < min || valor > max)
        {
            p.Add($"I51:{nome} deve estar entre {min} e {max} (valor atual: {valor}).");
        }
    }
}
