using System.Text.Json;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Construtores.Dtos;

namespace KitWmsIntegrador.Tests.Infra;

/// <summary>
/// WMS FALSO para a I51/I60. Implementa o cliente bruto (consulta I51) e o cliente tipado (I60). NÃO faz rede.
/// TODOS devolve os documentos disponíveis AINDA NÃO puxados e os marca como puxados (não repete); UNICO devolve
/// o pesquisado quantas vezes for pedido; sem nada devolve <c>sucesso=N</c> sem documentos. Falhas e corpos
/// brutos são programáveis; cada envio da I60 é registrado.
/// </summary>
public sealed class FakeWmsI51 : IClienteWms, IClienteWmsBruto
{
    public sealed record ItemDoc(int Seq, string Cod, decimal Qtde, decimal Sep);

    public sealed record Doc(string Numero, string CnpjProp, string CnpjDest, string Tipo, string SubTipo, int Volumes, IReadOnlyList<ItemDoc> Itens);

    public sealed record Consulta(string Identificador, string Corpo, string Tipo, string Numero, string NroDocumento);

    private readonly List<Doc> _disponiveis = new();
    private readonly HashSet<Doc> _puxados = new();
    private int _chamadasI60;

    /// <summary>Consultas da I51 recebidas.</summary>
    public List<Consulta> Consultas { get; } = new();

    /// <summary>Exceções lançadas, em ordem, nas próximas consultas.</summary>
    public Queue<Exception> FalhasConsulta { get; } = new();

    /// <summary>Corpos brutos devolvidos (em ordem) antes do comportamento normal.</summary>
    public Queue<string> CorposForcados { get; } = new();

    /// <summary>Payloads da I60 recebidos.</summary>
    public List<PayloadI60> EnviosI60 { get; } = new();

    /// <summary>Resposta da I60 por (payload, índice da chamada começando em 1).</summary>
    public Func<PayloadI60, int, RespostaWms> ResponderI60 { get; set; } = (_, _) => Sucesso();

    /// <summary>Exceções lançadas, em ordem, nas próximas chamadas da I60.</summary>
    public Queue<Exception> FalhasI60 { get; } = new();

    public int ChamadasI60 => _chamadasI60;

    public static string Prop { get; } = "01838723009850";

    /// <summary>Disponibiliza um documento separado no WMS.</summary>
    public Doc Separar(string numero, string cnpjDest, IReadOnlyList<ItemDoc> itens, int volumes = 1, string cnpjProp = "01838723009850", string tipo = "PEDIDO")
    {
        var d = new Doc(numero, cnpjProp, cnpjDest, tipo, "CN", volumes, itens);
        _disponiveis.Add(d);
        return d;
    }

    public Task<string> PostBrutoAsync(string identificadorEdi, string jsonBruto, CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(jsonBruto);
        var c = doc.RootElement.GetProperty("CONSULTA");
        var tipo = c.GetProperty("TIPO").GetString() ?? string.Empty;
        var numero = c.GetProperty("numero").GetString() ?? string.Empty;
        var nro = c.GetProperty("nroDocumento").GetString() ?? string.Empty;
        Consultas.Add(new Consulta(identificadorEdi, jsonBruto, tipo, numero, nro));

        if (FalhasConsulta.Count > 0)
        {
            throw FalhasConsulta.Dequeue();
        }

        if (CorposForcados.Count > 0)
        {
            return Task.FromResult(CorposForcados.Dequeue());
        }

        IEnumerable<Doc> alvo;
        if (tipo == "TODOS")
        {
            alvo = _disponiveis.Where(d => !_puxados.Contains(d)).ToList();
            foreach (var d in alvo)
            {
                _puxados.Add(d);
            }
        }
        else
        {
            var procurado = (nro != "0" && nro.Length > 0 ? nro : numero).TrimStart('0');
            alvo = _disponiveis.Where(d => d.Numero.TrimStart('0') == procurado).ToList();
        }

        var lista = alvo.ToList();
        return Task.FromResult(lista.Count == 0 ? CorpoSemRetorno() : CorpoDe(lista));
    }

    public static string CorpoSemRetorno() =>
        JsonSerializer.Serialize(new { transacao = "1", sucesso = "N", mensagem = "Nenhum documento disponivel para retorno." });

    public static string CorpoDe(IEnumerable<Doc> docs) =>
        JsonSerializer.Serialize(new
        {
            transacao = "2",
            sucesso = "S",
            mensagem = "OK",
            documentos = docs.Select(d => new
            {
                CnpjProprietario = Mascara(d.CnpjProp),
                CnpjDestinatario = Mascara(d.CnpjDest),
                CodDestinatario = "123",
                Numero = d.Numero,
                Tipo = d.Tipo,
                SubTipo = d.SubTipo,
                Placa = "AAA0A00",
                Doca = "D1",
                itens = d.Itens.Select(i => new
                {
                    numeroSequencialItem = i.Seq,
                    codProduto = i.Cod,
                    qtdeProduto = i.Qtde.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
                    qtdeProdutoSep = i.Sep.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
                    detalhes = Array.Empty<object>(),
                }).ToArray(),
                volumes = Enumerable.Range(1, d.Volumes).Select(v => new { codVolume = "V" + v }).ToArray(),
            }).ToArray(),
        });

    private static string Mascara(string digitos) =>
        digitos.Length == 14
            ? $"{digitos[..2]}.{digitos[2..5]}.{digitos[5..8]}/{digitos[8..12]}-{digitos[12..]}"
            : digitos;

    public Task<RespostaWms> EnviarAsync(InterfaceWms identificador, object payload, CancellationToken cancellationToken = default)
    {
        if (identificador != InterfaceWms.I60 || payload is not PayloadI60 p)
        {
            throw new InvalidOperationException("FakeWmsI51 só atende a I60 tipada.");
        }

        if (FalhasI60.Count > 0)
        {
            throw FalhasI60.Dequeue();
        }

        EnviosI60.Add(p);
        return Task.FromResult(ResponderI60(p, ++_chamadasI60));
    }

    public Task<RespostaWms> EnviarAsync(InterfaceWms identificador, string jsonBruto, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ResultadoWms Classificar(RetornoWms retorno) => ResultadoWms.Rejeitado;

    // ---- Respostas prontas da I60 (textos no formato real do WMS) ----

    public static RespostaWms Sucesso() => Resp("S", "Registrado com sucesso.");

    public static RespostaWms Pendente(string complemento = "") =>
        Resp("N", "Foi importado e esta pendente na interface." + complemento);

    public static RespostaWms PendenteComRessalva(string ressalva) =>
        Resp("N", "Foi importado e esta pendente na interface. Ressalvas : {" + ressalva + "}");

    public static RespostaWms Recusa(string mensagem) => Resp("N", mensagem);

    public static RespostaWms Resp(string sucesso, string mensagem) => new()
    {
        Transacao = "T" + Guid.NewGuid().ToString("N")[..6],
        TipoEDI = "I60",
        Sucesso = sucesso,
        Retornos = new[] { new RetornoWms { Sucesso = sucesso, Mensagem = mensagem, IdDocumento = "187161" } },
    };
}
