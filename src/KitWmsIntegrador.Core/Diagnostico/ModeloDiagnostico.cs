using KitWmsIntegrador.Core.Monitoramento;
using KitWmsIntegrador.Core.Relatorio;

namespace KitWmsIntegrador.Core.Diagnostico;

/// <summary>Par rótulo/quantidade (classe de falha, mensagem agrupada, desfecho etc.).</summary>
public sealed record ContagemRotulo(string Rotulo, int Quantidade);

/// <summary>Tentativas de GetToken de um servidor do balanceador (<c>BackendWms.Chave</c>).</summary>
public sealed record LinhaBackend(string Backend, int Tentativas, int Falhas, double PercentualFalha);

/// <summary>Janela de indisponibilidade do login. <see cref="Situacao"/>: "recuperada", "em andamento" ou "início antes da janela".</summary>
public sealed record JanelaQueda(DateTimeOffset? Inicio, DateTimeOffset? Fim, TimeSpan? Duracao, string Situacao);

/// <summary>Seção 1: login do WMS (GetToken).</summary>
public sealed record SecaoLogin(
    int Total, int Sucessos, int Falhas,
    IReadOnlyList<ContagemRotulo> FalhasPorClasse,
    IReadOnlyList<LinhaBackend> PorBackend,
    IReadOnlyList<JanelaQueda> Quedas,
    int AtivacoesCooldown, int PacotesInterrompidos, int NotasAguardando);

/// <summary>Seção 2: pacotes e notas (telemetria) e arquivos ignorados.</summary>
public sealed record SecaoPacotes(ResumoPeriodo Resumo, int ArquivosIgnorados, string LotesDivididos);

/// <summary>Gatilhos e chamadas de uma interface de cadastro (I10 ou I30).</summary>
public sealed record InterfaceCadastro(
    string Interface,
    int Gatilhos, int NotasDistintas, IReadOnlyList<ContagemRotulo> GatilhosPorMensagem,
    int Chamadas, int Sucessos, int JaExistia, int Erros, IReadOnlyList<ContagemRotulo> ErrosPorMensagem);

/// <summary>Seção 3: I10 e I30.</summary>
public sealed record SecaoCadastros(IReadOnlyList<InterfaceCadastro> Interfaces);

/// <summary>Nota que a I60 não conseguiu associar (problema ou atrasada).</summary>
public sealed record LinhaNaoAssociada(string Nota, string Serie, string Transporte, string Onda, string Motivo, string Mensagem);

/// <summary>Seção 4: I51 e I60.</summary>
public sealed record SecaoSeparacao(
    bool I51Habilitada,
    int Consultas, int ConsultasOk, int ConsultasVazias, IReadOnlyList<ContagemRotulo> ConsultasFalhas,
    int NovasSeparadas, int NovasAtrasadas,
    IReadOnlyList<ContagemRotulo> I60PorDesfecho,
    int AjustesQuantidade, int Aguardando, int Atrasadas,
    IReadOnlyList<ContagemRotulo> AlertasPorEvento,
    SecaoI60<LinhaNaoAssociada> NaoAssociadas);

/// <summary>Envios de devolução (I40) de um usuário declarado na tela.</summary>
public sealed record LinhaUsuarioDevolucao(string Usuario, int Total, int Sucessos, int Falhas);

/// <summary>Seção 5: devolução pela I40.</summary>
public sealed record SecaoDevolucao(
    bool Habilitada, int Total, int Sucessos, int Falhas, IReadOnlyList<LinhaUsuarioDevolucao> PorUsuario);

/// <summary>Resultado da última limpeza na janela.</summary>
public sealed record LimpezaDiagnostico(DateTimeOffset Quando, int Arquivos, int Pastas, bool SemErros);

/// <summary>Seção 6: saúde do serviço. Tamanhos e espaços em bytes; <c>null</c> = não foi possível medir.</summary>
public sealed record SecaoSaude(
    string Versao, DateTimeOffset? UltimoInicio, int Reinicios,
    long? EspacoLivreEntrada, long? EspacoLivreAplicacao,
    long? TamanhoProcessados, long? TamanhoErro, long? TamanhoEstado,
    LimpezaDiagnostico? Limpeza, string LogWarningError);

/// <summary>
/// Seção 7: memória do integrador e do servidor (amostras <c>saude-amostra</c> da janela). Valores em MB; <c>null</c> = sem
/// dado. Pico e média são do integrador; o mínimo de memória livre é do servidor. Os consumidores são os 5 processos que
/// mais usam memória no instante do pico do integrador (<see cref="ConsumidoresNoPico"/>) e no instante do mínimo de
/// memória livre (<see cref="ConsumidoresNoMinimoLivre"/>).
/// </summary>
public sealed record SecaoMemoria(
    int Amostras,
    int? PicoWsMb, DateTimeOffset? QuandoPicoWs, double? MediaWsMb,
    int? PicoPrivadoMb, double? MediaPrivadoMb,
    int? PicoHandles, int? PicoThreads,
    int? MinimoLivreMb, DateTimeOffset? QuandoMinimoLivre, int? TotalMb,
    int? PicoPaginacaoUsadaMb, int? PaginacaoTotalMb,
    IReadOnlyList<ConsumidorMemoria> ConsumidoresNoPico, IReadOnlyList<ConsumidorMemoria> ConsumidoresNoMinimoLivre,
    int AlertasMemoria, int AlertasMemoriaEnviados);

/// <summary>Modelo único do "Diagnóstico agente integrador" (HTML/PDF/e-mail).</summary>
public sealed record ModeloDiagnostico(
    DateTimeOffset GeradoEm, DateTimeOffset JanelaInicio, DateTimeOffset JanelaFim,
    SecaoLogin Login, SecaoPacotes Pacotes, SecaoCadastros Cadastros,
    SecaoSeparacao Separacao, SecaoDevolucao Devolucao, SecaoSaude Saude,
    SecaoMemoria? Memoria = null);
