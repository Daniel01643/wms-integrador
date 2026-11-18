# Checklist da release 2026.11.18-1: Diagnóstico agente integrador (e-mail das 14:00, diário, memória, limpeza de logs, e-mails enxutos)

> **O DEPLOY NÃO FOI EXECUTADO.** O pacote só foi gerado localmente. Nada foi copiado, publicado ou alterado no servidor, e nada foi enviado ao remoto (sem push). Sem credenciais neste arquivo.

- Conteúdo: e-mail técnico "Diagnóstico agente integrador" todos os dias às 14:00 só para a TI; diário de eventos (JSONL); CLI `--diagnostico`; amostra de memória e alerta de memória; limpeza dos logs rotacionados do NSSM; log HTTP em Warning; corpo dos e-mails enxuto (resumo + problemas, detalhes no PDF anexo).
- Pacote: `c:\wms-integrador\artifacts\2026.11.18-1\KitWmsIntegrador-2026.11.18-1-win-x64.zip` (46.813.604 bytes). A pasta `artifacts\` é ignorada pelo git.
- SHA-256: `8D27C857FC5B284DECCE7C7E6A942A7B83B000CC211901CA8A85F7FC1FEB4E00`
- Scripts: pasta `c:\wms-integrador\deploy\` (copiar junto do zip)
- Manuais: `c:\wms-integrador\artifacts\2026.11.18-1\manuais.zip` (opcional; SHA-256 `EE6BCC30256A854FC15BDA64D0BFF949A286E8653458ED95CB372B8ECA438969`)
- Servidor: SRVAPPALC02, PowerShell como Administrador. Release atual esperada: 2026.11.17-1.
- Esforço: registrar somente em horas (humanas e de IA) em `docs\CONTROLE-ESFORCO.md`.

## Mudança de comportamento a avisar

- **O alerta de falha/recuperação do GetToken (autenticação) agora vai somente aos destinatários de diagnóstico (TI)**, não mais à lista do relatório/fechamento. A lista é a de `Diagnostico:CaminhoDestinatarios` (TXT) > `Diagnostico:Destinatarios` > `ti@friozem.com.br`.
- O corpo dos e-mails do relatório diário I51/I60 (12:00) e do diagnóstico (14:00) passa a ter só resumo e problemas. As listas do que deu certo (associadas, separadas, visão por pacote) ficam no **PDF anexo** (o PDF segue completo). Avisar os destinatários.
- O primeiro diagnóstico chega com PDF anexo; a seção de memória só tem dados após as primeiras amostras (15 min).

## 1. Copiar o pacote para o servidor

Tentar primeiro pelo share administrativo:

```powershell
$zip = 'c:\wms-integrador\artifacts\2026.11.18-1\KitWmsIntegrador-2026.11.18-1-win-x64.zip'
$c = Import-Clixml "$env:USERPROFILE\.srv-cred.xml"
New-Item -ItemType Directory -Force '\\200.200.200.213\C$\temp' | Out-Null
Copy-Item $zip '\\200.200.200.213\C$\temp\' -Force
Copy-Item 'c:\wms-integrador\deploy' '\\200.200.200.213\C$\temp\deploy' -Recurse -Force
```

Se o share `C$` falhar, copiar por PSSession em blocos de 4 MB:

```powershell
$s = New-PSSession -ComputerName 200.200.200.213 -Credential $c
Invoke-Command -Session $s { New-Item -ItemType Directory -Force C:\temp | Out-Null; Remove-Item C:\temp\KitWmsIntegrador-2026.11.18-1-win-x64.zip -ErrorAction SilentlyContinue }
Copy-Item 'c:\wms-integrador\deploy' -Destination C:\temp\deploy -ToSession $s -Recurse -Force
$tam = 4MB
$fs = [IO.File]::OpenRead($zip); $buf = New-Object byte[] $tam
while (($n = $fs.Read($buf,0,$tam)) -gt 0) {
  $parte = if ($n -eq $tam) { $buf } else { $buf[0..($n-1)] }
  Invoke-Command -Session $s -ArgumentList (,$parte) -ScriptBlock {
    param($b)
    $f = [IO.File]::Open('C:\temp\KitWmsIntegrador-2026.11.18-1-win-x64.zip','Append','Write'); $f.Write($b,0,$b.Length); $f.Close()
  }
}
$fs.Close()
```

- [ ] Zip e `deploy\` em `C:\temp` do servidor.

## 2. Conferir o hash SHA-256 (local x servidor)

Hash local esperado (gerado em 18/11/2026): `8D27C857FC5B284DECCE7C7E6A942A7B83B000CC211901CA8A85F7FC1FEB4E00`

```powershell
# na máquina de build
(Get-FileHash 'c:\wms-integrador\artifacts\2026.11.18-1\KitWmsIntegrador-2026.11.18-1-win-x64.zip' -Algorithm SHA256).Hash

