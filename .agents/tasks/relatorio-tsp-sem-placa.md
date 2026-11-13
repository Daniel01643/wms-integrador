# Relatório: Puxada TSP sempre sem placa

Decisão do usuário (13/11/2026): a Puxada TSP vai ao WMS com `placaVeiculo` em branco na I50.

## O que mudou

- Nova opção `I50:EnviarPlacaVeiculoTsp` (`OpcoesI50`, padrão `false`; env `I50__EnviarPlacaVeiculoTsp`).
- `ConstrutorI50.Construir/ConstruirJson` ganharam o parâmetro opcional `ehTsp` (padrão `false`). Com `ehTsp`, a placa
  segue `EnviarPlacaVeiculoTsp`; sem ele, segue `EnviarPlacaVeiculo` (comportamento atual da Puxada).
- O tipo chega ao construtor assim: `ProcessadorLote` (já tinha `Ctx.EhTsp`, que cobre ZIP e XML solto em
  `PuxadaTSP`) -> `IOrquestrador.IntegrarAsync(..., ehTsp)` -> `Orquestrador` -> `ConstrutorI50.Construir(..., ehTsp)`.
  Chamadas existentes sem o parâmetro continuam compilando e valem como Puxada.
- `appsettings.json` do Worker e `deploy/config-exemplo/appsettings.Production.json`: chave nova
  `EnviarPlacaVeiculoTsp: false` e comentário atualizado.
- Docs: `ESCOPO-PUXADA-TSP.md` (decisão 10 e seção 3.4), `OPERACAO.md` (8.3.1), `ENCERRAMENTO.md`,
  `manuais/MANUAL-TECNICO.md` (8.2 e tabela de alterações). Manuais regenerados (HTML pelo `gerar-manuais.ps1`, PDF do
  Técnico via Edge headless `--print-to-pdf`). O Manual do Usuário não cita placa (só o HTML mudou, por regeneração).

## Investigação do override por env (`$env:I50__EnviarPlacaVeiculo = 'false'`)

Não há bug no código. Verifiquei:

- `Program.cs` usa `Host.CreateApplicationBuilder` (já inclui variáveis de ambiente) e
  `AddOptions<OpcoesI50>().Bind(builder.Configuration.GetSection("I50"))`. Não há valor hard-coded nem snapshot
  lido antes do env.
- `ConstrutorI50` é o único construtor que usa a placa na I50 (I60, I40 e I10 não enviam `placaVeiculo`;
  a I40 manda `placaVeiculoPrevistoNota` sempre em branco). A I50 só é montada em `Orquestrador`.
- Teste novo `VariaveisDeAmbiente_SobrescrevemAppsettings_NoMesmoRegistroDoWorker` monta o host com o mesmo
  registro do Worker, define `I50__EnviarPlacaVeiculo=false` (contra `true` na config) e prova que o valor chega no
  construtor e o JSON sai sem placa. Também há teste de bind in-memory e do `appsettings.json` real do Worker.

Causa provável de as placas terem ido mesmo assim (não verificável daqui, a máquina do usuário não foi inspecionada):
- a variável foi definida num terminal e o `dotnet run` rodou em outro, ou o `$env:` foi definido numa sessão diferente;
- um Worker antigo (serviço Windows ou processo anterior) continuou rodando e processando a pasta de entrada;
- o serviço instalado lê `appsettings.Production.json`/env de máquina, não o `$env:` do terminal.
Para conferir: `Get-Process KitWmsIntegrador.Worker` e, no mesmo terminal, `$env:I50__EnviarPlacaVeiculo` antes do
`dotnet run`. O log da I50 mostra o `placaVeiculo` enviado.

## Testes novos

`PlacaTspConfiguracaoTests` (9): TSP sem placa por padrão; TSP sem placa mesmo com `EnviarPlacaVeiculo=true`;
PUXADA com placa quando `true` e sem placa quando `false`; PUXADA ignora `EnviarPlacaVeiculoTsp`;
`EnviarPlacaVeiculoTsp=true` faz a TSP enviar; JSON da TSP com `"placaVeiculo":""`; bind in-memory; bind por env
via host; `appsettings.json` do Worker.
`ProcessadorLoteTspTests` (2): ZIP e XML solto em `PuxadaTSP` chegam ao orquestrador com `ehTsp=true`; Puxada com `false`.

## Build e testes

- `dotnet build KitWmsIntegrador.sln -c Release`: 0 avisos, 0 erros.
- `dotnet test KitWmsIntegrador.sln -c Release`: 863 aprovados, 0 falhas, 9 ignorados (baseline 851 aprovados, 9 ignorados).

## Esforço

- Horas de IA: 1,0 h (estimativa pelo tempo da sessão do agente).
- Horas humanas: não informadas.
