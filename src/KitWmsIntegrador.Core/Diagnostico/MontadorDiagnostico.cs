using KitWmsIntegrador.Core.Relatorio;

namespace KitWmsIntegrador.Core.Diagnostico;

/// <summary>
/// Monta o <see cref="ModeloDiagnostico"/> a partir de <see cref="EntradaDiagnostico"/>. Puro (sem I/O). Todo texto
/// livre passa de novo pelo <see cref="SanitizadorDiagnostico"/> antes de ir ao modelo (regra D).
/// </summary>
public static class MontadorDiagnostico
{
    private const int MaxMensagem = 200;
    private const string SemMensagem = "(sem mensagem)";
    private const string SemBackend = "(sem identificação)";

    /// <summary>Monta o modelo das 6 seções.</summary>
    public static ModeloDiagnostico Montar(EntradaDiagnostico e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var linhas = Math.Clamp(e.MaxLinhasPorSecao, 10, 1000);
        var grupos = Math.Clamp(e.MaxGruposMensagem, 1, 100);
        var eventos = e.Eventos.OrderBy(x => x.QuandoLocal).ToList();

        return new ModeloDiagnostico(
            e.GeradoEm, e.Inicio, e.Fim,
            Login(eventos, linhas),
            new SecaoPacotes(e.Resumo, eventos.Where(x => x.Tipo == "arquivos-ignorados").Sum(x => x.Quantidade ?? 0), "não coletado"),
            Cadastros(eventos, grupos),
            Separacao(eventos, e, linhas),
            Devolucao(e, linhas),
            Saude(eventos, e.Saude));
    }

    private static string? Limpo(string? texto) => SanitizadorDiagnostico.Texto(texto, MaxMensagem);

    private static SecaoLogin Login(List<EventoDiagnostico> eventos, int maxLinhas)
    {
        var tokens = eventos.Where(x => x.Tipo == "gettoken").ToList();
        var falhas = tokens.Where(x => x.Ok != true).ToList();

        var porClasse = falhas
            .GroupBy(ClasseFalha)
            .Select(g => new ContagemRotulo(g.Key, g.Count()))
            .OrderByDescending(c => c.Quantidade).ThenBy(c => c.Rotulo, StringComparer.Ordinal)
            .ToList();

        var porBackend = tokens
            .GroupBy(x => Limpo(x.Backend) ?? SemBackend)
            .Select(g =>
            {
                var tentativas = g.Count();
                var f = g.Count(x => x.Ok != true);
                return new LinhaBackend(g.Key, tentativas, f, tentativas == 0 ? 0d : f * 100d / tentativas);
            })
            .OrderByDescending(b => b.Falhas).ThenByDescending(b => b.Tentativas).ThenBy(b => b.Backend, StringComparer.Ordinal)
            .Take(maxLinhas)
            .ToList();

        var quedas = new List<JanelaQueda>();
        var aberta = false;
        DateTimeOffset? inicio = null;
        foreach (var ev in eventos.Where(x => x.Tipo is "auth-queda" or "auth-recuperada"))
        {
            if (ev.Tipo == "auth-queda")
            {
                if (!aberta)
                {
                    aberta = true;
                    inicio = ev.Subtipo == "inicio" ? ev.QuandoLocal : null;
                }
            }
            else if (aberta)
            {
                quedas.Add(Fechada(inicio, ev.QuandoLocal));
                aberta = false;
                inicio = null;
            }
            else
            {
                quedas.Add(Fechada(null, ev.QuandoLocal));
            }
        }

        if (aberta)
        {
            quedas.Add(new JanelaQueda(inicio, null, null, "em andamento"));
        }

        var interrompidos = eventos.Where(x => x.Tipo == "auth-pacote-interrompido").ToList();
        return new SecaoLogin(
            tokens.Count, tokens.Count - falhas.Count, falhas.Count, porClasse, porBackend, quedas.Take(maxLinhas).ToList(),
            eventos.Count(x => x.Tipo == "auth-queda"), interrompidos.Count, interrompidos.Sum(x => x.Quantidade ?? 0));
    }

    private static JanelaQueda Fechada(DateTimeOffset? inicio, DateTimeOffset fim) =>
        inicio is { } i
            ? new JanelaQueda(i, fim, fim - i, "recuperada")
            : new JanelaQueda(null, fim, null, "início antes da janela");

