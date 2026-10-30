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
            Cnpj = FormatoWms.Documento(nota.DestinatarioCnpj), // CHAVE, com máscara
            Endereco = endereco?.Logradouro ?? string.Empty,
            Numero = endereco?.Numero ?? string.Empty,
            Bairro = endereco?.Bairro ?? string.Empty,
            Cidade = endereco?.Municipio ?? string.Empty,
            Uf = endereco?.Uf ?? string.Empty,
            Cep = FormatoWms.Cep(endereco?.Cep),

            // CONFIG operacional (não hardcoded), com máscara.
            CnpjArmazem = FormatoWms.Documento(_operacional.CnpjArmazem),
            CnpjProprietaria = FormatoWms.Documento(_operacional.CnpjDepositante),
            Deposito = _operacional.Deposito,

            // Campos fixos vêm dos defaults do DTO.
            // A CONFIRMAR (msg usuário): campo "indireto" — usuário indicou "sempre IN"
            // (INDIRETO), hoje default pode estar "CL". Validar na Etapa 5/produção.
            // codigoempresa/siglaempresa/tpoperacao ainda a definir — ver TODOs no PayloadI10.
        };

        return new PayloadI10 { Empresa = new[] { empresa } };
    }

    /// <inheritdoc />
    public string ConstruirJson(NotaFiscal nota) =>
        JsonSerializer.Serialize(Construir(nota), JsonWms.Opcoes);
}
