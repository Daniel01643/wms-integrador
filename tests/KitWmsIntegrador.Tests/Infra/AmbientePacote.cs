using KitWmsIntegrador.Core.Agrupamento;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Email;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Core.Modelos;
using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Orquestrador;
using KitWmsIntegrador.Core.Pacote;
using KitWmsIntegrador.Core.Relatorio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests.Infra;

/// <summary>Enviador de e-mail que apenas grava as mensagens.</summary>
public sealed class GravadorEmail : IEnviadorEmail
{
    public List<MensagemEmail> Enviadas { get; } = new();

    public ResultadoEnvioEmail Resultado { get; set; } = ResultadoEnvioEmail.Enviado();

    public Task<ResultadoEnvioEmail> EnviarAsync(MensagemEmail mensagem, CancellationToken ct)
    {
        Enviadas.Add(mensagem);
        return Task.FromResult(Resultado);
    }
}

/// <summary>Orquestrador falso e programável: grava chamadas, mede concorrência e aceita atraso.</summary>
public sealed class OrquestradorFalso : IOrquestrador
{
    private int _emAndamento;
    private int _maximo;

    public Func<NotaFiscal, string, Task<ResultadoIntegracaoNota>> Comportamento { get; set; } =
        (nota, transporte) => Task.FromResult(Sucesso(nota.Numero, transporte));

    public List<(string Nota, string Transporte)> Chamadas { get; } = new();

    public int MaximoSimultaneo => _maximo;

    public TimeSpan Atraso { get; set; } = TimeSpan.Zero;

    public static ResultadoIntegracaoNota Sucesso(string nota, string transporte) =>
        new(nota, transporte, true, 1, EstadoIntegracao.Integrada, "Registrado com sucesso.", new[] { "ok" });

    public static ResultadoIntegracaoNota Falha(string nota, string transporte, string mensagem, int tentativas = 1) =>
        new(nota, transporte, false, tentativas, EstadoIntegracao.NaoIntegrada, mensagem, new[] { mensagem });

    public Task<IReadOnlyList<ResultadoIntegracaoNota>> IntegrarAsync(Onda onda, CancellationToken cancellationToken = default) =>
        IntegrarAsync(onda.Notas, onda.NumeroTransporte, cancellationToken);

    public async Task<IReadOnlyList<ResultadoIntegracaoNota>> IntegrarAsync(
        IEnumerable<NotaFiscal> notas, string numeroTransporte, CancellationToken cancellationToken = default)
    {
        var lista = new List<ResultadoIntegracaoNota>();
        foreach (var nota in notas)
        {
            lock (Chamadas)
            {
                Chamadas.Add((nota.Numero, numeroTransporte));
            }

            var atual = Interlocked.Increment(ref _emAndamento);
            int anterior;
            while (atual > (anterior = Volatile.Read(ref _maximo)))
            {
                Interlocked.CompareExchange(ref _maximo, atual, anterior);
            }

            try
            {
                if (Atraso > TimeSpan.Zero)
                {
                    await Task.Delay(Atraso, cancellationToken);
                }

                lista.Add(await Comportamento(nota, numeroTransporte));
            }
            finally
            {
                Interlocked.Decrement(ref _emAndamento);
            }
        }

        return lista;
    }
}

/// <summary>Monta a pilha completa do pacote em pastas temporárias, com relógio falso.</summary>
public sealed class AmbientePacote : IDisposable
{
    public AmbientePacote()
    {
        Raiz = Path.Combine(Path.GetTempPath(), "kitwms-pacote-" + Guid.NewGuid().ToString("N"));
        Entrada = Path.Combine(Raiz, "entrada");
        Relatorios = Path.Combine(Raiz, "relatorios");
        Directory.CreateDirectory(Entrada);
        Pastas = new OpcoesPasta { Entrada = Entrada };
    }

    public string Raiz { get; }

    public string Entrada { get; }

    public string Relatorios { get; }

    public FakeClock Clock { get; } = new(new DateTimeOffset(2025, 1, 10, 12, 0, 0, TimeSpan.Zero));

