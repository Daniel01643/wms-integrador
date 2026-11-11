using System.Globalization;
using System.Text;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Devolucao;

/// <summary>
/// Auditoria simples dos envios da tela: um arquivo por dia
/// (<c>devolucao_yyyyMMdd.log</c>) com uma linha por chave:
/// <c>data;ip;chave;nota;status;transacao;mensagem</c>. Sem credenciais nem JSON.
/// Pasta: <c>Devolucao:PastaAuditoria</c> ou <c>&lt;Estado&gt;\devolucao</c>. Falha de gravação só é logada.
/// </summary>
public sealed class AuditoriaDevolucao
{
    private static readonly object Trava = new();

    private readonly IClock _clock;
    private readonly OpcoesPasta _pastas;
    private readonly OpcoesDevolucao _devolucao;
    private readonly ILogger<AuditoriaDevolucao> _logger;

    public AuditoriaDevolucao(
        IClock clock,
        IOptions<OpcoesPasta> pastas,
        IOptions<OpcoesDevolucao> devolucao,
        ILogger<AuditoriaDevolucao> logger)
    {
        _clock = clock;
        _pastas = pastas.Value;
        _devolucao = devolucao.Value;
        _logger = logger;
    }

    /// <summary>Grava uma linha por chave. Nunca lança.</summary>
    public void Registrar(string? origem, string transacao, IEnumerable<ResultadoChaveDevolucao> resultados)
    {
        try
        {
            var pasta = ResolverPasta();
            Directory.CreateDirectory(pasta);
            var agora = _clock.UtcNow.ToLocalTime();
            var arquivo = Path.Combine(pasta, $"devolucao_{agora.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");
            var data = agora.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            var sb = new StringBuilder();
            foreach (var r in resultados)
            {
                sb.Append(string.Join(';',
                    data,
                    Limpar(origem, "-"),
                    r.Chave,
                    Limpar(r.Nota, "-"),
                    r.Status,
                    Limpar(transacao, "-"),
                    Limpar(r.Mensagem, "-")))
                  .Append(Environment.NewLine);
            }

            lock (Trava)
            {
                File.AppendAllText(arquivo, sb.ToString(), new UTF8Encoding(false));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao gravar a auditoria da devolução: {Mensagem}", ex.Message);
        }
    }

    private string ResolverPasta() =>
        string.IsNullOrWhiteSpace(_devolucao.PastaAuditoria)
            ? Path.Combine(_pastas.CaminhoEstado, "devolucao")
            : Path.GetFullPath(_devolucao.PastaAuditoria);

    private static string Limpar(string? valor, string padrao)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return padrao;
        }

        var limpo = valor.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();
        return limpo.Length > 300 ? limpo[..300] : limpo;
    }
}
