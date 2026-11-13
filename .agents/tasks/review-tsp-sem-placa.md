# Revisão: Puxada TSP sempre sem placa

Veredito: APROVADO. Nenhum bloqueio.

## Evidência verificada

- Commit 6244204 (único acima de origin/main). `OpcoesI50.EnviarPlacaVeiculoTsp` padrão `false`.
- `ConstrutorI50`: `ehTsp ? EnviarPlacaVeiculoTsp : EnviarPlacaVeiculo`. A Puxada normal segue `EnviarPlacaVeiculo` (padrão true), sem mudança.
- `ehTsp` chega de `ProcessadorLote` (`ctx.EhTsp`, cobre ZIP e XML solto em PuxadaTSP) a `Orquestrador` e a `ConstrutorI50`. O único ponto de produção que monta a I50 é `Orquestrador`; o overload `Onda` só serve a Puxada (default false).
- `appsettings.json` do Worker e o exemplo de Production têm a chave nova com comentário atualizado.
- Override por env: o relatório conclui que não há bug, e o teste de bind via host cobre `I50__EnviarPlacaVeiculo`. A análise de `Program.cs` está coerente com o diff, que não toca o bind.
- Testes: `PlacaTspConfiguracaoTests` (9) e `ProcessadorLoteTspTests` (2) cobrem TSP, PUXADA e `Tsp=true`.
- Build e testes (evidência registrada no relatório e no commit): Release com 0 avisos e 0 erros; 863 aprovados, 0 falhas, 9 ignorados (baseline 851/9). Não reexecutei.
- Docs e manuais atualizados (ESCOPO-PUXADA-TSP, OPERACAO, ENCERRAMENTO, MANUAL-TECNICO e HTML/PDF).
- A lista de arquivos do commit não tem `*.xml` nem `*.zip`, nem `docs/CONTROLE-ESFORCO.md`, `docs/controle/*` ou `docs/diario/*`.
- O relatório registra só horas (1,0 h de IA; humanas não informadas), sem valores monetários.

## Achados

Nenhum.
