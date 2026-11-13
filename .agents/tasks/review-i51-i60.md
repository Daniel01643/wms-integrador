# Retorno da separação (I51) como gatilho da I60 ajustada

A mudança introduz o fluxo I50 → separação → I51 → I60. Um Worker consulta a I51 (TODOS e UNICO), grava a resposta bruta antes de processar, casa os documentos com as notas acompanhadas e só então envia a I60 com a mesma NF-e da I50, tipo INE, quantidades ajustadas às separadas. Doccarga encerrado (ENC) reenvia como IMP com TpAcao configurável e registra o erro. Um relatório diário às 12:00 (HTML, PDF, e-mail) lista o que ficou sem associação como candidata à I40. Tudo vem desligado por padrão (`I51:Habilitada=false`). Com a flag desligada, a I60 mantém o comportamento anterior.

Watch for: nada foi testado contra o WMS real (homolog bloqueado pela proprietária), então texto do ENC, TpAcao do IMP, campo do UNICO e mensagem do `sucesso=N` são padrões configuráveis (possible, já documentado no roteiro de teste). Deck e manuais ficam para etapa posterior, fora do escopo desta revisão.

**Verdict**: APPROVED

## High-level view

O fluxo é gateado por `I51:Habilitada`, desligada nos três appsettings. O bruto da resposta TODOS é gravado em disco antes do processamento e reprocessado sem nova chamada ao WMS em caso de falha, o que protege o caráter consumível do TODOS. UNICO é repetível e serve para recuperar atrasadas. `sucesso=N` é tratado por lista configurável de padrões. A I60 usa INE como operação normal e IMP no ENC.

A I40 não teve arquivos de código alterados e há teste de regressão com a I51 ligada. A evidência de verificação mostra build sem avisos e 839 testes aprovados, 0 falhas, 9 ignorados (stress anterior).

<details>
<summary>Issues (2)</summary>

1. **Sem validação no WMS real** — textos de ENC/`sucesso=N`, TpAcao do IMP e campo do UNICO são suposições configuráveis; validar com o roteiro `docs/ROTEIRO-TESTE-I51-I60.md` assim que o homolog liberar (possible, informativo).
2. **Itens cortados do plano** — `I60:Max4xxConsecutivos` e o disjuntor por ciclo não foram implementados; 4xx conta como falha de comunicação comum com backoff. Confirmar que o corte é aceitável (confirmed, marcado como cortável no plano).

</details>

<details>
<summary>Details</summary>

## Conformidade com as restrições do brief

A evidência (`verificacao-i51-i60.md`) cobre TODOS consumível, bruto gravado antes de processar, UNICO repetível, `sucesso=N`, ajuste de quantidade (Omitir/Zerar, soma 0), INE nunca IMB, ENC → IMP com `TpAcaoReenvioImp` configurável (UPD/I) e reenvio UPD, I51 desligada sem tocar disco e regressão da I40. Não reexecutei build nem testes. Spot-checks no diff: o único uso de "IMB" em `src` é em comentários/documentação; `appsettings` mantém `I51:Habilitada=false` (linha 83) e `I60:Habilitada=false`; os logs novos recebem apenas nomes de arquivo, número da nota e mensagens curtas, e nenhum log recebe corpo, JSON ou XML (a busca por `corpo|jsonBruto|xml|conteudo` em chamadas de log só retornou mensagens da CLI manual com caminhos); nenhum arquivo `.xml`/`.zip` foi adicionado no intervalo; nenhum valor monetário foi introduzido.

## Edições em docs de esforço

`docs/CONTROLE-ESFORCO.md`, `docs/controle/*` e `docs/diario/*` aparecem no diff contra o commit do brief somente por causa do commit `00f020c` ("Fechamento 12/11"), que é o fechamento do dia anterior, anterior ao código. Os commits de implementação e de documentação (`608a0ef`, `80829a1`, `8e5ecb2`, `95697a7`, `57d50b7`, `6996b24`) não tocam esses arquivos (confirmado por `git diff 00f020c HEAD --name-only`). Sem violação.

## Pendência de escopo

Deck e manuais não foram atualizados nesta etapa (declarado na verificação como etapa posterior). A solicitação original os inclui, então o trabalho posterior precisa cobri-los.

## Arquivos

Implementação em `src/*/Separacao`, `OpcoesI51/I60`, Worker, relatório I60, testes em `tests/KitWmsIntegrador.Tests`, docs `ESCOPO-I51-I60`, `ROTEIRO-TESTE-I51-I60`, `ARQUITETURA`, `OPERACAO` e exemplo de relatório. Diff completo: `git diff 97c7a3a HEAD`.

</details>
