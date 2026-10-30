# Teste de comunicação com o WMS (homolog) — 2026-10-30

Teste feito **antes de escrever o agente**, para medir o tempo real de comunicação por
nota e validar o contrato, conforme pedido. Ambiente: homolog
`friozem.assistsoftware.com.br:8888`, usuário `BLUEFOOD`.

## Header / REQUISITANTE (CORREÇÃO importante)

Nos testes iniciais usamos `REQUISITANTE=02.559.193/0001-40`, que é o CNPJ da
**Ipanema** (outro cliente). O correto para a **BRF** é
**`REQUISITANTE=01.838.723/0098-50`** (CNPJ do depositante/BRF), conforme a tela de
configuração das interfaces no WMS (I50/I60 → campo "Requisitante"). `DESTINO`
permanece `14.605.065/0001-75` (armazém Friozem). Corrigido em `appsettings.json`
(seção `Wms`). Observação: a tela "Entrada" do WMS mostra um envelope
`{ "header": {...}, "body": {...} }`; nos nossos testes os 5 parâmetros foram na query
string e funcionaram (gerou idDocumento). A CONFIRMAR se a API aceita só a query string
ou se o envelope header/body é a forma preferida/obrigatória para produção.

## Validação do payload corrigido (2026-10-30)

Após as correções de formato (CNPJ/CEP com máscara, protocolo extraído,
numeroPlanejamento = transporte, dataprev = emissão+2, qtdMandante=E), a I50 gerada
pelos construtores foi enviada ao homolog: **HTTP 200 em 78ms**; o retorno reconheceu o
documento (`documento: 15665807`, `idDocumento: 1427345`) — prova de que
estrutura/máscaras/campos foram aceitos. Única ressalva: "Empresa não localizada"
(caso D → fluxo I10). Formato validado.

## Teste do caso D: I10 + reenvio da I50 (2026-10-30) — DESCOBERTA IMPORTANTE

Sequência testada no homolog para validar o fluxo de empresa não cadastrada e revelar
erros escondidos atrás do "empresa não localizada":

**Passo 1 — I10 (WEBAPI-I10-Json)**: destinatário FREE MAN (28.288.914/0001-19).
Retorno: `{CodEmpresa: -28.288.914/0001-19- já existe no cadastro. Utilizar o comando de
alteracao(U).}`. Aprendizado:
- Para I10, **"já existe" = sucesso efetivo** (empresa presente). O orquestrador deve
  tratar essa ressalva como OK, não como erro.
- Para atualizar empresa existente, usar `tpacao = "U"` (hoje default "I").
- `indireto = "IN"` confirmado (era "CL" no default; corrigido).

**Passo 2 — reenvio I50 (empresa agora existe)**: o erro "empresa não localizada"
desapareceu e **surgiu um novo erro antes oculto**:
`{Há item no documento de carga 15665807 com erro!} || {Produto Cod\EAN: "175886" da
empresa "3509308-01.838.723/0098-50" não ativo ou faltando ou não cadastrado!} || {... "175894" ...}`

**DESCOBERTA**: além da empresa (I10), **os PRODUTOS também precisam estar cadastrados**
no WMS para a BRF (3509308) antes da I50 ser aceita. Isso adiciona uma pré-condição ao
fluxo que não estava mapeada:
- Pode existir uma interface de cadastro de produto (ex.: I-algo) a enviar antes da I50,
  OU os produtos são cadastrados por outro processo/carga mestre no WMS.
- **A CONFIRMAR com o cliente**: como os produtos são cadastrados? Há uma interface para
  isso que o agente deve chamar (como faz com I10 para empresa), ou é pré-requisito
  externo (cadastro mestre)?

Consequência para o Orquestrador (Etapa 5): a cadeia de pré-condições da I50 pode ser
**empresa (I10) + produtos (?)** antes de a nota ser efetivamente aceita. O padrão de
retorno (ressalvas em bloco `{...} || {...}`) é parseável para o relatório de erros.

## Teste PONTA A PONTA I30→I10→I50→I60 (2026-10-30) — DESCOBERTA CRÍTICA

