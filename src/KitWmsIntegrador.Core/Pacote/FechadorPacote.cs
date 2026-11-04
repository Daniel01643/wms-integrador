using System.Globalization;
using System.Text;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Pacote;

/// <summary>
/// Fecha o pacote: grava o relatório HTML + PDF (<c>pacote_&lt;yyyyMMdd_HHmmss&gt;</c>) e envia
/// o e-mail conforme a política. Falha de e-mail nunca impede o fechamento; falha ao gravar o
/// relatório devolve falso (o diário é preservado para nova tentativa).
/// </summary>
public sealed class FechadorPacote
{
    private readonly IGeradorRelatorio _gerador;
    private readonly IEnviadorEmail _email;
    private readonly OpcoesEmail _opcoesEmail;
    private readonly OpcoesRelatorio _opcoesRelatorio;
    private readonly ILogger<FechadorPacote> _logger;

    /// <summary>Cria o fechador.</summary>
    public FechadorPacote(
        IGeradorRelatorio gerador,
        IEnviadorEmail email,
        IOptions<OpcoesEmail> opcoesEmail,
        IOptions<OpcoesRelatorio> opcoesRelatorio,
        ILogger<FechadorPacote> logger)
    {
        _gerador = gerador ?? throw new ArgumentNullException(nameof(gerador));
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _opcoesEmail = opcoesEmail?.Value ?? throw new ArgumentNullException(nameof(opcoesEmail));
        _opcoesRelatorio = opcoesRelatorio?.Value ?? throw new ArgumentNullException(nameof(opcoesRelatorio));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Devolve verdadeiro se o pacote pode ser considerado fechado (relatório gravado ou nada a reportar).</summary>
    public async Task<bool> FecharAsync(
        IReadOnlyList<RegistroDiario> registros, DateTimeOffset inicio, DateTimeOffset fim, CancellationToken ct,
        PeriodoColeta? periodo = null)
    {
        if (registros.Count == 0)
        {
            _logger.LogInformation("Pacote sem resultados; nenhum relatório gerado.");
            return true;
        }

        var linhas = registros.Select(r => r.ParaLinha()).ToList();
        var xmlsInvalidos = registros.Count(r => r.XmlInvalido);
        byte[] pdf;

        try
        {
            var html = _gerador.GerarHtml(linhas, fim, periodo);
            pdf = _gerador.GerarPdf(linhas, fim, periodo);

            Directory.CreateDirectory(_opcoesRelatorio.PastaSaida);
            var carimbo = fim.ToLocalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var baseNome = NomeUnico(_opcoesRelatorio.PastaSaida, "pacote_" + carimbo);
            var caminhoHtml = Path.Combine(_opcoesRelatorio.PastaSaida, baseNome + ".html");
            var caminhoPdf = Path.Combine(_opcoesRelatorio.PastaSaida, baseNome + ".pdf");
            await File.WriteAllTextAsync(caminhoHtml, html, Encoding.UTF8, CancellationToken.None).ConfigureAwait(false);
            await File.WriteAllBytesAsync(caminhoPdf, pdf, CancellationToken.None).ConfigureAwait(false);
            _logger.LogInformation("Relatório do pacote salvo em {Html} e {Pdf}.", caminhoHtml, caminhoPdf);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao gerar/gravar o relatório do pacote; será tentado novamente.");
            return false;
        }

        try
        {
            var resumo = ModeloRelatorio.Montar(linhas).Resumo;
            if (!PoliticaEnvioEmail.Deve(resumo, xmlsInvalidos, _opcoesEmail))
            {
                _logger.LogInformation("E-mail não enviado pela política ({Quando}).", _opcoesEmail.EnviarQuando);
                return true;
            }

            var assunto = $"Fechamento do pacote {fim.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)} - " +
                          $"{resumo.Total} nota(s), {resumo.NaoIntegradas} não integrada(s), {resumo.Pendentes} pendente(s)";
            var corpo = CorpoEmailResumo.Montar(linhas, xmlsInvalidos, inicio, fim, periodo: periodo);
            var nomePdf = "pacote_" + fim.ToLocalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".pdf";
            var resultado = await _email.EnviarAsync(
                new MensagemEmail(assunto, corpo, new[] { new AnexoEmail(nomePdf, pdf) }), ct).ConfigureAwait(false);

            switch (resultado.Status)
            {
                case StatusEnvioEmail.Enviado:
                    _logger.LogInformation("E-mail do pacote enviado.");
                    break;
                case StatusEnvioEmail.NaoConfigurado:
                    _logger.LogInformation("E-mail não configurado; relatório salvo em {Pasta}.", _opcoesRelatorio.PastaSaida);
                    break;
                default:
                    _logger.LogError("Falha ao enviar o e-mail do pacote: {Motivo}", resultado.Motivo);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha inesperada ao enviar o e-mail do pacote (ignorada).");
        }

        return true;
    }

    private static string NomeUnico(string pasta, string baseNome)
    {
        var candidato = baseNome;
        for (var i = 1; File.Exists(Path.Combine(pasta, candidato + ".pdf"))
                        || File.Exists(Path.Combine(pasta, candidato + ".html")); i++)
        {
            candidato = $"{baseNome}_{i}";
        }

        return candidato;
    }
}
