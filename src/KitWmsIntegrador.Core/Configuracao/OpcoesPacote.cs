namespace KitWmsIntegrador.Core.Configuracao;

/// <summary>
/// Parâmetros do ciclo do pacote (seção "Pacote"): intervalo de varredura, estabilização de
/// arquivos, janela de coleta (foto da pasta), backoff e concorrência entre transportes.
/// </summary>
public sealed class OpcoesPacote
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string Secao = "Pacote";

    /// <summary>Intervalo entre varreduras da pasta de entrada.</summary>
    public int IntervaloVarreduraSegundos { get; set; } = 5;

    /// <summary>Tempo mínimo com tamanho/data inalterados para o arquivo ser considerado completo.</summary>
    public int EstabilizacaoSegundos { get; set; } = 3;

    /// <summary>
    /// Janela de coleta FIXA, contada da primeira detecção. Ao fim dela é tirada a "foto" da pasta:
    /// os arquivos prontos naquele instante formam o pacote; os que chegarem depois ficam para o próximo.
    /// </summary>
    public int JanelaColetaSegundos { get; set; } = 10;

    /// <summary>Tempo após o qual um arquivo que não fica pronto é tratado como instável (alerta em log; fica na entrada).</summary>
    public int TimeoutArquivoSegundos { get; set; } = 300;

    /// <summary>
    /// Teto do backoff exponencial entre fotos quando a foto não tem nenhum arquivo pronto
    /// (arquivos ainda em gravação). Começa em <see cref="JanelaColetaSegundos"/> e dobra a cada foto vazia.
    /// </summary>
    public int BackoffMaximoSegundos { get; set; } = 120;

    /// <summary>Quantidade máxima de transportes (ondas) enviados em paralelo.</summary>
    public int MaxTransportesConcorrentes { get; set; } = 1;
}
