# Instalação no servidor — serviço NSSM `Friozem-ImportacaoNotas` (Etapa 9)

Guia passo a passo para instalar o KitWmsIntegrador como **serviço** no servidor de produção
(**SRVAPPALC02**, 200.200.200.213, Windows Server 2012 R2) sob **NSSM**, com a estratégia
**validar em homologação primeiro, depois virar para produção**.

Os scripts estão em `deploy\` na raiz do repositório. Todos são idempotentes, param em erro
(`$ErrorActionPreference='Stop'`), nunca logam segredos e aceitam os caminhos-raiz por parâmetro
(defaults da casa). Os que escrevem suportam `-WhatIf`.

> **Servidor autossuficiente (operação por RDP).** A partir da **1ª instalação**, `instalar.ps1`
> **replica** todos os `.ps1` de `deploy\` (inclusive `_comum.ps1`) para a pasta **permanente**
> **`C:\Friozem\importacao_notas\tools\deploy`** no servidor — essa pasta passa a ser a **fonte de
> verdade** dos scripts de operação no servidor. Depois da produção, o técnico abre um PowerShell
> **como Administrador no próprio servidor** (RDP), vai até essa pasta e opera dali
> (`servico.ps1`, `publicar-release.ps1`, `rollback.ps1`, `configurar-segredos.ps1`,
> `diagnostico.ps1`, `compartilhar-pasta.ps1`, `limpar-extensao-invalida.ps1`, `remover.ps1`) —
> **sem** depender de `C:\temp` nem
> de WinRM da máquina de build. `C:\temp\deploy` é usado **apenas no primeiro install** (quando a
> pasta ainda não existe no servidor). Como os scripts fazem dot-source de `_comum.ps1` por
> `$PSScriptRoot` e resolvem o `nssm.exe` por parâmetro/fallback, rodar de `tools\deploy` funciona
> sem mudança. `publicar-release.ps1` também atualiza esses scripts ao promover uma release, para
> acompanharem a versão. Só a subpasta `tools\deploy` é gerenciada; o resto de `tools\` (ex.
> `nssm.exe` referenciado de outro lugar) não é tocado.

> Nada neste guia deve ser executado contra o servidor real sem a supervisão do operador. Os
> scripts foram validados localmente (parse + publish real); a instalação no servidor é conduzida
> pelo operador seguindo os passos abaixo.

---

## 0. Fatos do servidor (verificados)

- Host **SRVAPPALC02** (IP 200.200.200.213), Windows Server **2012 R2** (NT 6.3.9600), PowerShell 4.0/5.1.
  - **Compatibilidade PS 4.0**: os cmdlets `Expand-Archive`/`Compress-Archive` **só existem no
    PowerShell 5.0+**. Os scripts que rodam no servidor (`instalar.ps1`, `publicar-release.ps1`)
    usam os helpers portáveis `Expand-ZipPortavel`/`Compress-ZipPortavel` de `deploy\_comum.ps1`,
    baseados em `System.IO.Compression` do .NET (presente no Server 2012 R2). O layout do zip é o
    mesmo (conteúdo na raiz), compatível com os `.zip` gerados por `publicar.ps1`.
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
C:\Friozem\importacao_notas\          (APP — binários, config, logs)
├── app\
│   ├── releases\<versao>\   (binários self-contained por release)
│   └── current\             (cópia da release ativa; o NSSM aponta aqui)
├── config\                  (FORA do git, ACL restrita — segredos e overrides)
│   ├── appsettings.Production.json
│   ├── email.settings.json
│   ├── destinatarios-resumo.txt
│   └── destinatarios-fechamento.txt
├── data\                    (conveniência; a Entrada real fica em C:\Friozem\kitbrf)
├── logs\                    (worker.out.log / worker.err.log, rotação pelo NSSM)
├── backups\                 (zips do current anterior antes de cada virada)
├── manuais\                 (cópia dos manuais .md/.html/.pdf para consulta do técnico por RDP)
└── tools\
    └── deploy\              (FONTE DE VERDADE dos scripts .ps1 de operação no servidor — RDP)

C:\Friozem\kitbrf\                     (ENTRADA — dados; pasta LOCAL compartilhada na rede)
├── Puxada\          (ZIP ou XML de puxada normal — Tipo PUXADA)
├── PuxadaTSP\       (somente ZIP de puxada TSP — Tipo TSP)
├── (XML solto na raiz: aceito como puxada, Origem "raiz", se Pastas:AceitarRaiz=true)
├── Processados\pacote_<...>\
├── Erro\pacote_<...>\
└── Estado\  (diário + telemetria\)
```

