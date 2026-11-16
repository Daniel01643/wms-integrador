# Checklist da release 2026.11.16-2 (hotfix sobre a 2026.11.16-1)

- Pacote: `c:\wms-integrador\artifacts\2026.11.16-2\KitWmsIntegrador-2026.11.16-2-win-x64.zip` (46.732.854 bytes)
- SHA-256: `6D555EAC9359F1E09EC390F09B0BE967B3D0B0216A461F3B0110A7C3D1F9B15F`
- Scripts: pasta `c:\wms-integrador\deploy\` (copiar junto do zip)
- Esforço: registrar somente em horas (humanas e de IA) em `docs\CONTROLE-ESFORCO.md`.

Servidor: SRVAPPALC02, PowerShell como Administrador. Nada disto foi executado no servidor.

## 1. Copiar o pacote
```powershell
# máquina de build: copiar zip e deploy\ para C:\temp do servidor
Get-FileHash 'c:\wms-integrador\artifacts\2026.11.16-2\KitWmsIntegrador-2026.11.16-2-win-x64.zip' -Algorithm SHA256
```
- [ ] Zip e `deploy\` em `C:\temp` do servidor.

## 2. Verificar o hash (no servidor)
```powershell
(Get-FileHash C:\temp\KitWmsIntegrador-2026.11.16-2-win-x64.zip -Algorithm SHA256).Hash -eq '6D555EAC9359F1E09EC390F09B0BE967B3D0B0216A461F3B0110A7C3D1F9B15F'
```
- [ ] Resultado `True`. Se `False`, parar e copiar de novo.
- [ ] Anotar a release atual: `Get-ChildItem C:\Friozem\importacao_notas\app\releases | Sort-Object LastWriteTime | Select-Object Name` (esperado: 2026.11.16-1).

## 3. Publicar
```powershell
cd C:\temp\deploy
.\publicar-release.ps1 -ReleaseZip C:\temp\KitWmsIntegrador-2026.11.16-2-win-x64.zip -Versao 2026.11.16-2 -WhatIf
.\publicar-release.ps1 -ReleaseZip C:\temp\KitWmsIntegrador-2026.11.16-2-win-x64.zip -Versao 2026.11.16-2
.\servico.ps1 -Acao Status
```
- [ ] `-WhatIf` sem erro; publicação real concluída; serviço `Running`.

## 4. Testar o token (sem override de URL)
```powershell
Remove-Item Env:\Wms__BaseUrl -ErrorAction SilentlyContinue   # sessão atual
[Environment]::GetEnvironmentVariable('Wms__BaseUrl','Machine')   # esperado: vazio
& 'C:\Friozem\importacao_notas\app\current\KitWmsIntegrador.Worker.exe' --testar-token
```
- [ ] Saída com `GetToken: HTTP 200 OK`.
- [ ] Saída com host `friozem.assistsoftware.com.br` e porta `443` (nunca `:8888`).
- Se não for 200 ou o host/porta estiver errado: não reprocessar nada, seguir o §5 e registrar código e hora.

## 5. Rollback (volta para a 2026.11.16-1)
```powershell
cd C:\Friozem\importacao_notas\tools\deploy
.\rollback.ps1 -Versao 2026.11.16-1 -WhatIf
.\rollback.ps1 -Versao 2026.11.16-1
.\servico.ps1 -Acao Status
& 'C:\Friozem\importacao_notas\app\current\KitWmsIntegrador.Worker.exe' --testar-token
```
- [ ] Serviço `Running` na 2026.11.16-1 e `--testar-token` com HTTP 200 e host de produção na porta 443.
