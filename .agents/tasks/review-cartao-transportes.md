# Cartão "Transportes" conta transportes distintos de todas as notas

`ModeloRelatorio.Montar` deixa de usar `gruposPuxada.Count` e passa a contar números de transporte distintos de todas as notas (PUXADA, TSP, raiz), em qualquer status, ignorando vazio e os rótulos "sem transporte". Isso corrige o cartão zerado em pacote só TSP. Watch for: nada bloqueante.

**Verdict**: APPROVED

## High-level view

A causa era o cartão ler apenas os grupos PUXADA. O commit 267a0e1 troca por uma contagem distinta sobre todas as linhas. HTML (`RenderizadorHtml`), PDF (`RenderizadorPdf`) e e-mail (`CorpoEmailResumo`) leem o mesmo `Resumo.Transportes`, então os três ficam corrigidos com uma única alteração (confirmed por grep). A telemetria também usa `resumo.Transportes`, e passa a refletir o novo valor.

<details>
<summary>Issues (2)</summary>

1. **Linhas de arquivo ignorado entram na contagem se tiverem transporte** — possible; `ordenadas` inclui linhas com `ArquivoIgnorado`. Se alguma tiver `NumeroTransporte` preenchido, será contada. Baixo impacto, e coerente com "todas as notas".
2. **Evidência de build/testes feita em cópia em %TEMP%** — o Worker trava a DLL; a revisão não re-executou nada, conforme instruído. O relatório do coder traz 655 aprovados e 0 avisos.

</details>

<details>
<summary>Details</summary>

## Contagem e cobertura dos três canais

A nova expressão usa `Distinct(StringComparer.Ordinal)` e descarta `RotuloSemTransporte` e `RotuloSemTransporteTsp`. Não depende de `EhTsp` nem de status. `NotasPorTransporte` continua baseado em `gruposPuxada`, então a seção por transporte não muda. A contagem por onda (`g.Transportes`) permanece independente, e o teste ajustado confirma 8 = 2 PUXADA + 4 + 2 TSP.

## Teste de regressão

O teste novo `Modelo_PacoteSoTsp_NaoIntegrado_CartaoTransportesContaTransportesDistintos` monta um pacote só TSP, com notas Pendente e não integradas e um transporte repetido (1000000011 duas vezes). Espera 6 no modelo e `<b>6</b>Transportes</div>` no HTML. Não testados: PDF e e-mail diretamente. Como compartilham `Resumo.Transportes`, o risco é baixo.

## Higiene do commit

O commit tem 2 arquivos (`ModeloRelatorio.cs` e `RelatorioTipoOrigemTests.cs`). Nenhum `*.xml`/`*.zip`, relatório de exemplo, `docs/CONTROLE-ESFORCO.md`, `docs/controle/*` ou `docs/diario/*`. O `git status` mostra só `.agents/tasks/*` não rastreados, sem commit. `main` está 1 à frente de `origin/main`, sem push.

</details>

<details>
<summary>File map</summary>

- `src/KitWmsIntegrador.Core/Relatorio/ModeloRelatorio.cs`: contagem distinta de transportes
- `tests/KitWmsIntegrador.Tests/RelatorioTipoOrigemTests.cs`: teste novo e expectativa ajustada (2 para 8)

Diff completo: `git show 267a0e1`.

</details>