    private static string ClasseFalha(EventoDiagnostico x)
    {
        if (string.Equals(x.Mensagem, "timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "timeout";
        }

        return x.Status switch
        {
            401 => "401 não autorizado",
            403 => "403 proibido",
            >= 500 => "5xx erro do servidor",
            _ => "rede/outros",
        };
    }

    private static SecaoCadastros Cadastros(List<EventoDiagnostico> eventos, int maxGrupos)
    {
        var lista = new List<InterfaceCadastro>();
        foreach (var iface in new[] { "I10", "I30" })
        {
            var gatilhos = eventos.Where(x => x.Tipo == "cadastro-gatilho" && x.Subtipo == iface).ToList();
            var chamadas = eventos.Where(x => x.Tipo == "cadastro-chamada" && x.Subtipo == iface).ToList();
            var jaExistia = chamadas.Count(x => x.Ok == true && x.Mensagem == "ja-existe");
            var sucessos = chamadas.Count(x => x.Ok == true) - jaExistia;
            var erros = chamadas.Where(x => x.Ok != true).ToList();
            lista.Add(new InterfaceCadastro(
                iface,
                gatilhos.Count,
                gatilhos.Select(x => x.Nota).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Count(),
                Agrupar(gatilhos, maxGrupos),
                chamadas.Count, sucessos, jaExistia, erros.Count, Agrupar(erros, maxGrupos)));
        }

        return new SecaoCadastros(lista);
    }

    private static IReadOnlyList<ContagemRotulo> Agrupar(IEnumerable<EventoDiagnostico> eventos, int maxGrupos)
    {
        var grupos = eventos
            .GroupBy(x => Limpo(x.Mensagem) ?? SemMensagem)
            .Select(g => new ContagemRotulo(g.Key, g.Count()))
            .OrderByDescending(c => c.Quantidade).ThenBy(c => c.Rotulo, StringComparer.Ordinal)
            .ToList();
        if (grupos.Count <= maxGrupos)
        {
            return grupos;
        }

        var topo = grupos.Take(maxGrupos).ToList();
        topo.Add(new ContagemRotulo("outros", grupos.Skip(maxGrupos).Sum(c => c.Quantidade)));
        return topo;
    }

    private static SecaoSeparacao Separacao(List<EventoDiagnostico> eventos, EntradaDiagnostico e, int maxLinhas)
    {
        var consultas = eventos.Where(x => x.Tipo == "i51-consulta").ToList();
        var vazias = consultas.Count(x => x.Ok == true && x.Mensagem == "vazio");
        var okComDados = consultas.Count(x => x.Ok == true) - vazias;
        var falhas = consultas
            .Where(x => x.Ok != true)
            .GroupBy(x => x.Status is { } s ? "HTTP " + s : Limpo(x.Mensagem) ?? "falha")
            .Select(g => new ContagemRotulo(g.Key, g.Count()))
            .OrderByDescending(c => c.Quantidade).ThenBy(c => c.Rotulo, StringComparer.Ordinal)
            .ToList();

        var ciclos = eventos.Where(x => x.Tipo == "i51-ciclo").ToList();
        var desfechos = eventos.Where(x => x.Tipo == "i60-desfecho")
            .GroupBy(x => x.Subtipo ?? "outro")
            .Select(g => new ContagemRotulo(g.Key, g.Count()))
            .OrderByDescending(c => c.Quantidade).ThenBy(c => c.Rotulo, StringComparer.Ordinal)
            .ToList();
        var alertas = eventos.Where(x => x.Tipo == "i60-alerta")
            .GroupBy(x => x.Subtipo ?? "outro")
            .Select(g => new ContagemRotulo(g.Key, g.Sum(x => x.Quantidade ?? 0)))
            .OrderByDescending(c => c.Quantidade).ThenBy(c => c.Rotulo, StringComparer.Ordinal)
            .ToList();

        var m = e.ModeloI60;
        var naoAssociadas = SecaoI60<LinhaNaoAssociada>.Vazia;
        if (m is not null)
        {
            var todas = m.Problemas.Linhas.Select(n => Linha(n, n.Motivo))
                .Concat(m.ListaAtrasadas.Linhas.Select(n => Linha(n, string.IsNullOrWhiteSpace(n.Motivo) ? "atrasada" : n.Motivo)))
                .Take(maxLinhas)
                .ToList();
            naoAssociadas = new SecaoI60<LinhaNaoAssociada>(todas, m.Problemas.Total + m.ListaAtrasadas.Total);
        }

        return new SecaoSeparacao(
            m is not null,
            consultas.Count, okComDados, vazias, falhas,
            ciclos.Sum(x => x.Quantidade ?? 0), ciclos.Sum(x => x.Quantidade2 ?? 0),
            desfechos,
            m?.Ajustes.Total ?? 0, m?.Aguardando ?? 0, m?.Atrasadas ?? 0,
            alertas, naoAssociadas);
    }

    private static LinhaNaoAssociada Linha(LinhaNotaI60 n, string motivo) =>
        new(Limpo(n.Numero) ?? string.Empty, Limpo(n.Serie) ?? string.Empty, Limpo(n.Transporte) ?? string.Empty,
            Limpo(n.Onda) ?? string.Empty, Limpo(motivo) ?? string.Empty, Limpo(n.Mensagem) ?? string.Empty);

    private static SecaoDevolucao Devolucao(EntradaDiagnostico e, int maxLinhas)
    {
        var usuarios = e.Devolucao
            .Select(u => new LinhaUsuarioDevolucao(Limpo(u.Usuario) ?? "(não informado)", u.Total, u.Sucessos, u.Falhas))
            .OrderByDescending(u => u.Total).ThenBy(u => u.Usuario, StringComparer.Ordinal)
            .ToList();
        return new SecaoDevolucao(
            e.DevolucaoHabilitada, usuarios.Sum(u => u.Total), usuarios.Sum(u => u.Sucessos), usuarios.Sum(u => u.Falhas),
            usuarios.Take(maxLinhas).ToList());
    }

    private static SecaoSaude Saude(List<EventoDiagnostico> eventos, DadosSaude s)
    {
        var ultima = eventos.LastOrDefault(x => x.Tipo == "limpeza");
        var limpeza = ultima is null
            ? null
            : new LimpezaDiagnostico(ultima.QuandoLocal, ultima.Quantidade ?? 0, ultima.Quantidade2 ?? 0, ultima.Ok == true);
        return new SecaoSaude(
            Limpo(s.Versao) ?? "desconhecida", s.UltimoInicio, s.Reinicios,
            s.EspacoLivreEntrada, s.EspacoLivreAplicacao, s.TamanhoProcessados, s.TamanhoErro, s.TamanhoEstado,
            limpeza, "não coletado");
    }
}
