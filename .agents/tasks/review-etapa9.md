# Revisão — ETAPA 9 (pacote de instalação + scripts PowerShell, serviço NSSM) — Passo 2

Escopo revisado: diff da `main` da Etapa 9 contra `.agents\tasks\plan-etapa9.md`, com foco na
iteração de correção após o passo 1. HEAD atual `d774de5`; commits da etapa
`e596e59..d774de5`. A correção do blocker vive em `0a23a01`
(`fix: aplicar AppExit Default Restart do NSSM com tokens separados`). Evidência de verificação
lida em `.agents\tasks\verificacao-etapa9.md`. Não re-executei build/test/publish; não toquei o
servidor real (200.200.200.213).

## Estado do passo anterior

O passo 1 aprovou todos os critérios da etapa (scripts presentes e idempotentes, caminhos
absolutos, publish self-contained win-x64, segurança/segredos, TLS 1.2 só-leitura no diagnóstico,
SMTP 587 sem TLS, docs RDP+WinRM com decisões em aberto, commits pequenos sem push/segredos/artifacts)
exceto por UM blocker:

- Blocker 1 — `instalar.ps1`: a chave de hashtable `'AppExit Default'` virava um único argumento
  com espaço ao splatar para o NSSM; a sintaxe exige `AppExit` e `Default` como tokens separados.
  Consequência: instalação podia abortar (exceção do `Invoke-Nssm`) ou deixar a recuperação após
  falha não aplicada, além de quebrar a idempotência (o `get` nunca casava).

## Verificação da correção do blocker 1

Commit `0a23a01` corrige exatamente o ponto apontado, confirmado no código vivo:

- `deploy\_comum.ps1` → `Set-NssmParam` ganhou o parâmetro opcional `-SubParametro`. Quando
  informado, `$argsGet`/`$argsSet` são montados com tokens SEPARADOS
  (`@('get',$svc,'AppExit','Default')` e `@('set',$svc,'AppExit','Default',$Valor)`). O rótulo de
  log também é composto. A idempotência é mantida (o `get` agora usa os mesmos tokens separados e
  casa com `Restart` em execuções seguintes) e o `-WhatIf`/`ShouldProcess` é preservado.
- `deploy\instalar.ps1` → a chave `'AppExit Default'` foi removida do hashtable `$params` e a
  recuperação após falha passou a ser aplicada FORA do laço com
  `Set-NssmParam ... -Parametro 'AppExit' -SubParametro 'Default' -Valor 'Restart'`, com comentário
  explicando a razão e o comando de confirmação (`nssm get <servico> AppExit Default`).

O requisito D8/§3.3 (recuperação automática após falha) volta a ser atendido de forma idempotente.
Nenhum código de negócio foi tocado; o `.csproj` do Worker continua intocado.

## Evidência de verificação (lida, não re-executada)

Seção "Iteração de revisão — correção do blocker 1" de `verificacao-etapa9.md` registra a
re-verificação após a correção:

- Parse de todos os `deploy\*.ps1` (`Parser::ParseFile`) → 0 erros nos 9 scripts.
- `dotnet build KitWmsIntegrador.sln -c Release` → 0 avisos / 0 erros, exit 0.
- `dotnet test KitWmsIntegrador.sln -c Release` → 0 falhas / 313 aprovados / 9 ignorados, exit 0.
- `publicar.ps1 -Versao 0.0.0-test` → exit 0; exe e zip gerados (`KitWmsIntegrador.Worker.exe` e
  `KitWmsIntegrador-0.0.0-test-win-x64.zip`); pasta ~75,5 MB / zip ~33,4 MB; `artifacts\` removido
  após a conferência.

A evidência exigida está presente e cobre os caminhos afetados pela correção (parse + publish),
além de build e testes. Nenhuma execução contra o servidor real.

## Git

`git status` limpo (só os arquivos de review não rastreados). Commits pequenos por arquivos
nomeados; nenhum segredo nem `artifacts/` rastreado.

## Blockers

Nenhum. O blocker 1 do passo anterior foi corrigido e re-verificado; nenhum novo blocker material
foi identificado nos caminhos afetados nem no restante do diff.

## Observação não-bloqueante (sem ação nesta etapa)

- `diagnostico.ps1` testa o WMS em `:8888` por default enquanto o molde usa BaseUrl sem `:8888`
  (DECISÃO EM ABERTO (b)). É um teste de alcance parametrizável, não falha de correção; já listado
  como decisão em aberto. Mantido.

## Conclusão

A correção do único blocker está aplicada e verificada; todos os demais critérios da etapa
permanecem atendidos e a evidência de verificação exigida está presente. Veredito: APPROVED.
