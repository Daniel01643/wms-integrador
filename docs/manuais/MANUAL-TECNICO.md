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
   - [8.1. Arquivos não-XML (ExtensaoInvalida) e a flag `Pastas:TratarNaoXmlComoErro`](#81-arquivos-não-xml-extensaoinvalida-e-a-flag-pastastratarnaoxmlcomoerro)
   - [8.2. Ligar/desligar o envio da placa do veículo (`I50:EnviarPlacaVeiculo`)](#82-ligardesligar-o-envio-da-placa-do-veículo-i50enviarplacaveiculo)
   - [8.4. Devolução (I40): tela de chaves, desligada em produção](#84-devolução-i40-tela-de-chaves-desligada-em-produção)
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
| Janela de coleta do pacote | `Pacote:JanelaColetaSegundos` | `10` |
| Limpeza ligada/desligada | `Limpeza:Habilitada` | `true` |
| Dias de retenção (limpeza) | `Limpeza:DiasRetencao` | `60` |

Outras chaves de `Pacote` úteis: `IntervaloVarreduraSegundos` (5), `EstabilizacaoSegundos` (3),
`TimeoutArquivoSegundos` (300), `BackoffMaximoSegundos` (120), `MaxTransportesConcorrentes` (1).

A limpeza roda na partida e a cada 24 h, atua **apenas** nas subpastas de `Processados` e `Erro`,
e **nunca** toca a entrada, o `Estado`, a telemetria ou os `relatorios`. Como a subpasta
`Erro\<pacote>\ExtensaoInvalida` (arquivos não-XML; ver seção 8.1) fica dentro de `Erro`, ela
**já é coberta** por essa limpeza de 60 dias — a pasta inteira do pacote é removida no vencimento,
sem configuração extra.

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
`ExtensaoInvalida` **sem esperar** o ciclo de 60 dias, use esse script a partir de
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
| `true` (**padrão**) | Envia a placa exatamente como está no XML (`transp/veicTransp/placa`). |
| `false` | Envia a placa **em branco** (`placaVeiculo = ""`), como no comportamento anterior. |

A placa é enviada **como está** no XML, sem maiúsculas/minúsculas forçadas, sem hífen e sem outras
transformações além do básico.

Para **desligar** o envio, edite o arquivo e deixe a chave em `false`:

```json
"I50": {
  "EnviarPlacaVeiculo": false
}
```

Para **religar**, volte a chave para `true` (ou remova-a: o padrão é `true`).

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

Situação: a I60 está **desligada em produção** (`I60:Habilitada=false`, padrão). O Plano A (a I60
associar sozinha a nota de venda ao DocCarga criado pela I50) foi **testado no WMS de homologação em
09/11 e 10/11 e está pausado, aguardando o fornecedor do WMS**. Nada foi para produção. Histórico
completo em `docs\wms-codigo-customizado\HISTORICO-PLANO-A.md`.

- **Como ligar (somente em teste/homologação, com OK do responsável):** defina `I60__Habilitada=true`
  (variável de ambiente, ou `I60:Habilitada` no `appsettings`) e reinicie o serviço.
- **Opções de diagnóstico (teste):** `I60__TipoOperacaoNota` (padrão `INE`) e `I60__QtdeVolumeZero`
  (`true` envia `qtdeVolume = 0`, usado só para isolar a conferência de volumes).
- **Enviar só a I60 de um XML:** `--enviar-i60 <xml>` no Worker; mostra o JSON e grava envio e resposta
  em `envio-manual-i60\`.
- **Em produção, mantenha `I60__Habilitada=false`.**

### 8.4. Devolução (I40): tela de chaves, desligada em produção

Plano B da devolução: uma página web, hospedada pelo próprio serviço, onde o colaborador digita ou
bipa a chave de NF-e **ou pesquisa pelo número da nota + série**; o sistema acha o XML em `Processados`,
monta a I40 e envia ao WMS. Não há placa nem motorista (removidos em 10/11/2026). Está
**desligada** (`Devolucao:Habilitada=false`) e **nada foi ligado em produção**. No homolog (10/11),
a I40 como IMP sem placa deu "Registrado com sucesso" e o recebimento foi concluído.

**Habilitar (somente com OK do responsável e depois do firewall, ver `INSTALACAO-SERVIDOR.md`):**

1. Defina `Devolucao__Habilitada=true` (variável de ambiente do serviço) ou `Devolucao:Habilitada` em
   `config\appsettings.Production.json`.
2. Reinicie o serviço. No `logs\worker.out.log` deve aparecer "Tela de devolução ligada na porta 8085".
3. Teste `http://<servidor>:8085/api/saude` (responde `{"status":"ok"}`).

**Chaves (seção `Devolucao`):**

| Chave | Padrão | O que faz |
|---|---|---|
| `Habilitada` | `false` | Liga a tela. `false`: a porta não abre. |
| `Porta` | `8085` | Porta HTTP (escuta em todas as interfaces; **sem login**). |
| `TipoOperacaoNota` | `IMP` | Tipo de operação da I40 (`placaVeiculoPrevistoNota` e `nomeMotorista` vão sempre em branco). |
| `MaxChavesPorEnvio` | `30` | Limite por envio. |
| `PastaAuditoria` | vazio | Vazio = `<Estado>\devolucao`. |
| `InverterPartes` | `false` | Validado no homolog; a variante invertida não é necessária. |

**Busca por número + série:** `GET /api/devolucao/buscar?numero=&serie=` (número até 9 dígitos, série até 3,
só dígitos). Não abre os XMLs: lê número, série e CNPJ do emitente do nome do arquivo `NFE<chave>.XML`
(chave de 44 dígitos: série nas posições 23-25, número em 26-34, CNPJ em 7-20), em `Processados` e
`pacote_*` (nunca `Erro`), só do CNPJ de `Operacional:CnpjDepositante`, comparando como inteiros. Mesma
chave em dois pacotes vale uma vez (a mais recente). Responde 200 com `opcoes[]` (uma ou mais chaves; a
tela pede a escolha quando há mais de uma e não envia sozinha), 404 "Nota não encontrada na retenção de
60 dias." ou 400 para entrada inválida. O `POST /api/devolucao/enviar` recebe só `{ "chaves": [...] }`.

**Sem placa/motorista:** o `ConstrutorI40` não recebe placa; `placaVeiculoPrevistoNota` e `nomeMotorista`
vão sempre em branco. Chaves antigas `EnviarPlacaPadrao`, `TipoOperacaoNotaComPlaca` e `NomeMotorista` no
`appsettings` são ignoradas. Modo manual: `--enviar-i40 --chave <44>` ou `--enviar-i40 --numero <n> --serie <s>`,
mais `--xml <caminho>` opcional.

**Firewall:** a tela não tem login. Libere a porta **somente para a rede interna** (comando de
exemplo em `INSTALACAO-SERVIDOR.md`, seção 3.12).

**Auditoria:** um arquivo por dia, `devolucao_yyyyMMdd.log`, em `Devolucao:PastaAuditoria` ou
`<Estado>\devolucao`. Uma linha por chave: `data;ip;chave;nota;status;transacao;mensagem`.
Sem senha nem JSON. Cada envio também aparece em `logs\worker.out.log`.

**Isolamento:** a tela tem o próprio servidor web dentro do serviço. Se a porta estiver ocupada ou
houver erro, o log registra "Tela de devolução falhou e foi desligada" e a integração das notas
continua normalmente.

**Erros da API:** 400 (pedido inválido: lista vazia, mais de 30 chaves, número/série inválidos na busca),
503 (`Pastas:Entrada` ou `Operacional:CnpjDepositante` não configurados), 500 (erro inesperado; veja o log).

**Para desligar:** `Devolucao__Habilitada=false` e reiniciar o serviço.

**Teste no homolog nesta máquina:** `docs\ROTEIRO-TESTE-I40.md`.

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
| Janela de coleta / limpeza (60 dias) | `config\appsettings.Production.json`: `Pacote:JanelaColetaSegundos` / `Limpeza:DiasRetencao` | **Sim** |
| Ligar/desligar envio da placa do veículo | editar `I50:EnviarPlacaVeiculo` em `config\appsettings.Production.json` | **Sim** |
| Ligar/desligar a tela de devolução (I40) | `Devolucao__Habilitada` / `Devolucao:Habilitada` (e firewall, ver seção 8.4) | **Sim** |
| Ver a auditoria da devolução | `<Estado>\devolucao\devolucao_<data>.log` | Não |
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
- **Não ligue a I60 em produção** (`I60:Habilitada` deve seguir `false`; testada no homolog, pausada aguardando o fornecedor; ver seção 8.3). Os dados fiscais vão na I50.
- **Valide mudança de código** com build + test **no repositório, fora de produção**, antes de
  promover.
- **Não rode teste de carga** em produção ou homologação sem o **OK do responsável**.
- **Config antes de código**: prefira ajustar configuração (JSON e os TXT de destinatários) a mexer
  no código; e deixe a **execução no servidor** (restart, publicar, rollback) para o técnico humano.
