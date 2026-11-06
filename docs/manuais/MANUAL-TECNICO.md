# Manual Técnico (Nível 1 / Júnior) — KitWms Integrador

<!--CAPA:tecnico-->

Guia de operação para técnico humano no servidor **SRVAPPALC02** (Windows Server 2012 R2,
PowerShell 4.0/5.1). O passo a passo pressupõe **acesso LOCAL ao servidor por RDP (Área de
Trabalho Remota) ou direto na máquina**, abrindo o **PowerShell como Administrador** no próprio
servidor. (Alternativa avançada: os scripts também rodam por WinRM com `Invoke-Command`, mas este
manual foca no acesso local.)

Todos os comandos abaixo assumem que você está na pasta `deploy\` do projeto no servidor. Rode o
PowerShell como Administrador e use sempre:

```powershell
powershell -ExecutionPolicy Bypass -File .\<script>.ps1 <parâmetros>
```

## Índice

1. [Dados do ambiente](#1-dados-do-ambiente)
2. [Iniciar / parar / reiniciar / status do serviço](#2-iniciar--parar--reiniciar--status-do-serviço)
3. [Ver e interpretar os logs](#3-ver-e-interpretar-os-logs)
4. [Trocar usuário e senha do WMS](#4-trocar-usuário-e-senha-do-wms)
5. [Trocar usuário / senha / remetente do SMTP](#5-trocar-usuário--senha--remetente-do-smtp)
6. [Trocar o endpoint/URL do WMS (virada homolog → produção)](#6-trocar-o-endpointurl-do-wms-virada-homolog--produção)
7. [Destinatários de e-mail (fechamento e resumo semanal)](#7-destinatários-de-e-mail-fechamento-e-resumo-semanal)
8. [Dia/hora do resumo, janela de coleta e limpeza](#8-diahora-do-resumo-janela-de-coleta-e-limpeza)
9. [Publicar nova versão e rollback](#9-publicar-nova-versão-e-rollback)
10. [Estado e telemetria (não mexer)](#10-estado-e-telemetria-não-mexer)
11. [O serviço não sobe — diagnóstico e TLS 1.2](#11-o-serviço-não-sobe--diagnóstico-e-tls-12)
12. [Tabela: "Quero fazer X → arquivo/comando → reinicia?"](#12-tabela-quero-fazer-x--arquivocomando--reinicia)
13. [Avisos de segurança](#13-avisos-de-segurança)
14. [Guia rápido: usar a IA de apoio](#14-guia-rápido-usar-a-ia-de-apoio)

---

## 1. Dados do ambiente

| Item | Valor |
|---|---|
| Servidor | `SRVAPPALC02` / `200.200.200.213` (Windows Server 2012 R2) |
| Raiz do app | `C:\Friozem\importacao_notas` |
| Subpastas do app | `app\releases\<versao>`, `app\current`, `config`, `data`, `logs`, `backups`, `tools` |
| Serviço (NSSM) | `Friozem-ImportacaoNotas` |
| nssm.exe | `C:\Friozem\Gestao_Enderecos\tools\nssm.exe` (fallback `deploy\tools\nssm.exe`) |
| Pasta de entrada (local) | `C:\Friozem\kitbrf` (share SMB `kitbrf`: `\\SRVAPPALC02\kitbrf`) |
| Config não-secreta | `C:\Friozem\importacao_notas\config\appsettings.Production.json` |
| Segredos SMTP | `C:\Friozem\importacao_notas\config\email.settings.json` |
| Segredos WMS | variáveis de ambiente de **MÁQUINA** `WMS__Usuario` / `WMS__Senha` |
| Logs do serviço | `C:\Friozem\importacao_notas\logs\worker.out.log` e `worker.err.log` |

Os scripts de deploy ficam em `deploy\`. Defaults já embutidos em todos eles:
`-RaizInstalacao C:\Friozem\importacao_notas`, `-NomeServico Friozem-ImportacaoNotas`,
`-NssmPath C:\Friozem\Gestao_Enderecos\tools\nssm.exe`. Os scripts que escrevem aceitam `-WhatIf`
(simula sem alterar nada).

O diagrama abaixo resume a implantação no servidor: o serviço NSSM, o share `kitbrf` (com
`Processados\`, `Erro\` e `relatorios\` dentro), a pasta `data\` (Estado + telemetria) **fora** do
share, a `config\` com segredos e ACL restrita, e as saídas HTTPS (WMS) e SMTP (e-mail).

<!--DIAGRAMA:arquitetura-tecnico-->

O ciclo de processamento do pacote (orquestrador reativo) está detalhado em
[`docs\OPERACAO.md`](../OPERACAO.md) e ilustrado a seguir: do estado **Ocioso** até o fechamento do
pacote (relatório + e-mail + telemetria), passando pelo envio `I50` e pelos reenvios `I10`/`I30`,
com o limite de **3 tentativas** por nota.

<!--DIAGRAMA:ciclo-pacote-->

## 2. Iniciar / parar / reiniciar / status do serviço

Use `deploy\servico.ps1`. O parâmetro `-Acao` é **obrigatório** e aceita exatamente
`Start`, `Stop`, `Restart` ou `Status`:

```powershell
powershell -ExecutionPolicy Bypass -File .\servico.ps1 -Acao Status
powershell -ExecutionPolicy Bypass -File .\servico.ps1 -Acao Start
powershell -ExecutionPolicy Bypass -File .\servico.ps1 -Acao Stop
powershell -ExecutionPolicy Bypass -File .\servico.ps1 -Acao Restart
```

O `Status` combina a saída de `nssm status` com `Get-Service` (mostra situação e tipo de
inicialização). As ações são idempotentes (parar algo já parado não gera erro).

**Pelo `services.msc`** (interface gráfica): abra `services.msc`, localize o serviço
**`Friozem-ImportacaoNotas`**, e use o botão direito para **Iniciar / Parar / Reiniciar**. É o
mesmo serviço; prefira o `servico.ps1` quando puder, pois ele usa o NSSM de forma consistente.

## 3. Ver e interpretar os logs

Os logs ficam em:

```text
C:\Friozem\importacao_notas\logs\worker.out.log   (saída normal)
C:\Friozem\importacao_notas\logs\worker.err.log   (erros)
```

São gravados pelo NSSM, com rotação diária ou a cada ~10 MB. Abra com o Bloco de Notas, com o
`notepad`, ou acompanhe o fim do arquivo no PowerShell:

```powershell
Get-Content C:\Friozem\importacao_notas\logs\worker.out.log -Tail 50 -Wait
```

Linhas comuns e o que significam:

| Linha no log | Significado |
|---|---|
| `Watcher iniciado. Entrada=... Processados=... Erro=...` | O serviço subiu bem e está observando a pasta. |
| `Relatório do pacote salvo em <Html> e <Pdf>.` | Um pacote fechou e gerou relatório. |
| `E-mail do pacote enviado.` | O e-mail de fechamento saiu. |
| `E-mail não configurado; relatório salvo em <Pasta>.` | SMTP não configurado; o relatório ficou em disco (sem erro). |
| `Falha ao enviar o e-mail do pacote: <Motivo>` | O envio falhou; veja o motivo (host/porta/credencial SMTP). |
| `HTTP 401 no envio WMS (identificador=...); renovando token e repetindo uma vez.` | **Erro de credencial/token do WMS.** O sistema renova e tenta 1x; se continuar 401, as credenciais `WMS__Usuario`/`WMS__Senha` estão inválidas (ver seção 4). |
| `Serviço não iniciado: corrija a seção 'Pastas' do appsettings.` | Configuração de `Pastas` inválida; o serviço **não sobe** até corrigir. |
| `Resumo semanal enviado para ...` | O resumo semanal saiu. |

Se muitas notas aparecem como **Pendente** ao mesmo tempo, geralmente o problema está **no WMS**
(fila/processamento), não no integrador.

## 4. Trocar usuário e senha do WMS

As credenciais do WMS ficam, por padrão, em **variáveis de ambiente de MÁQUINA**
`WMS__Usuario` / `WMS__Senha`. Troque com `deploy\configurar-segredos.ps1` (ele **nunca** exibe os
valores; se você não passar, ele pede sem eco):

```powershell
powershell -ExecutionPolicy Bypass -File .\configurar-segredos.ps1 -Fase Homolog
```

- `-Fase` aceita `Homolog` (padrão) ou `Producao` — é apenas um rótulo do que está sendo gravado.
- Por padrão (`-Escopo Maquina`) grava nas env de máquina `WMS__Usuario`/`WMS__Senha`.
- O script pergunta `WMS Usuario` e `WMS Senha` sem mostrar o que você digita. Deixar em branco
  **pula** aquele item (não apaga o que já existe).

> **Depois de gravar, REINICIE o serviço** para que ele herde a nova variável de ambiente:
>
> ```powershell
> powershell -ExecutionPolicy Bypass -File .\servico.ps1 -Acao Restart
> ```

## 5. Trocar usuário / senha / remetente do SMTP

O SMTP vive em `C:\Friozem\importacao_notas\config\email.settings.json`. A forma recomendada é
pelo mesmo `configurar-segredos.ps1`, que grava o bloco `Email` com os valores da casa
(`Host=smtp.friozem.com.br`, `Porta=587`, `UsarTls=false`, `Habilitado=true`):

```powershell
powershell -ExecutionPolicy Bypass -File .\configurar-segredos.ps1 -Fase Producao
```

O script pede, sem eco quando for segredo:

- `SMTP Usuario` e `SMTP Senha` (SecureString).
- `SMTP Remetente` — parâmetro `-SmtpRemetente` (ex.: `kitwms@friozem.com.br`).
- `SMTP Nome do remetente` — parâmetro `-SmtpRemetenteNome` (campo `RemetenteNome`; padrão
  `KitWms Integrador`).
- `Destinatarios do fechamento, separados por ;` — parâmetro `-SmtpDestinatarios`.

Você também pode **editar direto** o `email.settings.json` (campos `Host`, `Porta`, `UsarTls`,
`Usuario`, `Senha`, `Remetente`, `RemetenteNome`, `Destinatarios`, `EnviarQuando`). O arquivo é
recarregado ao salvar. Em produção os valores são `smtp.friozem.com.br:587` com **`UsarTls=false`**.

> Limitações do envio: suporta STARTTLS (587) e SMTP interno; **não** suporta TLS implícito (465)
> nem OAuth2.

## 6. Trocar o endpoint/URL do WMS (virada homolog → produção)

A URL do WMS é a chave `Wms:BaseUrl` em
`C:\Friozem\importacao_notas\config\appsettings.Production.json`:

- **Homologação/validação:** `https://friozem.assistsoftware.com.br:8888/wmsapi/api`
- **Produção (virada futura):** a mesma URL **sem a porta `:8888`** (confirme a URL exata de
  produção com o fornecedor do WMS antes da virada — ver lacunas no resumo).

