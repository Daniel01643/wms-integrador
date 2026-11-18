using System.Globalization;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Devolucao;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Diagnostico;

/// <summary>
/// Leitura (somente) da auditoria da devolução: <c>devolucao_yyyyMMdd.log</c> com linhas
/// <c>data;ip;usuario;chave;nota;status;transacao;mensagem</c> (as antigas têm 7 campos, sem usuário). Devolve só
/// contagens por usuário; IP, chave e mensagem não saem daqui. Nunca lança.
/// </summary>
public sealed class LeitorAuditoriaDevolucao
{
    /// <summary>Rótulo dos envios de linhas antigas, sem a coluna de usuário.</summary>
    public const string UsuarioNaoInformado = "(não informado)";

    private readonly OpcoesPasta _pastas;
    private readonly OpcoesDevolucao _devolucao;
    private readonly ILogger<LeitorAuditoriaDevolucao> _logger;

    /// <summary>Cria o leitor.</summary>
    public LeitorAuditoriaDevolucao(
        IOptions<OpcoesPasta> pastas, IOptions<OpcoesDevolucao> devolucao, ILogger<LeitorAuditoriaDevolucao> logger)
    {
        _pastas = pastas?.Value ?? throw new ArgumentNullException(nameof(pastas));
        _devolucao = devolucao?.Value ?? throw new ArgumentNullException(nameof(devolucao));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Pasta da auditoria (<c>Devolucao:PastaAuditoria</c> ou <c>&lt;Estado&gt;\devolucao</c>).</summary>
    public string Pasta =>
        string.IsNullOrWhiteSpace(_devolucao.PastaAuditoria)
            ? Path.Combine(_pastas.CaminhoEstado, "devolucao")
            : Path.GetFullPath(_devolucao.PastaAuditoria);

    /// <summary>Contagem de envios por usuário com a data da linha em [<paramref name="inicio"/>, <paramref name="fim"/>].</summary>
    public IReadOnlyList<LinhaUsuarioDevolucao> Ler(DateTimeOffset inicio, DateTimeOffset fim)
    {
        var contagem = new Dictionary<string, (int Sucessos, int Falhas)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var ultimo = fim.ToLocalTime().Date;
            for (var dia = inicio.ToLocalTime().Date; dia <= ultimo; dia = dia.AddDays(1))
            {
                var caminho = Path.Combine(Pasta, "devolucao_" + dia.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                if (!File.Exists(caminho))
                {
                    continue;
                }

                using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                string? linha;
                while ((linha = sr.ReadLine()) is not null)
                {
                    Acumular(linha, inicio, fim, contagem);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao ler a auditoria da devolução para o diagnóstico: {Mensagem}", ex.Message);
        }

        return contagem
            .Select(kv => new LinhaUsuarioDevolucao(kv.Key, kv.Value.Sucessos + kv.Value.Falhas, kv.Value.Sucessos, kv.Value.Falhas))
            .OrderByDescending(u => u.Total).ThenBy(u => u.Usuario, StringComparer.Ordinal)
            .ToList();
    }

    private static void Acumular(
        string linha, DateTimeOffset inicio, DateTimeOffset fim, Dictionary<string, (int Sucessos, int Falhas)> contagem)
    {
        if (string.IsNullOrWhiteSpace(linha))
        {
            return;
        }

        var campos = linha.Split(';');
        if (campos.Length < 7)
        {
            return;
        }

        if (!DateTime.TryParseExact(campos[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var quando))
        {
            return;
        }

        var instante = new DateTimeOffset(quando, TimeZoneInfo.Local.GetUtcOffset(quando));
        if (instante < inicio || instante > fim)
        {
            return;
        }

        var legado = campos.Length == 7;
        var usuario = legado ? UsuarioNaoInformado : campos[2].Trim();
        if (usuario.Length == 0 || usuario == "-")
        {
            usuario = UsuarioNaoInformado;
        }

        var status = (legado ? campos[4] : campos[5]).Trim();
        var sucesso = string.Equals(status, nameof(StatusChaveDevolucao.Enviada), StringComparison.OrdinalIgnoreCase);
        var atual = contagem.GetValueOrDefault(usuario);
        contagem[usuario] = sucesso ? (atual.Sucessos + 1, atual.Falhas) : (atual.Sucessos, atual.Falhas + 1);
    }
}
