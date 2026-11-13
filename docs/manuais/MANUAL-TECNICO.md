# Manual Técnico (Nível 1 / Júnior) — KitWms Integrador

<!--CAPA:tecnico-->

Guia de **operação do dia a dia** para o técnico humano no servidor **SRVAPPALC02** (Windows Server
2012 R2, PowerShell 4.0/5.1). O passo a passo pressupõe **acesso LOCAL ao servidor por RDP (Área de
Trabalho Remota) ou direto na máquina**, abrindo o **PowerShell como Administrador** no próprio
servidor. (Alternativa avançada: os scripts também rodam por WinRM com `Invoke-Command`, mas este
manual foca no acesso local.)

Depois que o sistema está em produção, o técnico **opera a partir do servidor**. Todos os scripts de
operação vivem de forma **permanente** em **`C:\Friozem\importacao_notas\tools\deploy`** — essa é a
pasta de trabalho do dia a dia. Rode o PowerShell como Administrador, entre nessa pasta e use sempre:

```powershell
cd C:\Friozem\importacao_notas\tools\deploy
powershell -ExecutionPolicy Bypass -File .\<script>.ps1 <parâmetros>
```

> A **instalação completa** (criar as pastas, publicar a primeira release, configurar o serviço
> NSSM, compartilhar a entrada, virar para produção) **não** está aqui: ela vive em
> [`docs\INSTALACAO-SERVIDOR.md`](../INSTALACAO-SERVIDOR.md). Este manual cobre só a **operação
> recorrente** no servidor. O `C:\temp\deploy` só aparece na **primeira instalação** (ver o guia de
> instalação); no dia a dia use sempre `tools\deploy`.

## Índice