Edite o valor no arquivo, salve e **reinicie o serviço** (`servico.ps1 -Acao Restart`).

## 7. Destinatários de e-mail (fechamento e resumo semanal)

São **duas listas diferentes**, cada uma num **arquivo de texto (TXT) dedicado e editável a
quente**: **um e-mail por linha**; linhas em branco são ignoradas; linhas que começam com `#` são
comentários. Os dois arquivos são **relidos a cada envio** — editar e salvar vale no **próximo**
envio, **sem reiniciar o serviço**. As listas **reais** não são versionadas (ficam em `config\`,
cobertas pelo `.gitignore`).

### 7.1. Fechamento do pacote — `Email:CaminhoDestinatarios`

O caminho do TXT de fechamento é a chave `Email:CaminhoDestinatarios`. Se vazia, o padrão do
sistema é `<telemetria>\destinatarios-fechamento.txt`. Exemplo de arquivo:

```text
# Destinatarios do fechamento do pacote — um e-mail por linha. '#' comenta.
operacao@friozem.com.br
gestor@friozem.com.br
```

**Precedência e fallback do fechamento** (nesta ordem):

1. se o TXT tiver **ao menos um e-mail**, ele é usado (tem **precedência** sobre
   `Email:Destinatarios`);
2. se o TXT estiver ausente/vazio, usa-se a lista `Email:Destinatarios` (separada por `;` no JSON)
   **apenas como fallback**;
3. se **ambos** estiverem vazios, o e-mail do pacote **não é enviado** e um aviso é registrado no
   log — mas o **fechamento conclui normalmente** (o relatório HTML + PDF é gerado assim mesmo).

> Editar `Email:Destinatarios` no JSON (`email.settings.json` / `appsettings.Production.json`) é
> **apenas o fallback** do fechamento, não o mecanismo principal. O mecanismo principal é o TXT
> apontado por `Email:CaminhoDestinatarios`.

### 7.2. Resumo semanal — `Monitoramento:CaminhoDestinatarios`

O caminho do TXT do resumo semanal é a chave `Monitoramento:CaminhoDestinatarios`. Se vazia, o
padrão do sistema é `<telemetria>\destinatarios.txt`. Exemplo de arquivo:

```text
# Destinatarios do resumo semanal — um e-mail por linha. '#' comenta.
ti@friozem.com.br
gestor@friozem.com.br
```

O **fallback é diferente** do fechamento: se o arquivo do resumo estiver ausente, vazio ou
ilegível, o resumo vai para o destinatário **mínimo** `ti@friozem.com.br` (não cai em não-envio) e
um aviso é registrado no log.

### 7.3. Nota de servidor — apontar as duas chaves para `config\`

Os **defaults** do código caem em `<telemetria>`, que fica dentro de `data\` — **fora** do share
`kitbrf`. Para manter as listas num lugar previsível e protegido, recomenda-se apontar **ambas** as
chaves para `C:\Friozem\importacao_notas\config\` no `appsettings.Production.json`, por exemplo:

- `Email:CaminhoDestinatarios` → `config\destinatarios-fechamento.txt`
- `Monitoramento:CaminhoDestinatarios` → `config\destinatarios-resumo.txt`

Confirme os valores efetivos em produção antes de supor onde os arquivos estão (ver lacuna no
resumo). Lembre que `config\` tem ACL restrita (seção 13) e que as listas reais não são
versionadas.

## 8. Dia/hora do resumo, janela de coleta e limpeza

Todas na config não-secreta (`appsettings.Production.json`), reinicie o serviço após alterar:

| O que | Chave | Padrão |
|---|---|---|
| Dia do resumo semanal | `Monitoramento:DiaDaSemana` | `Monday` |
| Hora do resumo semanal | `Monitoramento:Hora` | `"12:00"` |
| Janela de coleta do pacote | `Pacote:JanelaColetaSegundos` | `10` |
| Limpeza ligada/desligada | `Limpeza:Habilitada` | `true` |
| Dias de retenção (limpeza) | `Limpeza:DiasRetencao` | `10` |

Outras chaves de `Pacote` úteis: `IntervaloVarreduraSegundos` (5), `EstabilizacaoSegundos` (3),
`TimeoutArquivoSegundos` (300), `BackoffMaximoSegundos` (120), `MaxTransportesConcorrentes` (1).

A limpeza roda na partida e a cada 24 h, atua **apenas** nas subpastas de `Processados` e `Erro`,
e **nunca** toca a entrada, o `Estado`, a telemetria ou os `relatorios`.

## 9. Publicar nova versão e rollback

**Publicar uma nova release** (no servidor) com `deploy\publicar-release.ps1`. Informe a origem
(`-ReleaseZip` **ou** `-ReleaseDir`, nunca os dois):

```powershell
powershell -ExecutionPolicy Bypass -File .\publicar-release.ps1 -ReleaseZip C:\temp\rel.zip -Versao 2026.02.01-1
```

- `-ManterUltimas` (padrão `5`): quantas releases antigas preservar em `app\releases`.
- Faz backup do `current` anterior em `backups\current_<carimbo>.zip`, copia a config, aponta
  `app\current` para a nova release e reinicia o serviço.

**Rollback** para uma release anterior com `deploy\rollback.ps1`:

```powershell
powershell -ExecutionPolicy Bypass -File .\rollback.ps1
powershell -ExecutionPolicy Bypass -File .\rollback.ps1 -Versao 2026.01.15-1
```

- Sem `-Versao`, usa a **release anterior** (a segunda mais recente por data).
- Para o serviço, copia a config, reaponta `current` e reinicia.

Ambos aceitam `-WhatIf` para simular antes de executar.

## 10. Estado e telemetria (não mexer)

O **Estado** (diário do pacote `pacote-atual.jsonl`) e a **telemetria**
(`telemetria.jsonl`, `servico.jsonl`, `ultimo-resumo.txt`, resumos em `resumos\`) ficam **fora do
share**, previstos em `C:\Friozem\importacao_notas\data`. Eles ficam **invisíveis** aos usuários
de propósito.

- **Não edite nem apague** esses arquivos com o serviço rodando: o diário é a **fonte de verdade**
  para retomar um pacote após reinício sem reenviar notas ao WMS; a telemetria alimenta o resumo
  semanal.
- A limpeza automática **nunca** toca essas pastas.

> Nota de configuração: o caminho físico de `Estado`/telemetria depende de
> `Pastas:Estado`/`Monitoramento:CaminhoTelemetria`. Para ficarem em
> `C:\Friozem\importacao_notas\data` (fora do share), esses valores precisam ser **caminhos
> absolutos** no `appsettings.Production.json`. Confirme o valor efetivo em produção antes de supor
> onde estão (ver lacuna no resumo).

## 11. O serviço não sobe — diagnóstico e TLS 1.2

Passos na ordem:

1. **Veja os logs** (seção 3), especialmente `worker.err.log`. A mensagem
   `Serviço não iniciado: corrija a seção 'Pastas' do appsettings.` indica config de `Pastas`
   inválida (por exemplo `Estado` igual a `Processados`/`Erro`).
2. **Confirme as variáveis de máquina** do WMS: o serviço lê `WMS__Usuario`/`WMS__Senha` das env de
   **máquina**; se foram gravadas e o serviço não reiniciou, ele ainda não as herdou — reinicie.
3. **Rode o diagnóstico** (somente leitura, sempre termina sem erro):

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\diagnostico.ps1
   ```

   Ele checa serviço, host/porta do WMS (`friozem.assistsoftware.com.br:8888`), SMTP
   (`smtp.friozem.com.br:587`), a pasta de entrada e o share `kitbrf`, e lê o registro do SChannel
   (TLS).
4. **Falha de conexão HTTPS ao WMS** (handshake/TLS): em Windows Server 2012 R2 o TLS 1.2 depende do
   SChannel do sistema. Para habilitar o TLS 1.2 há a opção no instalador:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\instalar.ps1 -HabilitarTls12 ...
   ```

   O procedimento completo de TLS 1.2 está em [`docs\INSTALACAO-SERVIDOR.md`](../INSTALACAO-SERVIDOR.md)
   (seções 3.6/6). Não há uma mensagem fixa de "handshake" no log — o sintoma é a falha de conexão
   HTTPS ao WMS.

## 12. Tabela: "Quero fazer X → arquivo/comando → reinicia?"

| Quero fazer | Arquivo / comando | Precisa reiniciar o serviço? |
|---|---|---|
| Ver situação do serviço | `servico.ps1 -Acao Status` | — |
| Iniciar / parar / reiniciar | `servico.ps1 -Acao Start\|Stop\|Restart` | — (é a própria ação) |
| Ver logs | `logs\worker.out.log` / `worker.err.log` | Não |
| Trocar usuário/senha do WMS | `configurar-segredos.ps1` (env de máquina) | **Sim** |
| Trocar SMTP (usuário/senha/remetente) | `configurar-segredos.ps1` ou editar `config\email.settings.json` | Recarrega ao salvar; na dúvida reinicie |
| Trocar URL do WMS | editar `Wms:BaseUrl` em `appsettings.Production.json` | **Sim** |
| Destinatários do fechamento | editar o TXT `destinatarios-fechamento.txt` (`Email:CaminhoDestinatarios`); `Email:Destinatarios` no JSON é só fallback | Não (vale no próximo envio) |
| Destinatários do resumo semanal | editar o TXT `destinatarios-resumo.txt` (`Monitoramento:CaminhoDestinatarios`) | Não (vale no próximo envio) |
| Dia/hora do resumo | `Monitoramento:DiaDaSemana`/`Hora` | **Sim** |
| Janela de coleta / limpeza (10 dias) | `Pacote:JanelaColetaSegundos` / `Limpeza:DiasRetencao` | **Sim** |
| Publicar nova versão | `publicar-release.ps1` | Automático (o script reinicia) |
| Rollback | `rollback.ps1` | Automático (o script reinicia) |
| Diagnosticar | `diagnostico.ps1` | Não (só leitura) |

## 13. Avisos de segurança

- **Nunca** versione nem compartilhe senhas (WMS ou SMTP). As credenciais do WMS ficam em env de
  máquina; o SMTP em `email.settings.json`, que está no `.gitignore`.
- A pasta `config\` tem **ACL restrita** (apenas Administrators/SYSTEM e a conta do serviço leem).
  Os scripts reaplicam essa ACL; não afrouxe as permissões.
- Os scripts **não** exibem segredos no console nem nos logs — mantenha esse comportamento.
- Prefira `-WhatIf` em scripts de escrita quando estiver em dúvida, para simular antes de aplicar.

## 14. Guia rápido: usar a IA de apoio

Se você decidir usar um agente de IA para **diagnosticar falhas** ou **propor alterações**, o guia
completo (fluxo, mapa de arquivos, como diagnosticar e como propor mudanças com segurança) está em
[`./MANUAL-AGENTE-IA.md`](./MANUAL-AGENTE-IA.md). Usar a IA é **opcional**: você decide.

Antes de aceitar qualquer sugestão da IA, confira estes **guarda-rails**:

- **Não versione nem exponha segredos** do WMS/SMTP (as env `WMS__*` e o `email.settings.json`
  continuam sensíveis; nada de colar senha em prompt, log ou resposta).
- **Não ligue a I60** (`I60:Habilitada` deve seguir `false`; os dados fiscais vão na I50).
- **Valide mudança de código** com build + test **no repositório, fora de produção**, antes de
  promover.
- **Não rode teste de carga** em produção ou homologação sem o **OK do responsável**.
- **Config antes de código**: prefira ajustar configuração (JSON e os TXT de destinatários) a mexer
  no código; e deixe a **execução no servidor** (restart, publicar, rollback) para o técnico humano.
