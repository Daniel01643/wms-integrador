using KitWmsIntegrador.Core.Separacao;

namespace KitWmsIntegrador.Core.Relatorio;

/// <summary>Linha de nota nas tabelas do relatório diário da I51/I60.</summary>
public sealed record LinhaNotaI60(
    string Numero, string Serie, string Tipo, string Transporte, string Onda, string Pacote,
    string Situacao, string Idade, string Quando, string Operacao, string Motivo, string Mensagem,
    string Ressalvas, int Tentativas);

/// <summary>Linha de ajuste de quantidade (original × separada) item a item.</summary>
public sealed record LinhaAjusteI60(string Nota, int Item, string Produto, decimal Original, decimal Separada, decimal Diferenca);

/// <summary>Linha da visão por pacote.</summary>
public sealed record LinhaPacoteI60(
    string Pacote, string Tipo, string Ondas, int Notas, int Aguardando, int Atrasadas, int Separadas,
    int Enviadas, int Associadas, int Imp, int Rejeitadas);

/// <summary>Seção com teto de linhas: <see cref="Total"/> conta tudo, <see cref="Linhas"/> o exibido.</summary>
public sealed record SecaoI60<T>(IReadOnlyList<T> Linhas, int Total)
{
    /// <summary>Linhas não exibidas pelo teto.</summary>
    public int NaoExibidas => Math.Max(0, Total - Linhas.Count);

    /// <summary>Seção vazia.</summary>
    public static SecaoI60<T> Vazia { get; } = new(Array.Empty<T>(), 0);
}

/// <summary>Modelo único consumido pelo HTML/e-mail e pelo PDF do relatório diário da I51/I60.</summary>
public sealed class ModeloRelatorioI60
{
    /// <summary>Instante da geração.</summary>
    public DateTimeOffset GeradoEm { get; init; }

    /// <summary>Início da janela (24 h).</summary>
    public DateTimeOffset JanelaInicio { get; init; }

    /// <summary>Fim da janela.</summary>
    public DateTimeOffset JanelaFim { get; init; }

    /// <summary>Não há nota em acompanhamento nem movimento na janela.</summary>
    public bool SemAtividade { get; init; }

    // Atenção (estado atual das notas; exibido no topo)
    /// <summary>Notas ENC/IMP (doccarga encerrado reenviado como IMP, sem associação).</summary>
    public int AtencaoImp { get; init; }

    /// <summary>Notas rejeitadas.</summary>
    public int AtencaoRejeitadas { get; init; }

    /// <summary>Notas com resposta não reconhecida do WMS (ainda em tentativas).</summary>
    public int AtencaoNaoReconhecidas { get; init; }

    /// <summary>Notas atrasadas.</summary>
    public int AtencaoAtrasadas { get; init; }

    /// <summary>Total do bloco "Atenção".</summary>
    public int AtencaoTotal => AtencaoImp + AtencaoRejeitadas + AtencaoNaoReconhecidas + AtencaoAtrasadas;

    // Saúde da consulta
    /// <summary>Última consulta que respondeu sem erro.</summary>
    public DateTimeOffset? UltimaConsultaOkEm { get; init; }

    /// <summary>Falhas de consulta seguidas.</summary>
    public int FalhasConsulta { get; init; }

    /// <summary>Brutos que esgotaram as tentativas.</summary>
    public int BrutosFalhos { get; init; }

    /// <summary>Documentos sem nota correspondente recebidos na janela.</summary>
    public int OrfaosNaJanela { get; init; }

    /// <summary>Notas com falhas de comunicação acima do alerta.</summary>
    public int NotasComAlertaComunicacao { get; init; }

    // Cartões
    /// <summary>Aguardando separação (estado atual).</summary>
    public int Aguardando { get; init; }

    /// <summary>Atrasadas (estado atual).</summary>
    public int Atrasadas { get; init; }

    /// <summary>Separadas com a I60 ainda por enviar (estado atual).</summary>
    public int SeparadasPendentesI60 { get; init; }

    /// <summary>I60 associadas na janela.</summary>
    public int AssociadasNaJanela { get; init; }

    /// <summary>I60 enviadas como IMP (ENC) na janela.</summary>
    public int ImpNaJanela { get; init; }

    /// <summary>I60 enviadas aguardando confirmação (estado atual).</summary>
    public int EnviadasAguardandoConfirmacao { get; init; }

    /// <summary>Rejeitadas (estado atual).</summary>
    public int Rejeitadas { get; init; }

    // Seções
    /// <summary>Aguardando separação.</summary>
    public SecaoI60<LinhaNotaI60> ListaAguardando { get; init; } = SecaoI60<LinhaNotaI60>.Vazia;

    /// <summary>Separadas na janela.</summary>
    public SecaoI60<LinhaNotaI60> SeparadasNaJanela { get; init; } = SecaoI60<LinhaNotaI60>.Vazia;

    /// <summary>I60 pendentes de envio.</summary>
    public SecaoI60<LinhaNotaI60> PendentesI60 { get; init; } = SecaoI60<LinhaNotaI60>.Vazia;

    /// <summary>I60 associadas na janela.</summary>
    public SecaoI60<LinhaNotaI60> Associadas { get; init; } = SecaoI60<LinhaNotaI60>.Vazia;

    /// <summary>I60 com problema (IMP/ENC, rejeitadas e motivos de problema).</summary>
    public SecaoI60<LinhaNotaI60> Problemas { get; init; } = SecaoI60<LinhaNotaI60>.Vazia;

    /// <summary>I60 enviadas aguardando confirmação.</summary>
    public SecaoI60<LinhaNotaI60> AguardandoConfirmacao { get; init; } = SecaoI60<LinhaNotaI60>.Vazia;

    /// <summary>Ajustes de quantidade item a item.</summary>
    public SecaoI60<LinhaAjusteI60> Ajustes { get; init; } = SecaoI60<LinhaAjusteI60>.Vazia;

    /// <summary>Atrasadas.</summary>
    public SecaoI60<LinhaNotaI60> ListaAtrasadas { get; init; } = SecaoI60<LinhaNotaI60>.Vazia;

    /// <summary>Visão por pacote.</summary>
    public SecaoI60<LinhaPacoteI60> PorPacote { get; init; } = SecaoI60<LinhaPacoteI60>.Vazia;

    /// <summary>Candidatas à devolução pela I40 (última alternativa).</summary>
    public SecaoI60<LinhaNotaI60> CandidatasI40 { get; init; } = SecaoI60<LinhaNotaI60>.Vazia;

    /// <summary>Lista as candidatas à I40.</summary>
    public bool MostrarCandidatasI40 { get; init; }

    /// <summary>A tela de devolução (I40) está desligada.</summary>
    public bool DevolucaoDesligada { get; init; }
}