# no servidor
(Get-FileHash C:\temp\KitWmsIntegrador-2026.11.18-1-win-x64.zip -Algorithm SHA256).Hash -eq '8D27C857FC5B284DECCE7C7E6A942A7B83B000CC211901CA8A85F7FC1FEB4E00'
```

- [ ] Resultado `True`. Se `False`, parar, apagar o zip do servidor e copiar de novo (tamanho esperado: 46.813.604 bytes).
- [ ] Anotar a release atual: `Get-ChildItem C:\Friozem\importacao_notas\app\releases | Sort-Object LastWriteTime | Select-Object Name` (esperado: 2026.11.17-1).

## 3. Backup da configuração (antes de publicar)

```powershell
$dest = "C:\Friozem\importacao_notas\backup-config-$(Get-Date -Format yyyyMMdd-HHmm)"
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item C:\Friozem\importacao_notas\config\* $dest -Recurse
Copy-Item C:\Friozem\importacao_notas\app\current\appsettings*.json $dest -ErrorAction SilentlyContinue
Get-ChildItem $dest -Recurse | Select-Object FullName, Length
```

- [ ] `appsettings.Production.json` presente no backup. Anotar o nome da pasta (usada no rollback, §9). Não copiar variáveis `WMS__*` para arquivos nem para o chat.

## 4. Criar a lista de destinatários do diagnóstico e apontar a configuração (ANTES de habilitar)

`Diagnostico:Habilitado` vale `true` por padrão, então o e-mail das 14:00 e o alerta de token só devem ser liberados com a lista correta. O `publicar-release.ps1` copia o config de `C:\Friozem\importacao_notas\config` para a nova release e não cria chaves novas.

```powershell
$cfg = 'C:\Friozem\importacao_notas\config'
Set-Content -Path "$cfg\destinatarios-diagnostico.txt" -Value @('# Diagnóstico agente integrador: um e-mail por linha','ti@friozem.com.br') -Encoding UTF8
Get-Content "$cfg\destinatarios-diagnostico.txt"
```

Modelo: `deploy\config-exemplo\destinatarios-diagnostico.exemplo.txt`. O arquivo é relido a cada envio (editar tem efeito no próximo envio, sem reiniciar).

Definir `Diagnostico:CaminhoDestinatarios` em `C:\Friozem\importacao_notas\config\appsettings.Production.json` (seção `Diagnostico`; JSON válido, vírgula na linha anterior):

```json
"Diagnostico": {
  "CaminhoDestinatarios": "C:\\Friozem\\importacao_notas\\config\\destinatarios-diagnostico.txt"
}
```

```powershell
Get-Content C:\Friozem\importacao_notas\config\appsettings.Production.json -Raw | ConvertFrom-Json | Out-Null
```

- [ ] `destinatarios-diagnostico.txt` criado com `ti@friozem.com.br` (e só esse).
- [ ] `Diagnostico:CaminhoDestinatarios` definido e JSON válido. Sem isso, vale `Diagnostico:Destinatarios` e por último a constante `ti@friozem.com.br` (com aviso no log).
- [ ] Se o config do servidor tiver a seção `Diagnostico` já pronta, só conferir o caminho. Para desligar apenas o e-mail diário: `Diagnostico:Habilitado=false` (o diário e o alerta de token continuam).

## 5. Novas chaves de configuração (adendo)

Todas têm padrão no código/`appsettings.json`; só é preciso mexer no config do servidor para mudar o comportamento. Variáveis de ambiente equivalentes: `Logging__LogLevel__System.Net.Http.HttpClient`, `Logging__LogLevel__Microsoft.AspNetCore`, `Limpeza__DiasRetencaoLogs`, `Limpeza__PastaLogs`, `Diagnostico__IntervaloAmostraMinutos`, `Diagnostico__AlertaMemoriaLivreMb`.

| Chave | Padrão | Efeito | Como ajustar no servidor |
|---|---|---|---|
| `Logging:LogLevel:System.Net.Http.HttpClient` | `Warning` | Remove as 4 linhas Information por chamada ao WMS no `worker.out.log`. Logs próprios (envio concluído, falhas, ciclo I51) continuam | Em `appsettings.Production.json`, `Logging:LogLevel`. Para voltar ao detalhe, `Information` (só para investigação) |
| `Logging:LogLevel:Microsoft.AspNetCore` | `Warning` | Remove o log de cada acesso à tela da porta 8085 | Idem |
| `Limpeza:DiasRetencaoLogs` | `30` (mínimo 1) | Apaga todo dia `worker.out-*.log` e `worker.err-*.log` (rotacionados do NSSM, 10 MB cada) mais antigos que N dias. Nunca apaga `worker.out.log`/`worker.err.log` atuais. Valor menor que 1 não apaga nada e gera erro no resultado da limpeza | Alterar o número na seção `Limpeza` e reiniciar o serviço |
| `Limpeza:PastaLogs` | vazio (= `<pasta do app>\..\..\logs`); no exemplo de produção `C:\Friozem\importacao_notas\logs` | Pasta dos logs do NSSM. Pasta inexistente é ignorada; raiz de drive é recusada | Informar o caminho completo se os logs estiverem em outro lugar |
| `Diagnostico:IntervaloAmostraMinutos` | `15` (0 desliga; aceita 1 a 1440) | Intervalo da amostra de memória gravada no diário (`Estado\diagnostico\AAAAMMDD.jsonl`, cerca de 96 linhas/dia) | Alterar na seção `Diagnostico`; `0` desliga amostra e alerta |
| `Diagnostico:AlertaMemoriaLivreMb` | `500` (0 desliga) | E-mail só para a TI quando a memória física livre do servidor ficar abaixo do valor por 2 amostras seguidas; no máximo 1 e-mail por hora | Subir o valor para alertar mais cedo, descer para alertar menos, `0` para desligar |

Notas:

- A "paginação" mostrada é a memória virtual comprometida (RAM + arquivo de paginação) obtida de `GlobalMemoryStatusEx`, não o percentual de `pagefile.sys`.
- Os brutos da I51 em `Estado\i51\brutos\processados` já seguem `I51:DiasRetencaoEstado` (30); sem mudança.
- Conferir que o `appsettings.Production.json` do servidor não sobrescreve `Logging:LogLevel` com valores mais detalhados:

```powershell
Select-String -Path C:\Friozem\importacao_notas\config\appsettings.Production.json -Pattern 'LogLevel|HttpClient|AspNetCore|DiasRetencaoLogs|AlertaMemoriaLivreMb|IntervaloAmostraMinutos'
```

- [ ] Nenhum override mais detalhado de `Logging:LogLevel`. Valores novos conferidos ou deixados no padrão.

## 6. Publicar (`-WhatIf` e depois de verdade)

Avisar antes: o serviço reinicia. Conferir que não há pacote em processamento.

```powershell
cd C:\temp\deploy
.\publicar-release.ps1 -ReleaseZip C:\temp\KitWmsIntegrador-2026.11.18-1-win-x64.zip -Versao 2026.11.18-1 -WhatIf
.\publicar-release.ps1 -ReleaseZip C:\temp\KitWmsIntegrador-2026.11.18-1-win-x64.zip -Versao 2026.11.18-1
.\servico.ps1 -Acao Status
```

- [ ] `-WhatIf` sem erro; publicação real concluída; serviço `Running`.
- [ ] `& 'C:\Friozem\importacao_notas\app\current\KitWmsIntegrador.Worker.exe' --testar-token` com HTTP 200, host de produção, porta 443.

## 7. Testar com `--diagnostico --enviar`

Sem `--enviar`, só gera HTML e PDF (janela das últimas 24 h até agora) e imprime o caminho. Com `--enviar`, tenta também o e-mail, só para os destinatários de diagnóstico. Não mexe na marca do slot das 14:00. Saída 0 = ok, 1 = erro.

```powershell
& 'C:\Friozem\importacao_notas\app\current\KitWmsIntegrador.Worker.exe' --diagnostico
& 'C:\Friozem\importacao_notas\app\current\KitWmsIntegrador.Worker.exe' --diagnostico --enviar
$LASTEXITCODE
```

- [ ] Saída 0; `diagnostico_*.html` e `diagnostico_*.pdf` em `Relatorio:PastaSaida\diagnostico`.
- [ ] E-mail "[KitWms] Diagnóstico agente integrador" chegou só em `ti@friozem.com.br`, com `diagnostico.pdf` anexo e corpo enxuto (resumo + problemas + linha "Detalhes completos no PDF anexo").
- [ ] Se o SMTP estiver desligado (`Email:Habilitado=false`), só o HTML/PDF são gravados.

## 8. Verificações após a publicação

```powershell
$log = (Get-ChildItem C:\Friozem\importacao_notas -Recurse -Filter worker.out.log | Select-Object -First 1).FullName
Get-Content $log -Tail 400 | Select-String 'Diagnóstico|Diagnostico|Consulta da I51 iniciada|limpeza'
Get-ChildItem C:\Friozem\importacao_notas\app\current\Estado\diagnostico -ErrorAction SilentlyContinue
```

- [ ] Depois de 15 a 30 min: linhas `saude-amostra` no diário do dia (`Estado\diagnostico\AAAAMMDD.jsonl`).
- [ ] `worker.out.log` sem as linhas Information de `System.Net.Http.HttpClient` e de acesso à porta 8085.
- [ ] Limpeza: rotacionados com mais de 30 dias em `C:\Friozem\importacao_notas\logs` removidos; arquivo atual preservado.
- [ ] Às 14:00 do dia seguinte (ou do mesmo dia, se ainda não passou): diagnóstico recebido só pela TI.
- [ ] Próximo relatório das 12:00 (I51/I60): corpo curto e `relatorio-i60.pdf` completo anexo.
- [ ] Alerta de falha/recuperação do token (se ocorrer) chega só à TI, não à lista do relatório.

## 9. Rollback (volta para a 2026.11.17-1)

```powershell
cd C:\Friozem\importacao_notas\tools\deploy
.\rollback.ps1 -Versao 2026.11.17-1 -WhatIf
.\rollback.ps1 -Versao 2026.11.17-1
.\servico.ps1 -Acao Status
& 'C:\Friozem\importacao_notas\app\current\KitWmsIntegrador.Worker.exe' --testar-token
```

Restaurar o config do backup do §3 se ele tiver sido alterado (§4/§5):

```powershell
$bk = 'C:\Friozem\importacao_notas\backup-config-<carimbo do §3>'
.\servico.ps1 -Acao Stop
Copy-Item "$bk\appsettings.Production.json" C:\Friozem\importacao_notas\config\ -Force
.\servico.ps1 -Acao Start
```

- [ ] Serviço `Running` na 2026.11.17-1 e `--testar-token` com HTTP 200, host de produção, porta 443.
- [ ] A 2026.11.17-1 não conhece as chaves novas (`Diagnostico`, `DiasRetencaoLogs`, `PastaLogs`, `AlertaMemoriaLivreMb`, `IntervaloAmostraMinutos`): são ignoradas. Com o rollback, somem o e-mail das 14:00, o diário, a amostra de memória e a limpeza de logs, e o alerta de token volta para a lista anterior (não só a TI). Os e-mails das 12:00 voltam ao corpo completo.
- [ ] `Estado\diagnostico` não é apagado pelo rollback (arquivos ficam no disco; a retenção é de 30 dias na release nova).
- [ ] O `publicar-release.ps1` guarda um zip do `current` anterior em `C:\Friozem\importacao_notas\backups\current_<carimbo>.zip` (alternativa de restauração).

## 10. Documentação atualizada nesta release

- `docs\OPERACAO.md` (§3 Limpeza, §10, §13 e novo §13.3 saúde/memória).
- `docs\INSTALACAO-SERVIDOR.md` (§3 chaves da release e §7 tabela).
- `docs\ARQUITETURA.md` (§2.4).
- `docs\manuais\MANUAL-TECNICO` (§3, §8, §8.5, §8.8, tabela 12) e `MANUAL-USUARIO` (e-mail das 12:00 e diagnóstico), em `.md`, `.html` e `.pdf`, também em `manuais.zip`.
