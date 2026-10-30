# Briefing — Integrador WMS (SolWMS Assist)

Documento de acordo inicial. Resume o problema, as regras de negócio decididas e o
contrato da API, para servir de base à arquitetura e à implementação.

## Problema

Hoje a entrada de notas no WMS é manual: o colaborador abre uma tela do WMS,
seleciona ~50 XMLs e clica em importar. O processo trava/demora (~10 min para 50
arquivos, ~12s por XML) e não há visibilidade do que entrou ou falhou. Depois, o
colaborador ainda monta a onda manualmente (I54) a partir de uma planilha.

## Objetivo

Um **agente autônomo** que lê a pasta de entrada, cruza XML × planilha e integra ao
WMS via API REST/JSON, criando a onda automaticamente — **sem deixar o colaborador
esperando nem fazendo trabalho manual**. Com controle do que entrou/falhou, reenvio
do que faltou e alertas por e-mail em exceções.

## Regras de negócio decididas

- **Fluxo**: cruzar XML × planilha → gerar **I50** (DocCarga) já com a onda embutida →
  depois enviar **I60** (notas) referenciando o DocCarga para associar.
- **Ordem**: I50 **sempre antes** da I60.
- **Onda**: criada pelo campo `nroagrupseparacao` na I50. Embarque opcional via
  `numeroEmbarque`.
- **Agrupamento da onda**: vem da coluna **"No. Transporte"** da planilha.
- **Cruzamento**: por **número da nota** (número da nota e doccarga podem coincidir).
- **1 DocCarga para N notas**: cria um único DocCarga (I50) com N notas e depois envia
  as notas (I60) separadas, cada uma referenciando esse DocCarga. A quantidade de
  produtos precisa bater.
- **Alertas por e-mail**: sim; quais exceções disparam alerta será definido depois.

## Interfaces WMS (do manual + collection Postman)

- **I60** = nota de venda (`NOTAS_VENDA`). Hoje o XML equivale à I60. A I60 já traz
  comandos de associação do WMS (ex.: `pedidos: ["..."]`, `chavenfe`).
- **I50** = pedido/DocCarga (`PEDIDO`). O campo `nroagrupseparacao` cria a onda
  automaticamente; `numeroEmbarque` pode criar embarque.
- **I54** = roteirização/sequenciamento (`ROTERIZACAO`). Hoje montada à mão; no novo
  fluxo a onda nasce da I50, então a I54 tende a não ser necessária na Fase inicial.

### Contrato da API (REST/JSON)

- **Token**: `GET https://<host>:8888/wmsapi/api/Integracao/GetToken` com **Basic Auth**
  (usuário/senha) → retorna um **JWT**.
- **Envio**: `POST .../wmsapi/api/integracao/Post` com **Bearer token**. Query string
  muda só o **IDENTIFICADOR**:
  - `WEBAPI-I60-Json` → NOTAS_VENDA
  - `WEBAPI-I50-Json` → PEDIDO
  - `WEBAPI-I54-Json` → ROTERIZACAO
- Mesmo endpoint e mesmos parâmetros `REQUISITANTE`/`DESTINO`; muda o corpo e o
  identificador. Um único cliente HTTP cobre todas as interfaces.
- **Amarração provável**: `numeroPedido` (I50) ↔ `pedidos[]` (I60); a onda vem de
  `nroagrupseparacao` (I50).
- Host de homologação observado na collection: `friozem.assistsoftware.com.br:8888`.

### Infraestrutura

- Endpoint **único**, com **3 servidores balanceados por trás** do endpoint. Portanto o
  ganho de velocidade NÃO vem de distribuir entre os 3 servidores manualmente — vem de
  (a) fugir da tela que trava e (b) enviar requisições **concorrentes**, deixando o
  balanceador distribuir.

## Decisões de organização

- **Código separado dos dados**:
  - `C:\wms-integrador\` → projeto (código, docs, git) = este workspace.
  - `C:\Envia xml\` → dados de entrada (XML + planilhas), apontado por configuração
    (`.env`), não por estar fisicamente dentro do projeto.
- Isso evita misturar fonte com dados, impede o agente de reprocessar os próprios
  arquivos e permite versionar só o projeto.

## Diagnóstico de ganho (honesto)

- **Controle/confiabilidade**: ganho certo e grande (saber o que entrou/falhou,
  reenviar só o que faltou, nunca perder o ponto quando trava).
- **Velocidade**: ganho provável, mas precisa medir 1 chamada real no homolog — os
  ~12s/arquivo podem ser pós-processamento interno do WMS, não transporte. A API
  permite concorrência, que a tela não faz.
- **Onda a partir da planilha**: maior salto de produtividade — elimina a montagem
  manual da onda pelo colaborador.

## Pendências / decisões em aberto

- Validar credenciais de Basic Auth (o usuário tem algumas, precisa validar).
- Cronometrar uma chamada I50/I60 real no homolog para confirmar o gargalo.
- Confirmar amarração exata I50↔I60 (`numeroPedido` ↔ `pedidos[]`).
- Concorrência (quantas requisições simultâneas).
- Onde guardar o estado/controle (arquivo, SQLite, etc.).
- Política de idempotência e retry.
- Quais exceções geram e-mail.
- Estrutura real da planilha (colunas) e dos XMLs em `C:\Envia xml`.
