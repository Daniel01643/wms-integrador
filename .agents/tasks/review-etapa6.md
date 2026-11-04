# Orquestrador reativo de integração com I10/I30 como pré-condição

Etapa 6 adiciona o orquestrador por nota (até 3 tentativas), o construtor I30 (todos os itens), o classificador por texto real do WMS e as colunas Integrada/Tentativas no relatório. O código bate com o plano e com os critérios dos FEAT. `dotnet test` em Debug: 109/109 verdes (confirmado, rodado nesta revisão).

Watch for: (possible) o resultado das pré-condições I10/I30 não é checado com `PreCondicaoOk`; (confirmed) nota integrada após I30 sai com `Integrada=true` e `Estado=Pendente`.

**Verdict**: APPROVED

## High-level view

O algoritmo reativo respeita o limite de 3 tentativas, sem loop e sem travar o lote: pendência e erro desconhecido retornam na hora, e o esgotamento vira NaoIntegrada com contagem e motivo. A I60 só é enviada com `OpcoesI60.Habilitada` verdadeiro. Os testes usam fake de `IClienteWms` (sem HTTP real, sem segredos). O classificador usa os trechos reais de TESTE-COMUNICACAO.md.

<details>
<summary>Issues (2)</summary>

1. **Pré-condição sem checagem (possible, não bloqueante)** — `Orquestrador.cs` ignora `PreCondicaoOk` nas respostas de I10/I30 e reenvia a I50 sempre; falha real da I10/I30 só aparece no Historico.
2. **Estado híbrido (confirmed, não bloqueante)** — nota integrada após I30 retorna `Integrada=true` com `Estado=Pendente`; consumidores do relatório devem usar `Integrada` para S/N.

</details>

<details>
<summary>Details</summary>

## Pré-condições no loop de tentativas

Cada reenvio da I50 consome uma tentativa. Se a 3ª tentativa cair em I10/I30, o loop termina sem reenviar a I50 e a nota vira NaoIntegrada, o que é coerente com a regra confirmada. A I30 vai uma vez com todos os itens da nota, como pedido.

## Pendência de revisão

O produto provisório fica marcado no Historico e no Estado final (`Pendente` com `Integrada=true`). Isso atende o requisito, mas mistura dois significados de Pendente.

</details>

<details>
<summary>File map</summary>

Orquestrador/*: orquestrador, classificador, estado e resultado. Construtores/*I30*: construtor e DTO. Relatorio/*: colunas Integrada/Tentativas. Worker/Program.cs: DI. Tests: classificador, I30, orquestrador, fake. Diff: `git diff 6aa1115 028865d`.

</details>
