# Checklist da release 2026.11.16-3 (token por envio, sobre a 2026.11.16-2)

- Pacote: `c:\wms-integrador\artifacts\2026.11.16-3\KitWmsIntegrador-2026.11.16-3-win-x64.zip` (46.743.505 bytes)
- SHA-256: `F2001E00C85B67ED2C9AE3A3FF5836736A09CB36F0173696F3A4668C283275F3`
- Scripts: pasta `c:\wms-integrador\deploy\` (copiar junto do zip)
- Manuais: `c:\wms-integrador\artifacts\2026.11.16-3\manuais.zip` (opcional)
- Servidor: SRVAPPALC02, PowerShell como Administrador. **Nada disto foi executado no servidor.** Sem credenciais neste arquivo.
- Esforço: registrar somente em horas (humanas e de IA) em `docs\CONTROLE-ESFORCO.md`.

## 1. Copiar o pacote

Tentar primeiro pelo share administrativo:

```powershell
$zip = 'c:\wms-integrador\artifacts\2026.11.16-3\KitWmsIntegrador-2026.11.16-3-win-x64.zip'
$c = Import-Clixml "$env:USERPROFILE\.srv-cred.xml"
New-Item -ItemType Directory -Force '\\200.200.200.213\C$\temp' | Out-Null
Copy-Item $zip '\\200.200.200.213\C$\temp\' -Force
Copy-Item 'c:\wms-integrador\deploy' '\\200.200.200.213\C$\temp\deploy' -Recurse -Force
```

Se o share `C$` falhar, copiar por PSSession em blocos (o `deploy\` vai com `Copy-Item -ToSession`):

```powershell
$s = New-PSSession -ComputerName 200.200.200.213 -Credential $c
Invoke-Command -Session $s { New-Item -ItemType Directory -Force C:\temp | Out-Null; Remove-Item C:\temp\KitWmsIntegrador-2026.11.16-3-win-x64.zip -ErrorAction SilentlyContinue }
Copy-Item 'c:\wms-integrador\deploy' -Destination C:\temp\deploy -ToSession $s -Recurse -Force
$tam = 4MB
$fs = [IO.File]::OpenRead($zip); $buf = New-Object byte[] $tam
while (($n = $fs.Read($buf,0,$tam)) -gt 0) {
  $parte = if ($n -eq $tam) { $buf } else { $buf[0..($n-1)] }
  Invoke-Command -Session $s -ArgumentList (,$parte) -ScriptBlock {
    param($b)
    $f = [IO.File]::Open('C:\temp\KitWmsIntegrador-2026.11.16-3-win-x64.zip','Append','Write'); $f.Write($b,0,$b.Length); $f.Close()
  }
}
$fs.Close()
```

- [ ] Zip e `deploy\` em `C:\temp` do servidor.

## 2. Verificar o hash (no servidor)

```powershell
(Get-FileHash C:\temp\KitWmsIntegrador-2026.11.16-3-win-x64.zip -Algorithm SHA256).Hash -eq 'F2001E00C85B67ED2C9AE3A3FF5836736A09CB36F0173696F3A4668C283275F3'
```

- [ ] Resultado `True`. Se `False`, parar, apagar o zip do servidor e copiar de novo (tamanho esperado: 46.743.505 bytes).
- [ ] Anotar a release atual: `Get-ChildItem C:\Friozem\importacao_notas\app\releases | Sort-Object LastWriteTime | Select-Object Name` (esperado: 2026.11.16-2).

## 3. Backup da configuração de produção

```powershell
$dest = "C:\Friozem\importacao_notas\backup-config-$(Get-Date -Format yyyyMMdd-HHmm)"
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item C:\Friozem\importacao_notas\app\current\appsettings*.json $dest
Copy-Item C:\Friozem\importacao_notas\app\current\email.settings.json $dest -ErrorAction SilentlyContinue
Get-ChildItem $dest | Select-Object Name, Length
```

- [ ] Arquivos copiados. Não copiar variáveis `WMS__*` para arquivos nem para o chat.
- [ ] Conferir que o `appsettings.Production.json` do servidor mantém `Wms:BaseUrl` de produção (porta 443). O `publicar-release.ps1` preserva a configuração do servidor; se a nova release trouxer chaves novas (`Wms:TokenPorEnvio`, `Wms:MaxGetTokenPorMinuto`, `Wms:TokenTentativasImediatas`), os padrões do código valem (`true`, 300, 3).

## 4. `--testar-token --vezes 5` ANTES

```powershell
Remove-Item Env:\Wms__BaseUrl -ErrorAction SilentlyContinue
& 'C:\Friozem\importacao_notas\app\current\KitWmsIntegrador.Worker.exe' --testar-token --vezes 5
```

Observação: a release 2026.11.16-2 ainda não tem `--vezes`. Se o exe atual ignorar o parâmetro, rodar `--testar-token` cinco vezes com 2 s de intervalo.

- [ ] Anotar quantos `HTTP 200` e quantos `401` (e o `servidor`, se aparecer), host `friozem.assistsoftware.com.br`, porta `443`.

## 5. Publicar (`-WhatIf` e depois de verdade)

Avisar antes: o serviço reinicia. Conferir que não há pacote em processamento.

```powershell
cd C:\temp\deploy
.\publicar-release.ps1 -ReleaseZip C:\temp\KitWmsIntegrador-2026.11.16-3-win-x64.zip -Versao 2026.11.16-3 -WhatIf
.\publicar-release.ps1 -ReleaseZip C:\temp\KitWmsIntegrador-2026.11.16-3-win-x64.zip -Versao 2026.11.16-3
.\servico.ps1 -Acao Status
```

- [ ] `-WhatIf` sem erro; publicação real concluída; serviço `Running`.

## 6. `--testar-token --vezes 5` DEPOIS

```powershell
& 'C:\Friozem\importacao_notas\app\current\KitWmsIntegrador.Worker.exe' --testar-token --vezes 5
```

- [ ] Cinco linhas `GetToken: HTTP 200 OK (friozem.assistsoftware.com.br, porta 443)`. Comparar com o ANTES.
- [ ] Se aparecer 401: anotar hora, quantidade e `servidor` (para a Assist). Uma tentativa isolada com 401 não obriga rollback; falha em todas as cinco ou erro de host/porta, sim (§8).

## 7. Conferir o log (`worker.out.log`)

```powershell
$log = (Get-ChildItem C:\Friozem\importacao_notas -Recurse -Filter worker.out.log | Select-Object -First 1).FullName
Get-Content $log -Tail 400 | Select-String 'GetToken|limitado|401|servidor'
```

- [ ] **GetToken por envio:** a cada POST (I50, I60, I40, I51) aparece um GetToken próprio; o número de GetToken é aproximadamente igual ao de POST.
- [ ] **Retry de 401:** linhas "GetToken falhou" com `status` 401 seguidas de nova tentativa que dá 200 (até 3 por GetToken). Nota não vai para `Erro` por falha de GetToken.
- [ ] **Throttling:** nenhuma linha "GetToken do WMS limitado" em operação normal (teto 300/min). Se aparecer, anotar horário e volume do pacote.
- [ ] **Contagem por backend:** nas linhas de falha, `servidor` (`nome#hash`) por GetToken. Contar por servidor (3 em balanceamento) e guardar para a Assist; no resumo do pacote/e-mail conferir "Falhas de autenticação (GetToken): N" e a lista `nome#hash=N`.
- [ ] Tempo por nota: esperado cerca de +120 ms por nota (um GetToken a mais).

## 8. Rollback (volta para a 2026.11.16-2)

```powershell
cd C:\Friozem\importacao_notas\tools\deploy
.\rollback.ps1 -Versao 2026.11.16-2 -WhatIf
.\rollback.ps1 -Versao 2026.11.16-2
.\servico.ps1 -Acao Status
& 'C:\Friozem\importacao_notas\app\current\KitWmsIntegrador.Worker.exe' --testar-token
```

- [ ] Serviço `Running` na 2026.11.16-2 e `--testar-token` com HTTP 200, host de produção, porta 443.
- [ ] Se a configuração foi alterada à mão, restaurar os arquivos do backup do §3.
