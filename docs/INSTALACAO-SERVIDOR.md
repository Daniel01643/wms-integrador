# Instalação no servidor — serviço NSSM `Friozem-ImportacaoNotas` (Etapa 9)

Guia passo a passo para instalar o KitWmsIntegrador como **serviço** no servidor de produção
(**SRVAPPALC02**, 200.200.200.213, Windows Server 2012 R2) sob **NSSM**, com a estratégia
**validar em homologação primeiro, depois virar para produção**.

Os scripts estão em `deploy\` na raiz do repositório. Todos são idempotentes, param em erro
(`$ErrorActionPreference='Stop'`), nunca logam segredos e aceitam os caminhos-raiz por parâmetro
(defaults da casa). Os que escrevem suportam `-WhatIf`.

> Nada neste guia deve ser executado contra o servidor real sem a supervisão do operador. Os
> scripts foram validados localmente (parse + publish real); a instalação no servidor é conduzida
> pelo operador seguindo os passos abaixo.

---

## 0. Fatos do servidor (verificados)

- Host **SRVAPPALC02** (IP 200.200.200.213), Windows Server **2012 R2** (NT 6.3.9600), PowerShell 4.0/5.1.
- **.NET 8 já instalado** — mesmo assim o pacote é **self-contained** (runtime embutido) para blindar.
  Framework-dependent também seria viável (ver §9), mas não é o caminho do pacote.
- **NSSM** já existe em `C:\Friozem\Gestao_Enderecos\tools\nssm.exe` e vários serviços NSSM já rodam.
  O pacote reutiliza esse nssm.exe (parâmetro `-NssmPath`, com fallback para `deploy\tools\nssm.exe`).
- O servidor **alcança** o WMS (`friozem.assistsoftware.com.br:8888`) e o SMTP
  (`smtp.friozem.com.br:587`) — ambos confirmados.
- **TLS 1.2** no SChannel: chaves de registro **vazias** (não explicitamente habilitado). Só habilitar
  se o handshake com o WMS falhar (§6) — passo reversível, opcional e com confirmação.
- As pastas `C:\Friozem` e `C:\Friozem\importacao_notas` **ainda não existem** (a instalação cria).
- Nome do serviço: **`Friozem-ImportacaoNotas`**. Raiz: **`C:\Friozem\importacao_notas`**.

---

## 1. Estrutura de diretórios criada no servidor

```
C:\Friozem\importacao_notas\
├── app\
│   ├── releases\<versao>\   (binários self-contained por release)
│   └── current\             (cópia da release ativa; o NSSM aponta aqui)
├── config\                  (FORA do git, ACL restrita — segredos e overrides)
│   ├── appsettings.Production.json
│   ├── email.settings.json
│   └── destinatarios-resumo.txt
├── data\                    (conveniência; a Entrada real é share de rede)
├── logs\                    (worker.out.log / worker.err.log, rotação pelo NSSM)
├── backups\                 (zips do current anterior antes de cada virada)
└── tools\
```

A **pasta de entrada** (`Pastas:Entrada`) é um **share de rede (UNC)**, FORA desta raiz.
`Processados`, `Erro`, `Estado` e a telemetria resolvem **dentro** do share (ver `docs\OPERACAO.md`).

---

## 2. Dois modos de execução: RDP e WinRM

Todos os scripts rodam igualmente nos dois modos.

### RDP (sessão local no servidor)
Abra um PowerShell **como administrador** no servidor e execute os scripts de `deploy\` (copiados
para, por exemplo, `C:\temp\deploy\`):

```powershell
powershell -ExecutionPolicy Bypass -File C:\temp\deploy\diagnostico.ps1
```

### WinRM (de outra máquina — usuário `SRVAPPALC02\administrator`)
```powershell
$cred = Get-Credential   # usuario: SRVAPPALC02\administrator ; senha do admin local do servidor

