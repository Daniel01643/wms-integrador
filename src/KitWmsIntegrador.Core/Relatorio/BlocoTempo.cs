namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>Par rótulo/valor já formatado.</summary>
internal sealed record LinhaTempo(string Rotulo, string Valor);

/// <summary>Etapa medida: duração e, quando se aplica, o tempo por nota.</summary>
internal sealed record EtapaTempo(string Etapa, string Duracao, string PorNota);

/// <summary>Linha da tabela por onda (somente pacotes com TSP).</summary>
internal sealed record OndaTempo(string Onda, string Notas, string Transportes, string Tempo);

/// <summary>
/// Bloco "Tempo de integração" já formatado em texto pt-BR. É montado UMA vez e HTML, PDF e
/// e-mail só o diagramam, o que garante os mesmos números nos três.
/// </summary>
internal sealed record BlocoTempo(
    IReadOnlyList<LinhaTempo> Resumo,
    IReadOnlyList<EtapaTempo> Etapas,
    IReadOnlyList<OndaTempo> Ondas)
{
    /// <summary>Rótulo da coluna "por nota" da integração no WMS (número comparável ao processo manual).</summary>
    public const string IntegracaoWms = "Integração no WMS";

    /// <summary>Monta o bloco; <c>null</c> quando não há medição.</summary>
    public static BlocoTempo? Montar(ResumoRelatorio resumo, MedicaoTempo? m)
    {
        ArgumentNullException.ThrowIfNull(resumo);
        if (m is null)
        {
            return null;
        }

        var notas = resumo.Total;
        var linhas = new List<LinhaTempo>
        {
            new("Início do pacote", IdentidadeVisual.Horario(m.Inicio)),
            new("Fim do pacote", IdentidadeVisual.Horario(m.Fim)),
            new("Duração total", FormatadorDuracao.Formatar(m.DuracaoTotalMs)),
            new("Notas", FormatadorDuracao.Inteiro(notas)),
        };
        if (notas > 0 && m.DuracaoTotalMs > 0)
        {
            linhas.Add(new("Tempo médio por nota", FormatadorDuracao.Formatar(m.DuracaoTotalMs / notas)));
            var porSegundo = notas / (m.DuracaoTotalMs / 1000.0);
            linhas.Add(new("Notas por segundo", FormatadorDuracao.Numero(porSegundo, 2)));
        }

        var etapas = new List<EtapaTempo>();
        if (m.EsperaJanelaMs is { } espera)
        {
            etapas.Add(new("Espera da janela de coleta", FormatadorDuracao.Formatar(espera), "-"));
        }

        if (m.LeituraMs is { } leitura)
        {
            etapas.Add(new("Leitura do ZIP/XML", FormatadorDuracao.Formatar(leitura), "-"));
        }

        if (m.IntegracaoFaseMs is { } fase)
        {
            var porNota = m.ChamadasWmsMs is { } soma && notas > 0
                ? FormatadorDuracao.Formatar(soma / notas)
                : "-";
            etapas.Add(new(IntegracaoWms, FormatadorDuracao.Formatar(fase), porNota));
        }

        var ondas = new List<OndaTempo>();
        if (resumo.NotasPorOnda is { Count: > 0 })
        {
            foreach (var (onda, n, transportes) in resumo.NotasPorOnda)
            {
                var medida = m.Ondas.FirstOrDefault(o => string.Equals(o.Onda, onda, StringComparison.Ordinal));
                ondas.Add(new OndaTempo(
                    onda,
                    FormatadorDuracao.Inteiro(n),
                    FormatadorDuracao.Inteiro(transportes),
                    medida?.DuracaoMs is { } d ? FormatadorDuracao.Formatar(d) : "-"));
            }
        }

        return new BlocoTempo(linhas, etapas, ondas);
    }
}
