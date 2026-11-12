using System.Net.Http;
using System.Text.Json;
using KitWmsIntegrador.Core.ClienteWms;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Devolucao;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Core.Modelos;
using KitWmsIntegrador.Core.Orquestrador;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Separacao;

/// <summary>Contagens de um passo da I60.</summary>
public sealed record ResumoI60(int Enviadas, int Associadas, int ImportadasImp, int Rejeitadas, int FalhasComunicacao)
{
    /// <summary>Resumo vazio.</summary>
    public static readonly ResumoI60 Vazio = new(0, 0, 0, 0, 0);
}

/// <summary>
/// Envia a I60 das notas <c>Separada</c> com a MESMA NF-e (XML) da I50, quantidades ajustadas às
/// separadas, operação normal <c>INE</c>. Doccarga encerrado (ENC) reenvia como <c>IMP</c> e registra o
/// erro. Comunicação nunca torna a nota terminal (backoff). No máximo 4 chamadas por nota por ciclo.
/// </summary>
public sealed class ProcessadorI60Separacao
{
    private const int MaxChamadas = 4;
    private const string AcaoInclusao = "I";
    private const string AcaoAtualizacao = "UPD";

    private readonly IRepositorioSeparacao _repositorio;
    private readonly IClienteWms _cliente;
    private readonly IConstrutorI60 _construtor;
    private readonly LeitorNotaFiscalXml _leitor;
    private readonly ClassificadorRetorno _classificador;
    private readonly OpcoesI60 _i60;
    private readonly OpcoesI51 _i51;
    private readonly OpcoesPasta _pastas;
    private readonly CaminhosI51 _caminhos;
    private readonly IClock _clock;
    private readonly ILogger<ProcessadorI60Separacao> _logger;

    /// <summary>Cria o processador.</summary>
    public ProcessadorI60Separacao(
        IRepositorioSeparacao repositorio, IClienteWms cliente, IConstrutorI60 construtor, LeitorNotaFiscalXml leitor,
        ClassificadorRetorno classificador, IOptions<OpcoesI60> i60, IOptions<OpcoesI51> i51, IOptions<OpcoesPasta> pastas,
        CaminhosI51 caminhos, IClock clock, ILogger<ProcessadorI60Separacao> logger)
    {
        _repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        _cliente = cliente ?? throw new ArgumentNullException(nameof(cliente));
        _construtor = construtor ?? throw new ArgumentNullException(nameof(construtor));
        _leitor = leitor ?? throw new ArgumentNullException(nameof(leitor));
        _classificador = classificador ?? throw new ArgumentNullException(nameof(classificador));
        _i60 = i60?.Value ?? throw new ArgumentNullException(nameof(i60));
        _i51 = i51?.Value ?? throw new ArgumentNullException(nameof(i51));
        _pastas = pastas?.Value ?? throw new ArgumentNullException(nameof(pastas));
        _caminhos = caminhos ?? throw new ArgumentNullException(nameof(caminhos));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private enum Desfecho { Enviada, Associada, Imp, Rejeitada, Comunicacao, Adiada }

    /// <summary>Processa as notas <c>Separada</c> devidas. <c>I60:Habilitada=false</c> pula o passo.</summary>
    public async Task<ResumoI60> ProcessarPendentesAsync(CancellationToken ct)
    {
        if (!_i60.Habilitada)
        {
            return ResumoI60.Vazio;
        }

        var agora = _clock.UtcNow;
        var devidas = _repositorio.ListarPorSituacao(SituacaoNota.Separada)
            .Where(n => n.ProximaTentativaI60Em is null || n.ProximaTentativaI60Em <= agora)
            .OrderBy(n => n.SeparadaEm ?? n.RegistradaEm)
            .ToList();

        int enviadas = 0, associadas = 0, imp = 0, rejeitadas = 0, falhas = 0;
        foreach (var nota in devidas)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                switch (await ProcessarNotaAsync(nota, ct).ConfigureAwait(false))
                {
                    case Desfecho.Associada:
                        enviadas++;
                        associadas++;
                        break;
                    case Desfecho.Enviada:
                        enviadas++;
                        break;
                    case Desfecho.Imp:
                        enviadas++;
                        imp++;
                        break;
                    case Desfecho.Rejeitada:
                        rejeitadas++;
                        break;
                    case Desfecho.Comunicacao:
                        falhas++;
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Falha inesperada ao processar a I60 da nota {Numero}; segue para a próxima.", nota.Numero);
            }
        }

        return new ResumoI60(enviadas, associadas, imp, rejeitadas, falhas);
    }

