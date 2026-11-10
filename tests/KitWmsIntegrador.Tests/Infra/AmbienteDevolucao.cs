using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Devolucao;
using KitWmsIntegrador.Core.Leitura;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests.Infra;

/// <summary>
/// Ambiente temporário da tela de devolução: pasta Entrada/Processados com os XMLs de teste,
/// auditoria em pasta temporária e <see cref="FakeClienteWms"/> (NUNCA toca o WMS).
/// </summary>
public sealed class AmbienteDevolucao : IDisposable
{
    public const string Chave1 = "35251201838723009850550060156658071604491612";
    public const string Chave2 = "35251201838723009850550060156663211093034056";
    public const string Nota1 = "15665807";
    public const string Nota2 = "15666321";

    private static readonly string PastaRecursos = Path.Combine(AppContext.BaseDirectory, "Recursos");

    public AmbienteDevolucao(OpcoesDevolucao? devolucao = null)
    {
        Raiz = Path.Combine(Path.GetTempPath(), "tela-dev-" + Guid.NewGuid().ToString("N"));
        Processados = Path.Combine(Raiz, "Processados");
        Auditoria = Path.Combine(Raiz, "auditoria");
        Directory.CreateDirectory(Processados);

        Devolucao = devolucao ?? new OpcoesDevolucao();
        if (string.IsNullOrWhiteSpace(Devolucao.NomeMotorista))
        {
            Devolucao.NomeMotorista = "MOTORISTA DEVOLUCAO";
        }

        Devolucao.PastaAuditoria = Auditoria;
        Pastas = new OpcoesPasta { Entrada = Raiz };
        Cliente = new FakeClienteWms();

        var operacional = new OpcoesOperacionais
        {
            Deposito = "JD",
            CnpjArmazem = "14.605.065/0001-75",
            CnpjDepositante = "01.838.723/0098-50",
        };
        Servico = new ServicoDevolucao(
            Cliente,
            new ConstrutorI40(Options.Create(operacional), Options.Create(Devolucao)),
            new LeitorNotaFiscalXml(),
            new AuditoriaDevolucao(
                new FakeClock(DateTimeOffset.UtcNow),
                Options.Create(Pastas),
                Options.Create(Devolucao),
                NullLogger<AuditoriaDevolucao>.Instance),
            Options.Create(Pastas),
            Options.Create(Devolucao),
            NullLogger<ServicoDevolucao>.Instance);
    }

    public string Raiz { get; }

    public string Processados { get; }

    public string Auditoria { get; }

    public OpcoesDevolucao Devolucao { get; }

    public OpcoesPasta Pastas { get; }

    public FakeClienteWms Cliente { get; }

    public ServicoDevolucao Servico { get; }

    /// <summary>Copia o XML de teste da chave para Processados\pacote_teste.</summary>
    public void CopiarXml(string chave)
    {
        var pacote = Path.Combine(Processados, "pacote_20260101_100000");
        Directory.CreateDirectory(pacote);
        var nome = $"NFE{chave}.XML";
        File.Copy(Path.Combine(PastaRecursos, nome), Path.Combine(pacote, nome), overwrite: true);
    }

    /// <summary>Resposta do WMS com um retorno por documento (sucesso, mensagem).</summary>
    public static RespostaWms Resposta(string transacao, params (string Documento, bool Sucesso, string Mensagem)[] retornos) =>
        new()
        {
            Transacao = transacao,
            TipoEDI = "I40",
            Sucesso = retornos.All(r => r.Sucesso) ? "S" : "N",
            Retornos = retornos.Select(r => new RetornoWms
            {
                Documento = r.Documento,
                Sucesso = r.Sucesso ? "S" : "N",
                Mensagem = r.Mensagem,
            }).ToList(),
        };

    /// <summary>Chave de 44 dígitos com DV válido, derivada de um prefixo de 43 dígitos.</summary>
    public static string ChaveComDv(string prefixo43)
    {
        var soma = 0;
        var peso = 2;
        for (var i = 42; i >= 0; i--)
        {
            soma += (prefixo43[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }

        var resto = soma % 11;
        return prefixo43 + (resto < 2 ? 0 : 11 - resto);
    }

    public void Dispose()
    {
        if (Directory.Exists(Raiz))
        {
            Directory.Delete(Raiz, recursive: true);
        }
    }
}
