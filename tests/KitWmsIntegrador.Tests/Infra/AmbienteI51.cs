using System.Text;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Construtores;
using KitWmsIntegrador.Core.Leitura;
using KitWmsIntegrador.Core.Modelos;
using KitWmsIntegrador.Core.Orquestrador;
using KitWmsIntegrador.Core.Separacao;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Tests.Infra;

/// <summary>Coletor de logs para verificar que nada sensível é gravado.</summary>
public sealed class ColetorLogs : ILoggerProvider
{
    public List<string> Linhas { get; } = new();

    public ILogger CreateLogger(string categoryName) => new L(this);

    public void Dispose()
    {
    }

    private sealed class L : ILogger
    {
        private readonly ColetorLogs _pai;

        public L(ColetorLogs pai) => _pai = pai;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_pai.Linhas)
            {
                _pai.Linhas.Add(logLevel + ": " + formatter(state, exception) + (exception is null ? string.Empty : " | " + exception));
            }
        }
    }
}

/// <summary>
/// Pilha completa do fluxo I51 → I60 em pasta temporária, com relógio e WMS falsos. <see cref="Reiniciar"/> recria todas
/// as instâncias sobre a MESMA pasta (simula reinício do processo).
/// </summary>
public sealed class AmbienteI51 : IDisposable
{
    public const string CnpjProprietario = "01838723009850";
    public const string CnpjDestinatario = "11111111000111";

    private readonly ILoggerFactory _logs;

    public AmbienteI51(ILoggerFactory? logs = null, OpcoesPasta? pastas = null)
    {
        _logs = logs ?? NullLoggerFactory.Instance;
        Raiz = Path.Combine(Path.GetTempPath(), "kitwms-i51-" + Guid.NewGuid().ToString("N"));
        Entrada = pastas?.CaminhoEntrada ?? Path.Combine(Raiz, "entrada");
        Directory.CreateDirectory(Entrada);
        Pastas = pastas ?? new OpcoesPasta { Entrada = Entrada };
        I51 = new OpcoesI51 { Habilitada = true };
        I60 = new OpcoesI60 { Habilitada = true };
        Operacional = new OpcoesOperacionais
        {
            Deposito = "JD",
            CnpjArmazem = "14.605.065/0001-75",
            CnpjDepositante = "01.838.723/0098-50",
        };
        Reiniciar();
    }

    public string Raiz { get; }

    public string Entrada { get; }

    public OpcoesPasta Pastas { get; }

    public OpcoesI51 I51 { get; }

    public OpcoesI60 I60 { get; }

