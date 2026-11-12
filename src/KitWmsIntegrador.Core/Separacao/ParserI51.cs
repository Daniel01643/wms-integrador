using System.Globalization;
using System.Text.Json;

namespace KitWmsIntegrador.Core.Separacao;

/// <summary>Item de um documento da I51 (só o que o integrador usa).</summary>
public sealed record ItemI51(int? Sequencia, string CodProduto, decimal QtdeProduto, decimal QtdeProdutoSep);

/// <summary>Documento (pedido/doccarga separado) da I51.</summary>
public sealed record DocumentoI51(
    string Numero, string CnpjProprietario, string CnpjDestinatario, string Tipo, string SubTipo,
    int QtdeVolumes, IReadOnlyList<ItemI51> Itens);

/// <summary>Resultado da leitura de uma resposta da I51.</summary>
/// <param name="Sucesso">Campo <c>sucesso</c> (S/N).</param>
/// <param name="Mensagem">Campo <c>mensagem</c> (para log truncado).</param>
/// <param name="Documentos">Documentos válidos.</param>
/// <param name="Invalidos">Documentos ignorados por dado inválido (número vazio, quantidade não numérica).</param>
public sealed record RespostaI51(string Sucesso, string Mensagem, IReadOnlyList<DocumentoI51> Documentos, int Invalidos);

/// <summary>
/// Lê a resposta da I51 a partir do corpo bruto. Tolerante: campos desconhecidos ignorados,
/// nomes sem diferenciar maiúsculas, quantidades "24.0000" (ponto) e datas/volumes não lidos
/// (de <c>volumes[]</c> só a contagem). Quantidade inválida isola o DOCUMENTO, não a resposta.
/// </summary>
public static class ParserI51
{
    /// <summary>Tamanho máximo aceito do corpo, em caracteres (acima disso é tratado como inválido).</summary>
    public const int LimiteCorpo = 20 * 1024 * 1024;

    /// <summary>
    /// Espiada barata: JSON válido cujo <c>documentos</c> está ausente/vazio (qualquer
    /// <c>sucesso</c>) é "nada a retornar". Devolve a mensagem e o sucesso. Falso se o corpo
    /// não é JSON, é grande demais ou tem documentos.
    /// </summary>
    public static bool NadaARetornar(string corpo, out string sucesso, out string mensagem)
    {
        sucesso = string.Empty;
        mensagem = string.Empty;
        if (string.IsNullOrWhiteSpace(corpo) || corpo.Length > LimiteCorpo)
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(corpo);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            sucesso = Texto(doc.RootElement, "sucesso");
            mensagem = Texto(doc.RootElement, "mensagem");
            if (!Propriedade(doc.RootElement, "documentos", out var docs))
            {
                return true;
            }

            return docs.ValueKind is JsonValueKind.Null || (docs.ValueKind == JsonValueKind.Array && docs.GetArrayLength() == 0);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Lê a resposta completa. Lança <see cref="JsonException"/>/<see cref="InvalidDataException"/> se ilegível.</summary>
    public static RespostaI51 Ler(string corpo)
    {
        if (string.IsNullOrWhiteSpace(corpo))
        {
            throw new InvalidDataException("Corpo da I51 vazio.");
        }

        if (corpo.Length > LimiteCorpo)
        {
            throw new InvalidDataException("Corpo da I51 acima do limite.");
        }

        using var doc = JsonDocument.Parse(corpo);
        var raiz = doc.RootElement;
        if (raiz.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Corpo da I51 não é um objeto JSON.");
        }

        var documentos = new List<DocumentoI51>();
        var invalidos = 0;
        if (Propriedade(raiz, "documentos", out var docs) && docs.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in docs.EnumerateArray())
            {
                if (d.ValueKind != JsonValueKind.Object)
                {
                    invalidos++;
                    continue;
                }

                var lido = LerDocumento(d);
                if (lido is null)
                {
                    invalidos++;
                }
                else
                {
                    documentos.Add(lido);
                }
            }
        }

        return new RespostaI51(Texto(raiz, "sucesso"), Texto(raiz, "mensagem"), documentos, invalidos);
    }

    /// <summary>Número do documento sem zeros à esquerda; vazio = inválido.</summary>
    public static string NormalizarNumero(string? numero) => (numero ?? string.Empty).Trim().TrimStart('0');

    private static DocumentoI51? LerDocumento(JsonElement d)
    {
        var numero = NormalizarNumero(Texto(d, "Numero"));
        if (numero.Length == 0)
        {
            return null;
        }

        var itens = new List<ItemI51>();
        if (Propriedade(d, "itens", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var i in arr.EnumerateArray())
            {
                if (i.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (!TentarDecimal(Texto(i, "qtdeProduto"), out var qtde)
                    || !TentarDecimal(Texto(i, "qtdeProdutoSep"), out var sep))
                {
                    return null;
                }

                int? seq = int.TryParse(Texto(i, "numeroSequencialItem"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)
                    ? s : null;
                itens.Add(new ItemI51(seq, Texto(i, "codProduto"), qtde, sep));
            }
        }

        var volumes = Propriedade(d, "volumes", out var v) && v.ValueKind == JsonValueKind.Array ? v.GetArrayLength() : 0;
        return new DocumentoI51(
            numero, Digitos(Texto(d, "CnpjProprietario")), Digitos(Texto(d, "CnpjDestinatario")),
            Texto(d, "Tipo"), Texto(d, "SubTipo"), volumes, itens);
    }

    // Vazio vale 0 (item sem quantidade separada); texto não numérico invalida o documento.
    private static bool TentarDecimal(string texto, out decimal valor)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            valor = 0m;
            return true;
        }

        return decimal.TryParse(texto.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out valor);
    }

    internal static string Digitos(string? texto) => new((texto ?? string.Empty).Where(char.IsDigit).ToArray());

    private static bool Propriedade(JsonElement obj, string nome, out JsonElement valor)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, nome, StringComparison.OrdinalIgnoreCase))
            {
                valor = p.Value;
                return true;
            }
        }

        valor = default;
        return false;
    }

    private static string Texto(JsonElement obj, string nome)
    {
        if (!Propriedade(obj, nome, out var v))
        {
            return string.Empty;
        }

        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? string.Empty,
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty,
        };
    }
}
