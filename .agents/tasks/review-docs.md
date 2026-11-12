# Revisão da fase DOCS: Puxada e Puxada TSP

Base da revisão: commits `2863c45..HEAD` (docs técnicos, manuais, deck, encerramento, diário e esforço), contra `plano-puxada-tsp.md` (itens 10 a 12) e as decisões de 12/11/2026. Evidência: `verificacao-docs.md` mais varreduras estreitas no diff versionado. Não regenerei manuais/PDF nem reexecutei suítes.

**Verdict**: APPROVED

A fase documenta Origem (`raiz`/`Puxada`/`PuxadaTSP`), Tipo, onda `TSP-AAAAMMDD-NN-NOME`, limites de ZIP e `Estado\tsp\ondas.json` de forma coerente com o `appsettings.json` do Worker (chaves `Pastas:Puxada`, `Pastas:PuxadaTsp`, `Pastas:AceitarRaiz`, `Zip:MaxArquivos=1000`, `Zip:MaxTamanhoMb=500`). Manuais oficiais ficaram sem devolução/I40, esforço está só em horas e nenhuma hora humana foi inventada.

Watch for: nada bloqueante. Itens informativos abaixo.

## High-level view

Os docs técnicos (ARQUITETURA §2.2, OPERACAO seção 8, INSTALACAO-SERVIDOR §3.4c, ROTEIRO-TESTE-PUXADA-TSP, ESCOPO) descrevem o fluxo por pasta e as premissas abertas (`numeroPlanejamento` = onda, a validar no homolog; XML solto na PuxadaTSP = ExtensaoInvalida). As chaves e valores citados batem com o appsettings versionado.

Os manuais oficiais (usuário, técnico, agente) ganharam o fluxo de ZIP e de Tipo/Origem sem tocar em devolução. A única ocorrência de "I40|devolu" nos `.md` é `I60__QtdeVolumeZero` (falso positivo preexistente).

O deck recebeu edições cirúrgicas: slide 8 com 11/11 "Aprox. 5,5 h (2,0 h humanas + 3,5 IA)" e 12/11 "Horas humanas a informar (IA aprox. 2,5 h)"; slide 12 com a Puxada TSP em andamento e próximos passos. Sem "~", sem valores monetários.

Esforço, diário de 12/11 e ENCERRAMENTO registram 12/11 com horas humanas "a informar pelo usuário", fora dos totais humanos.

## Verificações feitas

- `git ls-files` filtrado por `.xml`/`.zip`: vazio (confirmado).
- Linhas adicionadas em `docs` com `R$|US$|custo|valor/hora|câmbio|assinatura`: nenhuma (confirmado).
- `apresentacao.html`: BOM `EF BB BF` presente; nenhum U+FFFD em qualquer `.md`/`.html` de `docs` (confirmado).
- Diff do deck contém só as linhas esperadas (caption até 12/11, linhas 11/11 e 12/11, nota de rodapé, 580 testes, dois itens da Puxada TSP).
- `Habilitada=false` mantido em I60 e Devolução no appsettings.
- Working tree de `docs` limpo; PDFs regenerados estão nos commits.

<details>
<summary>Issues (2)</summary>

1. **Fidelidade visual dos PDFs não verificada (possible, informativo)** — a verificação só contou páginas dos PDFs dos manuais; inspecionar visualmente numa próxima rodada, sem bloquear.
2. **Horário de sessão no diário (informativo)** — "09:00 às 11:30" é base da estimativa de IA (2,5 h), não horas humanas; manter rotulado como estimativa e ajustar se houver mais sessão.

</details>

<details>
<summary>Details</summary>

Nenhum achado bloqueante exige detalhamento. A numeração do total de IA (aprox. 31,90 h) e os totais mensais com "a informar" foram lidos no diff e estão consistentes entre CONTROLE-ESFORCO, SEMANAL e MENSAL (IA 12/11 = 2,5 h; humanas excluídas dos totais).

</details>

<details>
<summary>File map</summary>

docs/ARQUITETURA.md, OPERACAO.md, INSTALACAO-SERVIDOR.md, ESCOPO-PUXADA-TSP.md, ROTEIRO-TESTE-PUXADA-TSP.md (novo), ENCERRAMENTO.md, diario/2026-11-12.md (novo), CONTROLE-ESFORCO.md, controle/SEMANAL.md, controle/MENSAL.md, manuais/*.md/html/pdf, apresentacao/apresentacao.html e .pdf. Diff completo: `git diff 2863c45..HEAD -- docs`.

</details>