Executada a cadeia completa para a nota 15665807 (2 itens), na ordem, observando cada
retorno. Todos os POSTs retornaram HTTP 200 e foram "importados e pendentes na
interface". Resumo:

| Passo | Interface | Resultado | Aprendizado |
|-------|-----------|-----------|-------------|
| 1 | I30 prod 175894/175886 | importado (idDoc 331061/331062) | ressalva: `Classificação "DEF" faltando` — o `codclassificacao` precisa de valor VÁLIDO cadastrado no WMS (DEF genérico não serve p/ BRF) |
| 2 | I10 empresa | "já existe" = OK | confirmado |
| 3 | I50 DocCarga | ainda "Produto não ativo/não cadastrado" | **I30 enviado segundos antes NÃO ativou o produto a tempo** |
| 4 | I60 nota | aceita (idDoc 187156); "item com erro" | **I60 nunca testada antes — ESTRUTURA OK**, só esbarra no produto em cascata |

### DESCOBERTA CRÍTICA: "pendente na interface" ≠ "ativo/utilizável"

Todos os envios caem como **"importado e pendente na interface"**. Isso NÃO significa
efetivado: o registro entra numa **fila de interface do WMS que precisa ser
processada/revisada** antes de o dado ficar ativo. Prova: o I30 importou os produtos
(passo 1), mas a I50 logo em seguida (passo 3) ainda os vê como "não ativos". Ou seja:

- **Não dá para encadear I30 → I50 → I60 na mesma rodada e esperar sucesso imediato.**
  Entre o cadastro (I30/I10) e o consumo (I50/I60) há um processamento da interface no
  lado do WMS.
- Isso impacta diretamente o Orquestrador (Etapa 5): o fluxo não é síncrono "cadastra e
  usa na hora". Precisa de uma de duas abordagens:
  1. **Assíncrono/retry com espera**: enviar I30/I10, marcar como pendente, e reprocessar
     a I50/I60 numa passada posterior (após o WMS processar a fila).
  2. **Pré-condição externa**: garantir empresa+produtos cadastrados ANTES (carga mestre),
     e o agente só envia I50/I60.
- **A CONFIRMAR com o cliente/Assist**: o que processa a fila "pendente na interface"?
  É automático (job do WMS a cada X min)? Manual (colaborador)? Há endpoint para
  consultar/forçar o processamento? Isso define se o agente espera-e-reenvia ou se
  trata tudo como pendência para processamento posterior.

### Boa notícia

A **estrutura de todos os 4 payloads foi aceita** (I10, I30, I50, I60 retornaram 200 e
foram importados). Nenhum erro de formato/máscara/campo restante — os erros são de
dados (produto não cadastrado/classificação), não de contrato. A I60, testada pela
primeira vez, passou na estrutura. Tempos: 50-136ms por chamada.

## Teste E2E v2 e v3 (2026-10-30) — cadeia quase fechada

Com as correções do usuário (I30 `codclassificacao="2"`, `codempresa="3509308"`), a
cadeia avançou muito:

**v2 — I30 com classificação/codempresa corretos:**
- I30 produto 175894 → **sucesso S** ("Registrado com sucesso", idDoc 331063)
- I30 produto 175886 → **sucesso S** (idDoc 331064)
- I10 empresa → "já existe" (OK)
- **I50 DocCarga → sucesso S** (idDoc 1427353) — passou limpa pela 1ª vez!
- I60 → erro: `Total de volume(s) do(s) pedido(s)(doccarga): 0, não confere com a
  qtdvolume da nota venda: 36`.

CORREÇÃO do entendimento: NÃO era fila assíncrona. A I50 falhava antes porque o I30
(com classificação "DEF" inválida) NÃO cadastrava o produto. Com classificação "2" e
codempresa "3509308", o produto cadastra na hora e a I50 passa em sequência imediata.

**v3 — I60 com qtdeVolume=0** (tentativa de alinhar com DocCarga de 0 volumes):
- Resolveu o erro de volume do cabeçalho, mas surgiu o próximo:
  `Produto 175894. Quantidade caixa do(s) 12 pedido(s) não confere com a quantidade da
  nota venda 0`.

