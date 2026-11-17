# Checklist da release (hotfix) 2026.11.17-1: I51 usa quantidade separada por embalagem

> **O DEPLOY NÃO FOI EXECUTADO.** O pacote só foi gerado localmente. Nada foi copiado, publicado ou alterado no servidor. Sem credenciais neste arquivo.

- Correção: a I51 passa a usar `qtdeEmbalagemSep` (unidade da NF-e/I50) em vez de `qtdeProdutoSep` (unidades). Evita o falso ajuste 18 -> 180 na I60 para produto com conversão (ex.: 5006566, 1 embalagem = 10 unidades).
- Pacote: `c:\wms-integrador\artifacts\2026.11.17-1\KitWmsIntegrador-2026.11.17-1-win-x64.zip` (46.745.901 bytes). A pasta `artifacts\` é ignorada pelo git (`.gitignore` linha 44).
- SHA-256: `2FC35BD4729BEB2D3FD953A2C611E988B6043A2C1A9D6A664F7D8DC46919537C`
- Scripts: pasta `c:\wms-integrador\deploy\` (copiar junto do zip)
- Manuais: `c:\wms-integrador\artifacts\2026.11.17-1\manuais.zip` (opcional)
- Servidor: SRVAPPALC02, PowerShell como Administrador. Release atual esperada: 2026.11.16-3.
- Esforço: registrar somente em horas (humanas e de IA) em `docs\CONTROLE-ESFORCO.md`.

## 1. Copiar o pacote para o servidor

Tentar primeiro pelo share administrativo:

```powershell
$zip = 'c:\wms-integrador\artifacts\2026.11.17-1\KitWmsIntegrador-2026.11.17-1-win-x64.zip'
$c = Import-Clixml "$env:USERPROFILE\.srv-cred.xml"
New-Item -ItemType Directory -Force '\\200.200.200.213\C$\temp' | Out-Null
Copy-Item $zip '\\200.200.200.213\C$\temp\' -Force
Copy-Item 'c:\wms-integrador\deploy' '\\200.200.200.213\C$\temp\deploy' -Recurse -Force
```

Se o share `C$` falhar, copiar por PSSession em blocos de 4 MB:

```powershell
$s = New-PSSession -ComputerName 200.200.200.213 -Credential $c
Invoke-Command -Session $s { New-Item -ItemType Directory -Force C:\temp | Out-Null; Remove-Item C:\temp\KitWmsIntegrador-2026.11.17-1-win-x64.zip -ErrorAction SilentlyContinue }
Copy-Item 'c:\wms-integrador\deploy' -Destination C:\temp\deploy -ToSession $s -Recurse -Force
$tam = 4MB
$fs = [IO.File]::OpenRead($zip); $buf = New-Object byte[] $tam
while (($n = $fs.Read($buf,0,$tam)) -gt 0) {
  $parte = if ($n -eq $tam) { $buf } else { $buf[0..($n-1)] }
  Invoke-Command -Session $s -ArgumentList (,$parte) -ScriptBlock {
    param($b)
    $f = [IO.File]::Open('C:\temp\KitWmsIntegrador-2026.11.17-1-win-x64.zip','Append','Write'); $f.Write($b,0,$b.Length); $f.Close()
  }
}
$fs.Close()
```

- [ ] Zip e `deploy\` em `C:\temp` do servidor.

## 2. Conferir o hash SHA-256 (local x servidor)

Hash local esperado (gerado em 17/11/2026): `2FC35BD4729BEB2D3FD953A2C611E988B6043A2C1A9D6A664F7D8DC46919537C`

```powershell
# na máquina de build
(Get-FileHash 'c:\wms-integrador\artifacts\2026.11.17-1\KitWmsIntegrador-2026.11.17-1-win-x64.zip' -Algorithm SHA256).Hash

