using System.Globalization;
using System.Text;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Relatorio;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Separacao;

/// <summary>
/// Alerta imediato da I60: varre o estado das notas (não eventos) depois do passo da I60 e avisa uma vez por nota e
/// por evento: Rejeitada, I60Enviada que não virou Associada (IMP/ENC, pendente), Separada com resposta não
/// reconhecida (<c>TentativasOutro &gt;= 1</c>) e Atrasada. O carimbo fica no JSON da nota (à prova de reinício).
/// Todas as notas elegíveis do ciclo vão em um e-mail só; também grava <c>Estado\i51\alertas\yyyy-MM-dd.log</c>
/// (calibração) e um log Warning. <c>I60:AlertaImediato=false</c> desliga tudo.
/// </summary>
public sealed class AlertaI60
{
    /// <summary>Evento: I60 rejeitada.</summary>
    public const string EventoRejeitada = "Rejeitada";

    /// <summary>Evento: doccarga encerrado reenviado como IMP.</summary>
    public const string EventoImp = "ENC/IMP";

    /// <summary>Evento: I60 aceita mas pendente (não associada).</summary>
    public const string EventoPendente = "Pendente";

    /// <summary>Evento: resposta do WMS não reconhecida (ainda em tentativas).</summary>
    public const string EventoNaoReconhecida = "NaoReconhecida";

    /// <summary>Evento: nota atrasada.</summary>
    public const string EventoAtrasada = "Atrasada";

    private const int MaxTextoEmail = 300;
    private const int MaxTextoArquivo = 500;

    private readonly IRepositorioSeparacao _repositorio;
    private readonly CaminhosI51 _caminhos;
    private readonly IEnviadorEmail _email;
    private readonly IDestinatariosAlertaI60 _destinatarios;
    private readonly OpcoesI60 _i60;
    private readonly OpcoesEmail _opcoesEmail;
    private readonly OpcoesRelatorioI60 _opcoesRelatorio;
    private readonly OpcoesRelatorio _relatorio;
    private readonly IClock _clock;
    private readonly ILogger<AlertaI60> _logger;