### Onde paramos (ÚLTIMO erro em aberto na I60)

A cadeia **I30 → I10 → I50 passa 100%**. Só a **I60** tem erro de **conferência de
quantidade entre I50 e I60**, em dois níveis:
1. Volume do cabeçalho (I60 `qtdeVolume` vs. total de volumes do DocCarga I50).
2. Quantidade de CAIXA por item (I60 item vs. I50 item).

Zerar `qtdeVolume` resolveu (1) mas quebrou (2) — o WMS passou a ler "quantidade da nota
venda 0" para o produto. O certo NÃO é zerar, é fazer os valores **baterem** entre I50
e I60.

**A DEFINIR com o usuário/BRF**: a semântica de quantidade.
- Na I50, o item tem `quantidadeProduto` e `qtdeEmbalagem` (ambos = qCom hoje: 12 e 24).
- Na I60, o item tem `qtdeProduto`, `qtdeProdutoConfirmada`, `qtdeEmbalagem`; o
  cabeçalho tem `qtdeTotalNota` e `qtdeVolume`.
- Precisa definir: qual campo é "caixa" e qual é "produto/unidade", e garantir que a
  contagem de caixa/volume seja a MESMA nos dois documentos. Dado `qtdMandante="E"`
  (caixas) na I50, provável que a I50 esteja tratando qCom como caixas e a I60 precise
  do mesmo. Testar: I60 `qtdeVolume` = soma das caixas (12+24=36) no cabeçalho, mantendo
  as caixas por item coerentes com a I50.

Boa notícia: nenhum erro de formato/contrato restante. Todos os 4 payloads têm estrutura
aceita. O que falta é só alinhar a semântica de quantidade caixa/volume na I60.

## Perguntas para o chamado no fornecedor (Assist) — 2026-10-30

Contexto: integração BLUEFOOD/BRF via WebAPI REST (I10/I30/I50/I60) em homolog
(friozem...:8888). Cadeia I30→I10→I50 valida 100% (todos sucesso=S). I60 trava em
conferência de quantidade. Perguntas objetivas:

1. **Conferência de volume/caixa I50 × I60**: ao enviar I60, retorna
   `Total de volume(s) do(s) pedido(s)(doccarga): 0, não confere com a qtdvolume da nota
   venda: 36`. Qual campo da I50 alimenta o "total de volumes do doccarga"? É a soma de
   `qtdeEmbalagem` dos itens do ITEM_PEDIDO? Ou um campo de cabeçalho? Como garantir que
   bata com o `qtdeVolume` da I60?

2. **Conferência de quantidade por item**: ao zerar `qtdeVolume` da I60, o erro muda
   para `Produto 175894. Quantidade caixa do(s) 12 pedido(s) não confere com a
   quantidade da nota venda 0`. Qual campo da I60 (item) representa a "quantidade de
   caixa" comparada com a I50? (`qtdeEmbalagem`? `qtdeProduto`?) E qual da I50?

3. **Semântica caixa vs. produto/unidade**: na I50 o item tem `quantidadeProduto` e
   `qtdeEmbalagem`; na I60 tem `qtdeProduto`, `qtdeProdutoConfirmada`, `qtdeEmbalagem`.
   Com `qtdMandante="E"` na I50, qual deles o WMS trata como caixa e qual como unidade?

4. **Envelope vs. query string**: a API aceita os 5 parâmetros (IDENTIFICADOR,
   REQUISITANTE, CNPJREQUISITANTE, DESTINO, CNPJDESTINO) tanto na query string quanto no
   envelope `{"header":{...},"body":{...}}`? Alguma é obrigatória/preferível?

5. **"Pendente na interface"**: o retorno `sucesso=N` com "Foi importado e esta pendente
   na interface" — quando há ressalva — significa que o documento precisa de
   processamento posterior (job/manual)? Como consultar se um documento saiu de pendente
   para efetivado?