# no servidor
(Get-FileHash C:\temp\KitWmsIntegrador-2026.11.17-1-win-x64.zip -Algorithm SHA256).Hash -eq '2FC35BD4729BEB2D3FD953A2C611E988B6043A2C1A9D6A664F7D8DC46919537C'
```

- [ ] Resultado `True`. Se `False`, parar, apagar o zip do servidor e copiar de novo (tamanho esperado: 46.745.901 bytes).
- [ ] Anotar a release atual: `Get-ChildItem C:\Friozem\importacao_notas\app\releases | Sort-Object LastWriteTime | Select-Object Name` (esperado: 2026.11.16-3).

## 3. Backup da configuração (antes de publicar)

```powershell
$dest = "C:\Friozem\importacao_notas\backup-config-$(Get-Date -Format yyyyMMdd-HHmm)"
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item C:\Friozem\importacao_notas\config\* $dest -Recurse
Copy-Item C:\Friozem\importacao_notas\app\current\appsettings*.json $dest -ErrorAction SilentlyContinue
Get-ChildItem $dest -Recurse | Select-Object FullName, Length
```

- [ ] `appsettings.Production.json` presente no backup. Anotar o nome da pasta (usada no rollback, §8). Não copiar variáveis `WMS__*` para arquivos nem para o chat.

## 4. Conferir/ajustar `I51:CampoQuantidadeSeparada`

O `publicar-release.ps1` copia o config de `C:\Friozem\importacao_notas\config` para a nova release e não cria chaves novas. Sem a chave, vale o padrão do código (`Embalagem`), que já é o desejado. Conferir:

```powershell
Select-String -Path C:\Friozem\importacao_notas\config\appsettings.Production.json -Pattern 'CampoQuantidadeSeparada'
```

- [ ] Sem resultado: nada a fazer (padrão `Embalagem`). Se quiser explícito, acrescentar na seção `I51` do arquivo: `"CampoQuantidadeSeparada": "Embalagem"` (vírgula na linha anterior; JSON válido).
- [ ] Se aparecer `Produto`: trocar para `Embalagem` (o valor `Produto` mantém o bug). Valores aceitos: `Embalagem` e `Produto`; outro valor impede a partida.
- [ ] Conferir também que não existe variável de ambiente do serviço `I51__CampoQuantidadeSeparada` com `Produto`.
- [ ] Validar o JSON: `Get-Content C:\Friozem\importacao_notas\config\appsettings.Production.json -Raw | ConvertFrom-Json | Out-Null`.

## 5. Publicar (`-WhatIf` e depois de verdade)

Avisar antes: o serviço reinicia. Conferir que não há pacote em processamento.

```powershell
cd C:\temp\deploy
.\publicar-release.ps1 -ReleaseZip C:\temp\KitWmsIntegrador-2026.11.17-1-win-x64.zip -Versao 2026.11.17-1 -WhatIf
.\publicar-release.ps1 -ReleaseZip C:\temp\KitWmsIntegrador-2026.11.17-1-win-x64.zip -Versao 2026.11.17-1
.\servico.ps1 -Acao Status
```

- [ ] `-WhatIf` sem erro; publicação real concluída; serviço `Running`.

## 6. Verificar a partida da I51 no log

```powershell
$log = (Get-ChildItem C:\Friozem\importacao_notas -Recurse -Filter worker.out.log | Select-Object -First 1).FullName
Get-Content $log -Tail 400 | Select-String 'Consulta da I51 iniciada'
```

- [ ] Linha `Consulta da I51 iniciada: identificador=... intervalo=... min, atraso após ... h, I60 ...` com horário posterior à publicação.
- [ ] Sem erro de configuração inválida (`CampoQuantidadeSeparada`) na partida.

## 7. Verificar que não há falso ajuste para produto com conversão (ex.: 5006566)

Depois de pelo menos um ciclo da I51/I60 com nota que tenha o produto 5006566:

```powershell
Get-Content $log -Tail 2000 | Select-String '5006566|Ajuste|qtdeEmbalagemSep|Observacao'
```

- [ ] Para o 5006566, a quantidade enviada na I60 é a da nota em embalagens (ex.: 18), não em unidades (180). Nenhum ajuste 18 -> 180.
- [ ] No relatório diário da I60, seção "Ajustes de quantidade": o campo usado aparece como `Embalagem` e o 5006566 não aparece com ajuste de multiplicação por 10.
- [ ] Aviso de fallback (`qtdeProdutoSep` com Observacao na nota) só é aceitável se a I51 não devolver `qtdeEmbalagemSep` nem `qtdeCaixaSep`; anotar produto e nota.
- [ ] Órfãos antigos guardam unidades (`qtdeProdutoSep`). Um órfão antigo de produto com conversão pode divergir da nota. Conferir `Estado\i51\orfaos` por produtos com conversão.

## 8. Rollback (volta para a 2026.11.16-3)

```powershell
cd C:\Friozem\importacao_notas\tools\deploy
.\rollback.ps1 -Versao 2026.11.16-3 -WhatIf
.\rollback.ps1 -Versao 2026.11.16-3
.\servico.ps1 -Acao Status
& 'C:\Friozem\importacao_notas\app\current\KitWmsIntegrador.Worker.exe' --testar-token
```

Restaurar o config do backup do §3 se ele tiver sido alterado (ajuste do §4):

```powershell
$bk = 'C:\Friozem\importacao_notas\backup-config-<carimbo do §3>'
.\servico.ps1 -Acao Stop
Copy-Item "$bk\appsettings.Production.json" C:\Friozem\importacao_notas\config\ -Force
.\servico.ps1 -Acao Start
```

- [ ] Serviço `Running` na 2026.11.16-3 e `--testar-token` com HTTP 200, host de produção, porta 443.
- [ ] A 2026.11.16-3 não conhece `CampoQuantidadeSeparada`: a chave extra no config é ignorada. Com o rollback, a I51 volta a usar `qtdeProdutoSep`, ou seja, volta o risco de falso ajuste em produto com conversão. Se a I60 estiver habilitada, avaliar desligar `I60__Habilitada` até nova correção.
- [ ] O `publicar-release.ps1` guarda um zip do `current` anterior em `C:\Friozem\importacao_notas\backups\current_<carimbo>.zip` (alternativa de restauração).
