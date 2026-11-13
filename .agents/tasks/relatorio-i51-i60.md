# Relatório final: I51 como gatilho da I60

Fluxo entregue: **I50 → separação no WMS → I51 → emissão da nota → I60 (INE)**, com ENC → IMP, ajuste de quantidade, relatório diário às 12:00 e tela de devolução I40 mantida como última alternativa. Tudo vem **desligado por padrão** (`I51:Habilitada=false`, `I60:Habilitada=false`, `Devolucao:Habilitada=false`). Nada foi testado contra o WMS real (homolog bloqueado pela proprietária BRF, pendente no fornecedor). Sem push.

> ## ⚠️ AVISO PARA O ORQUESTRADOR CONFIRMAR COM O USUÁRIO: I40 NOS MANUAIS
>
> Pelo pedido "tudo isso deve ser incluído no deck e nos manuais", a **I40 (tela de devolução) entrou nos manuais**, o que muda a decisão anterior (antes ela ficava FORA dos manuais oficiais).
>
> - Ela aparece **somente como "última alternativa"**, pronta e desligada por padrão.
> - Manual do Usuário: **seção 11** (própria). Manual Técnico: **seção 8.6** (própria, marcada como removível).
> - No deck (`apresentacao.html`): um box no slide 6 e uma linha no slide 14. Não há slide dedicado, então remover do deck exige editar esses dois pontos (achado da revisão das docs).
> - Para remover: apagar as seções 11 (Usuário) e 8.6 (Técnico), o box do slide 6 e a linha do slide 14, depois regerar HTML/PDF com `docs/manuais/gerar-manuais.ps1` e o PDF do deck pelo Edge.
>
> **Peço confirmação do usuário** de que a I40 deve permanecer nos manuais e no deck. Se não, a remoção é pequena e isolada.

## 1. O que foi implementado

Commits locais (ordem cronológica de trabalho):

| Commit | Conteúdo |
|---|---|
| `95697a7` | Opções `I51`, `I60` (ampliada) e `RelatorioI60`, normalização de texto, parser de ressalvas, cliente WMS bruto (`IClienteWmsBruto`/`PostBruto`) |
| `8e5ecb2` | Estado da separação, consulta I51 (TODOS/UNICO), armazenamento bruto, casamento documento↔nota, órfãos, ajuste de quantidade, I60 ajustada (INE, ENC → IMP), comandos de reenfileirar |
| `80829a1` | Relatório diário (HTML, PDF, e-mail) e agendador das 12:00 |
| `608a0ef` | Worker da I51, cliente WMS único (instância compartilhada), CLIs e chaves de configuração |
| `57d50b7` | Suíte de testes com WMS falso |
| `6996b24` | Roteiro de teste, escopo implementado, arquitetura, operação, relatório de exemplo |
| `5ce845f` | Deck (2 slides novos) e manuais (usuário e técnico) |

Comportamento principal:

- **Registro na I50:** nota aceita (ou pendente/já existe) é registrada em `Estado\i51\` com cópia do XML. Com I51 ligada, a I60 não sai mais junto da I50 (histórico mostra "I60 adiada"). Com I51 desligada, nada novo roda nem toca disco.
- **Consulta periódica** (BackgroundService, `I51:IntervaloMinutos`, padrão 5): TODOS grava a resposta bruta em disco antes de processar (o TODOS não devolve de novo), e UNICO (CNPJ + número) recupera notas atrasadas. `sucesso=N` é "nada a retornar", não erro.
- **I60 após a separação:** mesma NF-e da I50, quantidades ajustadas às separadas (original × separada guardadas por item), operação **INE** (nunca IMB).
- **Estados:** AguardandoSeparacao → Separada → I60Enviada → Associada | Rejeitada, mais Atrasada (padrão 24 h). Escrita atômica, sobrevive a reinício, nunca lança por falha de disco.
- **Ondas TSP** passam pelo mesmo fluxo (tipo e onda gravados no estado).
- **Reenfileirar** (`--reenfileirar-i60`) funciona entre processos: a CLI grava um comando em arquivo e o Worker em execução aplica no ciclo seguinte.
- **Relatório diário às 12:00** (HTML + PDF + e-mail, identidade "Relatório de Integração"): aguardando com idade, separadas, associadas, rejeitadas com motivo, ajustes item a item, atrasadas, visão por pacote e candidatas à I40.
- **Outras CLIs:** `--consultar-i51` (só UNICO) e `--relatorio-i60 [--enviar]`.
- **I40:** código não alterado; há testes de regressão com a I51 ligada.
- **Deck e manuais:** deck com 14 slides (novos: 6 e 7), PDF regerado; Manual do Usuário (seções 10 e 11), Manual Técnico (8.5 e 8.6) e Manual do Agente de IA com nota. Slides de esforço e de comparação de horas intactos (só o número do rodapé mudou).

Verificação: build Release com 0 avisos e 0 erros; **839 testes aprovados, 0 falhas, 9 ignorados** (baseline 655/9, +184 testes). Review de código: APPROVED. Review de docs: APPROVED. Relatório de exemplo (dados fictícios) conferido por screenshot do Edge headless.

## 2. Decisões tomadas

**Detecção de ENC (a confirmar no homolog).** Não há consulta de status do doccarga; a detecção é por **recusa da I60 INE com mensagem de encerrado**. Casamento por texto normalizado (sem acento, sem caixa), em qualquer mensagem ou ressalva do retorno, com padrões configuráveis (`I60:PadroesDocCargaEncerrado`, padrão `encerrad;status ENC`). ENC tem prioridade sobre os demais motivos. O reenvio sai como **IMP** (`tipoOperacaoNota` e `Tpsit` = `IMP`, configurável em `TipoOperacaoEncerrado`), o erro "doccarga encerrado" é registrado no estado e no relatório, e a nota **nunca vira Associada** (IMP não associa). Ela fica como candidata à devolução pela I40.

**Visão por pacote.** Decidida como **seção dentro do relatório diário**, e não como relatório separado por pacote. Motivo: evita um segundo agendamento e um segundo e-mail, e a identificação do pacote (e da onda TSP) já fica gravada no estado de cada nota.

**TpAcao no reenvio.** O reenvio IMP usa `TpAcaoReenvioImp = UPD` por padrão (configurável `I` ou `UPD`). Motivo: a INE recusada por ENC já importou a nota na interface do WMS (visto em `respostas-homolog.txt`), então `I` bateria em "Nota [...] já existe, TpAcao precisa ser UPD!". Se mesmo assim a resposta pedir UPD e o `tpacao` usado não era UPD, há **no máximo um reenvio com UPD** (`ReenviarComUpdSeJaExiste`). IMP que ainda pede UPD termina em Rejeitada/JaExiste. No máximo 4 chamadas por nota por ciclo. Coberto por testes com WMS falso.

Outras decisões relevantes:

- `CampoUnico = Numero` (o usuário disse "só CNPJ e número"), configurável para `NroDocumento`. As duas variantes entram no roteiro do homolog.
- Filtro de proprietário pelo `CnpjProprietario` do retorno (não pelo CNPJ da consulta). Documento de outro proprietário é só contado e logado.
- Documento sem nota correspondente vira **órfão** (não é descartado) e é reconfrontado a cada ciclo e a cada novo registro.
- Falha de comunicação nunca torna a nota terminal: backoff exponencial até `BackoffMaxMinutos`. Resposta não reconhecida (`Outro`) é reenviada até `MaxTentativasOutro` antes de Rejeitada.
- Uma única instância de `ClienteWmsHttp` (token, lock e retry compartilhados) entre Orquestrador, I51 e devolução.
- Gatilho do TODOS ignora notas muito antigas (`I51:DiasMaxTodos`, padrão 14).
- **Cortados** (marcados como cortáveis no plano): `I60:Max4xxConsecutivos` e o disjuntor por ciclo. 4xx conta como falha de comunicação comum, com backoff. Peço confirmação de que o corte é aceitável.

## 3. O que aguarda teste no homolog

Roteiro completo em `docs/ROTEIRO-TESTE-I51-I60.md`. Pré-requisito: liberação da proprietária BRF pelo fornecedor. Itens abertos, todos hoje como padrões configuráveis e não confirmados:

- Texto real da recusa por doccarga **ENC** e se o reenvio **IMP** é aceito, com qual `TpAcao` (`UPD` ou `I`).
- Campo do **UNICO**: `Numero` ou `NroDocumento`.
- Mensagem real do `sucesso=N` (para preencher `PadroesSemRetorno`).
- Sinal real de **associação** da I60 INE (`PadroesAssociadaSucesso`) e respostas reais da I60.
- Origem da quantidade de volume (`QtdeVolumeOrigem`: soma de caixas × contagem de volumes da I51).
- **Atenção:** a primeira ativação do TODOS consome o backlog da proprietária. Após o 1º TODOS, contar os órfãos: se houver órfãos e nenhuma nota casou, a premissa "Numero = nNF" está errada.
- Quantidade diferente, ondas TSP, reinício no meio do fluxo, nota atrasada, `--reenfileirar-i60` e `--relatorio-i60` forçado.
- Regressão da I40 via `ROTEIRO-TESTE-I40.md`, seção 9.

## 4. Horas

Somente horas, sem valores monetários.

- **Horas humanas: a informar pelo usuário.** Não foram estimadas nem inventadas.
- **Horas de IA (estimativa; não houve cronômetro de sessão registrado nesta etapa):**
  - Implementação e testes (itens 1 a 15 do plano): aprox. 13,5 h (estimativa do plano).
  - Deck e manuais: aprox. 2,0 h (estimativa do plano).
  - Total estimado desta entrega: aprox. 15,5 h de IA.
  - Design, revisões de design e plano já foram contados no fechamento de 12/11 (aprox. 7,5 h de IA) e **não** estão somados aqui.

Estes números são estimativas do plano, não medições. O registro oficial fica a cargo do orquestrador em `docs/CONTROLE-ESFORCO.md`, que **não foi editado** aqui (nem `docs/controle/*` nem `docs/diario/*`).
