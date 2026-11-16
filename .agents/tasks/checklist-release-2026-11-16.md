# Checklist da release 2026.11.16-1 (publicação no servidor SRVAPPALC02)

Substitui `checklist-release-2026-11-13.md`: a 2026.11.13-1 **nunca foi publicada** e está incluída nesta release.
Este checklist é para o usuário/orquestrador executar. **Nada foi copiado, instalado ou executado no servidor** por quem o escreveu. Nenhum segredo aqui.

## 1. Pacote

- Pacote (self-contained win-x64): `c:\wms-integrador\artifacts\2026.11.16-1\KitWmsIntegrador-2026.11.16-1-win-x64.zip` (44,6 MB, 46.732.548 bytes, 339 arquivos)
- SHA-256: `256560BB0C023FC436D05AB698D52836ED91474875FA0E13ADFE64F2D978906E`
- Manuais: `c:\wms-integrador\artifacts\2026.11.16-1\manuais\` e `manuais.zip` (1.101.893 bytes, SHA-256 `125D61C00D4D54B18CC7AC5F784926EDFE0AFACCFC1ABC48A0FEE9B45836C37A`) para `C:\Friozem\importacao_notas\manuais`
- Scripts: pasta `c:\wms-integrador\deploy\` inteira (copiar junto do zip; inclui o novo `reprocessar-erro-401.ps1`)
- Conferido no `appsettings.json` do zip: `I50:TipoSeparacao=PIC;FEF;PLL`, `I50:TipoSeparacao2=AGL;FEF;PLL`, `I50:EnviarPlacaVeiculo=true`, `I50:EnviarPlacaVeiculoTsp=false`, `Pacote:ModoJanela=Silencio`, `SilencioSegundos=20`, `JanelaMaximaSegundos=180`, `EstabilizacaoSegundos=3`, `Limpeza:DiasRetencao=30`, `I51:DiasRetencaoEstado=30`, `I51/I60/Devolucao:Habilitada=false`, `Wms:TokenTentativasImediatas=2`, `Wms:CooldownTokenSegundos=60;120;300;600;900`, `Wms:ValidadeMaximaTokenMinutos=3`, `Wms:AlertaAutenticacao=true`.
- Build Release: 0 avisos, 0 erros. `dotnet test` Release: 966 aprovados, 9 ignorados (stress), 0 falhas (registro em `verificacao-pacote-1611.md`).
- `artifacts\` é ignorado pelo git: o zip **não** está no repositório.

## 2. Antes do deploy (no servidor, PowerShell como Administrador)

1. Conferir o hash do zip na máquina de build e de novo no servidor depois de copiar:
   ```powershell
   Get-FileHash 'c:\wms-integrador\artifacts\2026.11.16-1\KitWmsIntegrador-2026.11.16-1-win-x64.zip' -Algorithm SHA256
   ```
2. Copiar o zip e a pasta `deploy\` para `C:\temp` do servidor.
3. **Backup da configuração atual** (o `publicar-release.ps1` só faz backup de `app\current`; a config fica em `config\` e é mantida, mas guarde uma cópia):
   ```powershell
   $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
   New-Item -ItemType Directory -Force -Path "C:\Friozem\importacao_notas\backups\config_$stamp" | Out-Null
   Copy-Item 'C:\Friozem\importacao_notas\config\*' "C:\Friozem\importacao_notas\backups\config_$stamp" -Recurse -Force
   ```
   Anote também a release atual: `Get-ChildItem C:\Friozem\importacao_notas\app\releases | Sort-Object LastWriteTime | Select-Object Name`.
4. **`--testar-token` ANTES do deploy** (um único GET; imprime só o status, nunca token nem senha). A release em produção (até 2026.11.06-x) ainda não tem a CLI, então use o executável da release nova extraído em uma pasta temporária, com a config do servidor ao lado (o serviço copia `config\appsettings.Production.json` para ao lado do exe; o teste lê `appsettings.json`, `appsettings.Production.json` e as variáveis de máquina `WMS__Usuario`/`WMS__Senha`):
   ```powershell
   Expand-Archive C:\temp\KitWmsIntegrador-2026.11.16-1-win-x64.zip C:\temp\kit-1611 -Force
   Copy-Item C:\Friozem\importacao_notas\config\appsettings.Production.json C:\temp\kit-1611\ -ErrorAction SilentlyContinue
   & C:\temp\kit-1611\KitWmsIntegrador.Worker.exe --testar-token
   ```
   Confirmar que o `Wms:BaseUrl` efetivo é o de produção (sem `:8888`; vem do `appsettings.Production.json` ou de `Wms__BaseUrl` de máquina). Registrar `GetToken: HTTP 200 OK` ou `FALHA` (código). Apagar `C:\temp\kit-1611` depois.
5. **Conferência de que o `appsettings.Production.json` do servidor NÃO sobrescreve** os tipos de separação nem a placa. Rodar e ler o resultado:
   ```powershell
   Select-String -Path 'C:\Friozem\importacao_notas\config\appsettings.Production.json' -Pattern 'TipoSeparacao|EnviarPlaca|SilencioSegundos|JanelaMaxima|Estabilizacao|ModoJanela|DiasRetencao|CooldownToken|ValidadeMaxima|TokenTentativas|AlertaAutent'
   [Environment]::GetEnvironmentVariables('Machine').GetEnumerator() | Where-Object { $_.Key -like 'I50__*' -or $_.Key -like 'Pacote__*' -or $_.Key -like 'Wms__*' } | ForEach-Object { $_.Key }   # só os NOMES; nunca imprimir valores de Wms__Senha
   ```
   Resultado esperado: o arquivo do servidor **não** define `TipoSeparacao`/`TipoSeparacao2` (ou define exatamente `PIC;FEF;PLL` e `AGL;FEF;PLL`), define `EnviarPlacaVeiculo=true` e `EnviarPlacaVeiculoTsp=false` (ou não define, pois são os padrões do pacote), e nenhuma variável de máquina `I50__*`/`Pacote__*` com valor antigo. Se aparecer `RES;FEF;PLI`, `EnviarPlacaVeiculo=false`, `EnviarPlacaVeiculoTsp=true` ou `SilencioSegundos=15`, corrigir **antes** do restart (regra: Puxada normal COM placa; TSP SEM placa).

## 3. Deploy

```powershell
cd C:\Friozem\importacao_notas\tools\deploy
# Se os scripts de deploy mudaram (reprocessar-erro-401.ps1 é novo), rode uma vez a partir de C:\temp\deploy (auto-replica para tools\deploy):
# cd C:\temp\deploy
.\publicar-release.ps1 -ReleaseZip C:\temp\KitWmsIntegrador-2026.11.16-1-win-x64.zip -Versao 2026.11.16-1 -WhatIf   # simulação primeiro
.\publicar-release.ps1 -ReleaseZip C:\temp\KitWmsIntegrador-2026.11.16-1-win-x64.zip -Versao 2026.11.16-1
.\servico.ps1 -Acao Status
```
O script faz backup do `app\current`, para o serviço, troca a release, mantém `config\` e reinicia.

Depois do deploy:
1. **Criar as pastas de depósito** (a 13/11 nunca foi publicada; ver `docs\INSTALACAO-SERVIDOR.md` §3.4c):
   ```powershell
   New-Item -ItemType Directory -Force -Path 'C:\Friozem\kitbrf\Puxada','C:\Friozem\kitbrf\PuxadaTSP'
   Get-ChildItem 'C:\Friozem\kitbrf' -Directory | Select-Object Name
   ```
   As subpastas herdam o share `\\SRVAPPALC02\kitbrf` e a ACL do `compartilhar-pasta.ps1`. Confirmar que um usuário gravador copia um arquivo de teste em cada uma. O serviço também as cria ao iniciar.
2. Manuais: extrair `manuais.zip` em `C:\Friozem\importacao_notas\manuais`.
3. **`--testar-token` DEPOIS do deploy** (agora com a release nova em `app\current`):
   ```powershell
   & 'C:\Friozem\importacao_notas\app\current\KitWmsIntegrador.Worker.exe' --testar-token
   ```
   Esperado: `GetToken: HTTP 200 OK`. Se der `FALHA`, **não** reprocessar nada: anotar o código e a hora, ver `docs\OPERACAO.md` §12.1 e acionar a Assist se passar de 15 min.

## 4. Configuração de `C:\Friozem\importacao_notas\config\appsettings.Production.json`

O arquivo do servidor sobrepõe o `appsettings.json` do pacote. O pacote já traz os valores abaixo como padrão, então só é preciso editar o arquivo do servidor se ele **definir outro valor** para a chave (ver a conferência do §2.5). Modelo: `deploy\config-exemplo\appsettings.Production.json`. Depois de editar: `.\servico.ps1 -Acao Restart`. **Não alterar `Wms:BaseUrl`** (produção, sem `:8888`, já configurada no servidor).

### 4.1 Aplicar agora (não liga fluxos novos)

| Chave | Valor | Observação |
|---|---|---|
| `I50:TipoSeparacao` | `PIC;FEF;PLL` | geral (cabeçalho) e item; era `RES;FEF;PLI` |
| `I50:TipoSeparacao2` | `AGL;FEF;PLL` | geral (cabeçalho) e item |
| `I50:EnviarPlacaVeiculo` | `true` | Puxada normal COM placa |
| `I50:EnviarPlacaVeiculoTsp` | `false` | Puxada TSP SEM placa |
| `Pacote:ModoJanela` | `Silencio` | `Fixa` volta ao comportamento antigo sem rollback |
| `Pacote:SilencioSegundos` | `20` | era 15: coleta fecha após 20 s sem arquivo novo |
| `Pacote:JanelaMaximaSegundos` | `180` | teto desde a abertura do pacote |
| `Pacote:EstabilizacaoSegundos` | `3` | mínimo de silêncio efetivo |
| `Wms:TokenTentativasImediatas` | `2` | tentativas imediatas do GetToken antes do cooldown |
| `Wms:CooldownTokenSegundos` | `60;120;300;600;900` | 1, 2, 5, 10 e 15 min (teto) sem chamar o WMS |
| `Wms:ValidadeMaximaTokenMinutos` | `3` | renova o token a cada 3 min (`0` desliga) |
| `Wms:AlertaAutenticacao` | `true` | 1 e-mail na indisponibilidade e 1 na recuperação (respeita `Email:Habilitado`) |
| `Limpeza:DiasRetencao` / `I51:DiasRetencaoEstado` | `30` / `30` | pedido do usuário (era 60) |
| `I60:QtdeVolumeZero` / `I60:QtdeTotalNotaZero` / `I60:AlertaImediato` | `true` | só agem com I51/I60 ligadas |
| `Pastas:Puxada` / `Pastas:PuxadaTsp` | `Puxada` / `PuxadaTSP` | |
| Destinatários do alerta de autenticação | os mesmos do fechamento (`Email:CaminhoDestinatarios`) | o alerta de 401 usa esse arquivo |

**Aviso da retenção de 30 dias:** com `Limpeza:DiasRetencao=30`, a **primeira limpeza depois da subida apaga** os arquivos de `Processados` e `Erro` com **mais de 30 dias** (e o estado terminal da I51). Antes de subir, se alguma nota antiga em `Erro` for necessária (por exemplo para o reprocesso), confirme que ela tem menos de 30 dias; as de 15/11 têm. A tela de devolução só encontra notas dos últimos 30 dias. Depois do rollback a retenção volta a 60 dias, mas o que foi apagado não volta.

### 4.2 SOMENTE depois do OK do usuário (não aplicar sem confirmação)

1. **Reprocesso das 383 notas de 15/11** (`Erro\pacote_20261115_*`), somente depois de `--testar-token` OK no §3 **e** com OK do usuário:
   ```powershell
   cd C:\Friozem\importacao_notas\tools\deploy
   .\reprocessar-erro-401.ps1 -WhatIf                         # sempre primeiro: lista o que seria movido, não move nada
   .\reprocessar-erro-401.ps1 -DestinoPadrao Puxada           # só com OK; o script pede confirmação s/n (use -Force só se o usuário mandar)
   ```
   As notas antigas não registram a origem (o sidecar `.motivo.txt` novo registra `Origem:`): o script usa `-DestinoPadrao`. Se houver notas da TSP entre as 383, rodar uma vez com `-DestinoPadrao PuxadaTSP` apenas para elas (separar antes por pacote/arquivo; o `-WhatIf` mostra a lista). O serviço integra os XMLs devolvidos no próximo pacote. Log do script: `C:\Friozem\kitbrf\Estado\logs\reprocessar-erro-401-<data>.log`. Idempotente.
2. **I51 e I60 juntas:** `I51:Habilitada=true` **e** `I60:Habilitada=true`, no mesmo reinício (`I60` ligada com `I51` desligada volta ao fluxo antigo). Pré-requisitos do WMS de produção: EDI `WEBAPI-I51` com o serviço `WEBSERVI` e o evento interno que gera o retorno da separação. Texto do ENC ainda desconhecido: calibrar `I60:PadroesDocCargaEncerrado`.
3. **Tela de devolução:** `Devolucao:Habilitada=true`, `Devolucao:Porta=8085` e regra de firewall restrita à rede interna (`docs\INSTALACAO-SERVIDOR.md` §3.12; nunca `Any`).
4. **Usuário exclusivo de produção no WMS** (hoje BLUEFOOD é o mesmo do homolog): depende do fornecedor; trocar a credencial só com OK (`configurar-segredos.ps1`) e testar com `--testar-token`.
5. Qualquer alteração dos valores de cooldown, validade do token ou silêncio além dos acima.

## 5. Rollback

```powershell
cd C:\Friozem\importacao_notas\tools\deploy
.\rollback.ps1                       # volta para a release anterior
.\rollback.ps1 -Versao 2026.11.06-5  # ou uma release explícita (confirmar o nome da pasta em app\releases)
.\servico.ps1 -Acao Status
```
- Restaurar a config só se a editou: copiar de volta `C:\Friozem\importacao_notas\backups\config_<carimbo>\*` para `config\` (§2.3) e `.\servico.ps1 -Acao Restart`. As chaves novas são ignoradas pela release antiga.
- Depois do rollback o 401 volta a mandar notas para `Erro` (comportamento antigo) e a retenção volta a 60 dias; o que a limpeza de 30 dias já apagou não volta.
- Para só desfazer a janela de silêncio use `Pacote:ModoJanela=Fixa` (sem rollback). Para só voltar o tempo de espera, `Pacote:SilencioSegundos=15`.
- Se notas foram reprocessadas (§4.2.1), elas já saíram de `Erro`: o rollback não as devolve.

## 6. Verificação pós-deploy (usar arquivos de teste/homolog, nunca notas reais)

- [ ] `.\servico.ps1 -Acao Status` = Running e `logs\worker.out.log` sem erro de partida (nenhum erro de validação de `Pacote`, `Limpeza`, `I51`, `Wms`).
- [ ] `--testar-token` antes e depois registrados (§2.4 e §3.3): `GetToken: HTTP 200 OK`.
- [ ] Log com "Pacote aberto" ao depositar um ZIP/XML de teste e "Fim da coleta por silêncio (20s sem novos arquivos)".
- [ ] Um arquivo de teste em `C:\Friozem\kitbrf\Puxada` e outro em `C:\Friozem\kitbrf\PuxadaTSP` sobem como Puxada e TSP (relatório mostra Tipo e Origem; Puxada com placa, TSP sem).
- [ ] No payload/relatório da I50 de teste: `tipoSeparacao` = `PIC;FEF;PLL` e `tipoSeparacao2` = `AGL;FEF;PLL` (cabeçalho e itens), conferido no WMS de produção com a equipe.
- [ ] E-mail de fechamento chega com cabeçalho "Relatório de Integração", tempo de integração e "Coleta (janela de silêncio)".
- [ ] Lote com vários arquivos copiados em sequência fecha em um único pacote (nenhum intervalo entre arquivos maior que 20 s).
- [ ] Com I51/I60 ainda desligadas, o log não traz "Ciclo I51".
- [ ] Sem "GetToken falhou" no log depois do deploy. Se houver: nenhuma nota em `Erro` (os arquivos ficam na entrada), log "Aguardando autenticação do WMS até HH:mm:ss" e e-mail `ALERTA: autenticação do WMS indisponível (GetToken)`.
- [ ] Limpeza: log do primeiro ciclo de limpeza com retenção de 30 dias (ver aviso do §4.1).
- [ ] (só se o usuário ligou a I51/I60) log "Ciclo I51" a cada ciclo; `--relatorio-i60 --enviar` (ver `docs\OPERACAO.md` §10).

## 7. Pendências conhecidas

- **Causa do 401 no fornecedor:** enviar à Assist as perguntas de `docs\OPERACAO.md` §12.1 (base `investigacao-401.md`) com os horários de 15/11 (17:27 a 17:38 e ~18:34) e de 06/11. A release torna o 401 inofensivo, mas não elimina a causa.
- Texto real do ENC (doccarga encerrado) ainda não capturado: o primeiro alerta mostra o texto; copiar um trecho para `I60:PadroesDocCargaEncerrado` e reiniciar.
- Trocar senhas que apareceram em chat (itens de `docs\ENCERRAMENTO.md`).