    public OpcoesPasta Pastas { get; }

    public OpcoesPacote Pacote { get; } = new();

    public GravadorEmail Email { get; } = new();

    // Fallback de destinatários (Email:Destinatarios) para que o e-mail do pacote seja emitido por
    // padrão nos testes que não exercitam explicitamente a fonte de destinatários do fechamento.
    public OpcoesEmail OpcoesDeEmail { get; } = new() { Destinatarios = "fechamento@exemplo.com" };

    public IGeradorRelatorio? GeradorSubstituto { get; set; }

    /// <summary>Registrador da separação (I51) injetado no ProcessadorLote; null = comportamento anterior.</summary>
    public KitWmsIntegrador.Core.Separacao.IRegistradorSeparacao? RegistradorSeparacao { get; set; }

    public string Caminho(string nome) => Path.Combine(Entrada, nome);

    /// <summary>Grava um XML de NF-e sintético na entrada e devolve o caminho.</summary>
    public string EscreverXml(string arquivo, string numero, string? transporte)
    {
        var caminho = Caminho(arquivo);
        File.WriteAllText(caminho, Xml(numero, transporte));
        return caminho;
    }

    /// <summary>Grava um arquivo não-XML qualquer (ex.: .pdf, .txt) na entrada e devolve o caminho.</summary>
    public string EscreverNaoXml(string arquivo, string conteudo = "conteudo")
    {
        var caminho = Caminho(arquivo);
        File.WriteAllText(caminho, conteudo);
        return caminho;
    }

    public static string Xml(string numero, string? transporte)
    {
        var chave = numero.PadLeft(44, '0');
        var infCpl = transporte is null
            ? "Pedido sem numero de transporte informado."
            : "Transporte: " + transporte;
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
              <NFe>
                <infNFe Id="NFe{chave}" versao="4.00">
                  <ide><nNF>{numero}</nNF><serie>1</serie><mod>55</mod><dhEmi>2025-12-15T08:00:00-03:00</dhEmi></ide>
                  <emit><CNPJ>99999999000199</CNPJ><xNome>EMITENTE LTDA</xNome></emit>
                  <dest><CNPJ>11111111000111</CNPJ><xNome>DESTINATARIO</xNome></dest>
                  <det nItem="1"><prod><cProd>ABC{numero}</cProd><xProd>PRODUTO</xProd><qCom>1.0000</qCom><uCom>UN</uCom><vProd>10.00</vProd><CFOP>5102</CFOP></prod></det>
                  <total><ICMSTot><vNF>10.00</vNF></ICMSTot></total>
                  <infAdic><infCpl>{infCpl}</infCpl></infAdic>
                </infNFe>
              </NFe>
            </nfeProc>
            """;
    }

    /// <summary>CNPJ do emitente (BRF) usado nos XMLs sintéticos da Puxada.</summary>
    public const string CnpjBrf = "01838723009850";

    /// <summary>Chave de 44 dígitos SINTÉTICA no layout real (UF, AAMM, CNPJ BRF, modelo 55, série 6, número).</summary>
    public static string ChaveBrf(int numero)
    {
        var baseChave = "35" + "2611" + CnpjBrf + "55" + "006" + numero.ToString("D9") + "1" + "00000001";
        var soma = 0;
        var peso = 2;
        for (var i = baseChave.Length - 1; i >= 0; i--)
        {
            soma += (baseChave[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }

        var resto = soma % 11;
        return baseChave + (resto < 2 ? 0 : 11 - resto);
    }

    /// <summary>
    /// XML sintético no padrão da Puxada: emitente BRF (CNPJ 01.838.723/0098-50), série 6 e
    /// "Transporte: NNNNNNNNNN" no infCpl. Nenhum dado real.
    /// </summary>
    public static string XmlBrf(int numero, string? transporte)
    {
        var chave = ChaveBrf(numero);
        var infCpl = transporte is null ? "Pedido sem transporte informado." : "Transporte: " + transporte;
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
              <NFe>
                <infNFe Id="NFe{chave}" versao="4.00">
                  <ide><nNF>{numero}</nNF><serie>6</serie><mod>55</mod><dhEmi>2026-11-10T08:00:00-03:00</dhEmi></ide>
                  <emit><CNPJ>{CnpjBrf}</CNPJ><xNome>EMITENTE SINTETICO</xNome></emit>
                  <dest><CNPJ>11111111000111</CNPJ><xNome>DESTINATARIO SINTETICO</xNome></dest>
                  <det nItem="1"><prod><cProd>ABC{numero}</cProd><xProd>PRODUTO</xProd><qCom>1.0000</qCom><uCom>UN</uCom><vProd>10.00</vProd><CFOP>5102</CFOP></prod></det>
                  <total><ICMSTot><vNF>10.00</vNF></ICMSTot></total>
                  <infAdic><infCpl>{infCpl}</infCpl></infAdic>
                </infNFe>
              </NFe>
            </nfeProc>
            """;
    }

