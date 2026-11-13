using System.Globalization;
using KitWmsIntegrador.Core.Configuracao;
using KitWmsIntegrador.Core.Separacao;

namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>Entradas do relatório diário (tudo já lido; o montador é puro).</summary>
public sealed record EntradaRelatorioI60(
    IReadOnlyList<NotaSeparacao> Notas, SaudeConsultaI51 Saude, int BrutosFalhos, int OrfaosNaJanela,
    DateTimeOffset Agora, DateTimeOffset JanelaInicio, bool DevolucaoHabilitada);

/// <summary>Monta o <see cref="ModeloRelatorioI60"/> a partir do estado das notas. Sem I/O.</summary>
public static class MontadorRelatorioI60
{
    /// <summary>Monta o modelo.</summary>
    public static ModeloRelatorioI60 Montar(EntradaRelatorioI60 e, OpcoesRelatorioI60 opcoes, OpcoesI60 i60)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(opcoes);
        ArgumentNullException.ThrowIfNull(i60);
        var teto = Math.Max(10, opcoes.MaxLinhasPorSecao);
        var notas = e.Notas;

        bool NaJanela(DateTimeOffset? t) => t is { } x && x >= e.JanelaInicio && x <= e.Agora;
        static bool EhImp(NotaSeparacao n) =>
            string.Equals(n.OperacaoI60, "IMP", StringComparison.OrdinalIgnoreCase) || n.Motivo == MotivoI60.DocCargaEncerrado;
        static bool Problema(NotaSeparacao n) =>
            n.Situacao == SituacaoNota.I60Enviada
            && (EhImp(n) || n.Motivo is not (MotivoI60.Nenhum or MotivoI60.PendenteNaInterface or MotivoI60.PendenteComRessalva));

        var aguardando = notas.Where(n => n.Situacao == SituacaoNota.AguardandoSeparacao).ToList();
        var atrasadas = notas.Where(n => n.Situacao == SituacaoNota.Atrasada).ToList();
        var separadasPend = notas.Where(n => n.Situacao == SituacaoNota.Separada).ToList();
        var separadasJanela = notas.Where(n => NaJanela(n.SeparadaEm)).ToList();
        var associadas = notas.Where(n => n.Situacao == SituacaoNota.Associada && NaJanela(n.FinalizadaEm ?? n.I60EnviadaEm)).ToList();
        var rejeitadas = notas.Where(n => n.Situacao == SituacaoNota.Rejeitada).ToList();
        var imp = notas.Where(n => n.Situacao == SituacaoNota.I60Enviada && EhImp(n) && NaJanela(n.FinalizadaEm ?? n.I60EnviadaEm)).ToList();
        var confirmacao = notas.Where(n => n.Situacao == SituacaoNota.I60Enviada && !Problema(n)).ToList();
        var naoReconhecidas = notas.Where(n => n.Situacao == SituacaoNota.Separada && n.TentativasOutro >= 1).ToList();
        var impAtencao = notas.Count(n => n.Situacao == SituacaoNota.I60Enviada && EhImp(n));
        var problemas = rejeitadas
            .Concat(notas.Where(Problema))
            .Concat(naoReconhecidas)
            .OrderBy(n => n.RegistradaEm).ToList();
        var candidatas = rejeitadas.Concat(notas.Where(Problema)).OrderBy(n => n.RegistradaEm).ToList();

        var ajustes = separadasJanela
            .SelectMany(n => n.Itens.Where(i => i.Diferenca != 0m || i.SemRetorno)
                .Select(i => new LinhaAjusteI60(n.Numero, i.Sequencia, i.CodProduto, i.QtdeOriginal, i.QtdeSeparada ?? 0m, i.QtdeSeparada.GetValueOrDefault() - i.QtdeOriginal)))
            .ToList();

        var pacotes = notas
            .Where(n => !string.IsNullOrEmpty(n.IdentificadorPacote)
                        && (n.Situacao is SituacaoNota.AguardandoSeparacao or SituacaoNota.Atrasada or SituacaoNota.Separada
                            || NaJanela(n.SeparadaEm) || NaJanela(n.FinalizadaEm)))
            .GroupBy(n => n.IdentificadorPacote)
            .OrderBy(g => g.Min(n => n.RegistradaEm))
            .Select(g => new LinhaPacoteI60(
                g.Key,
                string.Join(" + ", g.Select(n => n.Tipo).Distinct().OrderBy(x => x, StringComparer.Ordinal)),
                string.Join(", ", g.Select(n => n.Onda).Where(o => o.Length > 0).Distinct().OrderBy(x => x, StringComparer.Ordinal)),
                g.Count(),
                g.Count(n => n.Situacao == SituacaoNota.AguardandoSeparacao),
                g.Count(n => n.Situacao == SituacaoNota.Atrasada),
                g.Count(n => n.Situacao == SituacaoNota.Separada),
                g.Count(n => n.Situacao == SituacaoNota.I60Enviada && !EhImp(n)),
                g.Count(n => n.Situacao == SituacaoNota.Associada),
                g.Count(n => n.Situacao == SituacaoNota.I60Enviada && EhImp(n)),
                g.Count(n => n.Situacao == SituacaoNota.Rejeitada)))
            .ToList();

