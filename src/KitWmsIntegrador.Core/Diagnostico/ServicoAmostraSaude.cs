using System.Globalization;
using System.Net;
using System.Text;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Diagnostico;

/// <summary>
/// Amostragem periódica de saúde (memória do processo e do servidor): grava um evento <c>saude-amostra</c> no diário do
/// diagnóstico e avisa a TI por e-mail quando a memória física livre do servidor fica abaixo de
/// <c>Diagnostico:AlertaMemoriaLivreMb</c> por 2 amostras seguidas (no máximo 1 e-mail por hora). Nunca lança por falha
/// de coleta, de disco ou de e-mail.
/// </summary>
public sealed class ServicoAmostraSaude
{
    /// <summary>Amostras seguidas abaixo do limite para disparar o alerta.</summary>
    public const int AmostrasParaAlerta = 2;

    /// <summary>Intervalo mínimo entre dois e-mails de alerta.</summary>
    public static readonly TimeSpan IntervaloMinimoAlerta = TimeSpan.FromHours(1);

    private readonly IClock _clock;
    private readonly IColetorAmostraSistema _coletor;
    private readonly RegistroDiagnosticoArquivo _diario;
    private readonly IEnviadorEmail _email;
    private readonly FonteDestinatariosDiagnostico _destinatarios;
    private readonly OpcoesDiagnostico _opcoes;
    private readonly OpcoesEmail _opcoesEmail;
    private readonly ILogger<ServicoAmostraSaude> _logger;
    private int _seguidasAbaixo;
    private DateTimeOffset? _ultimoAlerta;
    private bool _ultimoAlertaCarregado;