    /// <summary>Nome da entrada do ZIP para a nota: NFE&lt;chave&gt;.XML.</summary>
    public static string NomeXmlBrf(int numero) => $"NFE{ChaveBrf(numero)}.XML";

    /// <summary>Cria a pasta (relativa à Entrada; vazio = raiz) e devolve o caminho completo.</summary>
    public string Pasta(string subpasta)
    {
        var caminho = string.IsNullOrEmpty(subpasta) ? Entrada : Path.Combine(Entrada, subpasta);
        Directory.CreateDirectory(caminho);
        return caminho;
    }

    /// <summary>Grava um ZIP SINTÉTICO em <paramref name="subpasta"/> (System.IO.Compression) e devolve o caminho.</summary>
    public string EscreverZip(string subpasta, string nomeZip, params (string Nome, string Conteudo)[] itens)
    {
        var caminho = Path.Combine(Pasta(subpasta), nomeZip);
        using (var fs = new FileStream(caminho, FileMode.Create, FileAccess.Write))
        using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
        {
            foreach (var (nome, conteudo) in itens)
            {
                var entrada = zip.CreateEntry(nome);
                using var w = new StreamWriter(entrada.Open(), new System.Text.UTF8Encoding(false));
                w.Write(conteudo);
            }
        }

        return caminho;
    }

    /// <summary>ZIP sintético da TSP: notas numeradas a partir de <paramref name="primeiro"/> com os transportes dados.</summary>
    public string EscreverZipBrf(string subpasta, string nomeZip, int primeiro, params string?[] transportes) =>
        EscreverZip(subpasta, nomeZip, transportes
            .Select((t, i) => (NomeXmlBrf(primeiro + i), XmlBrf(primeiro + i, t)))
            .ToArray());

    public ProcessadorLote CriarProcessador(IOrquestrador orquestrador, out DiarioPacote diario)
    {
        diario = new DiarioPacote(Options.Create(Pastas), NullLogger<DiarioPacote>.Instance);
        var organizador = new OrganizadorArquivos(Clock, Options.Create(Pastas), NullLogger<OrganizadorArquivos>.Instance);
        return new ProcessadorLote(
            orquestrador,
            new LeitorNotaFiscalXml(),
            new AgrupadorTransporte(),
            new ClassificadorCategoriaErro(new ClassificadorRetorno()),
            organizador,
            diario,
            Clock,
            Options.Create(Pacote),
            new LeitorZipSeguro(Options.Create(new OpcoesZip()), Options.Create(Pastas), NullLogger<LeitorZipSeguro>.Instance),
            new RegistroOndasTsp(Clock, Options.Create(Pastas), NullLogger<RegistroOndasTsp>.Instance),
            NullLogger<ProcessadorLote>.Instance,
            RegistradorSeparacao);
    }

