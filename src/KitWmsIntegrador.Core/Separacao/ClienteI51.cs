using System.Text.Json;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Separacao;

/// <summary>Consulta da I51 (retorno de separação): TODOS e UNICO. Devolve o corpo BRUTO.</summary>
public sealed class ClienteI51
{
    /// <summary>TIPO da consulta que traz o disponível e não repete o que já foi puxado.</summary>
    public const string TipoTodos = "TODOS";

    /// <summary>TIPO da consulta que traz só o pesquisado (repetível).</summary>
    public const string TipoUnico = "UNICO";

    private readonly IClienteWmsBruto _cliente;
    private readonly OpcoesI51 _opcoes;
    private readonly OpcoesOperacionais _operacional;

    /// <summary>Cria o cliente da I51 sobre a instância única do cliente WMS.</summary>
    public ClienteI51(IClienteWmsBruto cliente, IOptions<OpcoesI51> opcoes, IOptions<OpcoesOperacionais> operacional)
    {
        _cliente = cliente ?? throw new ArgumentNullException(nameof(cliente));
        _opcoes = opcoes?.Value ?? throw new ArgumentNullException(nameof(opcoes));
        _operacional = operacional?.Value ?? throw new ArgumentNullException(nameof(operacional));
    }

    /// <summary>CNPJ usado na consulta (<c>I51:CnpjConsulta</c> ou o do depositante), com máscara.</summary>
    public string CnpjConsulta =>
        FormatoWms.Documento(string.IsNullOrWhiteSpace(_opcoes.CnpjConsulta) ? _operacional.CnpjDepositante : _opcoes.CnpjConsulta);

    /// <summary>TODOS: devolve o corpo bruto. Consome no WMS: grave em disco antes de processar.</summary>
    public Task<string> ConsultarTodosAsync(CancellationToken ct) =>
        _cliente.PostBrutoAsync(_opcoes.Identificador.Trim(), MontarCorpo(TipoTodos, null), ct);

    /// <summary>UNICO (CNPJ + número da nota): repetível.</summary>
    public Task<string> ConsultarUnicoAsync(string numero, CancellationToken ct) =>
        ConsultarUnicoAsync(CnpjConsulta, numero, ct);

    /// <summary>UNICO com CNPJ explícito.</summary>
    public Task<string> ConsultarUnicoAsync(string cnpj, string numero, CancellationToken ct)
    {
        var erro = ValidarNumeroUnico(numero);
        if (erro is not null)
        {
            throw new ArgumentException(erro, nameof(numero));
        }

        return _cliente.PostBrutoAsync(_opcoes.Identificador.Trim(), MontarCorpo(TipoUnico, numero, cnpj), ct);
    }

    /// <summary>Valida o número para o UNICO: só dígitos; até 9 (Numero) ou 20 (NroDocumento).</summary>
    public string? ValidarNumeroUnico(string? numero)
    {
        var n = (numero ?? string.Empty).Trim();
        var max = _opcoes.UnicoUsaNroDocumento ? 20 : 9;
        if (n.Length == 0 || n.Length > max || !n.All(char.IsDigit))
        {
            return $"Número inválido para o UNICO (somente dígitos, 1 a {max}).";
        }

        return null;
    }

    /// <summary>Monta o JSON da CONSULTA (público para testes).</summary>
    public string MontarCorpo(string tipo, string? numero, string? cnpj = null)
    {
        var neutros = _opcoes.ValoresNeutros;
        var valorNumero = neutros.Numero;
        var valorNro = neutros.NroDocumento;
        if (numero is not null)
        {
            var n = numero.Trim();
            if (_opcoes.UnicoUsaNroDocumento)
            {
                valorNro = n;
            }
            else
            {
                valorNumero = FormatoWms.ZerosEsquerda(n, 9);
            }
        }

        var corpo = new Dictionary<string, object>
        {
            ["CONSULTA"] = new Dictionary<string, string>
            {
                ["TIPO"] = tipo,
                ["CNPJ"] = FormatoWms.Documento(string.IsNullOrWhiteSpace(cnpj) ? CnpjConsulta : cnpj),
                ["numero"] = valorNumero,
                ["serie"] = neutros.Serie,
                ["chaveNFe"] = neutros.ChaveNFe,
                ["nroDocumento"] = valorNro,
            },
        };
        return JsonSerializer.Serialize(corpo);
    }
}
