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

    /// <summary>Modo "Silencio" da janela de coleta (padrão): fecha após <see cref="SilencioSegundos"/> sem atividade.</summary>
    public const string ModoSilencio = "Silencio";

    /// <summary>Modo "Fixa" da janela de coleta: comportamento antigo, janela fixa de <see cref="JanelaColetaSegundos"/>.</summary>
    public const string ModoFixa = "Fixa";

    /// <summary>
    /// Modo da janela de coleta: <c>Silencio</c> (padrão; o pacote fecha quando passam
    /// <see cref="SilencioSegundos"/> sem chegar arquivo novo nem mudar um existente, com teto em
    /// <see cref="JanelaMaximaSegundos"/>) ou <c>Fixa</c> (comportamento antigo, <see cref="JanelaColetaSegundos"/>).
    /// </summary>
    public string ModoJanela { get; set; } = ModoSilencio;

    /// <summary>Modo Silencio: segundos sem nenhuma atividade (arquivo novo ou alterado) para encerrar a coleta.</summary>
    public int SilencioSegundos { get; set; } = 15;

    /// <summary>Modo Silencio: teto da coleta, contado da abertura do pacote (nunca passa disso).</summary>
    public int JanelaMaximaSegundos { get; set; } = 180;

    /// <summary>
    /// Janela de coleta FIXA, contada da primeira detecção (usada só no modo <c>Fixa</c>). Ao fim dela é tirada
    /// a "foto" da pasta: os arquivos prontos naquele instante formam o pacote; os que chegarem depois ficam para o próximo.
    /// </summary>
    public int JanelaColetaSegundos { get; set; } = 10;

    /// <summary>True quando o modo da janela é Silencio (qualquer valor diferente de Fixa; valor inválido é apontado por <see cref="Validar"/>).</summary>
    public bool UsaSilencio => !string.Equals(ModoJanela?.Trim(), ModoFixa, StringComparison.OrdinalIgnoreCase);

    /// <summary>Silêncio efetivo: nunca menor que <see cref="EstabilizacaoSegundos"/> (um arquivo só fica pronto após estabilizar).</summary>
    public int SilencioEfetivoSegundos => Math.Max(SilencioSegundos, EstabilizacaoSegundos);

    /// <summary>Teto efetivo da coleta: nunca menor que o silêncio efetivo.</summary>
    public int JanelaMaximaEfetivaSegundos => Math.Max(JanelaMaximaSegundos, SilencioEfetivoSegundos);

    /// <summary>Devolve a lista de problemas da configuração (vazia = válida).</summary>
    public IReadOnlyList<string> Validar()
    {
        var p = new List<string>();
        var modo = ModoJanela?.Trim();
        if (!string.Equals(modo, ModoSilencio, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(modo, ModoFixa, StringComparison.OrdinalIgnoreCase))
        {
            p.Add("Pacote:ModoJanela deve ser 'Silencio' ou 'Fixa'.");
        }

        if (SilencioSegundos < 1)
        {
            p.Add("Pacote:SilencioSegundos deve ser maior ou igual a 1.");
        }

        if (JanelaMaximaSegundos < SilencioSegundos)
        {
            p.Add("Pacote:JanelaMaximaSegundos deve ser maior ou igual a Pacote:SilencioSegundos.");
        }

        return p;
    }

    /// <summary>Avisos (não impedem a partida): silêncio configurado abaixo da estabilização é elevado a ela.</summary>
    public IReadOnlyList<string> Avisos()
    {
        var a = new List<string>();
        if (UsaSilencio && SilencioSegundos >= 1 && SilencioSegundos < EstabilizacaoSegundos)
        {
            a.Add($"Pacote:SilencioSegundos ({SilencioSegundos}) é menor que Pacote:EstabilizacaoSegundos ({EstabilizacaoSegundos}); será usado {EstabilizacaoSegundos}.");
        }

        return a;
    }

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
