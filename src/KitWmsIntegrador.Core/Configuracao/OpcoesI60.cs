namespace KitWmsIntegrador.Core.Configuracao;

/// <summary>
/// Flag de configuração da I60 (NOTA/DocCarga). Configurável via
/// <c>appsettings.json</c> (seção "I60") e variáveis de ambiente
/// (ex.: <c>I60__Habilitada</c>).
///
/// Pedido do usuário (mensagem 12): desligar a I60 por ora, pois a I50 agora
/// carrega os dados fiscais (chave, protocolo, números, valores) nos atributos
/// e observações. O código da I60 (<c>ConstrutorI60</c>, <c>PayloadI60</c> e
/// seus testes) permanece intacto e é reativável — basta ligar esta flag.
///
/// Esta flag apenas sinaliza se o envio da I60 deve ocorrer; o fluxo de envio
/// do Orquestrador (Etapa 5, ainda inexistente) deve respeitá-la.
/// </summary>
public sealed class OpcoesI60
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string Secao = "I60";

    /// <summary>
    /// Indica se o envio da I60 está habilitado. Default <c>false</c> — a I60
    /// está desligada por ora (ver observação da classe). O fluxo de envio da
    /// Etapa 5 deve consultar esta flag antes de montar/enviar a I60.
    /// </summary>
    public bool Habilitada { get; set; } = false;

    /// <summary>Valor padrão de <see cref="TipoOperacaoNota"/> (o do template atual).</summary>
    public const string TipoOperacaoNotaPadrao = "INE";

    /// <summary>
    /// Tipo de operação da nota na I60: preenche <c>tipoOperacaoNota</c> e <c>Tpsit</c> com o
    /// mesmo valor (como nos JSONs do chamado). Default <c>INE</c> (vincula sem gerar
    /// embarque). Valores citados na documentação do repo: IMB, INE, IMP. "INV" NÃO consta
    /// em nenhum documento versionado: trocar só após confirmação do fornecedor/homolog.
    /// Troca sem release: variável de ambiente <c>I60__TipoOperacaoNota</c>.
    /// </summary>
    public string TipoOperacaoNota { get; set; } = TipoOperacaoNotaPadrao;

    /// <summary>
    /// Diagnóstico de homolog: quando <c>true</c>, a I60 envia <c>qtdeVolume = 0</c> em vez da
    /// soma das caixas dos itens. Serve para testar se a conferência de volume do WMS passa
    /// com o valor que ele lê hoje (0) e expor a próxima conferência. Default <c>false</c>
    /// (envia a soma). Troca sem release: variável de ambiente <c>I60__QtdeVolumeZero</c>.
    /// </summary>
    public bool QtdeVolumeZero { get; set; } = false;

    // ---- Fluxo I51 -> I60 (somente com I51:Habilitada=true). Os padrões abaixo não alteram o
    // ---- comportamento da I60 enviada junto da I50 (I51 desligada). Listas separadas por ';'.

    /// <summary><c>tipoOperacaoNota</c> e <c>Tpsit</c> do reenvio quando o doccarga está ENC.</summary>
    public string TipoOperacaoEncerrado { get; set; } = "IMP";

    /// <summary>Liga a regra ENC → reenvio como IMP (com registro do erro).</summary>
    public bool ReenviarEncerradoComoImp { get; set; } = true;

    /// <summary>Textos (normalizados) que indicam doccarga encerrado, em mensagem ou ressalva.</summary>
    public string PadroesDocCargaEncerrado { get; set; } = "encerrad;status ENC";

    /// <summary>Textos que indicam quantidade diferente.</summary>
    public string PadroesQuantidadeDiferente { get; set; } =
        "não confere com a quantidade;quantidade diferente;quantidade caixa";

    /// <summary>Textos que indicam volume diferente.</summary>
    public string PadroesVolumeDiferente { get; set; } = "não confere com a qtdvolume;Total de volume";

    /// <summary>Textos "Nota [...] já existe, TpAcao precisa ser UPD!".</summary>
    public string PadroesExigeUpd { get; set; } = "TpAcao precisa ser UPD";

    /// <summary>Ressalvas que tornam a I60 recusada.</summary>
    public string PadroesRessalvaBloqueante { get; set; } =
        "encerrad;status ENC;não confere;quantidade;Total de volume";

    /// <summary>Textos extras que, além do sucesso padrão, indicam I60 associada.</summary>
    public string PadroesAssociadaSucesso { get; set; } = string.Empty;

    /// <summary><c>I</c> ou <c>UPD</c>: <c>tpacao</c> do reenvio IMP.</summary>
    public string TpAcaoReenvioImp { get; set; } = "UPD";

    /// <summary>Se a resposta exigir UPD e o tpacao usado não era UPD, reenvia uma vez com UPD.</summary>
    public bool ReenviarComUpdSeJaExiste { get; set; } = true;

    /// <summary><c>SomaCaixas</c> (padrão) ou <c>VolumesI51</c> (contagem de volumes da I51).</summary>
    public string QtdeVolumeOrigem { get; set; } = "SomaCaixas";

    /// <summary>Falhas de comunicação seguidas a partir das quais a saúde do relatório alerta.</summary>
    public int MaxFalhasComunicacaoAlerta { get; set; } = 10;

    /// <summary>Teto do backoff exponencial entre reenvios após falha de comunicação, em minutos.</summary>
    public int BackoffMaxMinutos { get; set; } = 60;

    /// <summary>Respostas desconhecidas (Outro) reenviadas até este número antes de rejeitar.</summary>
    public int MaxTentativasOutro { get; set; } = 3;

    /// <summary>Verdadeiro se o volume vem da contagem de volumes da I51.</summary>
    public bool VolumeVemDaI51 =>
        string.Equals(QtdeVolumeOrigem?.Trim(), "VolumesI51", StringComparison.OrdinalIgnoreCase);

    /// <summary>Tipo de operação normal (nunca vazio).</summary>
    public string TipoOperacaoNormal =>
        string.IsNullOrWhiteSpace(TipoOperacaoNota) ? TipoOperacaoNotaPadrao : TipoOperacaoNota.Trim();

    /// <summary>Tipo de operação do reenvio ENC (nunca vazio).</summary>
    public string TipoOperacaoImp =>
        string.IsNullOrWhiteSpace(TipoOperacaoEncerrado) ? "IMP" : TipoOperacaoEncerrado.Trim();

    /// <summary>
    /// Devolve a lista de problemas da configuração (vazia = válida). Chamada na partida só
    /// quando <c>I51:Habilitada=true</c>.
    /// </summary>
    public IReadOnlyList<string> Validar()
    {
        var p = new List<string>();
        var tp = TpAcaoReenvioImp?.Trim() ?? string.Empty;
        if (!tp.Equals("I", StringComparison.OrdinalIgnoreCase) && !tp.Equals("UPD", StringComparison.OrdinalIgnoreCase))
        {
            p.Add("I60:TpAcaoReenvioImp deve ser I ou UPD.");
        }

        var origem = QtdeVolumeOrigem?.Trim() ?? string.Empty;
        if (!origem.Equals("SomaCaixas", StringComparison.OrdinalIgnoreCase)
            && !origem.Equals("VolumesI51", StringComparison.OrdinalIgnoreCase))
        {
            p.Add("I60:QtdeVolumeOrigem deve ser SomaCaixas ou VolumesI51.");
        }

        if ((TipoOperacaoNota?.Trim().Length ?? 0) != 3)
        {
            p.Add("I60:TipoOperacaoNota deve ter 3 letras (ex.: INE).");
        }

        if ((TipoOperacaoEncerrado?.Trim().Length ?? 0) != 3)
        {
            p.Add("I60:TipoOperacaoEncerrado deve ter 3 letras (ex.: IMP).");
        }

        if (MaxFalhasComunicacaoAlerta < 1 || MaxFalhasComunicacaoAlerta > 1000)
        {
            p.Add("I60:MaxFalhasComunicacaoAlerta deve estar entre 1 e 1000.");
        }

        if (BackoffMaxMinutos < 1 || BackoffMaxMinutos > 1440)
        {
            p.Add("I60:BackoffMaxMinutos deve estar entre 1 e 1440.");
        }

        if (MaxTentativasOutro < 1 || MaxTentativasOutro > 20)
        {
            p.Add("I60:MaxTentativasOutro deve estar entre 1 e 20.");
        }

        if (ReenviarEncerradoComoImp
            && !KitWmsIntegrador.Core.Separacao.TextoNormalizado.Lista(PadroesDocCargaEncerrado).Any())
        {
            p.Add("I60:PadroesDocCargaEncerrado precisa de ao menos um texto quando ReenviarEncerradoComoImp=true.");
        }

        foreach (var (nome, lista) in new[]
        {
            ("PadroesDocCargaEncerrado", PadroesDocCargaEncerrado), ("PadroesQuantidadeDiferente", PadroesQuantidadeDiferente),
            ("PadroesVolumeDiferente", PadroesVolumeDiferente), ("PadroesExigeUpd", PadroesExigeUpd),
            ("PadroesRessalvaBloqueante", PadroesRessalvaBloqueante), ("PadroesAssociadaSucesso", PadroesAssociadaSucesso),
        })
        {
            if (!string.IsNullOrEmpty(lista) && lista.Split(';').Any(string.IsNullOrWhiteSpace))
            {
                p.Add($"I60:{nome} tem item vazio (será ignorado).");
            }
        }

        return p;
    }
}