A **pasta de entrada** (`Pastas:Entrada`) é uma **pasta LOCAL do próprio servidor**, default
`C:\Friozem\kitbrf` — separada da raiz do app. O serviço a acessa por caminho **local**, então
**LocalSystem basta** (não é UNC, não exige conta de domínio). Essa pasta é **compartilhada na
rede via SMB** (share `kitbrf`) para que os usuários depositem os ZIPs (nas subpastas `Puxada` e `PuxadaTSP`, ver §3.4c) de fora — rode
`deploy\compartilhar-pasta.ps1` (ver §3.4b). `Processados`, `Erro`, `Estado` e a telemetria
resolvem **dentro** dessa pasta (ver `docs\OPERACAO.md`).

---

## 2. Dois modos de execução: RDP e WinRM

Todos os scripts rodam igualmente nos dois modos.

### RDP (sessão local no servidor) — modo recomendado para o dia a dia
Abra um PowerShell **como administrador** no servidor.

- **Primeira instalação** (a pasta permanente ainda não existe): copie `deploy\` para
  `C:\temp\deploy\` e rode a partir dali:
  ```powershell
  powershell -ExecutionPolicy Bypass -File C:\temp\deploy\diagnostico.ps1
  ```
- **Dia a dia (após a 1ª instalação)**: os scripts já vivem em
  `C:\Friozem\importacao_notas\tools\deploy`. Opere a partir dessa pasta:
  ```powershell
  cd C:\Friozem\importacao_notas\tools\deploy
  powershell -ExecutionPolicy Bypass -File .\diagnostico.ps1
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
# 1ª instalação (ainda sem a pasta permanente): a partir de C:\temp\deploy
.\deploy\diagnostico.ps1
# Dia a dia (após instalar): a partir de C:\Friozem\importacao_notas\tools\deploy
# cd C:\Friozem\importacao_notas\tools\deploy ; .\diagnostico.ps1
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
Cria a estrutura de pastas (inclusive a pasta LOCAL de entrada `C:\Friozem\kitbrf`; as subpastas
`Processados`/`Erro`/`Estado`/`telemetria` o próprio serviço cria), expande a release em
`app\releases\<versao>`, aponta `app\current`, cria/atualiza o serviço NSSM (AppDirectory,
executável, stdout/stderr em `logs\` com rotação, start auto-delayed, reinício após falha), e
aplica ACL restrita em `config\`. **Não inicia** o serviço enquanto os segredos não existirem.
A pasta de entrada tem default `-PastaEntrada C:\Friozem\kitbrf`; passe `-CompartilharEntrada`
para já expô-la na rede (equivale a rodar o §3.4b).

Além disso, `instalar.ps1` **replica os próprios scripts de `deploy\`** (inclusive `_comum.ps1`)
para `C:\Friozem\importacao_notas\tools\deploy` — a **fonte de verdade** dos scripts de operação no
servidor, de forma idempotente (sobrescreve a cada instalação/atualização). O caminho de destino é
logado. A partir daí, **opere o dia a dia a partir de `tools\deploy`** (não mais de `C:\temp`).
Parâmetros: `-CopiarScripts $false` desliga a cópia; `-PastaScriptsDestino <pasta>` muda o destino
(default `<RaizInstalacao>\tools\deploy`). Só essa subpasta de `tools\` é gerenciada; o resto de
`tools\` não é tocado.

`instalar.ps1` **também copia os manuais** para `C:\Friozem\importacao_notas\manuais` (fora de
`app\current`), para o técnico tê-los à mão por RDP — coerente com o pacote gerado por
`publicar.ps1` (`artifacts\<versao>\manuais\` e `artifacts\<versao>\manuais.zip`). É idempotente
(sobrescreve a cada execução). Parâmetros: `-CopiarManuais $false` desliga; `-ManuaisDir <pasta>`
ou `-ManuaisZip <zip>` indicam a origem explicitamente (por padrão, procura uma pasta/zip `manuais`
irmã da release). Se a origem não for encontrada, apenas alerta e segue.

### 3.4b Compartilhar a pasta de entrada na rede (SMB)
```powershell
# A partir da pasta permanente no servidor (RDP): cd C:\Friozem\importacao_notas\tools\deploy
# Validação (gravação para Authenticated Users):
.\compartilhar-pasta.ps1
# Produção (recomendado restringir a um grupo de domínio — DECISÃO EM ABERTO (a)):
.\compartilhar-pasta.ps1 -GravadoresShare 'FRIOPART\<grupo>'
```
Cria a pasta local (se faltar), cria/atualiza o compartilhamento SMB `kitbrf` (Full para
Administradores, Change/escrita para o principal gravador) e concede `Modify` NTFS ao mesmo
principal, mantendo a herança de Administradores/SYSTEM. Idempotente, suporta `-WhatIf`. No
Server 2012 R2 usa os cmdlets `SmbShare`; se indisponíveis, cai para `net share`.

### 3.4c Criar as pastas de depósito Puxada e PuxadaTSP
Os colaboradores depositam **ZIPs** em duas subpastas do share `kitbrf`: `Puxada` (puxada normal) e
`PuxadaTSP` (puxada TSP). O serviço as cria sozinho ao iniciar, mas crie antes para já deixar o
compartilhamento pronto (PowerShell como Administrador, no servidor):
```powershell
New-Item -ItemType Directory -Force -Path 'C:\Friozem\kitbrf\Puxada','C:\Friozem\kitbrf\PuxadaTSP'
Get-ChildItem 'C:\Friozem\kitbrf' -Directory | Select-Object Name
```
Compartilhamento: as subpastas aparecem dentro do share `\\SRVAPPALC02\kitbrf` e herdam as permissões
da pasta pai (a ACL NTFS aplicada pelo `compartilhar-pasta.ps1` usa herança para pastas e arquivos),
então **não** é preciso criar outro share nem rodar o script de novo. Confirme com um usuário
gravador que ele consegue copiar um arquivo para cada subpasta. Os nomes são configuráveis em
`Pastas:Puxada` e `Pastas:PuxadaTsp`; os limites do ZIP, em `Zip:MaxArquivos` e `Zip:MaxTamanhoMb`
(detalhes em `docs\OPERACAO.md`, seção 8). O molde `deploy\config-exemplo\appsettings.Production.json`
já traz as chaves.

### 3.5 Configurar os segredos de HOMOLOGAÇÃO (no servidor)
```powershell
# A partir da pasta permanente no servidor (RDP): cd C:\Friozem\importacao_notas\tools\deploy
.\configurar-segredos.ps1 -Fase Homolog
```
Pede (sem eco) usuário/senha do WMS e do SMTP. Grava:
- WMS → variáveis de ambiente de **máquina** `WMS__Usuario`/`WMS__Senha` (padrão `-Escopo Maquina`;
  alternativa `-Escopo Arquivo` grava em `config\appsettings.Production.json`).
- SMTP → `config\email.settings.json` (`smtp.friozem.com.br:587`, `UsarTls=false`).
Reaplica a ACL de `config\`. Depois de gravar env de máquina, o serviço precisa ser **reiniciado**
para herdá-la.

> Também copie o molde `deploy\config-exemplo\appsettings.Production.json` para
> `C:\Friozem\importacao_notas\config\appsettings.Production.json`. `Pastas:Entrada` já vem com o
> default local `C:\Friozem\kitbrf` (não muda na virada); ajuste `Wms:BaseUrl` (homolog na
> validação), destinatários etc. O molde marca os pontos de virada com `_VIRADA` (apenas
> WMS/credenciais/destinatários — a pasta de entrada **não** é um deles). Copie também
> `destinatarios-resumo.exemplo.txt` para `config\destinatarios-resumo.txt` **e**
> `destinatarios-fechamento.exemplo.txt` para `config\destinatarios-fechamento.txt`, apontando
> `Monitoramento:CaminhoDestinatarios` e `Email:CaminhoDestinatarios` para eles (o molde de
> `appsettings.Production.json` já traz os dois caminhos). O txt de fechamento tem **precedência**
> sobre `Email:Destinatarios` (que vira fallback; se ambos vazios, o e-mail do pacote não é enviado).

### 3.6 (Opcional) habilitar TLS 1.2 se o handshake com o WMS falhar
**NECESSARIO em producao (licao de 06/11/2026):** o endpoint de PRODUCAO (sem `:8888`) EXIGE TLS 1.2.
No Windows Server 2012 R2 o .NET nao negocia TLS 1.2 por padrao, e o GetToken falha com
`Could not create SSL/TLS secure channel` (aparece como 401 no relatorio). O homolog (`:8888`) tolerava
protocolo antigo, por isso nao exigia. **Em producao, rode a instalacao com `-HabilitarTls12`.**
```powershell
.\instalar.ps1 -ReleaseZip ... -Versao ... -HabilitarTls12 -Confirm
```
Grava DUAS coisas:
1. SChannel: `HKLM\SYSTEM\...\SCHANNEL\Protocols\TLS 1.2\Client` (`Enabled=1`, `DisabledByDefault=0`) — pode exigir reinicio do servidor.
2. **.NET Framework** (`HKLM\SOFTWARE\Microsoft\.NETFramework\v4.0.30319` e o `WOW6432Node`): `SchUseStrongCrypto=1`, `SystemDefaultTlsVersions=1` — DECISIVAS para o Worker (.NET); valem apenas reiniciando o SERVICO (`servico.ps1 -Acao Restart`), SEM reboot da maquina.

Em producao bastou gravar essas chaves e reiniciar o servico (sem reboot do servidor).

Reverter (manual, se necessário):
```powershell
$k = 'HKLM:\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Client'
Remove-ItemProperty -Path $k -Name Enabled,DisabledByDefault -ErrorAction SilentlyContinue
# As chaves .NET (SchUseStrongCrypto/SystemDefaultTlsVersions) sao seguras; normalmente mantidas.
```

### 3.7 Iniciar e validar com um lote pequeno (em HOMOLOG)
```powershell
# A partir da pasta permanente no servidor (RDP): cd C:\Friozem\importacao_notas\tools\deploy
.\servico.ps1 -Acao Start
.\servico.ps1 -Acao Status
```
Coloque **poucos** XMLs na pasta de entrada (`C:\Friozem\kitbrf`, local ou via o share `kitbrf`),
acompanhe `logs\worker.out.log` e
`worker.err.log`, confira a subpasta `Processados\pacote_*`, o relatório e o e-mail de fechamento.

### 3.8 VIRAR para PRODUÇÃO (sem reinstalar)
A virada é só troca de valores de config + restart:
1. Ajuste `Wms:BaseUrl` para a URL de produção (**sem `:8888`**) em
   `config\appsettings.Production.json` (ou env `Wms__BaseUrl`).
2. Grave as credenciais de produção do WMS:
   ```powershell
   # A partir de C:\Friozem\importacao_notas\tools\deploy no servidor (RDP):
   .\configurar-segredos.ps1 -Fase Producao
   ```
3. Confirme destinatários de produção (fechamento em `config\destinatarios-fechamento.txt`, com
   `Email:Destinatarios` como fallback; resumo em `config\destinatarios-resumo.txt`).
4. Reinicie:
   ```powershell
   .\servico.ps1 -Acao Restart
   ```

### 3.9 Nova release / rollback (ATUALIZAR versão)
Fluxo de atualização: na **máquina de build** rode `publicar.ps1` para gerar o novo `.zip`; copie o
`.zip` (e, se os scripts de `deploy\` mudaram, a própria pasta `deploy\`) para o servidor; depois,
**no servidor (RDP)**, opere a partir de `C:\Friozem\importacao_notas\tools\deploy`.
`publicar-release.ps1` (idempotente) também **atualiza os scripts** em `tools\deploy` ao promover a
release, para acompanharem a versão (`instalar.ps1` faz o mesmo). Se os scripts de deploy mudaram e
`tools\deploy` ainda está com a versão antiga, rode a nova **uma vez** a partir de `C:\temp\deploy`
(ela se auto-replica para `tools\deploy`).

```powershell
# A partir de C:\Friozem\importacao_notas\tools\deploy no servidor (RDP):
# Promover nova release (backup do current, vira, reinicia, mantém as últimas 5, atualiza scripts):
.\publicar-release.ps1 -ReleaseZip C:\temp\KitWmsIntegrador-<nova>-win-x64.zip -Versao <nova>

# Rollback para a release anterior (ou -Versao explícita):
.\rollback.ps1
.\rollback.ps1 -Versao 2026.01.15-1
```

### 3.10 Remoção
```powershell
# A partir de C:\Friozem\importacao_notas\tools\deploy no servidor (RDP):
# Remove o serviço, preserva as pastas:
.\remover.ps1
# Remove o serviço E limpa app\/logs\, preservando data\/backups\/config\ (com confirmação):
.\remover.ps1 -LimparPastas -Confirm
```

### 3.11 Limpeza forçada das pastas `ExtensaoInvalida` (NÃO-XML)
Os arquivos que **não são XML** recusados na detecção vão para
`C:\Friozem\kitbrf\Erro\<pacote_...>\ExtensaoInvalida`. Eles são removidos **automaticamente** pela
limpeza de 60 dias (junto com a subpasta do pacote). Quando o operador quiser **esvaziar os NÃO-XML
antes** desse prazo (limpeza forçada sob demanda), use o `limpar-extensao-invalida.ps1`:

```powershell
# A partir de C:\Friozem\importacao_notas\tools\deploy no servidor (RDP):
# 1) SEMPRE primeiro: ver o que seria apagado, sem apagar nada:
.\limpar-extensao-invalida.ps1 -WhatIf
# 2) Apagar de fato (lista o que será removido e pede confirmação s/n):
.\limpar-extensao-invalida.ps1
# 3) Sem perguntar (automação):
.\limpar-extensao-invalida.ps1 -Force
```

O script só remove o conteúdo (incluindo os `.motivo.txt`) e a própria pasta de pastas chamadas
`ExtensaoInvalida` **dentro de** `<Entrada>\Erro` (default `C:\Friozem\kitbrf\Erro`); recusa raiz de
drive, ignora junctions/symlinks e nunca toca em nada fora de `Erro` nem em outras categorias (ex.
`XmlInvalido`). É idempotente e suporta `-WhatIf`. Parâmetros: `-Caminho` (Entrada, default
`C:\Friozem\kitbrf`), `-NomePastaErro` (default `Erro`), `-NomeCategoria` (default
`ExtensaoInvalida`), `-Force` (pula a confirmação). Como os demais `.ps1` de `deploy\`, ele é
**replicado automaticamente** para `C:\Friozem\importacao_notas\tools\deploy` pelo `instalar.ps1`
(via `Copy-ScriptsDeployParaServidor`), então acompanha a instalação sem passo extra.

---

### 3.12 Tela de devolução (I40): firewall restrito e como ligar em produção
A tela de devolução está **desligada** por padrão (`Devolucao:Habilitada=false` no `appsettings.json` e no
`appsettings.Production.json` de exemplo). **Nada deste item é executado pela instalação**: o procedimento
abaixo é para quando o responsável autorizar. A tela não tem login; o controle de acesso é o firewall.

**Viabilidade (2012 R2 / NSSM):** o Kestrel usa sockets e dispensa IIS, HTTP.sys e `netsh http add urlacl`.
O .NET 8 self-contained já roda neste servidor sob NSSM; uma porta a mais não muda o NSSM. Não houve
impedimento técnico; o teste real no servidor ainda não foi feito.

**Ordem para ligar (somente com OK do responsável):**
1. Publique a versão nova (itens 3.2 a 3.9) **com a tela ainda desligada** e confirme que o serviço
   sobe normalmente.
2. Crie a regra de firewall restrita à rede interna (no servidor, PowerShell como administrador;
   o cmdlet existe no 2012 R2). Substitua `<REDE_INTERNA>` pela sub-rede que usa a tela (ex.: a faixa
   da matriz) e mantenha a porta igual a `Devolucao:Porta`:
   ```powershell
   # EXEMPLO - NAO executado pela instalacao. Ajuste a sub-rede antes de rodar.
   New-NetFirewallRule -DisplayName 'Friozem Devolucao I40 (8085)' -Direction Inbound -Protocol TCP `
       -LocalPort 8085 -RemoteAddress '<REDE_INTERNA>' -Action Allow -Profile Domain
   ```
   Não use `-RemoteAddress Any`. Para remover: `Remove-NetFirewallRule -DisplayName 'Friozem Devolucao I40 (8085)'`.
3. Em `config\appsettings.Production.json` (bloco `Devolucao`) defina `"Habilitada": true`
   (não há mais placa nem motorista a configurar; chaves antigas `NomeMotorista`, `EnviarPlacaPadrao` e
   `TipoOperacaoNotaComPlaca` que ainda existam no arquivo são ignoradas).
   Alternativa: variável de ambiente `Devolucao__Habilitada=true`.
4. Reinicie o serviço (`.\servico.ps1 -Acao Restart`) e confira no `logs\worker.out.log` a linha
   "Tela de devolução ligada na porta 8085".
5. De uma máquina da rede interna, abra `http://SRVAPPALC02:8085/api/saude` (deve responder
   `{"status":"ok"}`) e depois a página `http://SRVAPPALC02:8085/`.
6. Um teste com **uma** nota real só depois de combinar com o responsável do WMS.

**Para desligar:** `Habilitada` volta a `false`, reinicie o serviço e, se quiser, remova a regra de firewall.
A auditoria fica em `<Estado>\devolucao\` (pasta de estado do serviço).

## 4. Conta do serviço

Por padrão o serviço roda como **LocalSystem** (`NT AUTHORITY\SYSTEM`). Como a pasta de entrada é
**LOCAL** do próprio servidor (`C:\Friozem\kitbrf`, acessada por caminho local), **LocalSystem
basta** — não é preciso conta de domínio. Quem precisa de credencial de rede são os **usuários que
depositam os XMLs** pelo share SMB `kitbrf`, não o serviço (ver §3.4b).

> **Ressalva hipotética:** se um dia a entrada deixar de ser local e passar a ser um share em
> **outra máquina** (UNC), aí sim LocalSystem pode não alcançá-la e seria preciso rodar o serviço
> sob uma conta de domínio com permissão RW nesse share:
> ```powershell
> & C:\Friozem\Gestao_Enderecos\tools\nssm.exe set Friozem-ImportacaoNotas ObjectName FRIOPART\<conta> <senha>
> .\deploy\servico.ps1 -Acao Restart
> ```
> `instalar.ps1 -ContaServico 'FRIOPART\<conta>'` inclui a conta na ACL de `config\`, mas **não**
> grava a senha do `ObjectName` (segredo) — defina-a com o comando acima. No cenário atual (entrada
> local) isso **não** é necessário.

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
- **Scripts de operação**: vivem de forma **permanente** em `tools\deploy` (fonte de verdade no
  servidor). `instalar.ps1`/`publicar-release.ps1` os **replicam/atualizam** (idempotente) junto da
  release, então acompanham a versão. O técnico opera a partir dessa pasta (RDP).

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
- Par portável `Compress-ZipPortavel`/`Expand-ZipPortavel` testado localmente (round-trip): conteúdo
  sai na raiz do destino e a re-extração é idempotente. Também validado expandindo um `.zip` real
  gerado por `publicar.ps1` (`KitWmsIntegrador.Worker.exe` cai direto na raiz da release).

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

- **(a) Nome do share e grupo de domínio com escrita na PRODUÇÃO.** A pasta de entrada é local e
  fixa (`C:\Friozem\kitbrf`, share `kitbrf`). Na **validação** a escrita é liberada para
  `Authenticated Users`; em **produção** recomenda-se restringir a um grupo de domínio
  `FRIOPART\<grupo>` (`compartilhar-pasta.ps1 -GravadoresShare 'FRIOPART\<grupo>'`) — grupo a
  confirmar.
- **(b) URL exata do WMS de PRODUÇÃO** (sem `:8888`) — se difere da de homologação
  (hipótese `https://friozem.assistsoftware.com.br/wmsapi/api`, a confirmar).
- **(c) Credenciais de PRODUÇÃO do WMS** (hoje usamos `BLUEFOOD` de homologação; produção pode diferir).
- **(d) Lista final de destinatários** do relatório de fechamento e do resumo semanal em produção
  (hoje `ti@friozem.com.br`).