    /// <summary>Cria o serviço.</summary>
    public ServicoAmostraSaude(
        IClock clock, IColetorAmostraSistema coletor, RegistroDiagnosticoArquivo diario, IEnviadorEmail email,
        FonteDestinatariosDiagnostico destinatarios, IOptions<OpcoesDiagnostico> opcoes, IOptions<OpcoesEmail> opcoesEmail,
        ILogger<ServicoAmostraSaude> logger)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _coletor = coletor ?? throw new ArgumentNullException(nameof(coletor));
        _diario = diario ?? throw new ArgumentNullException(nameof(diario));
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _destinatarios = destinatarios ?? throw new ArgumentNullException(nameof(destinatarios));
        _opcoes = opcoes?.Value ?? throw new ArgumentNullException(nameof(opcoes));
        _opcoesEmail = opcoesEmail?.Value ?? throw new ArgumentNullException(nameof(opcoesEmail));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Coleta uma amostra, grava no diário e avalia o alerta de memória. Só o cancelamento propaga.</summary>
    public async Task AmostrarAsync(CancellationToken ct)
    {
        try
        {
            var agora = _clock.UtcNow.ToLocalTime();
            var a = _coletor.Coletar();
            _diario.Registrar(new EventoDiagnostico
            {
                Tipo = TipoAmostra,
                QuandoLocal = agora,
                ProcessoWsMb = a.ProcessoWsMb,
                ProcessoPrivadoMb = a.ProcessoPrivadoMb,
                Handles = a.Handles,
                Threads = a.Threads,
                LivreMb = a.MemoriaFisicaLivreMb,
                TotalMb = a.MemoriaFisicaTotalMb,
                PaginacaoUsadaMb = a.PaginacaoUsadaMb,
                PaginacaoTotalMb = a.PaginacaoTotalMb,
                Consumidores = a.Consumidores.Count == 0 ? null : a.Consumidores,
            });

            await AvaliarAlertaAsync(a, agora, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao coletar a amostra de saúde (ignorada; tenta na próxima).");
        }
    }

    /// <summary>Tipo do evento de amostra no diário.</summary>
    public const string TipoAmostra = "saude-amostra";

    /// <summary>Tipo do evento de alerta de memória no diário.</summary>
    public const string TipoAlerta = "saude-alerta";

    private async Task AvaliarAlertaAsync(AmostraSistema a, DateTimeOffset agora, CancellationToken ct)
    {
        var limite = _opcoes.AlertaMemoriaLivreMb;
        if (limite <= 0 || a.MemoriaFisicaLivreMb is not { } livre)
        {
            return;
        }

        if (livre >= limite)
        {
            _seguidasAbaixo = 0;
            return;
        }

        _seguidasAbaixo++;
        if (_seguidasAbaixo < AmostrasParaAlerta)
        {
            return;
        }

        CarregarUltimoAlerta(agora);
        if (_ultimoAlerta is { } ultimo && agora - ultimo < IntervaloMinimoAlerta)
        {
            return;
        }

        _ultimoAlerta = agora;
        var enviado = await EnviarAlertaAsync(a, livre, limite, ct).ConfigureAwait(false);
        _diario.Registrar(new EventoDiagnostico
        {
            Tipo = TipoAlerta,
            QuandoLocal = agora,
            Ok = enviado,
            LivreMb = livre,
            Quantidade = _seguidasAbaixo,
            Quantidade2 = limite,
        });
    }

    /// <summary>Depois de um reinício, respeita o último alerta já gravado no diário na última hora.</summary>
    private void CarregarUltimoAlerta(DateTimeOffset agora)
    {
        if (_ultimoAlertaCarregado)
        {
            return;
        }

        _ultimoAlertaCarregado = true;
        var ultimo = _diario.Ler(agora - IntervaloMinimoAlerta, agora)
            .Where(e => e.Tipo == TipoAlerta)
            .Select(e => (DateTimeOffset?)e.QuandoLocal)
            .Max();
        if (ultimo is not null && (_ultimoAlerta is null || ultimo > _ultimoAlerta))
        {
            _ultimoAlerta = ultimo;
        }
    }

    private async Task<bool> EnviarAlertaAsync(AmostraSistema a, int livre, int limite, CancellationToken ct)
    {
        var assunto = $"{_opcoes.AssuntoPrefixo} ALERTA: memória livre do servidor baixa ({livre.ToString(CultureInfo.InvariantCulture)} MB)".Trim();
        try
        {
            if (!_opcoesEmail.Habilitado || string.IsNullOrWhiteSpace(_opcoesEmail.Host) || string.IsNullOrWhiteSpace(_opcoesEmail.Remetente))
            {
                _logger.LogWarning(
                    "Memória livre do servidor baixa: {Livre} MB (limite {Limite} MB) por {Amostras} amostras seguidas (e-mail de alerta desligado; apenas log).",
                    livre, limite, _seguidasAbaixo);
                return false;
            }

            var destinatarios = _destinatarios.Ler();
            if (destinatarios.Count == 0)
            {
                _logger.LogWarning("{Assunto} (sem destinatários; apenas log).", assunto);
                return false;
            }

            var resultado = await _email.EnviarAsync(
                new MensagemEmail(assunto, CorpoAlerta(a, livre, limite), Array.Empty<AnexoEmail>(), DestinatariosOverride: destinatarios), ct)
                .ConfigureAwait(false);
            if (resultado.Status == StatusEnvioEmail.Enviado)
            {
                return true;
            }

            _logger.LogWarning("Alerta de memória livre não enviado por e-mail (status {Status}).", resultado.Status);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Falha ao enviar o alerta de memória livre do servidor (ignorada).");
            return false;
        }
    }

    private string CorpoAlerta(AmostraSistema a, int livre, int limite)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("<html><body style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;\">");
        sb.Append("<p>A memória física livre do servidor ficou abaixo de <b>").Append(limite.ToString(ci)).Append(" MB</b> por ")
          .Append(_seguidasAbaixo.ToString(ci)).Append(" amostras seguidas. Última amostra: <b>").Append(livre.ToString(ci)).Append(" MB</b> livres");
        if (a.MemoriaFisicaTotalMb is { } total)
        {
            sb.Append(" de ").Append(total.ToString(ci)).Append(" MB");
        }

        sb.Append(".</p>");
        if (a.PaginacaoUsadaMb is { } usada && a.PaginacaoTotalMb is { } limitePag)
        {
            sb.Append("<p>Memória virtual comprometida (RAM + arquivo de paginação): ").Append(usada.ToString(ci)).Append(" MB de ")
              .Append(limitePag.ToString(ci)).Append(" MB.</p>");
        }

        sb.Append("<p>Integrador: working set ").Append(Texto(a.ProcessoWsMb)).Append(" MB, memória privada ").Append(Texto(a.ProcessoPrivadoMb))
          .Append(" MB, ").Append(Texto(a.Handles)).Append(" handles, ").Append(Texto(a.Threads)).Append(" threads.</p>");
        if (a.Consumidores.Count > 0)
        {
            sb.Append("<p>Processos que mais usam memória:</p><ul>");
            foreach (var c in a.Consumidores)
            {
                sb.Append("<li>").Append(WebUtility.HtmlEncode(c.Nome)).Append(": ").Append(c.Mb.ToString(ci)).Append(" MB</li>");
            }

            sb.Append("</ul>");
        }

        sb.Append("<p>Novo aviso só depois de 1 hora. Detalhes no Diagnóstico agente integrador das 14:00.</p></body></html>");
        return sb.ToString();
    }

    private static string Texto(int? valor) => valor is { } v ? v.ToString(CultureInfo.InvariantCulture) : "?";
}
