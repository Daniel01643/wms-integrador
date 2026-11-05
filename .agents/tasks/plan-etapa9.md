# Plano de Implementação — ETAPA 9: Pacote de instalação + scripts PowerShell (serviço NSSM em produção/validação)

Projeto KitWmsIntegrador (C#/.NET 8). Solução `c:\wms-integrador\KitWmsIntegrador.sln`.
Worker em `src\KitWmsIntegrador.Worker`. Branch `main`, SEM worktree. Commits pequenos, só
arquivos nomeados (nunca `git add .`). SEM push.

Esta etapa é de EMPACOTAMENTO/DEPLOY: scripts, publish e docs. NÃO alterar código de negócio
(Orquestrador, construtores, cliente WMS; I60 continua DESLIGADA). O `.csproj` do Worker só é
ajustado se necessário para o publish self-contained.

---

## 0. Decisões de design (com rationale)

Estas decisões estão tomadas; o implementador NÃO deve re-decidir.

- **D1 — Pasta de deploy do repositório: `deploy\`** (raiz do repo, `c:\wms-integrador\deploy\`).
  Rationale: os scripts são artefato de operação de primeiro nível, análogos a `docs\`; `deploy\`
  na raiz é mais visível e curto que `scripts\deploy\`. Toda a entrega de scripts fica em
  `deploy\` e os exemplos de config em `deploy\config-exemplo\`.

- **D2 — Publish self-contained, win-x64, SEM PublishSingleFile por padrão.**
  Publicar `--self-contained true -r win-x64` para blindar contra mudanças de runtime no servidor
  (apesar do .NET 8 já instalado). NÃO usar `PublishSingleFile` por padrão: o Core depende de
  `PdfSharpCore` → `SixLabors.ImageSharp`, cujo resolvedor de fontes/recursos pode falhar sob
  extração single-file no 2012 R2; a saída em pasta é a forma segura e é o que o NSSM aponta como
  executável. O `publicar.ps1` aceita `-SingleFile` como opção explícita (validável no publish),
  mas o default é a pasta self-contained. `ReadyToRun`/trimming NÃO são usados (trimming quebra
  reflection de binding de Options/DI). Framework-dependent é documentado como viável no
  INSTALACAO-SERVIDOR.md, mas não é o caminho do pacote.

- **D3 — Separação app × config × dados.** `C:\Friozem\importacao_notas` é SÓ o app e seus
  satélites (releases, logs, backups, tools, config). A pasta de ENTRADA é um SHARE DE REDE, fora
  da raiz do app (definição do usuário). Os segredos e overrides de produção vivem em `config\`
  (fora do git, protegido por ACL) e são lidos pelo Worker via `appsettings.Production.json` e/ou
  variáveis de ambiente de máquina — **nunca** dentro da pasta de release (que é trocada a cada
  deploy). Ver D4 para como o Worker enxerga esses arquivos.

- **D4 — Como o Worker lê config persistente entre releases.** O Worker usa
  `Host.CreateApplicationBuilder`, que carrega `appsettings.json` + `appsettings.{Environment}.json`
  (Environment default = `Production`) + `email.settings.json` (ao lado do exe, opcional,
  `reloadOnChange`) + variáveis de ambiente (`Secao__Chave`). Nenhum código lê
  `BaseDirectory`/`CurrentDirectory` (verificado por grep). O content root de um app publicado é a
  pasta do exe. Estratégia escolhida, nesta ordem de precedência e simplicidade:
  1. **Overrides de pastas/URL/flags** → variáveis de ambiente de MÁQUINA (ex.
     `Pastas__Entrada`, `Wms__BaseUrl`, `Email__Habilitado`). Persistem entre releases porque não
     ficam na pasta de release. Gravadas por `instalar.ps1`/`configurar-segredos.ps1`.
  2. **Segredos** → env de máquina `WMS__Usuario`/`WMS__Senha`/`Email__Senha` (preferível), OU
     `config\appsettings.Production.json` + `config\email.settings.json` com ACL.
  3. Para que os JSON de `config\` sejam enxergados pelo exe (que procura ao lado de si), o
     `instalar.ps1` cria em cada `app\releases\<versao>` **junções/links** (ou cópia idempotente)
     `email.settings.json` e `appsettings.Production.json` apontando para os arquivos de `config\`.
     Rationale: mantém um único ponto de verdade em `config\` e não duplica segredo na release.
     Decisão de implementação: usar **cópia** de `config\` → pasta da release ativa no momento do
     `publicar-release.ps1`/`instalar.ps1` (link simbólico de arquivo exige privilégio no 2012 R2;
     cópia é idempotente e robusta). O conteúdo real continua só em `config\` (git-ignored); a
     cópia na release é descartável.
  - **Preferência operacional:** usar variáveis de ambiente de máquina para tudo que não é segredo
    de arquivo, pois torna a "virada homolog→produção" trivial (reescrever env + restart), sem
    tocar a pasta de release. O `appsettings.Production.json` de `config\` serve de alternativa
    documentada para quem prefere arquivo.

- **D5 — Estratégia validação-primeiro (homolog → produção).** O pacote instala apontando para
  HOMOLOGAÇÃO (host/credenciais/destinatários de homolog que já usamos) para validar o ciclo
  completo no servidor; a virada para produção é só troca de valores de config (env de máquina ou
  `config\*.json`) + restart, SEM reinstalar. Os scripts e docs destacam essa sequência. O
  `configurar-segredos.ps1` aceita `-Fase Homolog|Producao` para deixar explícito o que grava.

- **D6 — SMTP de validação/produção: `smtp.friozem.com.br:587`, `UsarTls=false`** (o servidor
  respondeu em 587 sem TLS, validado). Email `Habilitado=true` no pacote (homolog usa
  `ti@friozem.com.br`).

- **D7 — TLS 1.2 do SChannel.** As chaves do registro estão vazias (não explicitamente
  habilitado). `diagnostico.ps1` CHECA (só leitura) e reporta. A habilitação é passo
  DOCUMENTADO/opcional, NUNCA automática: `instalar.ps1` pode receber `-HabilitarTls12` e, mesmo
  assim, pede confirmação (`ShouldProcess`) antes de escrever o registro; sem a flag, apenas avisa.

- **D8 — NSSM reutilizado.** Default `-NssmPath C:\Friozem\Gestao_Enderecos\tools\nssm.exe`
  (confirmado existente no servidor); aceita override e, se não achar, procura `deploy\tools\nssm.exe`
  embarcado. Seguir o padrão da casa: Start auto-delayed, recuperação após falha, rotação de log.

- **D9 — Nome do serviço `Friozem-ImportacaoNotas`; raiz `C:\Friozem\importacao_notas`** (parâmetros
  com esses defaults, sobrescrevíveis).

---

## 1. Estrutura de diretórios no servidor (criada/gerida pelos scripts)

Caminhos absolutos. A ENTRADA fica FORA desta raiz (share de rede).

```
C:\Friozem\importacao_notas\            (raiz do app — SÓ o programa e satélites)
├── app\
│   ├── releases\<versao>\              (binários publicados por release; ex. 2026.11.05-1)
│   │   └── KitWmsIntegrador.Worker.exe (+ runtime self-contained + appsettings.json)
│   └── current\                        (cópia/ponteiro da release ativa; NSSM aponta aqui)
├── config\                             (FORA do git, ACL restrita)
│   ├── appsettings.Production.json     (overrides de produção/validação SEM segredos de senha)
│   ├── email.settings.json             (SMTP real + senha; ACL)
│   └── destinatarios-resumo.txt        (lista do resumo semanal)
├── data\                               (opcional; ver nota — Entrada real é share de rede)
│   └── (Estado\telemetria, etc. se data local for usada)
├── logs\                               (stdout/stderr do NSSM, com rotação)
├── backups\                            (zips de release anteriores / backup de config)
└── tools\                              (nssm.exe se embarcado; senão usa o da casa)
```

Nota sobre `data\`: a ENTRADA (`Pastas:Entrada`) é um share UNC
(`\\SERVIDOR\share\importacao_notas_homolog`, DECISÃO EM ABERTO — placeholder). `Processados`,
`Erro`, `Estado` são relativos à Entrada (resolvem dentro do share), conforme OPERACAO.md. Logo a
telemetria (`<Estado>\telemetria`) também fica no share por padrão. `data\` local só é usada se o
usuário optar por apontar `Pastas:Estado`/`Relatorio:PastaSaida` para disco local; deixar
documentado e o script cria `data\` vazia como conveniência.

---

## 2. appsettings.Production.json de exemplo (sem segredos)

Entregue em `deploy\config-exemplo\appsettings.Production.json` (versionado, SEM segredos). É o
molde copiado para `config\` no servidor. Chaves que mudam em produção/validação vs. `appsettings.json`:

- `Pastas:Entrada` = caminho UNC do share (placeholder `\\SERVIDOR\share\importacao_notas_homolog`).
  DECISÃO EM ABERTO (a).
- `Wms:BaseUrl` = **sem `:8888`**. Na VALIDAÇÃO aponta para o host de HOMOLOGAÇÃO atual; na virada
  para produção troca-se para a URL de produção sem `:8888`
  (`https://friozem.assistsoftware.com.br/wmsapi/api`). DECISÃO EM ABERTO (b).
- `Email:Habilitado=true`, `Host=smtp.friozem.com.br`, `Porta=587`, `UsarTls=false`,
  `Remetente`/`Usuario` reais; senha NÃO aqui.
- `Monitoramento:Habilitado=true`, `CaminhoDestinatarios` apontando para
  `C:\Friozem\importacao_notas\config\destinatarios-resumo.txt`.
- `I60:Habilitada=false` (reafirmado).
- `Relatorio:LogoPath` opcional apontando para a logo em `app\current` ou `config\` (deixar vazio
  se não copiar logo).

O arquivo de exemplo traz comentários `_comentario` no mesmo estilo do `appsettings.json` atual,
marcando explicitamente os pontos de virada homolog→produção e os placeholders.

---

## 3. Scripts PowerShell (pasta `deploy\`) — parâmetros e comportamento

Todos: `#requires -Version 4` compatível com PS 4.0/5.1 (servidor tem 4.0/5.1); `Set-StrictMode
-Version Latest`; `$ErrorActionPreference = 'Stop'`; funções com `[CmdletBinding(SupportsShouldProcess)]`
e `-WhatIf` onde escrevem; NUNCA logar segredos; idempotentes. Cada script aceita os caminhos-raiz
como parâmetros com os defaults de D8/D9. Comentário de cabeçalho em cada script explica execução
LOCAL (RDP) e remota (Invoke-Command/WinRM).

### 3.1 `deploy\diagnostico.ps1` — SÓ LEITURA
Parâmetros: `-RaizInstalacao` (default `C:\Friozem\importacao_notas`), `-NssmPath` (default conhecido),
`-WmsHost`/`-WmsPort`, `-SmtpHost`/`-SmtpPort`, `-ShareEntrada` (UNC opcional).
Checa e imprime (sem alterar nada): SO/versão, PS version, runtimes `.NET` (`dotnet --list-runtimes`),
presença do `nssm.exe`, serviços NSSM existentes, TLS 1.2 Client (`Enabled`/`DisabledByDefault` no
registro), alcance `WMS:443` (host sem :8888) e `SMTP:587` via `Test-NetConnection`, existência das
pastas da estrutura (raiz/app/config/logs/...), acesso de leitura/escrita ao share de entrada se
informado, e permissões (ACL) de `config\`. Retorna resumo e código de saída 0 (sempre, é
diagnóstico), destacando itens em alerta.

### 3.2 `deploy\publicar.ps1` — LOCAL (máquina de build)
Parâmetros: `-Configuracao Release`, `-Runtime win-x64`, `-SelfContained $true`, `-SingleFile:$false`,
`-SaidaStaging` (default `artifacts\<versao>`), `-Versao` (default derivada: `yyyy.MM.dd` + contador
do dia ou `git rev-parse --short HEAD`), `-Zip:$true`.
Faz `dotnet publish src\KitWmsIntegrador.Worker\KitWmsIntegrador.Worker.csproj -c Release -r win-x64
--self-contained true` para `artifacts\<versao>\app`; opcionalmente `PublishSingleFile`. Gera
`artifacts\<versao>\KitWmsIntegrador-<versao>-win-x64.zip`. Imprime caminho e tamanho do artefato.
`artifacts\` é git-ignored (ver §5). Idempotente: limpa a saída da versão antes de republicar.

### 3.3 `deploy\instalar.ps1` — roda NO servidor (idempotente)
Parâmetros: `-RaizInstalacao`, `-NomeServico Friozem-ImportacaoNotas`, `-NssmPath` (default conhecido;
fallback `deploy\tools\nssm.exe`), `-ReleaseZip` ou `-ReleaseDir`, `-Versao`, `-HabilitarTls12:$false`,
`-IniciarServico:$false`.
Comportamento: cria a estrutura de pastas (§1) se faltar; expande/copia a release para
`app\releases\<versao>`; copia os JSON de `config\` para a pasta da release (D4.3); aponta
`app\current` para a release (cópia ou junção); cria/atualiza o serviço NSSM
(`AppDirectory=app\current`, `Application=...\KitWmsIntegrador.Worker.exe`,
`AppStdout`/`AppStderr=logs\`, rotação `AppRotateFiles/AppRotateBytes`, `Start=SERVICE_DELAYED_AUTO_START`,
`AppExit Default Restart`, `AppRestartDelay`), aplicando `nssm set` de forma idempotente (lê valor
atual, só altera se diferente); configura ACL de `config\` (só Administrators + conta do serviço);
se `-HabilitarTls12` → pede confirmação (`ShouldProcess`) e grava registro TLS 1.2 Client
(`Enabled=1`,`DisabledByDefault=0`), senão só avisa. **NÃO inicia** o serviço a menos que
`-IniciarServico` E os segredos existam (checa env/arquivos); caso contrário instrui rodar
`configurar-segredos.ps1` primeiro.

### 3.4 `deploy\configurar-segredos.ps1` — roda NO servidor
Parâmetros: `-Fase Homolog|Producao` (default `Homolog`), `-RaizInstalacao`, `-NomeServico`,
`-Escopo Maquina|Arquivo` (default `Maquina` para WMS; SMTP sempre em arquivo `config\email.settings.json`),
`-WmsUsuario`/`-WmsSenha` (`[SecureString]`), `-SmtpUsuario`/`-SmtpSenha` (`[SecureString]`),
`-NaoEcoar` sempre ativo.
Lê segredos via `Read-Host -AsSecureString` se não vierem por parâmetro. Grava:
WMS `Usuario/Senha` como env de MÁQUINA `WMS__Usuario`/`WMS__Senha` (escopo `Maquina`) OU em
`config\appsettings.Production.json` (escopo `Arquivo`, com ACL); SMTP em `config\email.settings.json`
(`Host=smtp.friozem.com.br`,`Porta=587`,`UsarTls=false`,`Usuario`,`Senha`,`Remetente`,`Destinatarios`,
`Habilitado=true`). Ajusta ACL de `config\` e dos arquivos (só Administrators + conta do serviço leem).
NUNCA ecoa valores; mensagens só confirmam "gravado" por nome de chave. Deixa explícito no output
que, na virada Homolog→Produção, basta rerodar com `-Fase Producao` e os novos valores.

### 3.5 `deploy\servico.ps1` — start/stop/restart/status (servidor)
Parâmetros: `-Acao Start|Stop|Restart|Status` (obrigatório), `-NomeServico`, `-NssmPath`.
Usa `nssm start/stop/restart` e `nssm status`/`sc query`/`Get-Service` para status. Idempotente
(parar serviço já parado não falha; status sempre retorna). `Stop`/`Restart` suportam `-WhatIf`.

### 3.6 `deploy\publicar-release.ps1` e `deploy\rollback.ps1` (servidor)
`publicar-release.ps1`: `-ReleaseZip`/`-ReleaseDir`, `-Versao`, `-ManterUltimas N` (default 5),
`-RaizInstalacao`, `-NomeServico`. Expande para `app\releases\<versao>`, copia config da release,
reaponta `app\current`, reinicia o serviço; remove releases antigas além de N (preserva `data`/`backups`).
Faz backup do `current` anterior em `backups\` antes de virar.
`rollback.ps1`: `-Versao` (alvo; default = release anterior), reaponta `app\current` para a release
anterior existente e reinicia; falha claro se não houver release anterior. Ambos idempotentes e com
`-WhatIf`.

### 3.7 `deploy\remover.ps1` (servidor)
Parâmetros: `-NomeServico`, `-NssmPath`, `-RaizInstalacao`, `-LimparPastas:$false`,
`-PreservarDados:$true`. Para e remove o serviço NSSM (`nssm stop`/`nssm remove ... confirm`).
Com `-LimparPastas` (e `ShouldProcess`/confirmação) apaga `app`/`logs`, preservando `data` e
`backups` quando `-PreservarDados`. NUNCA apaga o share de entrada. Idempotente (serviço inexistente
não falha).

### 3.8 `deploy\_comum.ps1` (módulo auxiliar, dot-sourced)
Funções compartilhadas: localizar `nssm.exe` (param → default conhecido → `deploy\tools`), wrappers
idempotentes `Set-NssmParam` (lê e só altera se diferente), `Ensure-Directory`, `Set-ConfigAcl`,
`Get-VersaoPadrao`, helpers de log sem segredo. Mantém os demais scripts enxutos e consistentes.

---

## 4. Ajustes de `.csproj` (se houver)

- `src\KitWmsIntegrador.Worker\KitWmsIntegrador.Worker.csproj`: adicionar propriedades de publish
  **sem travar o build normal**: `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>` e, como default
  de publish (aplicados quando `-r win-x64`), `<SelfContained>` fica a cargo do comando do
  `publicar.ps1` (não forçar no csproj para não quebrar `dotnet build`/`dotnet test` multi-plataforma).
  Opcional e seguro: `<InvariantGlobalization>false</InvariantGlobalization>` explícito e
  `<SatelliteResourceLanguages>pt-BR;en</SatelliteResourceLanguages>` para reduzir tamanho. NÃO
  habilitar trimming nem ReadyToRun (podem quebrar DI/reflection e PdfSharpCore). Só mexer se o
  publish do `publicar.ps1` exigir; caso contrário, manter o csproj como está e passar tudo por
  linha de comando. Verificar que `dotnet build`/`dotnet test` continuam 0 erros após o ajuste.

---

## 5. Entradas de `.gitignore`

Acrescentar bloco:

```
# Etapa 9 — artefatos de publish/deploy (gerados localmente, nunca versionados)
artifacts/
deploy/tools/nssm.exe
deploy/config/           # config real com segredos, se alguém copiar p/ cá por engano
```

Já cobertos pelo `.gitignore` atual: `email.settings.json`, `destinatarios.txt`, `bin/`, `obj/`.
O `config-exemplo\` (molde SEM segredos) é VERSIONADO; o `config\` real no servidor nunca entra no repo.

---

## 6. Itens de implementação (ordenados por dependência)

- [ ] 1. Criar o módulo auxiliar `deploy\_comum.ps1` com as funções compartilhadas (localizar nssm,
      `Set-NssmParam` idempotente, `Ensure-Directory`, `Set-ConfigAcl`, `Get-VersaoPadrao`, log sem
      segredo). Base dos demais scripts.
      Files: `deploy\_comum.ps1`
      Verify: parse sem erro — `powershell -NoProfile -Command "[void][System.Management.Automation.Language.Parser]::ParseFile('deploy\_comum.ps1',[ref]$null,[ref]$e); if($e){throw $e}"` → sem exceção.

- [ ] 2. Criar `deploy\diagnostico.ps1` (só leitura) conforme §3.1, usando `_comum.ps1`.
      Files: `deploy\diagnostico.ps1`
      Verify: parse OK (mesmo comando do item 1 no arquivo); execução local de fumaça
      `pwsh deploy\diagnostico.ps1 -WhatIf` roda sem alterar nada (as checagens de servidor podem
      reportar ausências — esperado na máquina de build).

- [ ] 3. Criar `deploy\publicar.ps1` conforme §3.2 (self-contained win-x64, pasta por padrão, zip,
      versão derivada).
      Files: `deploy\publicar.ps1`
      Verify: parse OK; e verificação real no item 11 (publish de verdade).

- [ ] 4. Criar `deploy\instalar.ps1` conforme §3.3 (estrutura de pastas, cópia da release, current,
      serviço NSSM idempotente, ACL de config, TLS opcional com confirmação, não inicia sem segredo).
      Files: `deploy\instalar.ps1`
      Verify: parse OK (não executar — depende do servidor).

- [ ] 5. Criar `deploy\configurar-segredos.ps1` conforme §3.4 (SecureString, env de máquina p/ WMS,
      `config\email.settings.json` p/ SMTP 587 sem TLS, ACL, sem ecoar; `-Fase Homolog|Producao`).
      Files: `deploy\configurar-segredos.ps1`
      Verify: parse OK; revisão manual de que nenhum `Write-*`/log imprime o valor do segredo.

- [ ] 6. Criar `deploy\servico.ps1` conforme §3.5 (start/stop/restart/status idempotente).
      Files: `deploy\servico.ps1`
      Verify: parse OK.

- [ ] 7. Criar `deploy\publicar-release.ps1` e `deploy\rollback.ps1` conforme §3.6 (promover/rollback,
      manter últimas N, backup do current, reinício).
      Files: `deploy\publicar-release.ps1`, `deploy\rollback.ps1`
      Verify: parse OK dos dois.

- [ ] 8. Criar `deploy\remover.ps1` conforme §3.7 (remove serviço, limpeza opcional confirmada,
      preserva data/backups).
      Files: `deploy\remover.ps1`
      Verify: parse OK.

- [ ] 9. Criar o exemplo de config de produção/validação `deploy\config-exemplo\appsettings.Production.json`
      (SEM segredos, com comentários de virada homolog→produção, placeholders de share UNC e BaseUrl
      sem :8888) e, por conveniência, copiar os moldes `email.settings.example.json` e
      `destinatarios-resumo.exemplo.txt` para `deploy\config-exemplo\` com nota de uso.
      Files: `deploy\config-exemplo\appsettings.Production.json`,
      `deploy\config-exemplo\email.settings.example.json`,
      `deploy\config-exemplo\destinatarios-resumo.exemplo.txt`
      Verify: `Get-Content ... | ConvertFrom-Json` (do appsettings) não lança — JSON válido.

- [ ] 10. Ajustar `.gitignore` (bloco §5) e, SE necessário para o publish, o `.csproj` do Worker
      (§4 — apenas `RuntimeIdentifiers`/propriedades seguras; sem trimming/R2R). Se o publish do
      item 11 funcionar sem tocar o csproj, não alterá-lo.
      Files: `.gitignore`, (cond.) `src\KitWmsIntegrador.Worker\KitWmsIntegrador.Worker.csproj`
      Verify: `dotnet build KitWmsIntegrador.sln -c Release` → 0 erros; `dotnet test KitWmsIntegrador.sln`
      → verde (hoje 313 passed + 9 skip).

- [ ] 11. Executar o `publicar.ps1` de verdade na máquina de build (self-contained win-x64, pasta
      + zip) para confirmar que o publish funciona e gera o artefato; reportar caminho e tamanho.
      Testar também, se viável, `-SingleFile` e registrar se funciona ou se há a ressalva do
      ImageSharp (D2).
      Files: (gera `artifacts\<versao>\` — git-ignored; limpar ao final)
      Verify: `pwsh deploy\publicar.ps1 -Versao <v>` conclui com exit 0, existe
      `artifacts\<v>\app\KitWmsIntegrador.Worker.exe` e o `.zip`; imprimir `Length`/tamanho.
      Limpar `artifacts\` após conferir (está no .gitignore de qualquer forma).

- [ ] 12. Validar sintaxe de TODOS os `.ps1` de uma vez (parser do PowerShell; e PSScriptAnalyzer se
      disponível) e corrigir o que acusar.
      Files: (nenhum novo)
      Verify: loop `Parser::ParseFile` sobre `deploy\*.ps1` sem erros; se `Get-Module -ListAvailable
      PSScriptAnalyzer` existir, `Invoke-ScriptAnalyzer deploy\*.ps1` sem erros de severidade Error.

- [ ] 13. Criar `docs\INSTALACAO-SERVIDOR.md` (passo a passo completo RDP e WinRM): pré-requisitos,
      diagnóstico, publicar, copiar ao servidor, instalar, configurar segredos (fase Homolog),
      habilitar TLS 1.2 se o handshake com o WMS falhar (passo reversível, opcional, com confirmação),
      iniciar, validar com um lote pequeno em HOMOLOG, VIRAR para produção (trocar BaseUrl sem :8888,
      credenciais, destinatários — sem reinstalar), rollback e remoção. Documentar o ponto de atenção
      da conta do serviço NSSM e o acesso ao SHARE DE REDE (possível `nssm set <svc> ObjectName`
      com conta de domínio com permissão RW no share). Destacar a sequência "validar em homolog no
      servidor → depois virar para produção".
      Files: `docs\INSTALACAO-SERVIDOR.md`
      Verify: revisão manual — todos os scripts de §3 referenciados com os parâmetros corretos; os
      dois modos (RDP e WinRM/`Invoke-Command`) presentes; decisões em aberto listadas.

- [ ] 14. Atualizar `docs\OPERACAO.md` se necessário: nota curta ligando a Etapa 9 (serviço NSSM
      `Friozem-ImportacaoNotas`, raiz `C:\Friozem\importacao_notas`, entrada em share de rede) e
      referência ao INSTALACAO-SERVIDOR.md. Não duplicar conteúdo; só o link/ponteiro.
      Files: `docs\OPERACAO.md`
      Verify: revisão manual — nota presente e coerente com OPERACAO.md atual.

- [ ] 15. Commits pequenos por arquivos nomeados (ver §7), na `main`, SEM push. Garantir que
      `config\` real, segredos e `artifacts\` não entram em nenhum commit.
      Files: (vários, nomeados)
      Verify: `git log --oneline -n 10` mostra a sequência; `git status` limpo; `git show --stat`
      de cada commit não lista segredos nem `artifacts/`.

---

## 7. Sequência de commits (pequenos, só arquivos nomeados, sem `git add .`)

1. `deploy: modulo auxiliar _comum.ps1 (helpers NSSM/ACL/versao)` → `deploy\_comum.ps1`
2. `deploy: script de diagnostico (so leitura)` → `deploy\diagnostico.ps1`
3. `deploy: script de publicacao self-contained win-x64` → `deploy\publicar.ps1`
4. `deploy: script de instalacao idempotente do servico NSSM` → `deploy\instalar.ps1`
5. `deploy: script de configuracao de segredos (WMS/SMTP, ACL)` → `deploy\configurar-segredos.ps1`
6. `deploy: scripts de servico/release/rollback/remocao` →
   `deploy\servico.ps1`, `deploy\publicar-release.ps1`, `deploy\rollback.ps1`, `deploy\remover.ps1`
7. `deploy: exemplos de config de producao (sem segredos)` → `deploy\config-exemplo\*`
8. `chore: gitignore artifacts/deploy + ajuste csproj p/ publish (se houve)` →
   `.gitignore` (+ csproj se alterado)
9. `docs: guia INSTALACAO-SERVIDOR (RDP e WinRM)` → `docs\INSTALACAO-SERVIDOR.md`
10. `docs: nota da Etapa 9 em OPERACAO` → `docs\OPERACAO.md` (se alterado)

(O item 6 agrupa quatro scripts do mesmo padrão num commit coeso; os demais são um por commit.)

---

## 8. Estratégia de verificação local (sem tocar o servidor real)

1. `dotnet build KitWmsIntegrador.sln -c Release` → **0 erros / 0 warnings** esperados.
2. `dotnet test KitWmsIntegrador.sln` → **verde** (baseline 313 passed + 9 skip).
3. `pwsh deploy\publicar.ps1 -Versao <v>` → publish self-contained win-x64 real; confere pasta
   `artifacts\<v>\app\KitWmsIntegrador.Worker.exe` + `.zip`; reporta caminho e tamanho; limpa depois.
4. Validação de sintaxe de todos os `.ps1` via `[System.Management.Automation.Language.Parser]::ParseFile`
   (e `Invoke-ScriptAnalyzer` se o módulo existir). NÃO executar `instalar.ps1`/`servico.ps1`/
   `remover.ps1`/`publicar-release.ps1`/`rollback.ps1` localmente (dependem do servidor); eles são
   escritos para rodar no servidor (RDP) e via WinRM.

NÃO executar nada contra 200.200.200.213 neste workflow. A instalação real será conduzida depois
pelo orquestrador com o usuário.

---

## 9. DECISÕES EM ABERTO (listar ao usuário ao final da implementação)

- (a) **Caminho UNC exato do share de entrada** de validação/produção (ex.
  `\\SERVIDOR\share\importacao_notas_homolog`). Placeholder deixado em `Pastas:Entrada`.
- (b) **URL exata do WMS de PRODUÇÃO** (sem `:8888`). A validação usa o host/credenciais de
  HOMOLOGAÇÃO; produção é `https://friozem.assistsoftware.com.br/wmsapi/api` a confirmar.
- (c) **Credenciais de PRODUÇÃO do WMS** (hoje usamos BLUEFOOD de homolog). Trocar na virada.
- (d) **Lista final de destinatários** do relatório de pacote e do resumo semanal em produção
  (hoje `ti@friozem.com.br` de homolog).
- (e) **Conta do serviço NSSM** com permissão RW no share de rede (possível necessidade de conta de
  domínio via `nssm set Friozem-ImportacaoNotas ObjectName <conta> <senha>`), caso LocalSystem não
  alcance o share UNC.

---

## 10. Nota sobre decomposição do workflow

Este trabalho NÃO foi decomposto em features FEAT separadas: é uma unidade coesa de
empacotamento/deploy em que todos os scripts dependem das mesmas convenções (estrutura de pastas,
nome do serviço, caminhos de config, carregamento de config do Worker) e compartilham uma única
sequência de commits e uma única verificação (build+test+publish). Separar em FEATs criaria seams
artificiais. O loop existente (coder → reviewer) implementa este plano; o revisor é SEMPRE o último
passo e dá o veredito final.
