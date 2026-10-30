# Teste de comunicação com o WMS (homolog) — 2026-10-30

Teste feito **antes de escrever o agente**, para medir o tempo real de comunicação por
nota e validar o contrato, conforme pedido. Ambiente: homolog
`friozem.assistsoftware.com.br:8888`, usuário `BLUEFOOD`.

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
