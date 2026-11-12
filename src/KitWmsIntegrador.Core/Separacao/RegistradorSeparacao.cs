using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Modelos;
using KitWmsIntegrador.Core.Tempo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KitWmsIntegrador.Core.Separacao;

/// <summary>Registra, na I50 aceita, a nota no acompanhamento da separação (I51).</summary>
public interface IRegistradorSeparacao
{
    /// <summary>
    /// Registra a nota. Com <c>I51:Habilitada=false</c> não faz nada (nem toca o disco). Nunca lança.
    /// </summary>
    void Registrar(NotaFiscal nota, string arquivoXml, string tipo, string onda, string transporte, string identificadorPacote);
}

/// <summary>
/// Cria o estado <c>AguardandoSeparacao</c> da nota e guarda uma cópia do XML (a mesma NF-e será
/// usada na I60). A primeira instrução testa a chave: desligada, retorna sem tocar nada.
/// </summary>
public sealed class RegistradorSeparacao : IRegistradorSeparacao
{
    private readonly IRepositorioSeparacao _repositorio;
    private readonly CasadorDocumentoNota _casador;
    private readonly CaminhosI51 _caminhos;
    private readonly OpcoesI51 _opcoes;
    private readonly IClock _clock;
    private readonly ILogger<RegistradorSeparacao> _logger;

    /// <summary>Cria o registrador.</summary>
    public RegistradorSeparacao(
        IRepositorioSeparacao repositorio, CasadorDocumentoNota casador, CaminhosI51 caminhos,
        IOptions<OpcoesI51> opcoes, IClock clock, ILogger<RegistradorSeparacao> logger)
    {
        _repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        _casador = casador ?? throw new ArgumentNullException(nameof(casador));
        _caminhos = caminhos ?? throw new ArgumentNullException(nameof(caminhos));
        _opcoes = opcoes?.Value ?? throw new ArgumentNullException(nameof(opcoes));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Chave de acompanhamento: a de 44 dígitos ou a sintética.</summary>
    public static string ChaveDe(NotaFiscal nota) =>
        nota.ChaveNFe.Length == 44 && nota.ChaveNFe.All(char.IsDigit)
            ? nota.ChaveNFe
            : $"nf-{nota.Numero}-{nota.Serie}-{ParserI51.Digitos(nota.DestinatarioCnpj)}";

    /// <inheritdoc />
    public void Registrar(NotaFiscal nota, string arquivoXml, string tipo, string onda, string transporte, string identificadorPacote)
    {
        if (!_opcoes.Habilitada)
        {
            return;
        }

        try
        {
            var chave = ChaveDe(nota);
            var copiado = CopiarXml(arquivoXml, chave, out var relativo);
            var itens = nota.Itens
                .Select((it, i) => new ItemSeparacao { Sequencia = i + 1, CodProduto = it.CodigoProduto, QtdeOriginal = it.Quantidade })
                .ToList();
            var agora = _clock.UtcNow;
            var registro = new NotaSeparacao
            {
                Chave = chave,
                Numero = nota.Numero,
                Serie = nota.Serie,
                CnpjDestinatario = ParserI51.Digitos(nota.DestinatarioCnpj),
                CnpjProprietario = ParserI51.Digitos(nota.EmitenteCnpj),
                Tipo = string.IsNullOrWhiteSpace(tipo) ? "PUXADA" : tipo,
                NumeroTransporte = string.IsNullOrWhiteSpace(nota.NumeroTransporte) ? transporte ?? string.Empty : nota.NumeroTransporte!,
                Onda = onda ?? string.Empty,
                IdentificadorPacote = identificadorPacote ?? string.Empty,
                CaminhoXml = relativo,
                XmlCopiado = copiado,
                Situacao = SituacaoNota.AguardandoSeparacao,
                RegistradaEm = agora,
                AtualizadoEm = agora,
                Itens = itens,
            };

            if (_repositorio.Registrar(registro))
            {
                _logger.LogInformation(
                    "Nota {Numero} registrada para acompanhar a separação (tipo={Tipo}, onda={Onda}).", nota.Numero, registro.Tipo, registro.Onda);
                // O documento pode ter chegado antes do registro (órfão): confronta agora.
                _casador.ReprocessarOrfaos();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Falha ao registrar a nota {Numero} no acompanhamento da separação.", nota.Numero);
        }
    }

    private bool CopiarXml(string origem, string chave, out string relativo)
    {
        relativo = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(origem) || !File.Exists(origem))
            {
                return false;
            }

            var nome = CaminhosI51.NomeSeguro(chave) + ".xml";
            Directory.CreateDirectory(_caminhos.Xml);
            var destino = Path.Combine(_caminhos.Xml, nome);
            var tmp = destino + ".tmp";
            File.Copy(origem, tmp, overwrite: true);
            File.Move(tmp, destino, overwrite: true);
            relativo = Path.Combine("xml", nome);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _logger.LogError(ex, "Falha ao copiar o XML da nota para o acompanhamento; seguirá com o fallback em Processados.");
            return false;
        }
    }
}
