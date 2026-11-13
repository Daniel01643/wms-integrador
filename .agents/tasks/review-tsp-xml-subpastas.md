# PuxadaTSP aceita XML solto e ZIP com XML em subpastas

A PuxadaTSP passa a aceitar `.xml` solto além de `.zip`. Todos os XMLs soltos de um pacote formam uma única onda `TSP-AAAAMMDD-NN` sem título, alocada pelo mesmo sequencial diário dos ZIPs e sem reaproveitamento por hash. ZIP continua com uma onda por ZIP. A leitura de XML em subpastas do ZIP já funcionava (o leitor usa só o nome do arquivo, em pasta numerada) e agora tem testes.

Watch for: nenhum item bloqueante. A evidência do coder (build 0 avisos; 851 aprovados, 9 ignorados, baseline 839 + 12 novos) foi lida e não foi reexecutada.

**Verdict**: APPROVED

## High-level view

O classificador aceita `.xml` e `.zip` na PuxadaTSP, e a varredura continua só no primeiro nível. Raiz e Puxada seguem como antes. O processador agrupa as notas TSP pelo ZIP de origem, e a chave vazia representa os XMLs soltos. Para essa chave pede-se uma onda com hash vazio, que nunca é reaproveitada, então o reprocesso consome novo sequencial.

O leitor de ZIP não foi alterado. Ele já descartava o caminho interno, bloqueava absoluto e `..`, recusava ZIP aninhado e respeitava os limites. Os testes novos cobrem subpastas aninhadas, mesmo nome em duas subpastas, traversal em subpasta e ZIP aninhado em subpasta.

<details>
<summary>Issues (1)</summary>

1. **Spot-check de dedupe de chave NF-e e exibição do caminho no relatório (possible, não bloqueante)** — a evidência cita o relatório com caminho, mas o dedupe não foi reverificado. Opcional: confirmar nos testes de subpastas.

</details>

<details>
<summary>Details</summary>

## Onda única para XMLs soltos

`RegistroOndasTsp.ObterOuCriar` só consulta e grava hash quando ele não é vazio. Com `("", "")` sempre incrementa o sequencial do dia e não persiste hash. Isso atende ao sequencial compartilhado e ao reprocesso gerando novo número. O rótulo de Origem do XML solto é PuxadaTSP com Zip vazio, o que o distingue das ondas de ZIP.

## Leitor de ZIP sem mudança de código

`LeitorZipSeguro` descarta o caminho interno e extrai em pasta numerada por entrada, então nomes iguais em subpastas diferentes não colidem. O diff não altera o leitor. A cobertura é feita por testes novos.

## Escopo, docs e higiene

Docs (ESCOPO, ARQUITETURA, OPERACAO, ROTEIRO, manuais MD/HTML/PDF) foram atualizados. O deck não mudou porque não afirmava "só ZIP". O diff não contém `*.xml` nem `*.zip`, e não toca `CONTROLE-ESFORCO.md`, `docs/controle` nem `docs/diario`. O relatório existe e traz só horas de IA (cerca de 0,7 h). Não há valores monetários.

</details>

<details>
<summary>File map</summary>

- `Pacote/ClassificadorEntrada.cs`: aceita XML na PuxadaTSP
- `Pacote/ProcessadorLote.cs`: grupo de XMLs soltos e mensagem de extensão
- Testes: `ClassificarPastasTests`, `LeitorZipSeguroTests`, `PacoteTspEndToEndTests`
- Docs e manuais regenerados
- `.agents/tasks`: relatório e verificação

Diff completo: `git diff b79d386 HEAD`.

</details>
