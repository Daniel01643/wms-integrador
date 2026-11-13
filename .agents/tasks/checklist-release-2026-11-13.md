# Checklist da release 2026.11.13-1 (publicação no servidor SRVAPPALC02)

Este checklist é para o orquestrador/usuário executar. **Nada foi copiado, instalado ou executado no servidor** por quem o escreveu. Nenhum segredo aqui.

## 1. Pacote

- Pacote (self-contained win-x64): `c:\wms-integrador\artifacts\2026.11.13-1\KitWmsIntegrador-2026.11.13-1-win-x64.zip` (44,6 MB, 46.721.320 bytes, 339 arquivos)
- SHA-256: `F097D770C9D0B4432BB2E9CB5614505C5E1918EAB5D7C8C762650DB8E565BC08`
- Manuais: `c:\wms-integrador\artifacts\2026.11.13-1\manuais\` e `manuais.zip` (SHA-256 `F080A8A0D588204B199EDE965AF05E606C61A69B7734D27DAE47DE8C5DE4B22C`) para `C:\Friozem\importacao_notas\manuais`
- Scripts: pasta `c:\wms-integrador\deploy\` (copiar inteira junto do zip)
- Conferido no zip: `KitWmsIntegrador.Worker.exe` e `appsettings.json` presentes; no `appsettings.json` do pacote `I51/I60/Devolucao:Habilitada=false`, `I60:QtdeVolumeZero=true`, `I60:QtdeTotalNotaZero=true`, `I60:AlertaImediato=true`, `Limpeza:DiasRetencao=30`, `I51:DiasRetencaoEstado=30`, `I50:EnviarPlacaVeiculo=true`, `I50:EnviarPlacaVeiculoTsp=false`, `Pacote:ModoJanela=Silencio` (15 s / 180 s).
- Build Release 0 avisos, 0 erros; 926 testes aprovados, 9 ignorados, 0 falhas.
- `artifacts\` é ignorado pelo git: o zip **não** está no repositório.

## 2. Publicar no servidor (com o serviço rodando a versão anterior; sem ligar nada novo)

1. Na máquina de build, confira o hash antes de copiar:
   ```powershell
   Get-FileHash 'c:\wms-integrador\artifacts\2026.11.13-1\KitWmsIntegrador-2026.11.13-1-win-x64.zip' -Algorithm SHA256
   ```
2. Copie o zip e a pasta `deploy\` para `C:\temp` do servidor (RDP/cópia de arquivos de costume). Confira o hash lá também.
3. No servidor, PowerShell como **Administrador**, em `C:\Friozem\importacao_notas\tools\deploy`:
   ```powershell
   cd C:\Friozem\importacao_notas\tools\deploy
   # Se os scripts de deploy mudaram, rode uma vez a partir de C:\temp\deploy (auto-replica para tools\deploy):
   # cd C:\temp\deploy
   .\publicar-release.ps1 -ReleaseZip C:\temp\KitWmsIntegrador-2026.11.13-1-win-x64.zip -Versao 2026.11.13-1
   .\servico.ps1 -Acao Status
   ```
   O script faz backup do `app\current`, para o serviço, troca a release, mantém `config\` e reinicia.
4. Pastas de depósito (ver `docs\INSTALACAO-SERVIDOR.md` §3.4c):
   ```powershell
   New-Item -ItemType Directory -Force -Path 'C:\Friozem\kitbrf\Puxada','C:\Friozem\kitbrf\PuxadaTSP'
   Get-ChildItem 'C:\Friozem\kitbrf' -Directory | Select-Object Name
   ```
   As subpastas herdam o share `\\SRVAPPALC02\kitbrf` e a ACL de `compartilhar-pasta.ps1` (não precisa rodá-lo de novo). Confirme que um usuário gravador consegue copiar um arquivo em cada uma. O serviço também as cria ao iniciar.
5. Manuais: extrair `manuais.zip` em `C:\Friozem\importacao_notas\manuais`.

## 3. Configuração de `C:\Friozem\importacao_notas\config\appsettings.Production.json`

O arquivo do servidor sobrepõe o `appsettings.json` do pacote. O pacote já traz os valores abaixo como padrão, então só é preciso editar o arquivo do servidor se ele **definir outro valor** para a chave. Modelo: `deploy\config-exemplo\appsettings.Production.json`. Depois de editar, reinicie: `.\servico.ps1 -Acao Restart`.

### 3.1 Pode aplicar agora (seguro, não liga fluxos novos)

| Chave | Valor | Observação |
|---|---|---|
| `I50:EnviarPlacaVeiculo` | `true` | Puxada normal COM placa (como no XML) |
| `I50:EnviarPlacaVeiculoTsp` | `false` | Puxada TSP sem placa |
| `I60:QtdeVolumeZero` | `true` | validado no homolog em 13/11 (7 de 7 associadas) |
| `I60:QtdeTotalNotaZero` | `true` | idem |
| `I60:AlertaImediato` | `true` | só age quando a I51/I60 estiver ligada |
| `Limpeza:DiasRetencao` | `30` | era 60: a limpeza passa a apagar o que tem mais de 30 dias em `Processados`/`Erro` |
| `I51:DiasRetencaoEstado` | `30` | idem para o estado terminal da I51 |
| `Pacote:ModoJanela` | `Silencio` | `Fixa` volta ao comportamento antigo sem rollback |
| `Pacote:SilencioSegundos` | `15` | |
| `Pacote:JanelaMaximaSegundos` | `180` | teto |
| `Pastas:Puxada` / `Pastas:PuxadaTsp` | `Puxada` / `PuxadaTSP` | |
| Destinatários do alerta e do relatório da I60 | arquivo `destinatarios-i60.txt`, um e-mail por linha | padrão: pasta de telemetria (`data\telemetria`); para usar `config\`, defina `RelatorioI60:CaminhoDestinatarios` = `C:\Friozem\importacao_notas\config\destinatarios-i60.txt`. Sem o arquivo, usa a lista do resumo semanal e depois `ti@friozem.com.br` |

Atenção: com 30 dias de retenção, na primeira limpeza depois da subida os arquivos de `Processados`/`Erro` com mais de 30 dias serão apagados. A tela de devolução só encontra notas dos últimos 30 dias.

### 3.2 SOMENTE depois do OK do usuário (não aplicar sem confirmação)

1. **I51 e I60 juntas:** `I51:Habilitada=true` **e** `I60:Habilitada=true`, no mesmo reinício. Atenção: `I60:Habilitada=true` com `I51:Habilitada=false` volta ao fluxo antigo (I60 junto da I50), que não é o desejado.
   Pré-requisitos do WMS de **produção** (já existem no homolog; confirmar com a equipe WMS antes):
   - EDI `WEBAPI-I51` com o serviço `WEBSERVI` configurado;
   - o evento interno do WMS que gera o retorno da separação (no homolog ele não havia sido criado e a I51 não recebia retorno).
   Depois de ligar: acompanhar o primeiro ciclo e o e-mail de alerta (ver §6). Texto do ENC ainda desconhecido: calibrar `I60:PadroesDocCargaEncerrado`.
2. **Tela de devolução:** `Devolucao:Habilitada=true`, `Devolucao:Porta=8085` e regra de firewall restrita à rede interna (`docs\INSTALACAO-SERVIDOR.md` §3.12). Comando (ajustar a sub-rede; não usar `Any`):
   ```powershell
   New-NetFirewallRule -DisplayName 'Friozem Devolucao I40 (8085)' -Direction Inbound -Protocol TCP `
       -LocalPort 8085 -RemoteAddress '<REDE_INTERNA>' -Action Allow -Profile Domain
   ```
   Depois: `.\servico.ps1 -Acao Restart`, log "Tela de devolução ligada na porta 8085", `http://SRVAPPALC02:8085/api/saude` deve responder `{"status":"ok"}`. Remover a regra: `Remove-NetFirewallRule -DisplayName 'Friozem Devolucao I40 (8085)'`.