        var alerta = notas.Count(n => n.Situacao == SituacaoNota.Separada && n.FalhasComunicacao >= i60.MaxFalhasComunicacaoAlerta);
        var semAtividade = aguardando.Count + atrasadas.Count + separadasPend.Count == 0
                           && separadasJanela.Count + associadas.Count + imp.Count + confirmacao.Count + rejeitadas.Count == 0;

        return new ModeloRelatorioI60
        {
            GeradoEm = e.Agora,
            JanelaInicio = e.JanelaInicio,
            JanelaFim = e.Agora,
            SemAtividade = semAtividade,
            UltimaConsultaOkEm = e.Saude.UltimaConsultaOkEm,
            FalhasConsulta = e.Saude.FalhasConsecutivas,
            BrutosFalhos = e.BrutosFalhos,
            OrfaosNaJanela = e.OrfaosNaJanela,
            NotasComAlertaComunicacao = alerta,
            AtencaoImp = impAtencao,
            AtencaoRejeitadas = rejeitadas.Count,
            AtencaoNaoReconhecidas = naoReconhecidas.Count,
            AtencaoAtrasadas = atrasadas.Count,
            Aguardando = aguardando.Count,
            Atrasadas = atrasadas.Count,
            SeparadasPendentesI60 = separadasPend.Count,
            AssociadasNaJanela = associadas.Count,
            ImpNaJanela = imp.Count,
            EnviadasAguardandoConfirmacao = confirmacao.Count,
            Rejeitadas = rejeitadas.Count,
            ListaAguardando = Secao(aguardando, teto, e.Agora, n => n.RegistradaEm),
            SeparadasNaJanela = Secao(separadasJanela, teto, e.Agora, n => n.SeparadaEm),
            PendentesI60 = Secao(separadasPend, teto, e.Agora, n => n.SeparadaEm),
            Associadas = Secao(associadas, teto, e.Agora, n => n.FinalizadaEm),
            Problemas = Secao(problemas, teto, e.Agora, n => n.FinalizadaEm),
            AguardandoConfirmacao = Secao(confirmacao, teto, e.Agora, n => n.I60EnviadaEm),
            Ajustes = new SecaoI60<LinhaAjusteI60>(ajustes.Take(teto).ToList(), ajustes.Count),
            ListaAtrasadas = Secao(atrasadas, teto, e.Agora, n => n.RegistradaEm),
            PorPacote = new SecaoI60<LinhaPacoteI60>(pacotes.Take(teto).ToList(), pacotes.Count),
            CandidatasI40 = opcoes.ListarCandidatasI40 ? Secao(candidatas, teto, e.Agora, n => n.FinalizadaEm) : SecaoI60<LinhaNotaI60>.Vazia,
            MostrarCandidatasI40 = opcoes.ListarCandidatasI40,
            DevolucaoDesligada = !e.DevolucaoHabilitada,
        };
    }

    /// <summary>Formata uma idade: "1 d 3 h", "3 h 20 min" ou "12 min".</summary>
    public static string Idade(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
        {
            t = TimeSpan.Zero;
        }

        if (t.TotalDays >= 1)
        {
            return $"{(int)t.TotalDays} d {t.Hours} h";
        }

        return t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : $"{(int)t.TotalMinutes} min";
    }

    private static SecaoI60<LinhaNotaI60> Secao(
        List<NotaSeparacao> notas, int teto, DateTimeOffset agora, Func<NotaSeparacao, DateTimeOffset?> quando)
    {
        var linhas = notas.Take(teto).Select(n => Linha(n, agora, quando(n))).ToList();
        return new SecaoI60<LinhaNotaI60>(linhas, notas.Count);
    }

    private static LinhaNotaI60 Linha(NotaSeparacao n, DateTimeOffset agora, DateTimeOffset? quando) => new(
        n.Numero, n.Serie, n.Tipo, n.NumeroTransporte, n.Onda, n.IdentificadorPacote,
        n.Situacao.ToString(),
        Idade(agora - (n.I60EnviadaEm is not null && n.Situacao == SituacaoNota.I60Enviada ? n.I60EnviadaEm.Value : n.RegistradaEm)),
        quando is { } q ? IdentidadeVisual.Horario(q) : string.Empty,
        n.OperacaoI60,
        n.Motivo != MotivoI60.Nenhum ? n.Motivo.ToString()
        : n.Situacao == SituacaoNota.Separada && n.TentativasOutro >= 1 ? "RespostaNaoReconhecida"
        : string.Empty,
        n.Mensagem,
        string.Join(" | ", n.Ressalvas),
        n.TentativasUnico);

    /// <summary>Texto "dd/MM/yyyy HH:mm" de uma data (uso nos renderizadores).</summary>
    internal static string Data(DateTimeOffset d) => d.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
}
