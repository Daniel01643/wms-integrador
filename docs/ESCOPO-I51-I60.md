# Escopo — Retorno de separação (I51) como gatilho da I60

Status: **implementado e testado com WMS fake; teste no homolog pendente** (proprietária BRF no fornecedor).
Data: 12/11/2026. Roteiro: docs\ROTEIRO-TESTE-I51-I60.md.

## Problema

A I60 (associação da nota ao doccarga) só funciona quando o doccarga está **separado**.
Hoje a I60 é enviada logo após a I50, antes da separação, e por isso não associa
(Plano A pausado: "quantidade caixa ... 0", doccarga encerrado, quantidade diferente etc.).

Ciclo real no WMS:

```
I50 (doccarga) → Separação → I51 (retorno da separação) → Emissão da nota → I60 (associação)
```

## Solução

O integrador passa a acompanhar cada nota depois da I50, consulta a I51 periodicamente e
só envia a I60 quando a separação retornou.

```
I50 aceita → AguardandoSeparacao → (I51 retornou) Separada → I60 enviada → Associada
                                                                        → Rejeitada (motivo)
            → (sem I51 depois de X horas) Atrasada → alerta no relatório
```

## Decisões (usuário, 12/11/2026)

| # | Tema | Decisão |
|---|------|---------|
| 1 | Tipos de consulta I51 | Só **TODOS** e **UNICO**, usando apenas **CNPJ** e **número**. |
| 1a | TODOS | Traz o que estiver disponível; **o que já foi puxado uma vez não volta**. A resposta deve ser gravada em disco antes de qualquer processamento (senão se perde). |
| 1b | UNICO | Traz só o pesquisado; **pode ser repetido** quantas vezes for preciso. Usar para recuperação/conferência de uma nota específica (ex.: perdeu-se o TODOS, reinício, verificação manual). |
| 2 | Repetição | Respondido pelo item 1. |
| 3 | Configuração no WMS | O usuário configura no WMS (EDI com "Meio envio e retorno = WEBSERVI"). O integrador só precisa dos **identificadores configuráveis** (ex.: `WEBAPI-I51`; o número do identificador pode mudar). |
| 4 | Separação parcial | Não existe: o retorno é um **evento configurado no WMS** que só dispara com a separação concluída. Se a I51 retornou, a separação ocorreu. |
| 5 | Quantidade diferente | **Ajustar a I60 para a quantidade separada** e registrar a diferença (original × separada, item a item). Objetivo: garantir a associação; a I60 serve para devolução depois, se necessário. |
| 6 | Nota da I60 | **Confirmado:** a I60 usa a **mesma NF-e (XML) da I50**, já guardada pelo integrador. |
| 6a | Doccarga ENC (encerrado) | Quando o doccarga estiver em status **ENC**, enviar a I60 como **IMP** (`tipoOperacaoNota`/`Tpsit` = `IMP`, só importa a nota, sem associação automática) e **registrar o erro** ("doccarga encerrado") no estado e no relatório. Nos demais casos, a I60 segue a operação normal **`INE`** (padrão do usuário; **não** usar `IMB`). |
| 7 | Frequência | **Configurável**; padrão 5 minutos (há clientes que consultam de 5 em 5 min). Prazo de "atrasada" também configurável. |
| 8 | Relatório | **Diário às 12:00** (HTML/PDF + e-mail). Avaliar também uma seção/relatório **por pacote**. |
| 9 | Puxada TSP | **Ondas TSP também passam por este fluxo.** |
| — | Liga/desliga | Tudo atrás de chave: `I51:Habilitada` e `I60:Habilitada`, **desligadas por padrão**. |
| — | Testes no WMS | Somente depois que o fornecedor resolver a proprietária BRF (01.838.723/0098-50) no homolog. |

## Ordem das alternativas para devolução (decisão do usuário, 12/11/2026)

1. **I60 associada** (fluxo I51 → I60 `INE`): caminho principal; a devolução usa a nota associada.
2. **I60 como `IMP`** (doccarga ENC): nota importada sem associação, erro registrado.
3. **Tela de envio de devolução pela I40** (Plano B, `docs\ARQUITETURA-PLANO-B-I40.md`):
   **última alternativa**, mas deve ficar **pronta e funcionando** (testada no homolog), atrás de
   `Devolucao:Habilitada` (hoje false). Continua **fora dos manuais oficiais** até decisão em contrário.
   O trabalho da I51/I60 **não pode quebrar** a tela da I40 (testes de regressão obrigatórios).
   Ideia a avaliar no relatório diário: listar as notas que só ficaram como `IMP`/rejeitadas como
   candidatas à devolução pela I40.