6. **Fluxo só-nota (confirmado pelo usuário)**: quando o ciclo é apenas importar a nota
   (sem gerar/alterar doccarga), o `tipoOperacaoNota`/`Tpsit` da I60 deve ser **"IMP"**
   (não "INE"). Confirmar os valores: IMP (só importa a nota) vs. INE (vincula doccarga
   sem embarque) vs. IMB (vincula e gera embarque).

## Endpoints confirmados

- **Token**: `GET /wmsapi/api/Integracao/GetToken` com Basic Auth → JSON `{ "token": "<JWT>" }`.
  Tempo: ~294 ms (inclui handshake TLS inicial).
- **Envio**: `POST /wmsapi/api/integracao/Post` com Bearer token. **Query string com os 5
  parâmetros é obrigatória** (a falta de `CNPJREQUISITANTE`/`CNPJDESTINO` causava 404):
  ```
  ?IDENTIFICADOR=WEBAPI-I50-Json&REQUISITANTE=02.559.193/0001-40&CNPJREQUISITANTE&DESTINO=14.605.065/0001-75&CNPJDESTINO
  ```
  - `IDENTIFICADOR`: `WEBAPI-I50-Json` | `WEBAPI-I60-Json` | `WEBAPI-I10-Json`
  - `REQUISITANTE`: `02.559.193/0001-40` (observado no Postman)
  - `DESTINO`: `14.605.065/0001-75`
  - `CNPJREQUISITANTE` e `CNPJDESTINO`: presentes na query, vazios.

## Tempo medido (I50, 5 repetições)

| Repetição | Tempo |
|-----------|-------|
| #1 (aquecimento) | 161 ms |
| #2 | 81 ms |
| #3 | 73 ms |
| #4 | 57 ms |
| #5 | 61 ms |
| **Média** | **87 ms** |

**Comparação**: tela atual ~12.000 ms/arquivo → API ~87 ms/nota. Ganho de ~100-200x no
processamento por nota. **O gargalo era a tela/UI, não o WMS.** Confirma a viabilidade
do projeto com folga; o envio concorrente só amplia o ganho.

## Formato de resposta (base para o store de estado e relatórios)

```json
{
  "transacao": "4019663",
  "tipoEDI": "I50",
  "sucesso": "N",
  "retornos": [
    { "documento": "", "idDocumento": "", "sucesso": "N", "mensagem": "..." }
  ]
}
```

- `transacao`: ID único no WMS — usar para **rastreio/auditoria** no store.
- `sucesso`: S/N — mas **N nem sempre é falha total** (ver abaixo).
- `retornos[].mensagem`: motivo legível — alimenta relatório de erros/pendências.

## Achados que ajustam a arquitetura

1. **Pendência ≠ falha**: com estrutura mínima válida, o retorno foi
   `"Foi importado e esta pendente na interface. Ressalvas : {Destinataria - Empresa
   não localizada, sigla não informada.}"` com `sucesso=N`. Ou seja, há um estado
   **PENDENTE** (importado, aguardando algo) distinto de **ERRO** (rejeitado). A máquina
   de estados precisa desse terceiro resultado.
2. **Confirmação do caso D (empresa não cadastrada)**: a ressalva "Empresa não
   localizada" é exatamente o gatilho para o fluxo **I10 → reenvia I50**.
3. **Estrutura mal-formada**: `PEDIDO` vazio retorna `"Mensagem EDI com estrutura
   mal-formada. Processo Abortado."` — útil como teste negativo.
4. **Duplicado neste estágio foi aceito**: 5 POSTs com o mesmo `numeroPedido` geraram 5
   transações distintas (4019663-4019667), todas "pendentes". O bloqueio de duplicado
   provavelmente só ocorre quando a onda sai de "editando" (a confirmar).

## Pendências levantadas

- Mapear os campos obrigatórios reais da I50/I60 para BRF (sigla da empresa, CNPJs de
  proprietário/armazém/depósito corretos de produção vs. homolog).
- Entender o ciclo de "pendente na interface" → o que o colaborador faz para efetivar.
- Confirmar quando o WMS passa a bloquear duplicado (estado da onda).
- Montar o fluxo I10 a partir dos dados do destinatário do XML (dest/CNPJ, xNome, ender).