    public OpcoesOperacionais Operacional { get; }

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 11, 12, 15, 0, 0, TimeSpan.Zero));

    public FakeWmsI51 Wms { get; private set; } = new();

    public CaminhosI51 Caminhos { get; private set; } = null!;

    public RepositorioSeparacaoArquivo Repositorio { get; private set; } = null!;

    public RepositorioOrfaosI51 Orfaos { get; private set; } = null!;

    public ArmazenamentoBrutoI51 Brutos { get; private set; } = null!;

    public CasadorDocumentoNota Casador { get; private set; } = null!;

    public RegistradorSeparacao Registrador { get; private set; } = null!;

    public ClienteI51 Consulta { get; private set; } = null!;

    public ProcessadorI60Separacao ProcessadorI60 { get; private set; } = null!;

    public ComandosI51 Comandos { get; private set; } = null!;

    public ServicoSeparacaoI51 Servico { get; private set; } = null!;

    /// <summary>Recria repositório, serviços e (opcionalmente) o WMS sobre a mesma pasta.</summary>
    public void Reiniciar(bool manterWms = true)
    {
        if (!manterWms || Wms is null)
        {
            Wms = new FakeWmsI51();
        }

        var i51 = Options.Create(I51);
        Caminhos = new CaminhosI51(Options.Create(Pastas));
        Repositorio = new RepositorioSeparacaoArquivo(Caminhos, i51, Clock, _logs.CreateLogger<RepositorioSeparacaoArquivo>());
        Orfaos = new RepositorioOrfaosI51(Caminhos, Clock, _logs.CreateLogger<RepositorioOrfaosI51>());
        Brutos = new ArmazenamentoBrutoI51(Caminhos, Clock, _logs.CreateLogger<ArmazenamentoBrutoI51>());
        Casador = new CasadorDocumentoNota(
            Repositorio, Orfaos, i51, Options.Create(Operacional), Clock, _logs.CreateLogger<CasadorDocumentoNota>());
        Registrador = new RegistradorSeparacao(Repositorio, Casador, Caminhos, i51, Clock, _logs.CreateLogger<RegistradorSeparacao>());
        Consulta = new ClienteI51(Wms, i51, Options.Create(Operacional));
        ProcessadorI60 = new ProcessadorI60Separacao(
            Repositorio, Wms, new ConstrutorI60(Options.Create(Operacional), Options.Create(I60)), new LeitorNotaFiscalXml(),
            new ClassificadorRetorno(), Options.Create(I60), i51, Options.Create(Pastas), Caminhos, Clock,
            _logs.CreateLogger<ProcessadorI60Separacao>());
        Comandos = new ComandosI51(Caminhos, Clock, _logs.CreateLogger<ComandosI51>());
        Servico = new ServicoSeparacaoI51(
            Repositorio, Brutos, Consulta, Casador, Orfaos, ProcessadorI60, Comandos, i51, Clock, _logs.CreateLogger<ServicoSeparacaoI51>());
    }

    /// <summary>XML sintético de NF-e (emitente = proprietária, série 6) com os itens dados. Nenhum dado real.</summary>
    public static string Xml(int numero, string cnpjDest, params (string Cod, decimal Qtde)[] itens)
    {
        var chave = AmbientePacote.ChaveBrf(numero);
        var det = new StringBuilder();
        for (var i = 0; i < itens.Length; i++)
        {
            det.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"<det nItem=\"{i + 1}\"><prod><cProd>{itens[i].Cod}</cProd><xProd>PRODUTO {i + 1}</xProd><qCom>{itens[i].Qtde:0.0000}</qCom><uCom>CX</uCom><vUnCom>10.00</vUnCom><vProd>{itens[i].Qtde * 10:0.00}</vProd><CFOP>5102</CFOP></prod></det>");
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
              <NFe>
                <infNFe Id="NFe{chave}" versao="4.00">
                  <ide><nNF>{numero}</nNF><serie>6</serie><mod>55</mod><dhEmi>2026-11-10T08:00:00-03:00</dhEmi></ide>
                  <emit><CNPJ>{CnpjProprietario}</CNPJ><xNome>EMITENTE SINTETICO</xNome></emit>
                  <dest><CNPJ>{cnpjDest}</CNPJ><xNome>DESTINATARIO SINTETICO</xNome></dest>
                  {det}
                  <total><ICMSTot><vNF>100.00</vNF></ICMSTot></total>
                  <infAdic><infCpl>Transporte: 0001234567</infCpl></infAdic>
                </infNFe>
              </NFe>
            </nfeProc>
            """;
    }

    /// <summary>Grava o XML da nota na entrada, lê e registra no acompanhamento (como o ProcessadorLote faz). Devolve a nota.</summary>
    public NotaFiscal RegistrarNota(
        int numero, (string Cod, decimal Qtde)[] itens, string cnpjDest = CnpjDestinatario,
        string tipo = "PUXADA", string onda = "", string pacote = "pacote_20261112_150000")
    {
        var caminho = Path.Combine(Entrada, $"NFE{AmbientePacote.ChaveBrf(numero)}.XML");
        File.WriteAllText(caminho, Xml(numero, cnpjDest, itens));
        var nota = new LeitorNotaFiscalXml().ParseArquivo(caminho);
        Registrador.Registrar(nota, caminho, tipo, onda, "0001234567", pacote);
        return nota;
    }

    /// <summary>Chave de acompanhamento da nota.</summary>
    public static string Chave(int numero) => AmbientePacote.ChaveBrf(numero);

    public NotaSeparacao Nota(int numero) =>
        Repositorio.Obter(Chave(numero)) ?? throw new InvalidOperationException("Nota " + numero + " não registrada.");

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