## Pontos a definir na implementação (planner)

- **Como detectar o ENC** (a confirmar com o usuário): a I51 não traz o status do doccarga no
  layout. Hipótese: a resposta da I60 `INE` recusa com mensagem de doccarga encerrado. Nesse caso o
  fluxo é: I60 `INE` → recusa "encerrado" → reenvio como `IMP` → registro do erro. O texto da
  mensagem de recusa deve ser **configurável** (lista de padrões).
- **Risco do reenvio:** no homolog, reenviar uma nota que já entrou na interface devolveu
  "Nota [...] já existe, TpAcao precisa ser UPD!" (`docs\chamado\respostas-homolog.txt`). O reenvio
  como `IMP` pode precisar de `TpAcao = UPD`. Confirmar no teste do homolog.

- Como casar o retorno da I51 com a nota enviada: `Numero` do documento × número do
  doccarga/transporte que enviamos na I50; filtro por CNPJ.
- Onde guardar o estado (padrão `Estado\i51\`) e por quanto tempo (retenção, como a de 30 dias).
- Se a I60 atual (ConstrutorI60, `I60:Habilitada`) é disparada junto com a I50 — ela deve
  passar a ser disparada **somente** pelo fluxo da I51.
- Reaproveitar o agendador do resumo semanal (Monitoramento) para o relatório diário das 12:00.

## Pendências externas

- Fornecedor: proprietária BRF "Proprietaria/Chave - 01.838.723/0098-50 nao encontrada" no homolog.
- Usuário: configurar o EDI da I51 (WEBSERVI) em homolog e produção; informar os identificadores finais.
- Teste da tela de devolução I40 no homolog (roteiro `docs\ROTEIRO-TESTE-I40.md`), junto do teste da I51/I60.

## Estimativa (somente horas)

| Fase | Entrega | Horas IA (estim.) | Horas humanas (estim.) |
|------|---------|-------------------|------------------------|
| 1 | Cliente I51 (TODOS/UNICO), estado das notas, consulta periódica | aprox. 3,0 | aprox. 0,5 |
| 2 | I60 disparada pela separação, com ajuste para quantidade separada | aprox. 2,5 | aprox. 0,5 |
| 3 | Relatório diário 12:00 da I60 (+ por pacote, se viável), e-mail | aprox. 2,0 | aprox. 0,5 |
| 4 | Teste no homolog (após proprietária resolvida) | aprox. 0,5 | aprox. 1,5 |

Horas humanas são estimativa; o registro usa somente as horas informadas pelo usuário.

## Implementado (13/11/2026)

- Chaves `I51:Habilitada` e `I60:Habilitada` **desligadas** por padrão; com a I51 desligada nada novo executa nem toca disco e a I60 segue saindo junto da I50.
- Registro na I50 aceita (inclusive pendente e "já existe"), cópia do XML e estado por nota em `Estado\i51\notas` (AguardandoSeparacao, Atrasada, Separada, I60Enviada, Associada, Rejeitada).
- `BackgroundService` consulta a I51 (TODOS, e UNICO para atrasadas), grava a resposta bruta antes de processar; documentos sem nota viram órfãos; filtro de proprietário pelo `CnpjProprietario` do retorno.
- I60 pós-separação: mesma NF-e, quantidades ajustadas (original × separada no estado), `INE` (nunca `IMB`); ENC detectado por texto configurável na resposta, reenvio como `IMP` com `TpAcaoReenvioImp` (padrão `UPD`, configurável) e erro registrado; "TpAcao precisa ser UPD" gera 1 reenvio; comunicação nunca rejeita (backoff); no máximo 4 chamadas por nota por ciclo.
- Ondas TSP no mesmo fluxo (`Tipo=TSP` e `Onda`).
- Relatório diário às 12:00 (HTML, PDF, e-mail) com visão por pacote (decisão: seção no diário) e candidatas à I40.
- CLIs: `--consultar-i51` (só UNICO), `--reenfileirar-i60` (grava pedido que o Worker aplica), `--relatorio-i60 [--enviar]`.
- Cliente WMS em instância única; I40 intacta e coberta por testes de regressão.
- Pendente no homolog: texto real do ENC, TpAcao do IMP, sinal de "associada", campo do UNICO, mensagem do `sucesso=N`.