    public GerenciadorPacote CriarGerenciador(IOrquestrador orquestrador)
    {
        var processador = CriarProcessador(orquestrador, out var diario);
        var detector = new DetectorArquivoPronto(Clock, Options.Create(Pacote), NullLogger<DetectorArquivoPronto>.Instance);
        var gerador = GeradorSubstituto
            ?? new GeradorRelatorio(Options.Create(new OpcoesRelatorio { PastaSaida = Relatorios }));
        var telemetria = new RegistradorTelemetria(
            Options.Create(Pastas),
            Options.Create(new OpcoesMonitoramento()),
            NullLogger<RegistradorTelemetria>.Instance);
        var destinatarios = new FonteDestinatariosFechamento(
            Options.Create(OpcoesDeEmail), telemetria, NullLogger<FonteDestinatariosFechamento>.Instance);
        var fechador = new FechadorPacote(
            gerador,
            Email,
            Options.Create(OpcoesDeEmail),
            Options.Create(new OpcoesRelatorio { PastaSaida = Relatorios }),
            telemetria,
            Options.Create(new OpcoesMonitoramento()),
            destinatarios,
            NullLogger<FechadorPacote>.Instance);
        return new GerenciadorPacote(
            Clock, Options.Create(Pastas), Options.Create(Pacote), detector, processador, diario, fechador,
            NullLogger<GerenciadorPacote>.Instance);
    }

    /// <summary>Roda ciclos (avançando o relógio) até o estado voltar a Ocioso. Devolve a quantidade de ciclos.</summary>
    public async Task<int> RodarAteOciosoAsync(GerenciadorPacote g, int max = 40, int passoSegundos = 10)
    {
        for (var i = 1; i <= max; i++)
        {
            await g.CicloAsync(CancellationToken.None);
            if (g.Estado == EstadoPacote.Ocioso)
            {
                return i;
            }

            Clock.Avancar(TimeSpan.FromSeconds(passoSegundos));
        }

        throw new InvalidOperationException("O pacote não voltou a Ocioso em " + max + " ciclos.");
    }

    public string[] Arquivos(string subpasta) =>
        Directory.Exists(Path.Combine(Entrada, subpasta))
            ? Directory.GetFiles(Path.Combine(Entrada, subpasta), "*.xml", SearchOption.AllDirectories)
            : Array.Empty<string>();

    /// <summary>
    /// XMLs de erro de uma categoria, independentemente da subpasta de pacote intermediária
    /// (<c>Erro\pacote_&lt;id&gt;\&lt;categoria&gt;\...</c>). Útil após a Etapa 7c.
    /// </summary>
    public string[] ArquivosErroCategoria(string categoria)
    {
        var erro = Path.Combine(Entrada, "Erro");
        return Directory.Exists(erro)
            ? Directory.GetFiles(erro, "*.xml", SearchOption.AllDirectories)
                .Where(a => string.Equals(
                    Path.GetFileName(Path.GetDirectoryName(a)), categoria, StringComparison.OrdinalIgnoreCase))
                .ToArray()
            : Array.Empty<string>();
    }

    /// <summary>
    /// Arquivos de erro de uma categoria com QUALQUER extensão (não só .xml), independentemente
    /// da subpasta de pacote intermediária (<c>Erro\pacote_&lt;id&gt;\&lt;categoria&gt;\...</c>).
    /// Exclui os sidecars <c>.motivo.txt</c>. Útil para ver arquivos ignorados (.pdf, .txt).
    /// </summary>
    public string[] ArquivosErroCategoriaQualquer(string categoria)
    {
        var erro = Path.Combine(Entrada, "Erro");
        return Directory.Exists(erro)
            ? Directory.GetFiles(erro, "*", SearchOption.AllDirectories)
                .Where(a => !a.EndsWith(".motivo.txt", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(
                                Path.GetFileName(Path.GetDirectoryName(a)), categoria, StringComparison.OrdinalIgnoreCase))
                .ToArray()
            : Array.Empty<string>();
    }

    public string[] RelatoriosGerados(string extensao) =>
        Directory.Exists(Relatorios) ? Directory.GetFiles(Relatorios, "*" + extensao) : Array.Empty<string>();

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Raiz))
            {
                Directory.Delete(Raiz, true);
            }
        }
        catch (IOException)
        {
        }
    }
}