    private async Task<Desfecho> ProcessarNotaAsync(NotaSeparacao nota, CancellationToken ct)
    {
        // 1) XML ANTES de qualquer write-ahead: indisponível não consome tentativa.
        var xml = CarregarNota(nota);
        if (xml is null)
        {
            _logger.LogError("XML da nota {Numero} indisponível (cópia e fallback ausentes); I60 não enviada.", nota.Numero);
            Rejeitar(nota, MotivoI60.XmlIndisponivel, "XML da nota indisponível (cópia do acompanhamento e fallback em Processados ausentes).", null, string.Empty, string.Empty);
            return Desfecho.Rejeitada;
        }

        var quantidades = nota.Itens
            .Where(i => i.QtdeSeparada is not null)
            .Select(i => new QuantidadeItemI60(i.Sequencia, i.QtdeSeparada!.Value))
            .ToList();
        if (quantidades.Sum(q => q.Quantidade) <= 0m)
        {
            Rejeitar(nota, MotivoI60.SemQuantidadeSeparada, "Quantidade separada igual a zero: a I60 não foi enviada.", null, string.Empty, string.Empty);
            return Desfecho.Rejeitada;
        }

        decimal? volume = _i60.VolumeVemDaI51 && nota.QtdeVolumesI51 > 0 ? nota.QtdeVolumesI51 : null;

        // 2) Write-ahead: a tentativa fica em disco antes de chamar o WMS.
        var agora = _clock.UtcNow;
        if (!_repositorio.Atualizar(nota.Chave, n => n with { TentativasI60 = n.TentativasI60 + 1, UltimaTentativaI60Em = agora }))
        {
            return Desfecho.Adiada;
        }

        var chamadas = 0;
        async Task<LeituraI60> Enviar(string operacao, string tpacao)
        {
            if (++chamadas > MaxChamadas)
            {
                throw new InvalidOperationException("Limite de chamadas da I60 por nota excedido.");
            }

            var payload = _construtor.Construir(xml, new ParametrosI60
            {
                Quantidades = quantidades,
                TipoOperacaoNota = operacao,
                Tpacao = tpacao,
                QtdeVolume = volume,
            });
            var resposta = await _cliente.EnviarAsync(InterfaceWms.I60, payload, ct).ConfigureAwait(false);
            return LeituraI60.Ler(resposta, _classificador);
        }

        try
        {
            var operacaoNormal = _i60.TipoOperacaoNormal;
            var leitura = await Enviar(operacaoNormal, AcaoInclusao).ConfigureAwait(false);
            var tpUsado = AcaoInclusao;

            // A. ENC tem prioridade: a INE recusada por ENC já importou a nota na interface.
            if (EhEncerrado(leitura))
            {
                return await FluxoImpAsync(nota, leitura, Enviar, ct).ConfigureAwait(false);
            }

            // B. "Nota já existe, TpAcao precisa ser UPD" (ex.: reenvio após queda): um reenvio UPD.
            if (ExigeUpd(leitura) && _i60.ReenviarComUpdSeJaExiste)
            {
                leitura = await Enviar(operacaoNormal, AcaoAtualizacao).ConfigureAwait(false);
                tpUsado = AcaoAtualizacao;
                if (EhEncerrado(leitura))
                {
                    return await FluxoImpAsync(nota, leitura, Enviar, ct).ConfigureAwait(false);
                }
            }

            return FinalizarNormal(nota, leitura, operacaoNormal, tpUsado);
        }
        catch (Exception ex) when (EhFalhaDeComunicacao(ex, ct))
        {
            return RegistrarFalhaComunicacao(nota, ex);
        }
    }

