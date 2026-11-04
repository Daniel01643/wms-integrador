# Harness de stress local e relatório de capacidade da Etapa 7 (Parte B)

O diff da Parte B adiciona um harness de carga em `tests\KitWmsIntegrador.Tests\Stress\` (6 arquivos, 953 linhas). O harness usa o Orquestrador real sobre um fake thread-safe de `IClienteWms`, com latência e plano de falhas. Os resultados foram medidos e documentados em `relatorio-stress-etapa7.md`, com estimativa de capacidade, roteiro seguro e decisões em aberto. A evidência está em `verificacao-etapa7b.md`. Nenhum código de produção mudou na Parte B, e a ausência de correções de gargalo é justificada pelas medições (custo interno de cerca de 2 ms por nota contra 80 a 90 ms de latência).

Watch for: o fake não modela rate limit, 429/5xx nem renovação de token, e o relatório declara isso (confirmed). O pico de working set é do processo inteiro e não é comparável entre linhas, e o relatório avisa (confirmed).

**Verdict**: APPROVED

## High-level view

O stress só executa com `STRESS=1`. Sem a variável, os 9 `[StressFact]` aparecem como ignorados e o `dotnet test` padrão leva cerca de 2 s. Os testes cobrem os 5 cenários pedidos, além do comparativo de paralelismo e de um cenário sem latência para isolar o custo interno.

O harness não tem `HttpClient` nem `ClienteWmsHttp`. A busca por esses termos em `Stress\*.cs` retornou vazio, e foi isso que confirmei. A integridade é verificada de verdade: cada arquivo termina em Processados ou Erro exatamente uma vez, a soma das linhas dos relatórios bate com o total, e e-mails são iguais a pacotes.

O paralelismo entre transportes escala quase linearmente (1,87x com 2, 3,35x com 4). O relatório mantém o padrão 1 e condiciona a subida à aprovação do WMS. A estimativa de capacidade foi validada contra os valores medidos (95 a 96%).

## Issues (0 bloqueantes)

<details>
<summary>Issues (2)</summary>

1. **Pico WS não comparável (possible, informativo)** — o relatório já ressalva, e o "Pico heap" é a métrica útil. Nenhuma ação necessária.
2. **Cenário 3 com produção maior que a vazão** — o relatório já explica que os pacotes seguintes acumulam. Considerar um teste futuro com taxa abaixo da vazão, se quiserem medir a latência fim a fim.

</details>

<details><summary>Details</summary>

## Gating e isolamento do stress

`StressFactAttribute` define `Skip` quando `STRESS != "1"`. Os 9 testes em `CenariosStress.cs` usam esse atributo e nenhum `[Fact]` comum. O relatório descreve uma invocação de `dotnet test` por cenário, o que é necessário para o pico por processo fazer sentido.

## Cobertura dos cinco cenários e métricas

Os cenários são: 500 XMLs em 1 pacote, 2000 XMLs, chegada contínua (5 pacotes, 600 arquivos), 1000 XMLs com 10% de erros (4 categorias x 25 em `Erro`) e relatórios de 1k, 5k e 10k linhas (94, 432 e 857 páginas). O relatório traz vazão, tempo, pico de WS e de heap, GC, tempos de HTML e PDF, tamanho do PDF, e-mails e integridade. Os números têm origem em `stress-resultados-base.jsonl` (15 linhas, uma por execução).

## Paralelismo 1 vs 2 vs 4

Seis execuções (500 limpas e 300 com erros), todas com integridade OK e concorrência máxima observada menor ou igual ao configurado. A recomendação de manter 1 é coerente com o risco que o fake não cobre.

## Relatório, capacidade e roteiro

O relatório tem a estimativa notas/hora por número de chamadas (1, 2, 3, 6, 9) e por paralelismo, com tempo de drenagem. O roteiro seguro é marcado como NÃO EXECUTADO e exige aprovação prévia, homolog, credenciais por variável de ambiente, critério de parada e reversão. A seção DECISÕES EM ABERTO tem 5 itens: SMTP, teste em homolog, tempos de produção, paralelismo e conteúdo do PDF.

## Verificação, commits e áreas intocadas

`verificacao-etapa7b.md` registra build 0/0 (Debug e Release) e testes 242 aprovados, 0 falhas e 9 ignorados. Eu não re-executei build nem testes, conforme instruído. Os commits `f6dab8d` (harness, 6 arquivos nomeados) e `49ff9db` (relatório, evidência e jsonl, 3 arquivos) não usam `git add .`. O `git diff 0853335 HEAD` em `docs/diario` e `docs/controle` está vazio. O `deck.html` modificado e os arquivos `.agents` não rastreados são anteriores a esta etapa e não entraram nos commits.

</details>

<details><summary>File map</summary>

- `Stress\StressFactAttribute.cs` — skip sem STRESS=1
- `Stress\FakeClienteWmsCarga.cs` — fake thread-safe com latência e falhas
- `Stress\AmbienteStress.cs` — montagem do ambiente de carga e integridade
- `Stress\CenariosStress.cs` — 9 cenários
- `Stress\GeradorMedido.cs`, `MedidorRecursos.cs` — medições
- `.agents\tasks\relatorio-stress-etapa7.md`, `stress-resultados-base.jsonl`, `verificacao-etapa7b.md` — evidência e relatório

Diff completo: `git diff 0853335 HEAD`.

</details>