## 4. Rollback

```powershell
cd C:\Friozem\importacao_notas\tools\deploy
.\rollback.ps1                       # volta para a release anterior
.\rollback.ps1 -Versao 2026.11.06-5  # ou uma release explícita (confirmar o nome da pasta em app\releases)
.\servico.ps1 -Acao Status
```
Reverter as chaves novas do `appsettings.Production.json` é opcional: a release antiga as ignora. Atenção: depois do rollback a retenção volta a ser a da release antiga (60 dias), mas o que já foi apagado com 30 dias não volta. Para só desfazer a janela de silêncio use `Pacote:ModoJanela=Fixa` (sem rollback).

## 5. Verificação pós-deploy (usar arquivos de teste/homolog, nunca notas reais)

- [ ] `.\servico.ps1 -Acao Status` = Running e `logs\worker.out.log` sem erro de partida (nenhum erro de validação de `Pacote`, `Limpeza`, `I51`).
- [ ] Log com "Pacote aberto" ao depositar um ZIP/XML de teste e "Fim da coleta por silêncio (15s sem novos arquivos)".
- [ ] Um arquivo de teste em `C:\Friozem\kitbrf\Puxada` e outro em `C:\Friozem\kitbrf\PuxadaTSP` sobem como Puxada e TSP (relatório mostra Tipo e Origem; Puxada com placa, TSP sem).
- [ ] E-mail de fechamento chega com cabeçalho "Relatório de Integração", tempo de integração e rótulo "Coleta (janela de silêncio)".
- [ ] Lote com vários arquivos copiados em sequência fecha em um único pacote.
- [ ] Com I51/I60 ainda desligadas, o log não traz "Ciclo I51".
- [ ] (só se o usuário ligou a I51/I60) log "Ciclo I51" a cada ciclo; teste do relatório da I60: comando `--relatorio-i60 --enviar` do `KitWmsIntegrador.Worker.exe` (ver `docs\OPERACAO.md` §10); conferir `Estado\i51\alertas\AAAA-MM-DD.log` e o e-mail `[KitWms] I60 precisa de atenção: N nota(s)` quando houver ENC/rejeitada/não reconhecida.
- [ ] Limpeza: log do primeiro ciclo de limpeza com retenção de 30 dias.

## 6. Pendências conhecidas

- Texto real do ENC (doccarga encerrado) ainda não capturado: o primeiro alerta mostra o texto; copiar um trecho para `I60:PadroesDocCargaEncerrado` (separado por `;`, sem acento) e reiniciar o serviço.
- Trocar senhas que apareceram em chat (itens de `docs\ENCERRAMENTO.md`).
