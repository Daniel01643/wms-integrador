namespace KitWmsIntegrador.Core.Configuracao;

/// <summary>
/// Parâmetros do ciclo do pacote (seção "Pacote"): intervalo de varredura, estabilização de
/// arquivos, janela de coleta, quietude para fechamento e concorrência entre transportes.
/// </summary>
public sealed class OpcoesPacote
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string Secao = "Pacote";

    /// <summary>Intervalo entre varreduras da pasta de entrada.</summary>
    public int IntervaloVarreduraSegundos { get; set; } = 5;

    /// <summary>Tempo mínimo com tamanho/data inalterados para o arquivo ser considerado completo.</summary>
    public int EstabilizacaoSegundos { get; set; } = 3;

    /// <summary>Espera após o último arquivo novo antes do primeiro lote do pacote.</summary>
    public int JanelaColetaSegundos { get; set; } = 10;

    /// <summary>Tempo sem arquivo novo/processado, com a entrada vazia, para fechar o pacote.</summary>
    public int QuietudeSegundos { get; set; } = 30;

    /// <summary>Tempo após o qual um arquivo que não fica pronto deixa de bloquear o fechamento.</summary>
    public int TimeoutArquivoSegundos { get; set; } = 300;

    /// <summary>Quantidade máxima de transportes (ondas) enviados em paralelo.</summary>
    public int MaxTransportesConcorrentes { get; set; } = 1;
}