    // Reenvio como IMP (doccarga ENC): registra o erro, não associa.
    private async Task<Desfecho> FluxoImpAsync(
        NotaSeparacao nota, LeituraI60 respIne, Func<string, string, Task<LeituraI60>> enviar, CancellationToken ct)
    {
        var op = _i60.TipoOperacaoImp;
        var tp = string.Equals(_i60.TpAcaoReenvioImp?.Trim(), AcaoInclusao, StringComparison.OrdinalIgnoreCase)
            ? AcaoInclusao
            : AcaoAtualizacao;
        var resp = await enviar(op, tp).ConfigureAwait(false);
        if (ExigeUpd(resp) && tp != AcaoAtualizacao && _i60.ReenviarComUpdSeJaExiste)
        {
            tp = AcaoAtualizacao;
            resp = await enviar(op, tp).ConfigureAwait(false);
        }

        if (ExigeUpd(resp))
        {
            Rejeitar(nota, MotivoI60.JaExiste, "Nota já existe no WMS e o reenvio IMP ainda exige UPD. " + resp.Resumo, resp, op, tp);
            return Desfecho.Rejeitada;
        }

        if (resp.Sucesso || resp.Pendencia)
        {
            var mensagem = $"Doccarga encerrado (ENC): I60 enviada como {op} (tpacao={tp}), sem associação automática. WMS({_i60.TipoOperacaoNormal}): {respIne.Primeira}";
            _logger.LogWarning(
                "Nota {Numero}: doccarga encerrado (ENC); I60 enviada como {Operacao} (tpacao={Tp}).", nota.Numero, op, tp);
            Aceitar(nota, MotivoI60.DocCargaEncerrado, mensagem, resp, op, tp, associar: false);
            return Desfecho.Imp;
        }

        return TratarOutro(nota, resp, op, tp, respIne);
    }

    private Desfecho FinalizarNormal(NotaSeparacao nota, LeituraI60 r, string op, string tp)
    {
        if (ExigeUpd(r))
        {
            Rejeitar(nota, MotivoI60.JaExiste, "Nota já existe no WMS e o reenvio UPD não resolveu. " + r.Resumo, r, op, tp);
            return Desfecho.Rejeitada;
        }

        if (EhEncerrado(r))
        {
            // ReenviarEncerradoComoImp=false: rejeita com o motivo.
            Rejeitar(nota, MotivoI60.DocCargaEncerrado, "Doccarga encerrado (ENC). " + r.Resumo, r, op, tp);
            return Desfecho.Rejeitada;
        }

        if (r.Casa(Padroes(_i60.PadroesQuantidadeDiferente)))
        {
            Rejeitar(nota, MotivoI60.QuantidadeDiferente, r.Resumo, r, op, tp);
            return Desfecho.Rejeitada;
        }

        if (r.Casa(Padroes(_i60.PadroesVolumeDiferente)))
        {
            Rejeitar(nota, MotivoI60.VolumeDiferente, r.Resumo, r, op, tp);
            return Desfecho.Rejeitada;
        }

        if (r.RessalvaCasa(Padroes(_i60.PadroesRessalvaBloqueante)))
        {
            Rejeitar(nota, MotivoI60.Outro, r.Resumo, r, op, tp);
            return Desfecho.Rejeitada;
        }

        if (r.Sucesso || r.Casa(Padroes(_i60.PadroesAssociadaSucesso)))
        {
            Aceitar(nota, MotivoI60.Nenhum, r.Primeira, r, op, tp, associar: string.Equals(op, _i60.TipoOperacaoNormal, StringComparison.OrdinalIgnoreCase));
            return Desfecho.Associada;
        }

        if (r.Pendencia)
        {
            var motivo = r.Ressalvas.Count == 0 ? MotivoI60.PendenteNaInterface : MotivoI60.PendenteComRessalva;
            Aceitar(nota, motivo, r.Resumo, r, op, tp, associar: false);
            return Desfecho.Enviada;
        }

        return TratarOutro(nota, r, op, tp, null);
    }

