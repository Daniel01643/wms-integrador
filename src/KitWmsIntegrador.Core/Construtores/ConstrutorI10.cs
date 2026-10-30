using System.Text.Json;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores.Dtos;
using KitWmsIntegrador.Core.Modelos;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Construtores;

/// <summary>
/// Implementação do <see cref="IConstrutorI10"/>. Monta a EMPRESA a partir do
/// destinatário do XML (CNPJ/nome/endereço). Campos não disponíveis no XML ficam em
/// branco/defaults do template; armazém/proprietária/depósito vêm da config operacional.
/// </summary>
public sealed class ConstrutorI10 : IConstrutorI10
{
    private readonly OpcoesOperacionais _operacional;

    public ConstrutorI10(IOptions<OpcoesOperacionais> operacional)
    {
        _operacional = operacional.Value;
    }

    /// <inheritdoc />
    public PayloadI10 Construir(NotaFiscal nota)
    {
        ArgumentNullException.ThrowIfNull(nota);

        var endereco = nota.Endereco;

        var empresa = new EmpresaI10
        {
            Razaosocial = nota.DestinatarioNome,
            Cnpj = nota.DestinatarioCnpj, // CHAVE
            Endereco = endereco?.Logradouro ?? string.Empty,
            Numero = endereco?.Numero ?? string.Empty,
            Bairro = endereco?.Bairro ?? string.Empty,
            Cidade = endereco?.Municipio ?? string.Empty,
            Uf = endereco?.Uf ?? string.Empty,
            Cep = endereco?.Cep ?? string.Empty,

            // CONFIG operacional (não hardcoded).
            CnpjArmazem = _operacional.CnpjArmazem,
            CnpjProprietaria = _operacional.CnpjDepositante,
            Deposito = _operacional.Deposito,

            // Campos fixos (indireto="CL", ie="ISENTO", classe="B", tpoperacao="FI",
            // tpacao="I") e os "a confirmar" (codigoempresa, siglaempresa vazios) vêm
            // dos defaults do DTO — ver TODOs no PayloadI10.
        };

        return new PayloadI10 { Empresa = new[] { empresa } };
    }

    /// <inheritdoc />
    public string ConstruirJson(NotaFiscal nota) =>
        JsonSerializer.Serialize(Construir(nota), JsonWms.Opcoes);
}
