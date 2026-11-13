using System.Collections.Concurrent;
using KitWmsIntegrador.Core.Agrupamento;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Core.Modelos;
using KitWmsIntegrador.Core.Orquestrador;
using KitWmsIntegrador.Core.Relatorio;
using KitWmsIntegrador.Core.Separacao;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Pacote;

/// <summary>
/// Processa um lote de arquivos prontos: parse → agrupa por transporte → integra cada nota pelo
/// Orquestrador (ondas em paralelo limitado, notas em sequência) → classifica → move logo após
/// cada nota → registra no diário. Exceção em um arquivo/onda não derruba os demais.
/// </summary>
public sealed class ProcessadorLote
{
    private sealed record MovePendente(DestinoArquivo Destino, CategoriaErro? Categoria, string Motivo, string IdentificadorPacote);

    private readonly IOrquestrador _orquestrador;
    private readonly LeitorNotaFiscalXml _leitor;
    private readonly AgrupadorTransporte _agrupador;
    private readonly ClassificadorCategoriaErro _classificador;
    private readonly OrganizadorArquivos _organizador;
    private readonly DiarioPacote _diario;
    private readonly LeitorZipSeguro _leitorZip;
    private readonly RegistroOndasTsp _ondasTsp;
    private readonly IClock _clock;
    private readonly OpcoesPacote _opcoes;
    private readonly ILogger<ProcessadorLote> _logger;
    private readonly IRegistradorSeparacao? _separacao;
    private static readonly ClassificadorRetorno ClassificadorJaExiste = new();