    // Resposta desconhecida: permanece Separada com backoff até MaxTentativasOutro; depois Rejeitada/Outro.
    private Desfecho TratarOutro(NotaSeparacao nota, LeituraI60 r, string op, string tp, LeituraI60? anterior)
    {
        var texto = anterior is null ? r.Resumo : anterior.Primeira + " | " + r.Resumo;
        var tentativas = nota.TentativasOutro + 1;
        if (tentativas >= _i60.MaxTentativasOutro)
        {
            _logger.LogError("Nota {Numero}: resposta desconhecida do WMS na I60 esgotou {Max} tentativa(s); rejeitada.", nota.Numero, _i60.MaxTentativasOutro);
            Rejeitar(nota, MotivoI60.Outro, texto, r, op, tp);
            return Desfecho.Rejeitada;
        }

        _logger.LogWarning(
            "Nota {Numero}: resposta desconhecida do WMS na I60 (tentativa {Tentativa}/{Max}): {Mensagem}",
            nota.Numero, tentativas, _i60.MaxTentativasOutro, TextoNormalizado.Truncar(texto, 200));
        var proxima = _clock.UtcNow + Backoff(tentativas);
        _repositorio.Atualizar(nota.Chave, n => n with
        {
            TentativasOutro = tentativas,
            FalhasComunicacao = 0,
            ProximaTentativaI60Em = proxima,
            Mensagem = TextoNormalizado.Truncar(texto, 500),
            TransacaoI60 = r.Transacao,
        });
        return Desfecho.Adiada;
    }

    private Desfecho RegistrarFalhaComunicacao(NotaSeparacao nota, Exception ex)
    {
        var status = ex is HttpRequestException { StatusCode: { } sc } ? (int)sc : 0;
        var falhas = nota.FalhasComunicacao + 1;
        var proxima = _clock.UtcNow + Backoff(falhas);
        var descricao = status > 0 ? $"{ex.GetType().Name} (HTTP {status})" : ex.GetType().Name;
        if (falhas >= _i60.MaxFalhasComunicacaoAlerta)
        {
            _logger.LogError("Nota {Numero}: {Falhas} falhas de comunicação seguidas na I60 ({Erro}); continuará sendo reenviada.", nota.Numero, falhas, descricao);
        }
        else
        {
            _logger.LogWarning("Nota {Numero}: falha de comunicação na I60 ({Erro}); nova tentativa após {Proxima:HH:mm}.", nota.Numero, descricao, proxima.ToLocalTime());
        }

        _repositorio.Atualizar(nota.Chave, n => n with
        {
            FalhasComunicacao = falhas,
            ProximaTentativaI60Em = proxima,
            Mensagem = "Falha de comunicação: " + descricao,
        });
        return Desfecho.Comunicacao;
    }

    private TimeSpan Backoff(int n)
    {
        var minutos = Math.Min(Math.Pow(2, Math.Max(0, n - 1)) * Math.Max(1, _i51.IntervaloMinutos), _i60.BackoffMaxMinutos);
        return TimeSpan.FromMinutes(minutos);
    }