0. [Ambiente: servidor x máquina de build (local)](#0-ambiente-servidor-x-máquina-de-build-local)
1. [Dados do ambiente](#1-dados-do-ambiente)
2. [Iniciar / parar / reiniciar / status do serviço](#2-iniciar--parar--reiniciar--status-do-serviço)
3. [Ver e interpretar os logs](#3-ver-e-interpretar-os-logs)
4. [Trocar usuário e senha do WMS](#4-trocar-usuário-e-senha-do-wms)
5. [Trocar usuário / senha / remetente do SMTP](#5-trocar-usuário--senha--remetente-do-smtp)
6. [Trocar o endpoint/URL do WMS (virada homolog → produção)](#6-trocar-o-endpointurl-do-wms-virada-homolog--produção)
7. [Destinatários de e-mail (fechamento e resumo semanal)](#7-destinatários-de-e-mail-fechamento-e-resumo-semanal)
8. [Dia/hora do resumo, janela de coleta e limpeza](#8-diahora-do-resumo-janela-de-coleta-e-limpeza)
   - Janela de silêncio da coleta e retenção de 30 dias (logo após a tabela da seção 8)
   - [8.1. Arquivos não-XML (ExtensaoInvalida) e a flag `Pastas:TratarNaoXmlComoErro`](#81-arquivos-não-xml-extensaoinvalida-e-a-flag-pastastratarnaoxmlcomoerro)
   - [8.2. Ligar/desligar o envio da placa do veículo (`I50:EnviarPlacaVeiculo`)](#82-ligardesligar-o-envio-da-placa-do-veículo-i50enviarplacaveiculo) (Puxada normal com placa, TSP sem placa)
   - 8.3. I60 (associação automática da nota de venda)
   - [8.4. Puxada e Puxada TSP: pastas, ZIP e nome da onda](#84-puxada-e-puxada-tsp-pastas-zip-e-nome-da-onda)
   - [8.5. I51 como gatilho da I60: retorno da separação, associação e relatório diário](#85-i51-como-gatilho-da-i60-retorno-da-separação-associação-e-relatório-diário) (inclui pré-requisitos do WMS, alerta imediato e calibração do ENC)
   - [8.6. Última alternativa: tela de devolução pela I40 (seção removível)](#86-última-alternativa-tela-de-devolução-pela-i40-seção-removível) (inclui o login simples)
9. [Publicar nova versão e rollback](#9-publicar-nova-versão-e-rollback)
10. [Estado e telemetria (não mexer)](#10-estado-e-telemetria-não-mexer)
11. [O serviço não sobe — diagnóstico e TLS 1.2](#11-o-serviço-não-sobe--diagnóstico-e-tls-12)
12. [Tabela: "Quero fazer X → arquivo/comando → reinicia?"](#12-tabela-quero-fazer-x--arquivocomando--reinicia)
13. [Avisos de segurança](#13-avisos-de-segurança)
14. [Guia rápido: usar a IA de apoio](#14-guia-rápido-usar-a-ia-de-apoio)

---

## 0. Ambiente: servidor x máquina de build (local)

Há **dois ambientes** diferentes, e a maior parte das tarefas acontece só no **servidor**. Não
confunda as pastas de um com as do outro.

### Servidor `SRVAPPALC02` (produção) — onde você opera no dia a dia

É onde o serviço roda e onde o técnico **trabalha** (via RDP). A estrutura do app fica em
`C:\Friozem\importacao_notas`:

- `app\current` — a release ativa (o NSSM aponta aqui); releases antigas em `app\releases\<versao>`.
- `config\` — `appsettings.Production.json`, `email.settings.json` e os dois TXT de destinatários
  (ACL restrita).
- `data\` — **Estado** (diário do pacote) e **telemetria**, fora do share.
- `logs\` — `worker.out.log` / `worker.err.log`.
- `backups\` — zips do `current` anterior a cada virada.
- `tools\deploy\` — **os scripts de operação** (`servico.ps1`, `publicar-release.ps1`,
  `rollback.ps1`, `configurar-segredos.ps1`, `diagnostico.ps1`, `remover.ps1`, etc.). É a **fonte de
  verdade** dos scripts no servidor.

A **entrada** compartilhada fica em `C:\Friozem\kitbrf` (share SMB `kitbrf`). Para operar: abra o
**PowerShell como Administrador** no servidor e vá para **`C:\Friozem\importacao_notas\tools\deploy`**.

### Máquina de build (local, opcional/avançado) — só para gerar nova versão

É o **repositório com o código-fonte** (`src\`) e os **scripts-fonte** (`deploy\`). Serve **apenas**
para **gerar uma nova versão** quando houver mudança de código: rode `deploy\publicar.ps1`, que
produz o `.zip` em `artifacts\`. A **maioria das tarefas de operação NÃO precisa** da máquina de
build.

> As pastas `deploy\` e `artifacts\` são do **repositório local** (máquina de build), **não** existem
> no servidor. No servidor, os scripts de operação ficam em `tools\deploy` (não em `deploy\`) e não
> há `artifacts\`. Se você está no servidor e vê uma instrução com `deploy\` ou `artifacts\`, ela é
> da máquina de build — no servidor, o equivalente é `tools\deploy`.

### Onde estou? → o que uso

| Onde estou | O que uso |
|---|---|
| **No servidor (RDP)** — operação do dia a dia | `C:\Friozem\importacao_notas\tools\deploy` (ex.: `cd C:\Friozem\importacao_notas\tools\deploy ; .\servico.ps1 -Acao Status`) |
| **Na máquina de build** — gerar nova versão (mudança de código) | repositório: `deploy\publicar.ps1` → gera o `.zip` em `artifacts\<versao>\` |

---

## 1. Dados do ambiente

| Item | Valor |
|---|---|
| Servidor | `SRVAPPALC02` / `200.200.200.213` (Windows Server 2012 R2) |
| Raiz do app | `C:\Friozem\importacao_notas` |
| Subpastas do app | `app\releases\<versao>`, `app\current`, `config`, `data`, `logs`, `backups`, `tools\deploy` (scripts de operação) |
| Pasta de trabalho (RDP) | `C:\Friozem\importacao_notas\tools\deploy` (onde você roda os scripts) |
| Serviço (NSSM) | `Friozem-ImportacaoNotas` |
| nssm.exe | `C:\Friozem\Gestao_Enderecos\tools\nssm.exe` (já embutido como default `-NssmPath`) |
| Pasta de entrada (local) | `C:\Friozem\kitbrf` (share SMB `kitbrf`: `\\SRVAPPALC02\kitbrf`) |
| Pastas de depósito | `C:\Friozem\kitbrf\Puxada` (puxada normal) e `C:\Friozem\kitbrf\PuxadaTSP` (puxada TSP, ZIP ou XML solto); ver seção 8.4 |
| Config não-secreta | `C:\Friozem\importacao_notas\config\appsettings.Production.json` |
| Segredos SMTP | `C:\Friozem\importacao_notas\config\email.settings.json` |
| Segredos WMS | variáveis de ambiente de **MÁQUINA** `WMS__Usuario` / `WMS__Senha` |
| Logs do serviço | `C:\Friozem\importacao_notas\logs\worker.out.log` e `worker.err.log` |
| Manuais (cópia no servidor) | `C:\Friozem\importacao_notas\manuais` (cópia feita na instalação) |

Os **manuais** (este Manual Técnico e o Manual do Usuário, em HTML e PDF) ficam no servidor em
`C:\Friozem\importacao_notas\manuais` — uma cópia feita na instalação, para consulta local sem
precisar do repositório.

No servidor, os scripts de operação ficam de forma **permanente** em
`C:\Friozem\importacao_notas\tools\deploy` (fonte de verdade; `instalar.ps1` os replica para lá e
`publicar-release.ps1` os atualiza a cada release). Opere sempre a partir dessa pasta. Defaults já
embutidos em todos eles: `-RaizInstalacao C:\Friozem\importacao_notas`,
`-NomeServico Friozem-ImportacaoNotas`, `-NssmPath C:\Friozem\Gestao_Enderecos\tools\nssm.exe`. Os
scripts que escrevem aceitam `-WhatIf` (simula sem alterar nada).

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

Use `servico.ps1` a partir de `C:\Friozem\importacao_notas\tools\deploy`. O parâmetro `-Acao` é
**obrigatório** e aceita exatamente `Start`, `Stop`, `Restart` ou `Status`:

```powershell
cd C:\Friozem\importacao_notas\tools\deploy
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
| `Pacote aberto (<id>): N arquivo(s) na entrada; janela de silêncio de 15s (teto de 180s).` | Um pacote abriu. No modo `Fixa` a linha diz "janela de coleta fixa de 10s". |
| `Fim da coleta por <motivo> após Ns.` | A coleta fechou por silêncio, por teto ou por janela fixa, e quanto durou (seção 8). |
| `I60 precisa de atencao: N nota(s) neste ciclo (e-mail: ...).` e `I60 atencao: nota ... serie ... situacao ... motivo ...` | Alerta imediato da I60 (Warning). O texto do WMS está em `Estado\i51\alertas` (seção 8.5). |
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
`WMS__Usuario` / `WMS__Senha`. Troque com `configurar-segredos.ps1` (ele **nunca** exibe os
valores; se você não passar, ele pede sem eco), a partir de
`C:\Friozem\importacao_notas\tools\deploy`:

```powershell
cd C:\Friozem\importacao_notas\tools\deploy
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
(`Host=smtp.friozem.com.br`, `Porta=587`, `UsarTls=false`, `Habilitado=true`), a partir de
`C:\Friozem\importacao_notas\tools\deploy`:

```powershell
cd C:\Friozem\importacao_notas\tools\deploy
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
| Modo da janela de coleta | `Pacote:ModoJanela` | `Silencio` (ou `Fixa`) |
| Silêncio para fechar a coleta (modo `Silencio`) | `Pacote:SilencioSegundos` | `15` |
| Teto da coleta desde a abertura (modo `Silencio`) | `Pacote:JanelaMaximaSegundos` | `180` |
| Janela fixa de coleta (só no modo `Fixa`) | `Pacote:JanelaColetaSegundos` | `10` |
| Limpeza ligada/desligada | `Limpeza:Habilitada` | `true` |
| Dias de retenção (limpeza) | `Limpeza:DiasRetencao` | `30` (era 60 até 13/11/2026) |

Outras chaves de `Pacote` úteis: `IntervaloVarreduraSegundos` (5), `EstabilizacaoSegundos` (3),
`TimeoutArquivoSegundos` (300), `BackoffMaximoSegundos` (120), `MaxTransportesConcorrentes` (1).

### Janela de coleta: silêncio (padrão) ou fixa

Desde a release **2026.11.13-1** a coleta do pacote usa a **janela de silêncio** (`Pacote:ModoJanela` =
`Silencio`). Motivo: lotes grandes copiados aos poucos (de 06 a 13/11) eram divididos em 2 a 4 pacotes e
relatórios pela janela fixa de 10 s.

- **Como fecha:** a coleta termina quando passam `SilencioSegundos` (15 s) sem chegar arquivo novo nem
  mudar o tamanho/data de um arquivo existente, ou quando a coleta chega ao teto `JanelaMaximaSegundos`
  (180 s) contado da abertura do pacote. Cada arquivo novo ou alterado reinicia a contagem do silêncio.
  Lote pequeno fecha em cerca de 15 s; lote grande não é cortado; nada passa de 3 minutos.
- **Silêncio efetivo:** nunca é menor que `EstabilizacaoSegundos` (3 s). Se a config pedir menos, o
  serviço registra um aviso na partida e usa o mínimo.
- **No teto:** um arquivo ainda instável (em cópia) fica para o próximo pacote, como antes.
- **Vale para** a raiz, `Puxada` e `PuxadaTSP` (XML solto da TSP também cai num pacote só).
- **Voltar ao comportamento antigo sem nova release:** em `config\appsettings.Production.json` ponha
  `"Pacote": { "ModoJanela": "Fixa" }` e reinicie o serviço. No modo `Fixa` vale só
  `JanelaColetaSegundos` (10 s fixos desde a primeira detecção) e a chegada de arquivos não adia a janela.
- **No log:** `Pacote aberto` informa a janela e `Fim da coleta por ...` informa o motivo (silêncio, teto
  ou janela fixa) e a duração. No relatório a etapa aparece como "Coleta (janela de silêncio)" (telemetria
  `EsperaJanelaMs`, ver 10.1).

Override por variável de ambiente: `Pacote__ModoJanela`, `Pacote__SilencioSegundos`,
`Pacote__JanelaMaximaSegundos`.

### Limpeza e retenção de 30 dias

A limpeza roda na partida e a cada 24 h, atua **apenas** nas subpastas de `Processados` e `Erro`,
e **nunca** toca a entrada, o `Estado`, a telemetria ou os `relatorios`. Como a subpasta
`Erro\<pacote>\ExtensaoInvalida` (arquivos não-XML; ver seção 8.1) fica dentro de `Erro`, ela
**já é coberta** por essa limpeza de 30 dias — a pasta inteira do pacote é removida no vencimento,
sem configuração extra.

**Retenção ajustada de 60 para 30 dias em 13/11/2026** (`Limpeza:DiasRetencao=30` e
`I51:DiasRetencaoEstado=30`; a faixa válida continua de 7 a 3650). Impactos a conhecer:

- **Tela de devolução (I40):** só acha notas dos **últimos 30 dias**; mais antigas retornam "Nota não
  encontrada na retenção de 30 dias".
- **Estado da I51** (`Estado\i51`): XML copiado, brutos e notas **terminais** (associada/rejeitada) também
  expiram em 30 dias; notas não terminais nunca são apagadas.
- Os logs de `Estado\i51\alertas` seguem a mesma retenção (`I51:DiasRetencaoEstado`).
- `RegistroOndasTsp` (hash das ondas TSP, 90 dias) **não mudou**.

### 8.1. Arquivos não-XML (ExtensaoInvalida) e a flag `Pastas:TratarNaoXmlComoErro`

Quando um arquivo depositado na entrada **não** é `.xml`, ele não é uma nota: o sistema **não**
chama o WMS para ele. A separação acontece **na detecção** (ao listar os candidatos do pacote), em
três grupos:

- **XML** (`.xml`): segue o fluxo normal de integração.
- **Temporários (em cópia)**: extensões configuráveis em `Pastas:ExtensoesTemporarias` (default
  `.tmp`, `.temp`, `.crdownload`, `.part`, `.partial`, `.filepart`, `.!ut`, `.opdownload`, `.bak`,
  `.lock`; também arquivos com atributo Hidden/System). São **ignorados** — nem processa nem move,
  ficam na entrada até a cópia terminar (um `.tmp` pode virar `.xml`). Comparação case-insensitive,
  com o ponto normalizado.
- **Não-XML definitivo**: qualquer outra extensão. Com `Pastas:TratarNaoXmlComoErro = true`
  (**default**), o arquivo é movido para `Erro\<pacote>\ExtensaoInvalida` com um `.motivo.txt` ao
  lado e registrado no relatório. Com a flag em `false`, o não-XML definitivo é apenas **ignorado**
  (fica na entrada), como no comportamento legado.

Reflexos nas estruturas:

- **`CategoriaErro.ExtensaoInvalida`**: nova categoria de erro; define a subpasta
  `Erro\<pacote>\ExtensaoInvalida` (reusa o mesmo mecanismo de mover + `.motivo.txt` das demais
  categorias).
- **`StatusLinhaRelatorio.ArquivoIgnorado`**: novo status, com **cor neutra/cinza** (`#6B7280`,
  fora da paleta da marca) e rótulo **"Arquivo ignorado (não é XML)"**. No resumo do relatório e do
  e-mail há um contador próprio **"Arquivos ignorados"**. Esse status é um **aviso**, não uma falha
  de WMS: não conta como nota real (não infla `Total` nem `Não integradas`).

**Regra de e-mail do pacote 100% não-XML**: um pacote que só tem arquivos ignorados (nenhuma nota
real, XML válido ou inválido-de-conteúdo) **gera o relatório** HTML + PDF normalmente, mas **não
envia e-mail** (o log registra que o pacote não tinha notas, apenas arquivos ignorados). Um pacote
**misto** (ao menos uma nota real) segue a política de e-mail normal, e os arquivos ignorados
aparecem no relatório e no corpo do e-mail.

**Limpeza forçada sob demanda** — `limpar-extensao-invalida.ps1`: para apagar as pastas
`ExtensaoInvalida` **sem esperar** o ciclo de 30 dias, use esse script a partir de
`C:\Friozem\importacao_notas\tools\deploy` (ele é replicado para lá pelo `instalar.ps1`):

```powershell
cd C:\Friozem\importacao_notas\tools\deploy
powershell -ExecutionPolicy Bypass -File .\limpar-extensao-invalida.ps1 -WhatIf
powershell -ExecutionPolicy Bypass -File .\limpar-extensao-invalida.ps1
```

Ele só remove pastas cujo nome-folha seja exatamente `ExtensaoInvalida` **dentro** de
`<Entrada>\Erro`, com guardas de caminho (recusa raiz de drive, não segue junctions/symlinks),
suporta `-WhatIf` (lista sem apagar) e pede confirmação `s/n` quando não usa `-Force`. Parâmetros:
`-Caminho` (entrada, default `C:\Friozem\kitbrf`), `-NomePastaErro` (default `Erro`),
`-NomeCategoria` (default `ExtensaoInvalida`) e `-Force` (pula a confirmação).

### 8.2. Ligar/desligar o envio da placa do veículo (`I50:EnviarPlacaVeiculo`)

O integrador pode enviar ao WMS, no payload **I50**, a **placa do veículo de transporte** lida do
XML da nota em **`transp/veicTransp/placa`**. Esse envio é controlado pela chave
**`I50:EnviarPlacaVeiculo`** na config não-secreta
`C:\Friozem\importacao_notas\config\appsettings.Production.json`:

| Valor | Efeito |
|---|---|
| `true` (valor da release 2026.11.13-1) | Envia a placa exatamente como está no XML (`transp/veicTransp/placa`). |
| `false` | Envia a placa **em branco** (`placaVeiculo = ""`). |

A placa é enviada **como está** no XML, sem maiúsculas/minúsculas forçadas, sem hífen e sem outras
transformações além do básico.

**Resumo da decisão (corrigida em 13/11/2026): Puxada normal COM placa, Puxada TSP SEM placa.** O
`false` que apareceu em 13/11 foi só no teste local; o valor de produção é `EnviarPlacaVeiculo=true`.

**Puxada TSP:** a TSP **sempre** vai ao WMS **sem placa**. Notas da pasta `PuxadaTSP` (ZIP ou XML solto)
usam a chave própria **`I50:EnviarPlacaVeiculoTsp`** (padrão **`false`** = `placaVeiculo` em branco), e
**não** a `I50:EnviarPlacaVeiculo`. Ou seja, mesmo com `EnviarPlacaVeiculo = true` a TSP segue sem placa. A
Puxada normal (pasta `Puxada` e raiz) é governada só por `I50:EnviarPlacaVeiculo`.

| Nota | Chave que decide | Valor na release 2026.11.13-1 |
|---|---|---|
| PUXADA (pasta `Puxada` e raiz) | `I50:EnviarPlacaVeiculo` | `true` (com placa do XML) |
| TSP (pasta `PuxadaTSP`) | `I50:EnviarPlacaVeiculoTsp` | `false` (sempre sem placa) |

Override por variável de ambiente: `I50__EnviarPlacaVeiculo` e `I50__EnviarPlacaVeiculoTsp`.

Valores esperados em produção (`config\appsettings.Production.json`):

```json
"I50": {
  "EnviarPlacaVeiculo": true,
  "EnviarPlacaVeiculoTsp": false
}
```

Para **desligar** o envio da placa da Puxada normal, deixe `EnviarPlacaVeiculo` em `false`. Para
**religar**, volte a `true` (ou remova a chave: o padrão do `appsettings.json` versionado é `true`).

> A chave também pode ser sobrescrita por **variável de ambiente de máquina**
> `I50__EnviarPlacaVeiculo` (dois underscores). Na dúvida, prefira editar o
> `appsettings.Production.json`.

> **Só envia se existir no XML.** A placa só vai preenchida quando a nota traz
> `transp/veicTransp/placa`. Se o XML não tiver esse campo, a placa vai **em branco** mesmo com a
> chave em `true` (não é erro).

> **Depois de editar, REINICIE o serviço** para a mudança valer:
>
> ```powershell
> cd C:\Friozem\importacao_notas\tools\deploy
> powershell -ExecutionPolicy Bypass -File .\servico.ps1 -Acao Restart
> ```

### 8.3. I60 (associação automática da nota de venda): desligada em produção

Situação: a I60 está **desligada nos arquivos versionados** (`I60:Habilitada=false`, padrão). O Plano A (a I60
associar sozinha a nota de venda ao DocCarga criado pela I50, logo depois da I50) foi **testado no WMS de homologação em 09/11 e 10/11 e não associou, porque o DocCarga ainda não estava separado**. A
solução implementada é enviar a I60 **só depois do retorno da separação (I51)**, descrita na seção 8.5,
e ela foi validada no homolog em 13/11/2026 (7 de 7 associadas).
Com `I51:Habilitada=false` (padrão) a I60 continua saindo junto da I50 quando `I60:Habilitada=true`.
Ligar em produção só com o OK do responsável (seção 8.5). Histórico completo do Plano A em `docs\wms-codigo-customizado\HISTORICO-PLANO-A.md`.

- **Como ligar (somente em teste/homologação, com OK do responsável):** defina `I60__Habilitada=true`
  (variável de ambiente, ou `I60:Habilitada` no `appsettings`) e reinicie o serviço.
- **Opções da I60:** `I60:TipoOperacaoNota` (padrão `INE`), `I60:QtdeVolumeZero` e
  `I60:QtdeTotalNotaZero`. Com `true`, a I60 envia `qtdeVolume = 0` e `qtdeTotalNota = 0`. Validado no
  homolog em 13/11/2026 (7 de 7 notas associadas): o DocCarga já separado tem volume 0, e a soma das
  caixas fazia o WMS recusar. Por isso o `appsettings.json` versionado e o exemplo de produção trazem as
  duas em `true` (o padrão da classe C# continua `false`; vale o que está no arquivo). Troca sem release:
  `I60__QtdeVolumeZero` / `I60__QtdeTotalNotaZero`.
- **Enviar só a I60 de um XML:** `--enviar-i60 <xml>` no Worker; mostra o JSON e grava envio e resposta
  em `envio-manual-i60\`.
- **Em produção, `I60:Habilitada=true` só junto com `I51:Habilitada=true`** (com a I51 desligada, a I60
  voltaria ao fluxo antigo, junto da I50, que não associa). Até o OK do responsável, mantenha as duas
  em `false`.

### 8.4. Puxada e Puxada TSP: pastas, ZIP e nome da onda

A entrada (`C:\Friozem\kitbrf`) tem duas subpastas de depósito, criadas pelo serviço se faltarem
(e criadas na instalação, ver `INSTALACAO-SERVIDOR.md`):

```text
kitbrf\
  Puxada\       <- ZIP (ou XML solto) de puxada normal  -> Tipo PUXADA, onda = transporte
  PuxadaTSP\    <- ZIP (ou XML solto) de puxada TSP     -> Tipo TSP, uma onda por ZIP (XMLs soltos: uma onda TSP-AAAAMMDD-NN)
  (raiz)        <- XML/ZIP solto: aceito como PUXADA, Origem "raiz" (se AceitarRaiz=true)
  Processados\, Erro\, Estado\  <- como antes (não são monitoradas como entrada)
```

Chaves (reinicie o serviço após alterar; override por variável de ambiente com `__`):

| O que | Chave | Padrão |
|---|---|---|
| Nome da pasta da puxada normal | `Pastas:Puxada` | `Puxada` |
| Nome da pasta da puxada TSP | `Pastas:PuxadaTsp` | `PuxadaTSP` |
| Aceitar arquivos soltos na raiz (como PUXADA) | `Pastas:AceitarRaiz` | `true` |
| Máximo de arquivos dentro de um ZIP | `Zip:MaxArquivos` | `1000` |
| Tamanho máximo descompactado de um ZIP, em MB | `Zip:MaxTamanhoMb` | `500` |

Com `Pastas:AceitarRaiz=false`, arquivos soltos na raiz ficam parados na entrada (sem erro e sem
processamento). Os nomes das duas pastas não podem repetir nem ficar dentro de `Processados`, `Erro`
ou `Estado`; o serviço não inicia com configuração inválida (erro no log).

Como o ZIP é tratado:

- O ZIP só é aberto quando está pronto (mesmo detector de arquivo pronto dos XMLs).
- É extraído para `Estado\tmp\<id>` (apagado ao final). Só entram arquivos `.xml`; outro tipo vira
  `ExtensaoInvalida`; ZIP dentro de ZIP não é aberto; nomes com `..` ou caminho absoluto são ignorados.
- Passou de `Zip:MaxArquivos` ou `Zip:MaxTamanhoMb`, está corrompido, protegido por senha ou sem
  nenhum XML: o ZIP vai para `Erro\pacote_*\ZipInvalido` com `.motivo.txt`.
- O mesmo XML repetido em dois ZIPs do pacote é processado uma vez; o repetido aparece no relatório
  como "Arquivo ignorado (XML repetido)".
- Os XMLs e o ZIP original ficam juntos em `Processados\pacote_*\`.
- Na `PuxadaTSP`, XML solto é aceito: os XMLs soltos do pacote formam **uma onda** `TSP-AAAAMMDD-NN` (sem título,
  sequencial do dia compartilhado com as ondas de ZIP), sem reaproveitamento por hash (reprocessar gera novo
  sequencial). Arquivo que não seja ZIP nem XML vai para `ExtensaoInvalida`. Só `.zip` é suportado (sem `.rar`/`.7z`).
- Subpastas dentro do ZIP são lidas em qualquer nível (ZIP dentro de ZIP não é aberto).
- Os arquivos de `Puxada`, `PuxadaTSP` e raiz entram na mesma janela de silêncio do pacote (seção 8): um lote
  grande de ZIPs copiados aos poucos continua sendo um pacote só.
- Placa: a `Puxada` (e a raiz) vai com a placa do XML (`I50:EnviarPlacaVeiculo=true`); a `PuxadaTSP` vai sem placa
  (`I50:EnviarPlacaVeiculoTsp=false`). Ver 8.2.

Nome da onda da TSP: `TSP-AAAAMMDD-NN-NOME` (ex.: `TSP-20261111-01-SOROCABA`).

- `AAAAMMDD` é a data do servidor; `NN` é o sequencial do dia (reinicia a cada dia).
- `NOME` vem do nome do ZIP, sem extensão, sem as palavras `XML`/`XMLS`, em maiúsculas, sem acento,
  só letras, números e hífen, cortado em 14 caracteres (o campo da onda no WMS aceita até 30).
  Sem nome aproveitável, a onda é `TSP-AAAAMMDD-NN`.
- O sequencial e o reaproveitamento ficam em `Estado\tsp\ondas.json`. Reenviar o **mesmo** ZIP
  (mesmo conteúdo) reaproveita a mesma onda. Não apague esse arquivo com o serviço rodando.
- A nota da TSP sem transporte no `infCpl` sobe normalmente; o relatório mostra "(sem)".

No relatório, a coluna **Tipo** (`PUXADA` ou `TSP`) e a coluna **Origem** (`Puxada`, `PuxadaTSP` ou
`raiz`) permitem conferir se o ZIP foi para a pasta certa. A TSP é agrupada por onda; a puxada, por
transporte. O resumo do e-mail e o resumo semanal contam as notas por tipo e as da raiz.

### 8.5. I51 como gatilho da I60: retorno da separação, associação e relatório diário

**Status:** implementado, testado com WMS simulado (926 testes aprovados e 9 ignorados no build da release, 0
avisos) e **validado no WMS de homologação em 13/11/2026: 7 de 7 notas associadas** (retorno da I51, I60 `INE` e
associação). Roteiro e resultados: `docs\ROTEIRO-TESTE-I51-I60.md`. Escopo: `docs\ESCOPO-I51-I60.md`.
Nos arquivos versionados e no exemplo de produção, `I51:Habilitada`, `I60:Habilitada` e
`Devolucao:Habilitada` ficam `false`; ligar em produção só com o OK do responsável, as duas da I51/I60 juntas.

**Pré-requisitos no WMS de produção** (já existem no homolog; sem eles a I51 volta vazia e nada associa):

1. O **EDI `WEBAPI-I51`** configurado com o meio de retorno **`WEBSERVI`** (o identificador é configurável em
   `I51:Identificador`).
2. O **evento interno do WMS** que gera o retorno da separação. No homolog ele não tinha sido criado no início
   do teste de 13/11; depois de criado, o retorno passou a chegar e as 7 notas associaram.

**Defaults validados no homolog (13/11/2026):** `I60:QtdeVolumeZero=true` e `I60:QtdeTotalNotaZero=true`
(a I60 vai com volume 0 e total da nota 0; ver 8.3).

**Ciclo:** `I50 → separação (WMS) → I51 → emissão da nota → I60`. A I60 só associa DocCarga já
separado; por isso o integrador acompanha cada nota depois da I50 e só envia a I60 quando a I51
retorna. O retorno da I51 é um evento configurado no WMS (EDI com meio de retorno WEBSERVI) e só
existe com a separação concluída (não há separação parcial).

**Chaves de configuração** (`appsettings`, ou variável de ambiente com `__`; reinicie o serviço):

| Chave | Padrão | Para que serve |
|---|---|---|
| `I51:Habilitada` | `false` | Liga o fluxo. Desligada: nada novo roda, nada é gravado em disco e a I60 sai junto da I50. |
| `I60:Habilitada` | `false` | Liga o envio da I60. Para o fluxo completo, ligue as duas. |
| `I51:Identificador` | `WEBAPI-I51` | Identificador do EDI no WMS (o número pode mudar). |
| `I51:IntervaloMinutos` | `5` | De quantos em quantos minutos consulta o WMS. |
| `I51:HorasParaAtraso` | `24` | Sem retorno nesse prazo, a nota vira **Atrasada** e entra no relatório. |
| `I51:CampoUnico` | `Numero` | Campo usado na consulta UNICO: `Numero` (nota, 9 dígitos) ou `NroDocumento`. |
| `I51:DiasRetencaoEstado` | `30` | Retenção do estado terminal, XML copiado, brutos e logs de `alertas` (era 60 até 13/11/2026). |
| `I60:AlertaImediato` | `true` | Alerta imediato por e-mail, arquivo e log (ver abaixo). Só age com a I51 ligada. |
| `I60:QtdeVolumeZero` / `I60:QtdeTotalNotaZero` | `true` (arquivo) | `qtdeVolume = 0` e `qtdeTotalNota = 0` na I60 (validado no homolog). |
| `I60:MaxTentativasOutro` | `3` | Tentativas para uma resposta não reconhecida, antes de rejeitar a nota. |
| `I60:TipoOperacaoNota` | `INE` | Operação normal (nunca `IMB`). |
| `I60:TipoOperacaoEncerrado` | `IMP` | Operação do reenvio quando o DocCarga está encerrado (ENC). |
| `I60:TpAcaoReenvioImp` | `UPD` | `TpAcao` do reenvio como IMP (`UPD` ou `I`); confirmar no homolog. |
| `RelatorioI60:Hora` | `12:00` | Hora do relatório diário (horário local). |

As listas `I60:Padroes*` (textos de recusa do WMS, separados por `;`, sem diferenciar acento nem
maiúsculas) são configuráveis, porque o texto real do WMS ainda será confirmado no homolog.

**Consulta (TODOS e UNICO).** Só estes dois tipos, só com CNPJ e número:

- **TODOS** traz o que estiver disponível e **não devolve de novo o que já foi puxado**. Por isso a
  resposta bruta é gravada em `Estado\i51\brutos` **antes** de qualquer processamento. A primeira
  ativação consome o backlog da proprietária; documentos que não são nossos viram **órfãos**
  (`Estado\i51\orfaos`). O filtro de proprietário usa o `CnpjProprietario` do retorno.
- **UNICO** traz só o pesquisado e pode ser repetido: serve para recuperar notas atrasadas ou
  conferir uma nota. Manual: `Worker.exe --consultar-i51 --tipo UNICO --cnpj <cnpj> --numero <nNF>`
  (`TODOS` é recusado de propósito no comando manual, para não consumir o retorno).

**Estado por nota** (`Estado\i51\notas\<chave>.json`, escrita atômica, sobrevive a reinício):
`AguardandoSeparacao` → `Separada` → `I60Enviada` → `Associada` ou `Rejeitada`; `Atrasada` quando passa
do prazo. Guarda nota, série, chave, CNPJ, transporte, tipo (PUXADA ou TSP), onda e as datas de cada
transição. **Ondas TSP passam pelo mesmo fluxo.**

**I60 depois da separação.** Usa a **mesma NF-e (XML) da I50**, com as **quantidades ajustadas para as separadas** (original × separada fica registrado item a item), operação `INE`. O objetivo é garantir a
associação; a I60 serve à devolução depois, se necessário.

**DocCarga encerrado (ENC → IMP).** Quando o WMS recusa a I60 `INE` por DocCarga encerrado (texto
configurável em `I60:PadroesDocCargaEncerrado`), o integrador reenvia a I60 como **`IMP`** (só importa
a nota, sem associação automática) e **registra o erro "doccarga encerrado"** no estado e no relatório.
Essa nota nunca é marcada como Associada. A resposta "Nota já existe, TpAcao precisa ser UPD" gera um
reenvio com `UPD`. Falha de comunicação nunca rejeita a nota: há nova tentativa com espera crescente.
A detecção do ENC por texto de recusa é uma hipótese e **precisa ser confirmada no homolog**.

**Relatório diário (12:00).** HTML, PDF e e-mail, na identidade do "Relatório de Integração", gravado em
`<Relatorio:PastaSaida>\i60`. Mostra aguardando separação (com idade), separadas, I60 associadas, I60
rejeitadas com motivo (por exemplo doccarga encerrado, quantidade diferente), ajustes de quantidade
item a item e atrasadas. **Decisão sobre a visão por pacote:** ficou como **seção dentro do relatório diário**, sem relatório separado por pacote. O mesmo relatório lista as notas que ficaram em IMP ou
rejeitadas como **candidatas à devolução pela I40** (`RelatorioI60:ListarCandidatasI40`). Destinatários:
TXT `RelatorioI60:CaminhoDestinatarios` (um e-mail por linha); se não existir, usa a lista do resumo
semanal. Gerar na hora: `Worker.exe --relatorio-i60 [--enviar]`.

**Comandos úteis** (no servidor, em `app\`): `--consultar-i51` (só UNICO), `--reenfileirar-i60
[--motivo <motivo>] [--chave <chave>]` e `--relatorio-i60 [--enviar]`. O reenfileiramento grava um
pedido em `Estado\i51\comandos`, que o serviço em execução aplica no ciclo seguinte (sem reiniciar).

**Bloco Atenção no relatório diário.** O relatório (HTML, e-mail e PDF) abre com o bloco **Atenção**, antes de
"Saúde da consulta": ENC/IMP, rejeitadas, respostas não reconhecidas, atrasadas e total (estado atual das
notas). Notas `Separada` com resposta não reconhecida (`TentativasOutro >= 1`) entram também em "I60 com
problema". Zerado, o bloco diz que nenhuma nota exige atenção.

#### Alerta imediato (`I60:AlertaImediato`)

A cada ciclo da I51, logo depois do passo da I60, o Worker varre as notas e avisa
**uma vez por nota e por evento**. É por estado (não por evento), então reiniciar o serviço não repete o aviso: o carimbo fica no
JSON da nota (`AlertaI60Em` e `AlertaAtrasoEm`).

| Evento | Quando |
|---|---|
| `Rejeitada` | Qualquer motivo (outro, volume ou quantidade diferente, já existe, doccarga encerrado sem reenvio, XML indisponível, sem quantidade separada). |
| ENC/IMP ou pendente | I60 enviada que **não virou** `Associada` (IMP/ENC, pendente na interface, pendente com ressalva). |
| `NaoReconhecida` | Nota `Separada` com resposta do WMS que o integrador não reconhece, ainda em tentativas. |
| `Atrasada` | Sem retorno da separação em `I51:HorasParaAtraso`. |

`Associada` nunca alerta. Detalhes:

- **E-mail em lote:** todas as notas do ciclo num e-mail só, assunto `[KitWms] I60 precisa de atenção: N
  nota(s)` (prefixo de `RelatorioI60:AssuntoPrefixo`). Por nota: número, série, transporte, onda, situação,
  motivo e texto do WMS (mensagem + ressalvas, até 300 caracteres, sem tokens, senhas nem corpo de nota).
- **Destinatários:** os mesmos do relatório diário da I51/I60: `destinatarios-i60.txt` (apontado por
  `RelatorioI60:CaminhoDestinatarios`; vazio = `<telemetria>\destinatarios-i60.txt`), relido a cada envio;
  sem ele, a lista do resumo semanal; sem ela, `ti@friozem.com.br`. Em produção, ponha o arquivo em
  `C:\Friozem\importacao_notas\config\destinatarios-i60.txt` e aponte a chave.
- **Sem SMTP** (`Email:Habilitado=false`, sem `Host` ou sem `Remetente`): grava arquivo e log, carimba a nota
  e não envia. **Falha de e-mail:** vira Warning e a nota é carimbada mesmo assim (o texto continua no
  arquivo de alertas e no relatório diário).
- **Log (Warning):** `I60 precisa de atencao: N nota(s) neste ciclo (e-mail: ...)` e, por nota, `I60
  atencao: nota ... serie ... situacao ... motivo ...`.
- **Arquivo de calibração:** `Estado\i51\alertas\AAAA-MM-DD.log`, uma linha por evento, colunas separadas
  por tabulação: horário local, evento, nota, série, transporte, onda, situação, motivo e texto do WMS em
  uma linha (até 500 caracteres). Limpo com `I51:DiasRetencaoEstado` (30 dias).
- **Reenfileirar:** `--reenfileirar-i60` zera o carimbo `AlertaI60Em` (nova tentativa, novo alerta possível).
- **Desligar:** `I60:AlertaImediato=false` desliga e-mail, arquivo e log de alerta; o contador do relatório
  diário continua.

**Como calibrar o ENC (`I60:PadroesDocCargaEncerrado`).** O texto real do WMS para "doccarga encerrado" ainda
não foi capturado. Quando chegar um alerta `NaoReconhecida` (ou `ENC/IMP` com texto diferente do esperado):

1. Abra o e-mail do alerta ou a linha correspondente em `Estado\i51\alertas\AAAA-MM-DD.log`.
2. Copie um **trecho característico** do texto do WMS (curto, sem número de nota nem datas).
3. Acrescente o trecho a `I60:PadroesDocCargaEncerrado` em `config\appsettings.Production.json` (lista
   separada por `;`, sem diferenciar acento nem maiúsculas; padrão atual `encerrad;status ENC`) e reinicie o
   serviço.
4. Para a nota já em tentativas, `Worker.exe --reenfileirar-i60 --chave <chave>` libera nova tentativa.

**Ainda a confirmar:** texto real do ENC (calibrar com o alerta), `TpAcao` do IMP, sinal de "associada" e
mensagem do retorno sem documentos (`sucesso=N`). O fluxo normal (I50, separação, I51, I60 `INE`, associada)
foi validado no homolog com 7 de 7 notas.

### 8.6. Última alternativa: tela de devolução pela I40 (seção removível)

> Esta seção é independente do resto do manual e pode ser removida inteira (junto do item do índice e
> da linha da tabela da seção 12) sem afetar as demais.

A devolução tem uma ordem de preferência: (1) **I60 associada** pelo fluxo I51 → I60 `INE`; (2) **I60 como `IMP`** (DocCarga encerrado), com o erro registrado; (3) **tela de devolução pela I40**, a **última alternativa**.

A tela da I40 está **pronta e funcionando**, mas **desligada por padrão** (`Devolucao:Habilitada=false`).
Pesquisa a nota por chave de 44 dígitos **ou** por número + série, envia a devolução ao WMS com
`tipoOperacaoNota` `IMP`, sem placa e sem motorista. Porta HTTP em `Devolucao:Porta` (padrão 8085). O
trabalho da I51/I60 não altera a I40 (há testes de regressão).
**Busca limitada a 30 dias.** A busca lê o XML em `Processados` (nunca `Erro`), e a limpeza mantém 30 dias
(seção 8). Nota mais antiga: "Nota não encontrada na retenção de 30 dias." (era 60 dias até 13/11/2026).

#### Login simples (o link fica aberto)

Pedido do usuário em 13/11/2026: como a tela deve ser usada pouco e fica aberta na rede interna, ela tem uma
seção de **login falso**: o operador informa o **login do WMS** (a senha é opcional na tela, mas a senha
digitada nem é enviada pela página nem lida pelo servidor) e o sistema **registra quem enviou**.

- A tela só libera o botão "Enviar devolução" depois de confirmar o usuário; mostra "Enviando como: usuário" e
  "Trocar usuário".
- `POST /api/devolucao/enviar` exige `usuario` (1 a 60 caracteres; letras, números, `. _ - @` e espaço; senão
  400). O WMS segue chamado com a conta de serviço.
- A auditoria `devolucao_yyyyMMdd.log` (em `PastaAuditoria`, padrão `<Estado>\devolucao`) passou a
  `data;ip;usuario;chave;nota;status;transacao;mensagem` (linhas antigas têm 7 campos) e o log traz `usuario=`.
- **Risco aceito (decisão do usuário, 13/11/2026):** sem restrição de firewall na rede interna; quem tem o
  link consegue enviar e a identificação é **autodeclarada**, não autenticada. Serve para saber quem enviou,
  não para impedir. Se isso mudar, a regra de firewall da porta `Devolucao:Porta` é o controle disponível.

Arquitetura e roteiro: `docs\ARQUITETURA-PLANO-B-I40.md` e `docs\ROTEIRO-TESTE-I40.md`. O teste da tela no
homolog ainda está pendente. Só ligue (`Devolucao:Habilitada=true`) com OK do responsável.

## 9. Publicar nova versão e rollback

O `.zip` da nova versão é gerado **na máquina de build** (ver seção 0), rodando o
`deploy\publicar.ps1` do repositório; o arquivo sai em `artifacts\<versao>\`. Copie esse `.zip` para
o servidor (ex.: `C:\temp`) e faça o resto **no servidor**.

**Publicar uma nova release** (no servidor) com `publicar-release.ps1`, a partir de
`C:\Friozem\importacao_notas\tools\deploy`. Informe a origem (`-ReleaseZip` **ou** `-ReleaseDir`,
nunca os dois):

```powershell
cd C:\Friozem\importacao_notas\tools\deploy
powershell -ExecutionPolicy Bypass -File .\publicar-release.ps1 -ReleaseZip C:\temp\rel.zip -Versao 2026.02.01-1
```

- `-ManterUltimas` (padrão `5`): quantas releases antigas preservar em `app\releases`.
- Faz backup do `current` anterior em `backups\current_<carimbo>.zip`, copia a config, aponta
  `app\current` para a nova release e reinicia o serviço.
- Também **atualiza os próprios scripts** em `tools\deploy` para acompanharem a release.

**Rollback** para uma release anterior com `rollback.ps1`, na mesma pasta:

```powershell
cd C:\Friozem\importacao_notas\tools\deploy
powershell -ExecutionPolicy Bypass -File .\rollback.ps1
powershell -ExecutionPolicy Bypass -File .\rollback.ps1 -Versao 2026.01.15-1
```

- Sem `-Versao`, usa a **release anterior** (a segunda mais recente por data).
- Para o serviço, copia a config, reaponta `current` e reinicia.

Ambos aceitam `-WhatIf` para simular antes de executar.

**Remover o serviço** (raro; preserva as pastas por padrão) com `remover.ps1`, na mesma pasta:

```powershell
cd C:\Friozem\importacao_notas\tools\deploy
powershell -ExecutionPolicy Bypass -File .\remover.ps1
# Remove o serviço E limpa app\/logs\, preservando data\/backups\/config\ (com confirmação):
powershell -ExecutionPolicy Bypass -File .\remover.ps1 -LimparPastas -Confirm
```

## 10. Estado e telemetria (não mexer)

O **Estado** (diário do pacote `pacote-atual.jsonl`) e a **telemetria**
(`telemetria.jsonl`, `servico.jsonl`, `ultimo-resumo.txt`, resumos em `resumos\`) ficam **fora do
share**, previstos em `C:\Friozem\importacao_notas\data`. Eles ficam **invisíveis** aos usuários
de propósito.

- **Não edite nem apague** esses arquivos com o serviço rodando: o diário é a **fonte de verdade**
  para retomar um pacote após reinício sem reenviar notas ao WMS; a telemetria alimenta o resumo
  semanal.
- A limpeza automática **nunca** toca essas pastas.
- Dentro do `Estado` há também `tsp\ondas.json` (sequencial do dia e onda de cada ZIP da Puxada TSP;
  ver seção 8.4) e `tmp\` (extração temporária dos ZIPs, apagada a cada ZIP processado).

### 10.1. Tempo de integração: campos e como é medido

O relatório, o PDF e o e-mail de fechamento mostram o bloco **"Tempo de integração"**. Os tempos são
**medidos** pelo relógio do sistema (não estimados):

| Campo na telemetria (`telemetria.jsonl`) | O que mede | Onde é medido |
|---|---|---|
| `EsperaJanelaMs` | Abertura do pacote até a foto da pasta (coleta pela janela de silêncio; rótulo "Coleta (janela de silêncio)" no relatório) | `GerenciadorPacote` |
| `LeituraMs` | Leitura dos ZIP/XML | `ProcessadorLote` |
| `IntegracaoWmsMs` | Fase de integração: início da 1ª nota até o registro da última (tempo de parede) | `ProcessadorLote` |
| `ChamadasWmsMs` | Soma das chamadas ao WMS (I50, I10, I30 e, se ligada, I60) de todas as notas | `Orquestrador` |
| `TempoRelatorioMs` | Geração e gravação do HTML+PDF do pacote | `FechadorPacote` |
| `Ondas` | Por onda TSP: `Onda`, `Notas`, `Transportes`, `DuracaoMs` | diário do pacote |

- **Por nota no WMS** = `ChamadasWmsMs` ÷ notas. É o número para comparar com o lançamento manual
  (hoje ~12 s por nota na tela). O resumo semanal mostra a linha "Integração no WMS por nota" só
  quando há pacotes com essa medição.
- Os campos são **opcionais**: linhas antigas da telemetria continuam sendo lidas (valem 0/vazio). O
  diário do pacote também ganhou `InicioNota` e `DuracaoWmsMs` por nota, com o mesmo cuidado.
- **Limitação:** o tempo de geração do relatório não pode aparecer dentro do próprio relatório. Ele fica
  **só na telemetria** (`TempoRelatorioMs`); o relatório e o e-mail mostram as etapas concluídas antes.
- Pacote retomado depois de uma queda: espera da janela e leitura ficam sem valor (0 na telemetria,
  linha omitida no relatório); integração no WMS e ondas saem do diário e continuam corretas.

### 10.1.1. O relatório de integração: cabeçalho, tempo e cartões

O relatório do pacote (HTML, PDF e corpo do e-mail de fechamento) segue esta ordem:

1. **Cabeçalho:** faixa azul com a logo e o título "Relatório de Integração" e, abaixo, a linha com o pacote e o
   tipo (`PUXADA`, `TSP` ou `PUXADA + TSP`), a data de geração e a **Coleta** (primeiro e último arquivo
   gravado na pasta para o pacote).
2. **Resumo:** cartões Notas processadas, Integradas, Integradas — revisar produto, Pendentes, Não
   integradas, Arquivos ignorados e **Transportes** (transportes distintos do pacote), mais a linha "Por
   tipo" (notas `PUXADA`, `TSP` e da raiz). A linha "Notas por transporte" saiu do resumo; a contagem segue
   no título de cada grupo da tabela.
3. **Tempo de integração** (10.1): resumo do pacote, etapas ("Coleta (janela de silêncio)", leitura, integração
   no WMS) e, só quando há TSP, o tempo por onda.
4. **Tabela de notas:** agrupada por transporte (`PUXADA`) ou por onda (`TSP`), com as colunas Tipo e Origem.

### 10.2. Logo do relatório e do e-mail

A logo Friozem (versão negativa, branca) vem **embutida no programa**. `Relatorio:LogoPath` é opcional
e só troca por outro PNG; vazio, inexistente ou inválido = logo embutida. Nada a copiar no servidor.

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
3. **Rode o diagnóstico** (somente leitura, sempre termina sem erro), a partir de
   `C:\Friozem\importacao_notas\tools\deploy`:

   ```powershell
   cd C:\Friozem\importacao_notas\tools\deploy
   powershell -ExecutionPolicy Bypass -File .\diagnostico.ps1
   ```

   Ele checa serviço, host/porta do WMS (`friozem.assistsoftware.com.br:8888`), SMTP
   (`smtp.friozem.com.br:587`), a pasta de entrada e o share `kitbrf`, e lê o registro do SChannel
   (TLS).
4. **Falha de conexão HTTPS ao WMS** (handshake/TLS): em Windows Server 2012 R2 o TLS 1.2 depende do
   SChannel do sistema. A habilitação é feita pelo **instalador** (`instalar.ps1 -HabilitarTls12`,
   reversível e com confirmação) — um passo de instalação/manutenção, não de operação do dia a dia.
   O procedimento completo está em [`docs\INSTALACAO-SERVIDOR.md`](../INSTALACAO-SERVIDOR.md)
   (seções 3.6/6); siga o guia de instalação para esse ajuste. Não há uma mensagem fixa de
   "handshake" no log — o sintoma é a falha de conexão HTTPS ao WMS.

## 12. Tabela: "Quero fazer X → arquivo/comando → reinicia?"

Todos os comandos rodam de **`C:\Friozem\importacao_notas\tools\deploy`** (no servidor, via RDP); os
arquivos de config ficam em **`C:\Friozem\importacao_notas\config\`**.

| Quero fazer | Arquivo / comando (no servidor) | Precisa reiniciar o serviço? |
|---|---|---|
| Ver situação do serviço | `tools\deploy\servico.ps1 -Acao Status` | — |
| Iniciar / parar / reiniciar | `tools\deploy\servico.ps1 -Acao Start\|Stop\|Restart` | — (é a própria ação) |
| Ver logs | `logs\worker.out.log` / `logs\worker.err.log` | Não |
| Trocar usuário/senha do WMS | `tools\deploy\configurar-segredos.ps1` (env de máquina) | **Sim** |
| Trocar SMTP (usuário/senha/remetente) | `tools\deploy\configurar-segredos.ps1` ou editar `config\email.settings.json` | Recarrega ao salvar; na dúvida reinicie |
| Trocar URL do WMS | editar `Wms:BaseUrl` em `config\appsettings.Production.json` | **Sim** |
| Destinatários do fechamento | editar o TXT `config\destinatarios-fechamento.txt` (`Email:CaminhoDestinatarios`); `Email:Destinatarios` no JSON é só fallback | Não (vale no próximo envio) |
| Destinatários do resumo semanal | editar o TXT `config\destinatarios-resumo.txt` (`Monitoramento:CaminhoDestinatarios`) | Não (vale no próximo envio) |
| Dia/hora do resumo | `config\appsettings.Production.json`: `Monitoramento:DiaDaSemana`/`Hora` | **Sim** |
| Janela de coleta (silêncio 15 s, teto 180 s) ou voltar à janela fixa | `config\appsettings.Production.json`: `Pacote:ModoJanela` (`Silencio`/`Fixa`), `Pacote:SilencioSegundos`, `Pacote:JanelaMaximaSegundos`, `Pacote:JanelaColetaSegundos` (só no modo `Fixa`) | **Sim** |
| Retenção/limpeza (30 dias) | `config\appsettings.Production.json`: `Limpeza:DiasRetencao` e `I51:DiasRetencaoEstado` | **Sim** |
| Ligar/desligar o alerta imediato da I60 | `I60:AlertaImediato` (padrão `true`; destinatários no TXT `destinatarios-i60.txt`) (seção 8.5) | **Sim** |
| Calibrar o texto do ENC | acrescentar um trecho do texto do WMS (e-mail do alerta ou `Estado\i51\alertas`) a `I60:PadroesDocCargaEncerrado` (seção 8.5) | **Sim** |
| Ligar/desligar envio da placa do veículo | editar `I50:EnviarPlacaVeiculo` (Puxada normal, `true` = com placa) ou `I50:EnviarPlacaVeiculoTsp` (TSP, `false` = sem placa) em `config\appsettings.Production.json` | **Sim** |
| Mudar pastas da Puxada/PuxadaTSP, aceitar ou recusar a raiz, limites do ZIP | `Pastas:Puxada`, `Pastas:PuxadaTsp`, `Pastas:AceitarRaiz`, `Zip:MaxArquivos`, `Zip:MaxTamanhoMb` em `config\appsettings.Production.json` (seção 8.4) | **Sim** |
| Ligar o fluxo I51 → I60 (só com OK do responsável; as duas juntas e com os pré-requisitos do WMS) | `I51:Habilitada` e `I60:Habilitada` em `config\appsettings.Production.json` (seção 8.5) | **Sim** |
| Gerar o relatório diário da I51/I60 agora | `Worker.exe --relatorio-i60 [--enviar]` (seção 8.5) | Não |
| Ligar a tela de devolução I40 (última alternativa; login simples; busca de 30 dias) | `Devolucao:Habilitada` (seção 8.6, removível) | **Sim** |
| Limpar já as pastas de não-XML (ExtensaoInvalida) | `tools\deploy\limpar-extensao-invalida.ps1` (`-WhatIf` simula) | Não (só apaga as pastas `ExtensaoInvalida`) |
| Publicar nova versão | `tools\deploy\publicar-release.ps1` (o `.zip` vem da máquina de build) | Automático (o script reinicia) |
| Rollback | `tools\deploy\rollback.ps1` | Automático (o script reinicia) |
| Remover o serviço | `tools\deploy\remover.ps1` (`-LimparPastas -Confirm` para limpar `app\`/`logs\`) | — (para o serviço) |
| Diagnosticar | `tools\deploy\diagnostico.ps1` | Não (só leitura) |
| Habilitar TLS 1.2 | instalação/manutenção: `instalar.ps1 -HabilitarTls12` (ver INSTALACAO-SERVIDOR.md) | Pode exigir reinício do servidor |

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
- **Não ligue a I51/I60 em produção por conta própria** (`I51:Habilitada` e `I60:Habilitada` seguem `false` até o OK do responsável e os pré-requisitos do WMS; validada no homolog em 13/11/2026, ver seções 8.3 e 8.5). Os dados fiscais vão na I50.
- **Valide mudança de código** com build + test **no repositório, fora de produção**, antes de
  promover.
- **Não rode teste de carga** em produção ou homologação sem o **OK do responsável**.
- **Config antes de código**: prefira ajustar configuração (JSON e os TXT de destinatários) a mexer
  no código; e deixe a **execução no servidor** (restart, publicar, rollback) para o técnico humano.
