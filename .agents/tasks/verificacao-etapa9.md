# Verificação — ETAPA 9 (pacote de instalação + scripts PowerShell, serviço NSSM)

Execução local real na máquina de build. **Nada foi executado contra o servidor real
(200.200.200.213).** Datas/horas no fuso local da máquina de build.

## Ambiente da verificação

- PowerShell da máquina de build: `5.1.26100.9818` (Windows PowerShell). O servidor é PS 4.0/5.1,
  então os scripts foram escritos para esse dialeto (sem recursos exclusivos de PS7).
- .NET SDK 8.0.427; runtimes .NET 8 presentes.

## 1. Build

Comando:
```
dotnet build KitWmsIntegrador.sln -c Release --nologo
```
Resultado: **0 Aviso(s), 0 Erro(s)** — "Compilação com êxito". Exit 0.

## 2. Testes

Comando:
```
dotnet test KitWmsIntegrador.sln -c Release --nologo
```
Resultado: **Com falha: 0, Aprovado: 313, Ignorado: 9, Total: 322**. Exit 0.
(Bate com o baseline esperado: 313 passed + 9 skip.)

## 3. Publish self-contained (real)

Comando:
```
powershell -NoProfile -ExecutionPolicy Bypass -File deploy\publicar.ps1 -Versao 0.0.0-test
```
Interno: `dotnet publish src\KitWmsIntegrador.Worker\KitWmsIntegrador.Worker.csproj -c Release
-r win-x64 --self-contained true -o artifacts\0.0.0-test\app /p:PublishSingleFile=False`.

Resultado (exit 0):
- `artifacts\0.0.0-test\app\KitWmsIntegrador.Worker.exe` gerado (confirmado `Test-Path = True`).
- Pasta publicada: **~75,5 MB, 228 arquivos**.
- Zip de release: `artifacts\0.0.0-test\KitWmsIntegrador-0.0.0-test-win-x64.zip` — **~33,4 MB**.

A pasta `artifacts\` é git-ignored e foi **removida** após a conferência
(`Test-Path artifacts = False`). O `.csproj` do Worker **não** precisou ser alterado (o publish
self-contained funcionou com os parâmetros via linha de comando).

## 4. Sintaxe dos scripts PowerShell

Parser do PowerShell sobre todos os `deploy\*.ps1`:
```
[System.Management.Automation.Language.Parser]::ParseFile(<arquivo>, [ref]$null, [ref]$e)
```
Resultado: **0 erros** em `_comum.ps1`, `diagnostico.ps1`, `publicar.ps1`, `instalar.ps1`,
`configurar-segredos.ps1`, `servico.ps1`, `publicar-release.ps1`, `rollback.ps1`, `remover.ps1`.

PSScriptAnalyzer **não** está disponível nesta máquina (`Get-Module -ListAvailable PSScriptAnalyzer`
vazio); a validação de sintaxe foi feita pelo parser, conforme o plano.

Correções aplicadas durante a verificação (achadas pelo parse/smoke-run):
- Os scripts foram salvos em **UTF-8 com BOM** (o PS 5.1/4.0 interpreta arquivos sem BOM como ANSI,
  o que corrompia strings com acentuação/em-dash no servidor).
- Removidas expressões `(if (...) {...} else {...})` **inline** usadas como argumento — inválidas em
  runtime no Windows PowerShell 5.1 (geram "'if' não é reconhecido"). Substituídas por variáveis
  intermediárias (`$cor...`, `$enabledTxt`, `$preservadosTxt`, `$remetenteFinal` etc.). Em-dash de
  strings de código vivo trocado por hífen.

## 5. Smoke-run do diagnóstico (read-only)

```
powershell -NoProfile -ExecutionPolicy Bypass -File deploy\diagnostico.ps1 -RaizInstalacao C:\__nao_existe_etapa9
```
Rodou de ponta a ponta na máquina de build (exit 0), reportando SO/PS, runtimes .NET 8,
TLS 1.2 vazio, alcance WMS:8888 e SMTP:587 (ambos `True` a partir da build machine) e estrutura de
pastas ausente — comportamento esperado. Confirma que as correções de PS 5.1 funcionam em runtime.

## 6. JSON de exemplo

`ConvertFrom-Json` em `deploy\config-exemplo\appsettings.Production.json` e
`email.settings.example.json` → **ambos válidos**.

## Não executado localmente (depende do servidor)

`instalar.ps1`, `configurar-segredos.ps1`, `servico.ps1`, `publicar-release.ps1`, `rollback.ps1`,
`remover.ps1` — escritos para rodar no servidor (RDP e WinRM); validados por parse + smoke-run do
diagnóstico. A instalação real será conduzida depois pelo operador (ver `docs\INSTALACAO-SERVIDOR.md`).

## Decisões em aberto (ver docs\INSTALACAO-SERVIDOR.md §10)

(a) share UNC de entrada; (b) URL do WMS de produção (sem :8888); (c) credenciais de produção do WMS;
(d) destinatários de produção; (e) conta do serviço NSSM com RW no share.

---

## Iteração de revisão — correção do blocker 1 (AppExit Default)

Finding (review-etapa9.json #1, blocker): em `instalar.ps1` o parâmetro de recuperação do NSSM
estava no hashtable `$params` com a chave única `'AppExit Default'`. Ao splatar via
`& $NssmPath @Argumentos`, o NSSM recebia `AppExit Default` como UM argumento com espaço; a sintaxe
exige `AppExit` e `Default` como tokens SEPARADOS (`nssm set <svc> AppExit Default Restart`). O `set`
roda por `Invoke-Nssm` sem `-TolerarFalha` → poderia abortar a instalação ou deixar a recuperação
após falha não aplicada; e o `get` nunca casava, quebrando a idempotência.

Correção aplicada:
- `deploy\_comum.ps1` → `Set-NssmParam` ganhou o parâmetro opcional `-SubParametro`. Quando
  informado, o get/set monta `@('get'|'set', $svc, $Parametro, $SubParametro[, $Valor])` com tokens
  separados (o par `AppExit Default` deixa de virar um único argumento com espaço). Mantém a
  idempotência (get com os mesmos tokens) e o `-WhatIf`.
- `deploy\instalar.ps1` → removida a chave `'AppExit Default'` do hashtable `$params`; a recuperação
  passou a ser aplicada fora do laço com
  `Set-NssmParam ... -Parametro 'AppExit' -SubParametro 'Default' -Valor 'Restart'`.

Re-verificação (execução local real, nada contra o servidor):
1. Parse de todos os `deploy\*.ps1` (`Parser::ParseFile`) → **0 erros** nos 9 scripts.
2. `dotnet build KitWmsIntegrador.sln -c Release` → **0 Aviso(s), 0 Erro(s)**, exit 0.
3. `dotnet test KitWmsIntegrador.sln -c Release` → **Com falha: 0, Aprovado: 313, Ignorado: 9**, exit 0.
4. `powershell -File deploy\publicar.ps1 -Versao 0.0.0-test` → exit 0; exe e zip gerados
   (`...\app\KitWmsIntegrador.Worker.exe` e `KitWmsIntegrador-0.0.0-test-win-x64.zip` confirmados por
   `Test-Path = True`); pasta **~75,5 MB (228 arquivos)**, zip **~33,4 MB**; `artifacts\` removido
   após a conferência (`Test-Path artifacts = False`).

Validação no servidor (documentada, não executada aqui): após `instalar.ps1`, conferir com
`nssm get Friozem-ImportacaoNotas AppExit Default` → deve retornar `Restart`. O `.csproj` do Worker
continua intocado; nenhum código de negócio alterado.