# Script read-only (diagnóstico): pode rodar direto por -FilePath
Invoke-Command -ComputerName 200.200.200.213 -Credential $cred -FilePath C:\repo\deploy\diagnostico.ps1

# Scripts com parâmetros: copie deploy\ para o servidor e invoque lá dentro
Copy-Item C:\repo\deploy -Destination C:\temp\deploy -Recurse   # (ou via sessão: ver abaixo)
Invoke-Command -ComputerName 200.200.200.213 -Credential $cred -ScriptBlock {
    powershell -ExecutionPolicy Bypass -File C:\temp\deploy\instalar.ps1 `
        -ReleaseZip C:\temp\KitWmsIntegrador-<versao>-win-x64.zip -Versao <versao>
}
```

Para copiar arquivos via WinRM sem share, use uma sessão:
```powershell
$s = New-PSSession -ComputerName 200.200.200.213 -Credential $cred
Copy-Item -ToSession $s -Path C:\repo\deploy -Destination C:\temp\deploy -Recurse
Copy-Item -ToSession $s -Path C:\repo\artifacts\<versao>\KitWmsIntegrador-<versao>-win-x64.zip `
          -Destination C:\temp\
Invoke-Command -Session $s -ScriptBlock { powershell -ExecutionPolicy Bypass -File C:\temp\deploy\diagnostico.ps1 }
Remove-PSSession $s
```

---

## 3. Passo a passo (validação em HOMOLOG → depois produção)

### 3.1 Diagnóstico (só leitura)
```powershell
.\deploy\diagnostico.ps1
```
Confere SO, .NET, nssm, serviços NSSM, TLS 1.2, alcance WMS:8888 e SMTP:587, estrutura de pastas e
ACL de `config\`. Não altera nada. Corrija/anote os alertas antes de seguir.

### 3.2 Publicar (na MÁQUINA DE BUILD, não no servidor)
```powershell
.\deploy\publicar.ps1 -Versao 2026.01.15-1
```
Gera `artifacts\2026.01.15-1\app\` (self-contained win-x64) e o zip
`artifacts\2026.01.15-1\KitWmsIntegrador-2026.01.15-1-win-x64.zip`. Imprime caminho e tamanho.
A pasta `artifacts\` é git-ignored. (Referência: no build atual, ~75 MB em pasta / ~33 MB em zip.)

> `PublishSingleFile` **não** é o padrão (o Core usa PdfSharpCore/ImageSharp, cuja resolução de
> recursos pode falhar sob extração single-file no 2012 R2). Use `-SingleFile` só se validar a saída.

### 3.3 Copiar para o servidor
Copie `deploy\` e o `.zip` para o servidor (RDP: copie pela sessão; WinRM: `Copy-Item -ToSession`,
ver §2).

### 3.4 Instalar (no servidor; idempotente; NÃO inicia o serviço)
```powershell
.\deploy\instalar.ps1 -ReleaseZip C:\temp\KitWmsIntegrador-2026.01.15-1-win-x64.zip -Versao 2026.01.15-1
```
Cria a estrutura de pastas, expande a release em `app\releases\<versao>`, aponta `app\current`, cria/
atualiza o serviço NSSM (AppDirectory, executável, stdout/stderr em `logs\` com rotação, start
auto-delayed, reinício após falha), e aplica ACL restrita em `config\`. **Não inicia** o serviço
enquanto os segredos não existirem.

### 3.5 Configurar os segredos de HOMOLOGAÇÃO (no servidor)
```powershell
.\deploy\configurar-segredos.ps1 -Fase Homolog
```
Pede (sem eco) usuário/senha do WMS e do SMTP. Grava:
- WMS → variáveis de ambiente de **máquina** `WMS__Usuario`/`WMS__Senha` (padrão `-Escopo Maquina`;
  alternativa `-Escopo Arquivo` grava em `config\appsettings.Production.json`).
- SMTP → `config\email.settings.json` (`smtp.friozem.com.br:587`, `UsarTls=false`).
Reaplica a ACL de `config\`. Depois de gravar env de máquina, o serviço precisa ser **reiniciado**
para herdá-la.

> Também copie o molde `deploy\config-exemplo\appsettings.Production.json` para
> `C:\Friozem\importacao_notas\config\appsettings.Production.json` e ajuste `Pastas:Entrada`
> (share UNC), `Wms:BaseUrl` (homolog na validação), destinatários etc. O molde marca os pontos de
> virada com `_VIRADA`. Copie também `destinatarios-resumo.exemplo.txt` para
> `config\destinatarios-resumo.txt`.

### 3.6 (Opcional) habilitar TLS 1.2 se o handshake com o WMS falhar
Só se o diagnóstico/validação indicar falha de TLS no handshake com o WMS. É **reversível** e exige
confirmação:
```powershell
.\deploy\instalar.ps1 -ReleaseZip ... -Versao ... -HabilitarTls12 -Confirm
```
Grava em `HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Client`:
`Enabled=1`, `DisabledByDefault=0`. **Pode exigir reinício do servidor.**

Reverter (manual, se necessário):
```powershell
$k = 'HKLM:\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Client'
Remove-ItemProperty -Path $k -Name Enabled,DisabledByDefault -ErrorAction SilentlyContinue
```

### 3.7 Iniciar e validar com um lote pequeno (em HOMOLOG)
```powershell
.\deploy\servico.ps1 -Acao Start
.\deploy\servico.ps1 -Acao Status
```
Coloque **poucos** XMLs no share de entrada de homologação, acompanhe `logs\worker.out.log` e
`worker.err.log`, confira a subpasta `Processados\pacote_*`, o relatório e o e-mail de fechamento.

### 3.8 VIRAR para PRODUÇÃO (sem reinstalar)
A virada é só troca de valores de config + restart:
1. Ajuste `Wms:BaseUrl` para a URL de produção (**sem `:8888`**) em
   `config\appsettings.Production.json` (ou env `Wms__BaseUrl`).
2. Grave as credenciais de produção do WMS:
   ```powershell
   .\deploy\configurar-segredos.ps1 -Fase Producao
   ```
3. Confirme destinatários de produção (`Email:Destinatarios` e `config\destinatarios-resumo.txt`).
4. Reinicie:
   ```powershell
   .\deploy\servico.ps1 -Acao Restart
   ```

### 3.9 Nova release / rollback
```powershell
# Promover nova release (backup do current, vira, reinicia, mantém as últimas 5):
.\deploy\publicar-release.ps1 -ReleaseZip C:\temp\KitWmsIntegrador-<nova>-win-x64.zip -Versao <nova>

# Rollback para a release anterior (ou -Versao explícita):
.\deploy\rollback.ps1
.\deploy\rollback.ps1 -Versao 2026.01.15-1
```

### 3.10 Remoção
```powershell
# Remove o serviço, preserva as pastas:
.\deploy\remover.ps1
# Remove o serviço E limpa app\/logs\, preservando data\/backups\/config\ (com confirmação):
.\deploy\remover.ps1 -LimparPastas -Confirm
```

---

## 4. Conta do serviço e acesso ao share de rede

Por padrão o serviço roda como **LocalSystem** (`NT AUTHORITY\SYSTEM`). LocalSystem **pode não
alcançar um share UNC** de rede. Se a entrada for um share que exige credencial de domínio, rode o
serviço sob uma **conta de domínio** com permissão RW no share:

```powershell
# Defina a conta do serviço (exige senha):
& C:\Friozem\Gestao_Enderecos\tools\nssm.exe set Friozem-ImportacaoNotas ObjectName FRIOPART\<conta> <senha>
.\deploy\servico.ps1 -Acao Restart
```

`instalar.ps1 -ContaServico 'FRIOPART\<conta>'` inclui a conta na ACL de `config\`, mas **não** grava
a senha do `ObjectName` (segredo) — defina-a com o comando acima. Ver **DECISÃO EM ABERTO (e)**.

---

## 5. Onde vive cada configuração (persistência entre releases)

O Worker (host genérico) lê, em ordem: `appsettings.json` (na release) + `appsettings.Production.json`
+ `email.settings.json` (ao lado do exe) + variáveis de ambiente (`Secao__Chave`). A pasta `current`
é **trocada** a cada deploy, então o conteúdo persistente vive em `config\` (fora do git) e é
**copiado** para a release ativa por `instalar.ps1`/`publicar-release.ps1`/`rollback.ps1`.

- **Overrides sem segredo** (pastas/URL/flags): preferir **env de máquina** (`Pastas__Entrada`,
  `Wms__BaseUrl`, `Email__Habilitado`...) — persistem entre releases e tornam a virada trivial.
- **Segredos**: WMS em env de máquina `WMS__Usuario`/`WMS__Senha` (ou `config\appsettings.Production.json`
  com ACL); SMTP em `config\email.settings.json` (ACL). **Nunca** no `appsettings.json` versionado.

---

## 6. TLS 1.2 (resumo)

O diagnóstico só **lê** o estado do TLS 1.2 Client. A habilitação é passo **opcional e reversível**
(`instalar.ps1 -HabilitarTls12`, com confirmação) e **só** deve ser feita se o handshake com o WMS
falhar. Ver §3.6.

---

## 7. Logs e rotação

Stdout/stderr do serviço vão para `C:\Friozem\importacao_notas\logs\worker.out.log` e `worker.err.log`,
com rotação do NSSM (diária ou a cada 10 MB, append entre ciclos). Os logs de negócio e o relatório do
pacote seguem o descrito em `docs\OPERACAO.md`.

---

## 8. Verificação local já realizada (sem tocar o servidor)

- `dotnet build KitWmsIntegrador.sln -c Release` → **0 erros / 0 avisos**.
- `dotnet test KitWmsIntegrador.sln -c Release` → **313 aprovados / 9 ignorados / 0 falhas**.
- `publicar.ps1 -Versao 0.0.0-test` executado de verdade → publish self-contained win-x64 OK
  (~75 MB em pasta / ~33 MB em zip). Artefato removido depois (git-ignored).
- Parse de todos os `.ps1` de `deploy\` sem erros; `diagnostico.ps1` rodado de ponta a ponta.

Detalhes em `.agents\tasks\verificacao-etapa9.md`.

---

## 9. Alternativa framework-dependent (documentada, não é o padrão)

Como o servidor já tem .NET 8, seria possível publicar menor (framework-dependent):
```powershell
dotnet publish src\KitWmsIntegrador.Worker\KitWmsIntegrador.Worker.csproj -c Release -r win-x64 --self-contained false
```
O pacote usa **self-contained** por padrão (blindagem). Se optar por framework-dependent, o restante do
fluxo (NSSM, config, ACL) é idêntico.

---

## 10. DECISÕES EM ABERTO (para o usuário confirmar)

- **(a) Caminho UNC exato do share de entrada** de validação/produção
  (placeholder `\\SERVIDOR\share\importacao_notas_homolog` em `Pastas:Entrada`).
- **(b) URL exata do WMS de PRODUÇÃO** (sem `:8888`) — se difere da de homologação
  (hipótese `https://friozem.assistsoftware.com.br/wmsapi/api`, a confirmar).
- **(c) Credenciais de PRODUÇÃO do WMS** (hoje usamos `BLUEFOOD` de homologação; produção pode diferir).
- **(d) Lista final de destinatários** do relatório de fechamento e do resumo semanal em produção
  (hoje `ti@friozem.com.br`).
- **(e) Conta do serviço NSSM** com permissão RW no share (possível necessidade de conta de domínio
  via `nssm set Friozem-ImportacaoNotas ObjectName FRIOPART\<conta> <senha>`), caso LocalSystem não
  alcance o share UNC.
