using KitWmsIntegrador.Core.Orquestrador;

namespace KitWmsIntegrador.Core.Pacote;

/// <summary>
/// Decide a categoria de erro e o destino (Processados/Erro) de uma nota, reaproveitando o
/// <see cref="ClassificadorRetorno"/>. Não altera nenhuma decisão do Orquestrador.
/// </summary>
public sealed class ClassificadorCategoriaErro
{
    private const string TextoSemTransporte = "sem número de transporte";

    private readonly ClassificadorRetorno _classificador;

    /// <summary>Cria o classificador com o <see cref="ClassificadorRetorno"/> informado.</summary>
    public ClassificadorCategoriaErro(ClassificadorRetorno classificador)
    {
        _classificador = classificador ?? throw new ArgumentNullException(nameof(classificador));
    }

    /// <summary>Categoria para um XML que não pôde ser lido.</summary>
    public CategoriaErro ClassificarXmlInvalido() => CategoriaErro.XmlInvalido;

    /// <summary>
    /// Prioridade: sem transporte → produto → empresa → tentativas esgotadas → outro
    /// (a mensagem vem antes das tentativas por ser mais acionável).
    /// </summary>
    public CategoriaErro Classificar(ResultadoIntegracaoNota resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);

        var mensagem = resultado.UltimaMensagem;

        if (!string.IsNullOrEmpty(mensagem)
            && mensagem.Contains(TextoSemTransporte, StringComparison.OrdinalIgnoreCase))
        {
            return CategoriaErro.SemTransporte;
        }

        if (_classificador.EhProdutoNaoCadastrado(mensagem))
        {
            return CategoriaErro.ProdutoNaoCadastrado;
        }

        if (_classificador.EhEmpresaNaoLocalizada(mensagem))
        {
            return CategoriaErro.EmpresaNaoLocalizada;
        }

        if (resultado.Tentativas >= Orquestrador.Orquestrador.MaxTentativas)
        {
            return CategoriaErro.TentativasEsgotadas;
        }

        return CategoriaErro.Outro;
    }

    /// <summary>
    /// Integrada (inclusive híbrida) ou Pendente → Processados; NaoIntegrada com "já existe"
    /// (reprocesso idempotente) → Processados; demais → Erro.
    /// </summary>
    public DestinoArquivo DestinoDe(ResultadoIntegracaoNota resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);

        if (resultado.Integrada || resultado.Estado == EstadoIntegracao.Pendente)
        {
            return DestinoArquivo.Processados;
        }

        return _classificador.EhJaExiste(resultado.UltimaMensagem)
            ? DestinoArquivo.Processados
            : DestinoArquivo.Erro;
    }
}