    /// <summary>Cria o alerta.</summary>
    public AlertaI60(
        IRepositorioSeparacao repositorio, CaminhosI51 caminhos, IEnviadorEmail email, IDestinatariosAlertaI60 destinatarios,
        IOptions<OpcoesI60> i60, IOptions<OpcoesEmail> opcoesEmail, IOptions<OpcoesRelatorioI60> opcoesRelatorio,
        IOptions<OpcoesRelatorio> relatorio, IClock clock, ILogger<AlertaI60> logger)
    {
        _repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        _caminhos = caminhos ?? throw new ArgumentNullException(nameof(caminhos));
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _destinatarios = destinatarios ?? throw new ArgumentNullException(nameof(destinatarios));
        _i60 = i60?.Value ?? throw new ArgumentNullException(nameof(i60));
        _opcoesEmail = opcoesEmail?.Value ?? throw new ArgumentNullException(nameof(opcoesEmail));
        _opcoesRelatorio = opcoesRelatorio?.Value ?? throw new ArgumentNullException(nameof(opcoesRelatorio));
        _relatorio = relatorio?.Value ?? throw new ArgumentNullException(nameof(relatorio));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Varre e alerta. Devolve quantas notas foram alertadas neste ciclo.</summary>
    public async Task<int> ProcessarAsync(CancellationToken ct)
    {
        if (!_i60.AlertaImediato)
        {
            return 0;
        }

        var agora = _clock.UtcNow;
        var alertadas = new List<(NotaSeparacao Nota, string Evento)>();
        foreach (var n in _repositorio.Listar().OrderBy(x => x.RegistradaEm).ThenBy(x => x.Chave, StringComparer.Ordinal))
        {
            var (evento, atraso) = Classificar(n);
            if (evento is null)
            {
                continue;
            }

            // Carimbo ANTES de avisar: no máximo um alerta por nota/evento, mesmo se o envio falhar ou o processo cair.
            var ok = atraso
                ? _repositorio.Atualizar(n.Chave, x => x with { AlertaAtrasoEm = agora })
                : _repositorio.Atualizar(n.Chave, x => x with { AlertaI60Em = agora });
            if (ok)
            {
                alertadas.Add((n, evento));
            }
        }

        if (alertadas.Count == 0)
        {
            return 0;
        }

        var linhas = alertadas.Select(a => Linha(a.Nota, a.Evento, MaxTextoEmail)).ToList();
        GravarArquivo(alertadas, agora);
        var status = await EnviarEmailAsync(linhas, agora, ct).ConfigureAwait(false);

        _logger.LogWarning("I60 precisa de atencao: {Quantidade} nota(s) neste ciclo (e-mail: {StatusEmail}).", alertadas.Count, status);
        foreach (var (nota, evento) in alertadas)
        {
            _logger.LogWarning(
                "I60 atencao: nota {Numero} serie {Serie} situacao {Situacao} motivo {Motivo}.",
                nota.Numero, nota.Serie, nota.Situacao, MotivoTexto(nota, evento));
        }

        return alertadas.Count;
    }

    /// <summary>Apaga logs de alerta mais antigos que a retenção. Devolve quantos.</summary>
    public int Limpar(int diasRetencao)
    {
        var removidos = 0;
        try
        {
            if (!Directory.Exists(_caminhos.Alertas))
            {
                return 0;
            }

            var limite = _clock.UtcNow.UtcDateTime - TimeSpan.FromDays(diasRetencao);
            foreach (var arquivo in Directory.EnumerateFiles(_caminhos.Alertas, "*.log"))
            {
                if (File.GetLastWriteTimeUtc(arquivo) < limite)
                {
                    File.Delete(arquivo);
                    removidos++;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Falha ao limpar os logs de alerta da I60.");
        }

        return removidos;
    }

    // (evento, é de atraso). Evento nulo = não alerta (Associada nunca alerta).
    private static (string? Evento, bool Atraso) Classificar(NotaSeparacao n)
    {
        if (n.Situacao == SituacaoNota.Atrasada)
        {
            return n.AlertaAtrasoEm is null ? (EventoAtrasada, true) : (null, false);
        }

        if (n.AlertaI60Em is not null)
        {
            return (null, false);
        }

        return n.Situacao switch
        {
            SituacaoNota.Rejeitada => (EventoRejeitada, false),
            SituacaoNota.I60Enviada when n.Motivo == MotivoI60.DocCargaEncerrado
                                         || string.Equals(n.OperacaoI60, "IMP", StringComparison.OrdinalIgnoreCase) => (EventoImp, false),
            SituacaoNota.I60Enviada when n.Motivo != MotivoI60.Nenhum => (EventoPendente, false),
            SituacaoNota.Separada when n.TentativasOutro >= 1 => (EventoNaoReconhecida, false),
            _ => (null, false),
        };
    }

    private static string MotivoTexto(NotaSeparacao n, string evento) =>
        n.Motivo != MotivoI60.Nenhum ? n.Motivo.ToString()
        : evento == EventoNaoReconhecida ? "RespostaNaoReconhecida"
        : evento == EventoAtrasada ? "SemRetornoDaSeparacao"
        : "Nenhum";

    // Uma linha só: sem CR/LF/tab, truncada.
    private static string UmaLinha(string? texto, int max)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto)
        {
            sb.Append(c is '\r' or '\n' or '\t' ? ' ' : c);
        }

        return TextoNormalizado.Truncar(sb.ToString().Trim(), max);
    }

    private static string TextoWms(NotaSeparacao n, int max)
    {
        var partes = new List<string>();
        if (!string.IsNullOrWhiteSpace(n.Mensagem))
        {
            partes.Add(n.Mensagem);
        }

        partes.AddRange(n.Ressalvas.Where(r => !string.IsNullOrWhiteSpace(r)));
        return UmaLinha(string.Join(" | ", partes), max);
    }

    private static LinhaAlertaI60 Linha(NotaSeparacao n, string evento, int max) => new(
        evento, n.Numero, n.Serie, n.NumeroTransporte, n.Onda, n.Situacao.ToString(), MotivoTexto(n, evento), TextoWms(n, max));

    private void GravarArquivo(List<(NotaSeparacao Nota, string Evento)> alertadas, DateTimeOffset agora)
    {
        try
        {
            var local = agora.ToLocalTime();
            Directory.CreateDirectory(_caminhos.Alertas);
            var caminho = Path.Combine(_caminhos.Alertas, local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");
            var sb = new StringBuilder();
            foreach (var (n, evento) in alertadas)
            {
                sb.Append(local.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(evento).Append('\t')
                  .Append(UmaLinha(n.Numero, 20)).Append('\t')
                  .Append(UmaLinha(n.Serie, 10)).Append('\t')
                  .Append(UmaLinha(n.NumeroTransporte, 30)).Append('\t')
                  .Append(UmaLinha(n.Onda, 60)).Append('\t')
                  .Append(n.Situacao).Append('\t')
                  .Append(MotivoTexto(n, evento)).Append('\t')
                  .Append(TextoWms(n, MaxTextoArquivo)).Append('\n');
            }

            File.AppendAllText(caminho, sb.ToString(), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            _logger.LogWarning(ex, "Não foi possível gravar o arquivo de alertas da I60.");
        }
    }

    private async Task<string> EnviarEmailAsync(List<LinhaAlertaI60> linhas, DateTimeOffset agora, CancellationToken ct)
    {
        if (!(_opcoesEmail.Habilitado
              && !string.IsNullOrWhiteSpace(_opcoesEmail.Host)
              && !string.IsNullOrWhiteSpace(_opcoesEmail.Remetente)))
        {
            return "não enviado (SMTP não configurado)";
        }

        try
        {
            var corpo = CorpoEmailAlertaI60.Montar(linhas, agora, IdentidadeVisual.ObterLogo(_relatorio.LogoPath), ModoLogoRelatorio.Cid);
            var assunto = $"{_opcoesRelatorio.AssuntoPrefixo} I60 precisa de atenção: {linhas.Count.ToString(CultureInfo.InvariantCulture)} nota(s)".Trim();
            var resultado = await _email.EnviarAsync(
                new MensagemEmail(assunto, corpo.Html, Array.Empty<AnexoEmail>(), corpo.ImagemEmbutida, _destinatarios.Ler()), ct)
                .ConfigureAwait(false);
            return resultado.Status == StatusEnvioEmail.Enviado ? "enviado" : "não enviado (" + resultado.Status + ")";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Falha ao enviar o e-mail de alerta da I60; as notas foram carimbadas e o texto está no arquivo de alertas.");
            return "falhou";
        }
    }
}
