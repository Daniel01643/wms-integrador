namespace KitWmsIntegrador.Core.Monitoramento;

/// <summary>
/// Agrega <see cref="RegistroTelemetria"/> (pacotes) e <see cref="EventoServico"/> (saúde) de
/// uma janela em um <see cref="ResumoPeriodo"/>. Puro, sem I/O: filtra os pacotes por
/// <see cref="RegistroTelemetria.FechamentoLocal"/> dentro de <c>[inicio, fim)</c> e consolida
/// contagens, percentuais, frequências e performance.
/// </summary>
public static class AgregadorTelemetria
{
    /// <summary>
    /// Produz o resumo do período <c>[inicioPeriodo, fimPeriodo)</c> (bordas em horário local).
    /// Médias e percentuais têm guarda de divisão por zero; sem pacotes, devolve um resumo com
    /// <see cref="ResumoPeriodo.SemAtividade"/> = <c>true</c> e zeros coerentes.
    /// </summary>
    public static ResumoPeriodo Agregar(
        IEnumerable<RegistroTelemetria> pacotes,
        IEnumerable<EventoServico> eventos,
        DateTimeOffset inicioPeriodo,
        DateTimeOffset fimPeriodo)
    {
        ArgumentNullException.ThrowIfNull(pacotes);
        ArgumentNullException.ThrowIfNull(eventos);

        var noPeriodo = pacotes
            .Where(p => p.FechamentoLocal >= inicioPeriodo && p.FechamentoLocal < fimPeriodo)
            .ToList();

        var totalPacotes = noPeriodo.Count;
        var totalNotas = noPeriodo.Sum(p => p.TotalNotas);
        var integradas = noPeriodo.Sum(p => p.Integradas);
        var integradasRevisar = noPeriodo.Sum(p => p.IntegradasRevisarProduto);
        var pendentes = noPeriodo.Sum(p => p.Pendentes);
        var naoIntegradas = noPeriodo.Sum(p => p.NaoIntegradas);
        var xmlInvalido = noPeriodo.Sum(p => p.XmlInvalido);

        var mediaNotasPorPacote = totalPacotes == 0 ? 0d : (double)totalNotas / totalPacotes;

        double Pct(int parte) => totalNotas == 0 ? 0d : (double)parte * 100d / totalNotas;

        var diasComAtividade = noPeriodo
            .Select(p => p.FechamentoLocal.LocalDateTime.Date)
            .Distinct()
            .Count();

        var pacotesPorDia = new Dictionary<DayOfWeek, int>();
        var notasPorDia = new Dictionary<DayOfWeek, int>();
        var pacotesPorFaixa = new Dictionary<FaixaHorario, int>();
        var notasPorFaixa = new Dictionary<FaixaHorario, int>();

        foreach (var p in noPeriodo)
        {
            var dia = p.FechamentoLocal.LocalDateTime.DayOfWeek;
            pacotesPorDia[dia] = pacotesPorDia.GetValueOrDefault(dia) + 1;
            notasPorDia[dia] = notasPorDia.GetValueOrDefault(dia) + p.TotalNotas;

            var faixa = FaixaHorarioExtensoes.De(p.FechamentoLocal);
            pacotesPorFaixa[faixa] = pacotesPorFaixa.GetValueOrDefault(faixa) + 1;
            notasPorFaixa[faixa] = notasPorFaixa.GetValueOrDefault(faixa) + p.TotalNotas;
        }

        // Tempo médio por nota ponderado: soma de (médiaPorNota * notas) / total de notas.
        var tempoMedioPorNota = totalNotas == 0
            ? 0L
            : (long)Math.Round(noPeriodo.Sum(p => (double)p.TempoMedioPorNotaMs * p.TotalNotas) / totalNotas);
        var tempoMedioPorPacote = totalPacotes == 0
            ? 0L
            : (long)Math.Round(noPeriodo.Sum(p => (double)p.TempoTotalMs) / totalPacotes);

        var chamadasPorInterface = new Dictionary<string, int>();
        var chamadasAproximado = false;
        foreach (var p in noPeriodo)
        {
            if (p.ChamadasWmsAproximado)
            {
                chamadasAproximado = true;
            }

            foreach (var (iface, qtd) in p.ChamadasWms)
            {
                chamadasPorInterface[iface] = chamadasPorInterface.GetValueOrDefault(iface) + qtd;
            }
        }

        var totalChamadasWms = chamadasPorInterface.Values.Sum();

        var errosPorCategoria = new Dictionary<string, int>();
        foreach (var p in noPeriodo)
        {
            foreach (var (categoria, qtd) in p.ErrosPorCategoria)
            {
                errosPorCategoria[categoria] = errosPorCategoria.GetValueOrDefault(categoria) + qtd;
            }
        }

        var eventosLista = eventos.ToList();
        DateTimeOffset? servicoDesde = eventosLista.Count == 0
            ? null
            : eventosLista.Min(e => e.QuandoLocal);
        var inicializacoes = eventosLista.Count(e =>
            string.Equals(e.Tipo, "servico-iniciado", StringComparison.OrdinalIgnoreCase)
            && e.QuandoLocal >= inicioPeriodo && e.QuandoLocal < fimPeriodo);

        return new ResumoPeriodo(
            InicioPeriodo: inicioPeriodo,
            FimPeriodo: fimPeriodo,
            TotalPacotes: totalPacotes,
            TotalNotas: totalNotas,
            MediaNotasPorPacote: mediaNotasPorPacote,
            Integradas: integradas,
            IntegradasRevisarProduto: integradasRevisar,
            Pendentes: pendentes,
            NaoIntegradas: naoIntegradas,
            XmlInvalido: xmlInvalido,
            PercentualIntegradas: Pct(integradas),
            PercentualIntegradasRevisarProduto: Pct(integradasRevisar),
            PercentualPendentes: Pct(pendentes),
            PercentualNaoIntegradas: Pct(naoIntegradas),
            PercentualXmlInvalido: Pct(xmlInvalido),
            DiasComAtividade: diasComAtividade,
            PacotesPorDiaSemana: pacotesPorDia,
            NotasPorDiaSemana: notasPorDia,
            PacotesPorFaixa: pacotesPorFaixa,
            NotasPorFaixa: notasPorFaixa,
            TempoMedioPorNotaMs: tempoMedioPorNota,
            TempoMedioPorPacoteMs: tempoMedioPorPacote,
            TotalChamadasWms: totalChamadasWms,
            ChamadasWmsPorInterface: chamadasPorInterface,
            ChamadasWmsAproximado: chamadasAproximado,
            ErrosPorCategoria: errosPorCategoria,
            ServicoDesde: servicoDesde,
            InicializacoesNoPeriodo: inicializacoes,
            SemAtividade: totalPacotes == 0,
            NotasPuxada: noPeriodo.Sum(p => p.NotasPuxada),
            NotasTsp: noPeriodo.Sum(p => p.NotasTsp),
            NotasRaiz: noPeriodo.Sum(p => p.NotasRaiz));
    }
}