    // Arquivos já enviados ao WMS cujo move falhou: NUNCA reenviar, só repetir o move.
    private readonly ConcurrentDictionary<string, MovePendente> _movePendentes =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cria o processador.</summary>
    public ProcessadorLote(
        IOrquestrador orquestrador,
        LeitorNotaFiscalXml leitor,
        AgrupadorTransporte agrupador,
        ClassificadorCategoriaErro classificador,
        OrganizadorArquivos organizador,
        DiarioPacote diario,
        IClock clock,
        IOptions<OpcoesPacote> opcoes,
        LeitorZipSeguro leitorZip,
        RegistroOndasTsp ondasTsp,
        ILogger<ProcessadorLote> logger,
        IRegistradorSeparacao? separacao = null)
    {
        _separacao = separacao;
        _leitorZip = leitorZip ?? throw new ArgumentNullException(nameof(leitorZip));
        _ondasTsp = ondasTsp ?? throw new ArgumentNullException(nameof(ondasTsp));
        _orquestrador = orquestrador ?? throw new ArgumentNullException(nameof(orquestrador));
        _leitor = leitor ?? throw new ArgumentNullException(nameof(leitor));
        _agrupador = agrupador ?? throw new ArgumentNullException(nameof(agrupador));
        _classificador = classificador ?? throw new ArgumentNullException(nameof(classificador));
        _organizador = organizador ?? throw new ArgumentNullException(nameof(organizador));
        _diario = diario ?? throw new ArgumentNullException(nameof(diario));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _opcoes = opcoes?.Value ?? throw new ArgumentNullException(nameof(opcoes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Duração, em ms (medida com o <c>IClock</c>), da fase de leitura (ZIP/XML) da última chamada de
    /// <c>ProcessarAsync</c>. Um pacote tem uma chamada; antes de qualquer chamada vale 0.
    /// </summary>
    public long UltimaLeituraMs { get; private set; }

    /// <summary>Arquivos enviados ao WMS cujo move ainda está pendente (não bloqueiam o fechamento).</summary>
    public IReadOnlyCollection<string> MovePendentes => _movePendentes.Keys.ToList();

    /// <summary>
    /// Repete os moves que falharam (arquivo já enviado ao WMS). Nunca chama o WMS. A fonte de
    /// verdade após reinício é o diário (ver <see cref="ReconciliarMove"/>).
    /// </summary>
    public void RepetirMovesPendentes()
    {
        foreach (var par in _movePendentes.ToArray())
        {
            RepetirMove(par.Key, par.Value);
        }
    }

    /// <summary>
    /// Move um arquivo já tratado (registro do diário) sem reenviar ao WMS. Usado na retomada
    /// após queda entre registrar e mover. Se o move falhar, fica como pendente.
    /// </summary>
    public bool ReconciliarMove(RegistroDiario registro, string identificadorPacoteFallback)
    {
        ArgumentNullException.ThrowIfNull(registro);
        var arquivo = registro.Arquivo;
        if (!File.Exists(arquivo))
        {
            return true;
        }

        // O id do próprio registro vence; só caímos no fallback quando o diário é antigo (sem o campo).
        var id = string.IsNullOrEmpty(registro.IdentificadorPacote)
            ? identificadorPacoteFallback
            : registro.IdentificadorPacote!;
        var motivo = MotivoSidecar(registro);
        var mov = _organizador.Mover(arquivo, registro.Destino, registro.Categoria, motivo, id);
        if (mov.Sucesso || !File.Exists(arquivo))
        {
            return true;
        }

        _logger.LogWarning(
            "Arquivo {Arquivo} já tratado antes da queda, mas o move falhou ({Erro}); será repetido sem reenviar ao WMS.",
            arquivo, mov.Erro);
        _movePendentes[arquivo] = new MovePendente(registro.Destino, registro.Categoria, motivo, id);
        return false;
    }

    // Contexto da nota: decidido só pela pasta onde o arquivo foi depositado.
    private sealed record Ctx(string Tipo, string Origem, string Onda, string? Zip)
    {
        public bool EhTsp => string.Equals(Tipo, TipoNotaExtensoes.RotuloTsp, StringComparison.Ordinal);

        public static Ctx De(EntradaArquivo e) => new(e.Tipo.Rotulo(), e.Origem, string.Empty, null);
    }

    private sealed record Lido(string Arquivo, NotaFiscal Nota, Ctx Ctx, string? ZipCaminho);

    private sealed record ZipTratado(EntradaArquivo Entrada, string Hash);

    private static EntradaArquivo Raiz(string caminho) =>
        new(caminho, TipoNota.Puxada, TipoNotaExtensoes.OrigemRaiz);

    /// <summary>
    /// Trata arquivos não-XML definitivos: cada um vira um <see cref="RegistroDiario"/> com
    /// <see cref="CategoriaErro.ExtensaoInvalida"/>/<see cref="StatusLinhaRelatorio.ArquivoIgnorado"/>,
    /// é gravado no diário e movido para <c>Erro\&lt;id&gt;\ExtensaoInvalida</c> com o sidecar .motivo.txt.
    /// NUNCA chama o Orquestrador (não é nota fiscal).
    /// </summary>
    public IReadOnlyList<RegistroDiario> ProcessarNaoXml(
        IReadOnlyCollection<string> arquivos, string identificadorPacote)
    {
        ArgumentNullException.ThrowIfNull(arquivos);
        return ProcessarNaoXml(arquivos.Select(Raiz).ToList(), identificadorPacote);
    }

    /// <summary>
    /// Igual a <see cref="ProcessarNaoXml(IReadOnlyCollection{string}, string)"/>, com tipo e origem
    /// (pasta). Na PuxadaTSP o motivo diz que a pasta aceita apenas ZIP.
    /// </summary>
    public IReadOnlyList<RegistroDiario> ProcessarNaoXml(
        IReadOnlyCollection<EntradaArquivo> entradas, string identificadorPacote)
    {
        ArgumentNullException.ThrowIfNull(entradas);
        var registros = new ConcurrentQueue<RegistroDiario>();

        var ordenados = entradas
            .OrderBy(e => ObterCriacao(e.Caminho))
            .ThenBy(e => Path.GetFileName(e.Caminho), StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var entrada in ordenados)
        {
            var arquivo = entrada.Caminho;
            if (_movePendentes.TryGetValue(arquivo, out var pendente))
            {
                RepetirMove(arquivo, pendente);
                continue;
            }

            var extensao = Path.GetExtension(arquivo);
            var motivo = entrada.Tipo == TipoNota.Tsp
                ? $"Arquivo ignorado: a pasta {entrada.Origem} aceita apenas ZIP ou XML (extensão {extensao}). " +
                  "Salve aqui somente o ZIP ou os XMLs da puxada TSP."
                : $"Arquivo ignorado: extensão {extensao} não é XML. " +
                  "Coloque apenas arquivos .xml na pasta de entrada.";
            _logger.LogInformation(
                "Arquivo não-XML ignorado (extensão {Extensao}): {Arquivo}", extensao, Path.GetFileName(arquivo));
            Finalizar(registros, Registro(arquivo, false, Path.GetFileName(arquivo), string.Empty, motivo, false, 0,
                    StatusLinhaRelatorio.ArquivoIgnorado, string.Empty, string.Empty, null,
                    Array.Empty<string>(), CategoriaErro.ExtensaoInvalida, DestinoArquivo.Erro, identificadorPacote,
                    Ctx.De(entrada)),
                arquivo);
        }

        return registros.ToList();
    }

    /// <summary>
    /// Processa os arquivos (XML soltos da raiz, como PUXADA) e devolve os registros novos.
    /// Respeita a ordem de chegada (criação, depois nome). Propaga apenas o cancelamento.
    /// </summary>
    public Task<IReadOnlyList<RegistroDiario>> ProcessarAsync(
        IReadOnlyCollection<string> arquivos, string identificadorPacote, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(arquivos);
        return ProcessarAsync(arquivos.Select(Raiz).ToList(), identificadorPacote, ct);
    }

    /// <summary>
    /// Processa XML e ZIP com tipo e origem. PUXADA (raiz e pasta Puxada): ondas por número de
    /// transporte. TSP (pasta PuxadaTSP): uma onda por ZIP, com o nome gerado por
    /// <see cref="NomeadorOndaTsp"/>. Respeita a ordem de chegada. Propaga apenas o cancelamento.
    /// </summary>
    public async Task<IReadOnlyList<RegistroDiario>> ProcessarAsync(
        IReadOnlyCollection<EntradaArquivo> entradas, string identificadorPacote, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entradas);
        var registros = new ConcurrentQueue<RegistroDiario>();

        var ordenados = entradas
            .OrderBy(e => ObterCriacao(e.Caminho))
            .ThenBy(e => Path.GetFileName(e.Caminho), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var temporarias = new List<string>();
        try
        {
            // 1) Parse (e repetição de moves pendentes). ZIP é aberto aqui e seus XMLs entram na fila.
            var lidos = new List<Lido>();
            var zips = new List<ZipTratado>();
            var chaves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inicioLeitura = _clock.UtcNow;
            foreach (var entrada in ordenados)
            {
                ct.ThrowIfCancellationRequested();

                if (_movePendentes.TryGetValue(entrada.Caminho, out var pendente))
                {
                    RepetirMove(entrada.Caminho, pendente);
                    continue;
                }

                if (entrada.EhZip)
                {
                    PrepararZip(entrada, identificadorPacote, lidos, zips, chaves, temporarias, registros);
                }
                else
                {
                    LerXml(entrada.Caminho, Ctx.De(entrada), null, identificadorPacote, lidos, chaves, registros);
                }
            }

            UltimaLeituraMs = Math.Max(0L, (long)(_clock.UtcNow - inicioLeitura).TotalMilliseconds);

            if (lidos.Count > 0)
            {
                await IntegrarAsync(lidos, zips, identificadorPacote, registros, ct).ConfigureAwait(false);
            }

            // O ZIP só sai da pasta depois que todos os seus XMLs foram tratados (cada um já foi
            // movido para Processados\<id> ou Erro\<id>\...).
            foreach (var zip in zips)
            {
                MoverZipTratado(zip, identificadorPacote);
            }

            return registros.ToList();
        }
        finally
        {
            foreach (var pasta in temporarias)
            {
                _leitorZip.Limpar(pasta);
            }
        }
    }

    private async Task IntegrarAsync(
        List<Lido> lidos,
        List<ZipTratado> zips,
        string identificadorPacote,
        ConcurrentQueue<RegistroDiario> registros,
        CancellationToken ct)
    {
        // 2) Onda de cada ZIP da TSP (nome gerado na ordem de chegada, antes de qualquer envio).
        // Cada ZIP é uma onda (reaproveitada por hash). Todos os XMLs soltos da PuxadaTSP do pacote
        // formam UMA onda sem título (TSP-AAAAMMDD-NN), nunca reaproveitada: reprocesso gera novo
        // sequencial. A chave "" representa o grupo dos XMLs soltos.
        var tspGrupos = lidos
            .Select((l, i) => (L: l, I: i))
            .Where(x => x.L.Ctx.EhTsp)
            .GroupBy(x => x.L.ZipCaminho ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Min(x => x.I))
            .ToList();
        var ondaDoGrupo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var grupo in tspGrupos)
        {
            if (grupo.Key.Length == 0)
            {
                ondaDoGrupo[grupo.Key] = _ondasTsp.ObterOuCriar(string.Empty, string.Empty);
                continue;
            }

            var hash = zips.First(z => string.Equals(z.Entrada.Caminho, grupo.Key, StringComparison.OrdinalIgnoreCase)).Hash;
            ondaDoGrupo[grupo.Key] = _ondasTsp.ObterOuCriar(Path.GetFileName(grupo.Key), hash);
        }

        var arquivoDe = new Dictionary<NotaFiscal, (string Arquivo, int Ordem, Ctx Ctx)>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < lidos.Count; i++)
        {
            var l = lidos[i];
            var ctx = l.Ctx.EhTsp ? l.Ctx with { Onda = ondaDoGrupo[l.ZipCaminho ?? string.Empty] } : l.Ctx;
            arquivoDe[l.Nota] = (l.Arquivo, i, ctx);
        }

        // 3) PUXADA: agrupa por transporte. TSP: uma onda por ZIP (e uma para os XMLs soltos). Ondas na ordem do primeiro arquivo.
        var agrupamento = _agrupador.Agrupar(lidos.Where(l => !l.Ctx.EhTsp).Select(l => l.Nota));
        var ondas = agrupamento.Ondas
            .Select(o => (o.NumeroTransporte, Notas: (IReadOnlyList<NotaFiscal>)o.Notas))
            .ToList();
        ondas.AddRange(tspGrupos.Select(g => (ondaDoGrupo[g.Key], Notas: (IReadOnlyList<NotaFiscal>)g.Select(x => x.L.Nota).ToList())));
        ondas = ondas.OrderBy(o => o.Notas.Min(n => arquivoDe[n].Ordem)).ToList();
        if (agrupamento.SemTransporte.Count > 0)
        {
            ondas.Add((string.Empty, agrupamento.SemTransporte));
        }

        // 4) Ondas em paralelo limitado; notas de cada onda em sequência.
        using var semaforo = new SemaphoreSlim(Math.Max(1, _opcoes.MaxTransportesConcorrentes));
        var tarefas = ondas.Select(o => ProcessarOndaAsync(o.Item1, o.Notas, arquivoDe, semaforo, registros, identificadorPacote, ct))
            .ToList();
        await Task.WhenAll(tarefas).ConfigureAwait(false);
    }

    // Lê um XML (solto ou extraído de ZIP). Inválido: registra e move para Erro. Repetido (mesma
    // chave já vista nesta foto) vindo de ZIP: não vai ao WMS; registra como ignorado em Processados.
    private void LerXml(
        string arquivo, Ctx ctx, string? zipCaminho, string identificadorPacote,
        List<Lido> lidos, HashSet<string> chaves, ConcurrentQueue<RegistroDiario> registros)
    {
        NotaFiscal nota;
        try
        {
            nota = _leitor.ParseArquivo(arquivo);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var motivo = "XML inválido: " + ex.Message;
            _logger.LogWarning("Arquivo {Arquivo} não pôde ser lido: {Motivo}", arquivo, ex.Message);
            Finalizar(registros, Registro(arquivo, true, Path.GetFileName(arquivo), string.Empty, motivo, false, 0,
                    StatusLinhaRelatorio.NaoIntegrada, string.Empty, string.Empty, null,
                    new[] { motivo }, CategoriaErro.XmlInvalido, DestinoArquivo.Erro, identificadorPacote, ctx),
                arquivo);
            return;
        }

        var nova = string.IsNullOrEmpty(nota.ChaveNFe) || chaves.Add(nota.ChaveNFe);
        if (!nova && zipCaminho is not null)
        {
            var motivo = LinhaRelatorioErro.PrefixoXmlRepetido + " (chave já processada neste pacote). Não foi enviado de novo ao WMS.";
            _logger.LogInformation("XML repetido dentro do pacote ignorado: {Arquivo}", Path.GetFileName(arquivo));
            Finalizar(registros, Registro(arquivo, false, nota.Numero, nota.NumeroTransporte ?? string.Empty, motivo, false, 0,
                    StatusLinhaRelatorio.ArquivoIgnorado, string.Empty, string.Empty, null,
                    Array.Empty<string>(), null, DestinoArquivo.Processados, identificadorPacote, ctx),
                arquivo);
            return;
        }

        lidos.Add(new Lido(arquivo, nota, ctx, zipCaminho));
    }

    private void PrepararZip(
        EntradaArquivo zip, string identificadorPacote, List<Lido> lidos, List<ZipTratado> zips,
        HashSet<string> chaves, List<string> temporarias, ConcurrentQueue<RegistroDiario> registros)
    {
        var nomeZip = Path.GetFileName(zip.Caminho);
        var ctx = new Ctx(zip.Tipo.Rotulo(), zip.Origem, string.Empty, nomeZip);

        ResultadoZip resultado;
        try
        {
            resultado = _leitorZip.Extrair(zip.Caminho);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var motivo = "ZIP inválido: " + ex.Message;
            _logger.LogWarning("ZIP {Zip} não pôde ser lido: {Motivo}", nomeZip, ex.Message);
            Finalizar(registros, Registro(zip.Caminho, false, nomeZip, string.Empty, motivo, false, 0,
                    StatusLinhaRelatorio.NaoIntegrada, string.Empty, string.Empty, null,
                    new[] { motivo }, CategoriaErro.ZipInvalido, DestinoArquivo.Erro, identificadorPacote, ctx),
                zip.Caminho);
            return;
        }

        temporarias.Add(resultado.PastaTemporaria);
        zips.Add(new ZipTratado(zip, resultado.Hash));

        // Entradas não usadas (não-XML, ZIP aninhado, caminho perigoso): só aparecem no relatório.
        foreach (var ignorada in resultado.Ignoradas)
        {
            var motivo = $"{ignorada.Nome}: {ignorada.Motivo}";
            var registro = Registro(
                zip.Caminho + "!" + ignorada.Nome, false, ignorada.Nome, string.Empty, motivo, false, 0,
                ignorada.Insegura ? StatusLinhaRelatorio.NaoIntegrada : StatusLinhaRelatorio.ArquivoIgnorado,
                string.Empty, string.Empty, null, Array.Empty<string>(),
                ignorada.Insegura ? CategoriaErro.ZipInvalido : CategoriaErro.ExtensaoInvalida,
                DestinoArquivo.Erro, identificadorPacote, ctx);
            _diario.Registrar(registro);
            registros.Enqueue(registro);
        }

        foreach (var xml in resultado.Xmls)
        {
            LerXml(xml.Caminho, ctx, zip.Caminho, identificadorPacote, lidos, chaves, registros);
        }
    }

    private void MoverZipTratado(ZipTratado zip, string identificadorPacote)
    {
        var caminho = zip.Entrada.Caminho;
        var mov = _organizador.Mover(caminho, DestinoArquivo.Processados, null, string.Empty, identificadorPacote);
        if (!mov.Sucesso && File.Exists(caminho))
        {
            _logger.LogWarning(
                "ZIP {Zip} já tratado, mas o move falhou ({Erro}); será repetido sem reenviar ao WMS.", caminho, mov.Erro);
            _movePendentes[caminho] = new MovePendente(DestinoArquivo.Processados, null, string.Empty, identificadorPacote);
        }
    }

    private async Task ProcessarOndaAsync(
        string transporte,
        IReadOnlyList<NotaFiscal> notas,
        Dictionary<NotaFiscal, (string Arquivo, int Ordem, Ctx Ctx)> arquivoDe,
        SemaphoreSlim semaforo,
        ConcurrentQueue<RegistroDiario> registros,
        string identificadorPacote,
        CancellationToken ct)
    {
        await semaforo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var nota in notas)
            {
                ct.ThrowIfCancellationRequested();
                var (arquivo, _, ctx) = arquivoDe[nota];
                // TSP: o argumento é o nome da onda; o relatório mostra o transporte da própria nota.
                var exibido = ctx.EhTsp ? nota.NumeroTransporte ?? string.Empty : transporte;
                var inicioNota = _clock.UtcNow;
                try
                {
                    var resultados = await _orquestrador.IntegrarAsync(new[] { nota }, transporte, ct)
                        .ConfigureAwait(false);
                    var resultado = resultados.Count > 0
                        ? resultados[0]
                        : throw new InvalidOperationException("O Orquestrador não devolveu resultado para a nota.");
                    RegistrarSeparacao(nota, resultado, arquivo, ctx, transporte, identificadorPacote);
                    Concluir(registros, arquivo, resultado, identificadorPacote, ctx, ctx.EhTsp ? exibido : null, inicioNota);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Exceção ao integrar a nota {Nota} ({Arquivo}).", nota.Numero, arquivo);
                    var motivo = "Exceção ao integrar: " + ex.Message;
                    Finalizar(registros, Registro(arquivo, false, nota.Numero, exibido, motivo, false, 0,
                            StatusLinhaRelatorio.NaoIntegrada, string.Empty, string.Empty, null,
                            new[] { motivo }, CategoriaErro.Outro, DestinoArquivo.Erro, identificadorPacote, ctx,
                            inicioNota),
                        arquivo);
                }
            }
        }
        finally
        {
            semaforo.Release();
        }
    }

    // I51: registra a nota aceita (ou pendente/já existente) para acompanhar a separação. Só loga em falha.
    private void RegistrarSeparacao(
        NotaFiscal nota, ResultadoIntegracaoNota resultado, string arquivo, Ctx ctx, string transporte, string identificadorPacote)
    {
        if (_separacao is null)
        {
            return;
        }

        try
        {
            var aceita = resultado.Integrada
                         || resultado.Estado == EstadoIntegracao.Pendente
                         || ClassificadorJaExiste.EhJaExiste(resultado.UltimaMensagem);
            if (!aceita || _classificador.DestinoDe(resultado) != DestinoArquivo.Processados)
            {
                return;
            }

            _separacao.Registrar(nota, arquivo, ctx.Tipo, ctx.Onda, transporte, identificadorPacote);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Falha ao registrar a nota {Nota} no acompanhamento da separação.", nota.Numero);
        }
    }

    private void Concluir(
        ConcurrentQueue<RegistroDiario> registros, string arquivo, ResultadoIntegracaoNota r,
        string identificadorPacote, Ctx ctx, string? transporteExibido, DateTimeOffset? inicioNota = null)
    {
        var destino = _classificador.DestinoDe(r);
        var categoria = destino == DestinoArquivo.Erro ? _classificador.Classificar(r) : (CategoriaErro?)null;

        var mensagem = r.UltimaMensagem ?? string.Empty;
        if (destino == DestinoArquivo.Processados && !r.Integrada && r.Estado != EstadoIntegracao.Pendente)
        {
            mensagem = "Já existia no WMS (reprocesso): " + mensagem;
        }

        var status = r.Estado switch
        {
            EstadoIntegracao.Integrada => StatusLinhaRelatorio.Integrada,
            EstadoIntegracao.Pendente => StatusLinhaRelatorio.Pendente,
            _ => StatusLinhaRelatorio.NaoIntegrada,
        };

        Finalizar(registros, Registro(arquivo, false, r.NumeroNota, transporteExibido ?? r.NumeroTransporte, mensagem, r.Integrada,
                r.Tentativas, status, r.IdTransacao, r.IdDocumento, r.ProdutosProvisorios, r.Historico,
                categoria, destino, identificadorPacote, ctx, inicioNota, r.DuracaoWmsMs),
            arquivo);
    }

    // Registra no diário ANTES de mover (uma queda no meio reenvia no máximo 1 nota; o WMS barra a duplicata).
    private void Finalizar(ConcurrentQueue<RegistroDiario> registros, RegistroDiario registro, string arquivo)
    {
        _diario.Registrar(registro);
        registros.Enqueue(registro);

        var motivo = MotivoSidecar(registro);
        var id = registro.IdentificadorPacote ?? string.Empty;
        var mov = _organizador.Mover(arquivo, registro.Destino, registro.Categoria, motivo, id);
        if (!mov.Sucesso && File.Exists(arquivo))
        {
            _logger.LogWarning(
                "Arquivo {Arquivo} já tratado, mas o move falhou ({Erro}); será repetido sem reenviar ao WMS.",
                arquivo, mov.Erro);
            _movePendentes[arquivo] = new MovePendente(registro.Destino, registro.Categoria, motivo, id);
        }
    }

    private void RepetirMove(string arquivo, MovePendente p)
    {
        var mov = _organizador.Mover(arquivo, p.Destino, p.Categoria, p.Motivo, p.IdentificadorPacote);
        if (mov.Sucesso || !File.Exists(arquivo))
        {
            _movePendentes.TryRemove(arquivo, out _);
        }
    }

    private static string MotivoSidecar(RegistroDiario r) =>
        $"Nota: {r.NumeroNota}{Environment.NewLine}Transporte: {r.NumeroTransporte}{Environment.NewLine}" +
        $"Tentativas: {r.Tentativas}{Environment.NewLine}Data: {r.Horario.ToLocalTime():yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
        $"Mensagem: {r.Mensagem}";

    private RegistroDiario Registro(
        string arquivo, bool xmlInvalido, string nota, string transporte, string mensagem, bool integrada,
        int tentativas, StatusLinhaRelatorio status, string idTransacao, string idDocumento,
        IReadOnlyList<string>? produtos, IReadOnlyList<string> historico, CategoriaErro? categoria,
        DestinoArquivo destino, string identificadorPacote, Ctx? ctx = null,
        DateTimeOffset? inicioNota = null, long duracaoWmsMs = 0) =>
        new(arquivo, xmlInvalido, nota ?? string.Empty, transporte ?? string.Empty, _clock.UtcNow, mensagem,
            integrada, tentativas, status, idTransacao ?? string.Empty, idDocumento ?? string.Empty,
            produtos ?? Array.Empty<string>(), historico ?? Array.Empty<string>(), categoria, destino,
            identificadorPacote, ctx?.Tipo ?? TipoNotaExtensoes.RotuloPuxada, ctx?.Origem ?? string.Empty,
            ctx?.Onda ?? string.Empty, ctx?.Zip, inicioNota, duracaoWmsMs);

    private static DateTime ObterCriacao(string caminho)
    {
        try
        {
            return File.GetCreationTimeUtc(caminho);
        }
        catch (Exception)
        {
            return DateTime.MaxValue;
        }
    }
}
