# Arquitetura — Integrador WMS (SolWMS Assist)

Desenho de fluxo de comunicação. **Sem código de produção** — base para revisão e
iteração. Ver `BRIEFING.md` para as regras de negócio.

---

## 1. Contexto

Quem conversa com quem. O agente mora em `C:\wms-integrador`, lê dados de
`C:\Envia xml`, fala com a API do WMS (endpoint único balanceado) e envia alertas.

```mermaid
flowchart LR
    subgraph Entrada["C:\\Envia xml (dados)"]
        XML[XMLs NFe]
        PLAN[Planilhas]
    end

    subgraph Agente["Agente Integrador (C:\\wms-integrador)"]
        CORE[Núcleo de processamento]
        ESTADO[(Store de estado/controle)]
    end

    WMS[["API WMS<br/>endpoint único<br/>3 servidores balanceados"]]
    MAIL[[Servidor de e-mail]]

    XML --> CORE
    PLAN --> CORE
    CORE <--> ESTADO
    CORE -->|GetToken + POST I50/I60| WMS
    CORE -->|alertas de exceção| MAIL
```

---

## 2. Componentes

Peças internas do agente e suas responsabilidades.

```mermaid
flowchart TB
    WATCH[Watcher de pasta<br/>varre C:\\Envia xml]
    READPLAN[Leitor de planilha<br/>lê colunas, inclui No. Transporte]
    PARSEXML[Parser de XML NFe<br/>extrai nº nota, chave, itens, qtd]
    MATCH[Motor de cruzamento<br/>chave = nº da nota<br/>agrupa por No. Transporte]
    BUILD50[Construtor I50<br/>DocCarga + nroagrupseparacao onda]
    BUILD60[Construtor I60<br/>1 por nota, referencia DocCarga]
    CLIENT[Cliente WMS<br/>GetToken JWT + POST]
    STORE[(Store de estado<br/>pendente/enviado/falhou)]
    NOTIFY[Notificador<br/>e-mail em exceções]
    ORCH[Orquestrador<br/>ordem I50 -> I60, retry, concorrência]

    WATCH --> READPLAN
    WATCH --> PARSEXML
    READPLAN --> MATCH
    PARSEXML --> MATCH
    MATCH --> ORCH
    ORCH --> BUILD50 --> CLIENT
    ORCH --> BUILD60 --> CLIENT
    CLIENT --> STORE
    ORCH --> STORE
    ORCH --> NOTIFY
```

---

## 3. Sequência — fluxo feliz

Token → I50 (cria DocCarga + onda) → I60 por nota (associa). Um DocCarga pode ter N
notas (agrupadas por "No. Transporte").

```mermaid
sequenceDiagram
    participant A as Agente
    participant F as C:\\Envia xml
    participant W as API WMS
    participant S as Store estado

    A->>F: varre pasta (XMLs + planilha)
    A->>A: cruza por nº da nota
    A->>A: agrupa por "No. Transporte" (= onda)
    A->>W: GET GetToken (Basic Auth)
    W-->>A: JWT

    Note over A,W: Para cada grupo (1 DocCarga, N notas)
    A->>A: monta I50 (nroagrupseparacao = onda)
    A->>W: POST I50 (WEBAPI-I50-Json)
    W-->>A: ok (DocCarga + onda criados)
    A->>S: grupo = onda_criada

    loop para cada nota do grupo
        A->>A: monta I60 (referencia DocCarga)
        A->>W: POST I60 (WEBAPI-I60-Json)
        W-->>A: ok (nota associada)
        A->>S: nota = associada
    end
    A->>S: grupo = concluido
```

---

## 4. Sequência — token, erro e reenvio

Expiração de token, falha numa nota e reprocesso só do que faltou (idempotência).

```mermaid
sequenceDiagram
    participant A as Agente
    participant W as API WMS
    participant S as Store estado
    participant M as E-mail

    A->>W: POST I60 (nota X)
    W-->>A: 401 token expirado
    A->>W: GET GetToken (renova)
    W-->>A: novo JWT
    A->>W: POST I60 (nota X) de novo
    W-->>A: ok

    A->>W: POST I60 (nota Y)
    W-->>A: erro negocio (qtd nao bate)
    A->>S: nota Y = falhou (motivo)
    A->>M: alerta de exceção (nota Y)
    Note over A,S: proxima execucao reenvia so o que esta pendente/falhou
```

---

## 5. Máquina de estados

Estado do **grupo/DocCarga**. Regra: **falha é no grupo inteiro** — se o I50 ou
qualquer nota (I60) do grupo falha, o grupo todo vai para `falhou`. Não há estado
parcial; o grupo só é `concluido` quando o I50 e **todas** as I60 passaram.

```mermaid
stateDiagram-v2
    [*] --> pendente
    pendente --> enviando_i50: inicia grupo
    enviando_i50 --> enviando_i60: I50 ok
    enviando_i50 --> falhou: I50 erro
    enviando_i60 --> concluido: todas as notas ok
    enviando_i60 --> falhou: qualquer nota falha
    falhou --> pendente: reprocesso (grupo todo)
    concluido --> [*]
    falhou --> [*]: alerta por e-mail + move p/ Erro
```

### Idempotência (WMS é a fonte de verdade)

O WMS **bloqueia duplicados**: rejeita um I50 que já existe e também bloqueia se a
**onda já saiu do estado "editando"**. O store local apenas registra o progresso; a
trava real é do WMS. Consequência no reprocesso de um grupo falho:

- Ao reenviar o grupo, o I50 pode voltar **"já existe"** (porque passou antes). Isso
  **não é erro** — é tratado como "DocCarga já criado, seguir para as I60".
- Se a **onda já saiu de editando**, o WMS bloqueia alterações. Esse caso vira
  **exceção definitiva** (não adianta retry) → alerta por e-mail + pasta `Erro`.

---

## 6. Faseamento sugerido

1. **Fase 1 — transporte + controle**: cliente WMS (token + POST), I50 a partir do
   cruzamento, I60 referenciando DocCarga, store de estado e reenvio. Mede o gargalo
   real no homolog.
2. **Fase 2 — alertas**: notificador por e-mail nas exceções definidas.
3. **Fase 3 — automação total**: watcher contínuo da pasta, concorrência ajustada.

---

## 7. Decisões em aberto (a fechar no desenho)

- **Concorrência**: quantas requisições simultâneas ao endpoint? (começar baixo e medir)
- **Store de estado**: arquivo JSON por lote, SQLite, ou outro?
- **Idempotência**: como garantir que reenvio não duplica DocCarga/nota? (chave de
  controle local + consulta ao WMS?)
- **Retry**: nº de tentativas, backoff, o que é erro transitório vs. definitivo.
- **Amarração I50↔I60**: confirmar `numeroPedido` ↔ `pedidos[]`.
- **Exceções que geram e-mail**: lista a definir.
- **Movimentação de arquivos**: após processar, mover XML/planilha para
  `processados/` ou `erro/`? (evita reprocesso e organiza auditoria)
```