    private void Rejeitar(NotaSeparacao nota, MotivoI60 motivo, string mensagem, LeituraI60? r, string op, string tp)
    {
        var agora = _clock.UtcNow;
        _logger.LogWarning("Nota {Numero}: I60 rejeitada ({Motivo}).", nota.Numero, motivo);
        _repositorio.Atualizar(nota.Chave, n => n with
        {
            Situacao = SituacaoNota.Rejeitada,
            Motivo = motivo,
            Mensagem = TextoNormalizado.Truncar(mensagem, 500),
            Ressalvas = RessalvasCurtas(r),
            OperacaoI60 = op,
            TpAcaoI60 = tp,
            TransacaoI60 = r?.Transacao ?? n.TransacaoI60,
            IdDocumentoI60 = r?.IdDocumento ?? n.IdDocumentoI60,
            FinalizadaEm = agora,
            FalhasComunicacao = 0,
            ProximaTentativaI60Em = null,
        });
    }

    // I60Enviada em disco ANTES de promover a Associada (write-ahead de dois passos).
    private void Aceitar(NotaSeparacao nota, MotivoI60 motivo, string mensagem, LeituraI60 r, string op, string tp, bool associar)
    {
        var agora = _clock.UtcNow;
        var ok = _repositorio.Atualizar(nota.Chave, n => n with
        {
            Situacao = SituacaoNota.I60Enviada,
            I60EnviadaEm = agora,
            FinalizadaEm = agora,
            Motivo = motivo,
            Mensagem = TextoNormalizado.Truncar(mensagem, 500),
            Ressalvas = RessalvasCurtas(r),
            OperacaoI60 = op,
            TpAcaoI60 = tp,
            TransacaoI60 = r.Transacao,
            IdDocumentoI60 = r.IdDocumento,
            FalhasComunicacao = 0,
            TentativasOutro = 0,
            ProximaTentativaI60Em = null,
        });
        if (ok && associar && motivo == MotivoI60.Nenhum)
        {
            _repositorio.Atualizar(nota.Chave, n => n with { Situacao = SituacaoNota.Associada });
        }
    }

    private static IReadOnlyList<string> RessalvasCurtas(LeituraI60? r) =>
        r is null ? Array.Empty<string>() : r.Ressalvas.Take(5).Select(x => TextoNormalizado.Truncar(x, 200)).ToList();

    private bool EhEncerrado(LeituraI60 r) =>
        _i60.ReenviarEncerradoComoImp && r.Casa(Padroes(_i60.PadroesDocCargaEncerrado));

    private bool ExigeUpd(LeituraI60 r) => r.Casa(Padroes(_i60.PadroesExigeUpd));

    private static IReadOnlyList<string> Padroes(string lista) => TextoNormalizado.Lista(lista);

    private static bool EhFalhaDeComunicacao(Exception ex, CancellationToken ct) => ex switch
    {
        HttpRequestException => true,
        TaskCanceledException => !ct.IsCancellationRequested,
        JsonException => true,
        InvalidOperationException e when e.Message.StartsWith("GetToken", StringComparison.Ordinal) => true,
        _ => false,
    };

    // Carrega a NF-e: cópia do acompanhamento; senão, fallback por chave (44 dígitos) em Processados.
    private NotaFiscal? CarregarNota(NotaSeparacao nota)
    {
        try
        {
            string? caminho = null;
            if (nota.XmlCopiado && nota.CaminhoXml.Length > 0)
            {
                var copia = Path.Combine(_caminhos.Base, nota.CaminhoXml);
                if (File.Exists(copia))
                {
                    caminho = copia;
                }
            }

            if (caminho is null && nota.Chave.Length == 44 && nota.Chave.All(char.IsDigit))
            {
                try
                {
                    caminho = new LocalizadorXmlDevolucao(_pastas.CaminhoProcessados).Localizar(nota.Chave);
                }
                catch (ArgumentException)
                {
                    caminho = null;
                }
            }

            return caminho is null ? null : _leitor.ParseArquivo(caminho);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidOperationException or FormatException)
        {
            _logger.LogError(ex, "Falha ao ler o XML da nota {Numero} para a I60.", nota.Numero);
            return null;
        }
    }
}
