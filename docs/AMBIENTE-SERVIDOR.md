# Ambiente de produção e definições operacionais

## Servidor de produção

- Host: **SRVAPPALC02** (FQDN `srvappalc02.friopart.matriz.com`, IP 200.200.200.213).
- Domínio: `Friopart.Matriz.com` (NetBIOS `FRIOPART`).
- SO: **Windows Server 2012 R2 x64**.
- Runtimes já presentes: Python 3.10.11, Node.js v22, npm 10.9.4, PowerShell 4.0/5.1.
- Serviços existentes usam **NSSM** (supervisor de processo), com início automático
  atrasado, recuperação após falha e rotação de logs.
- Raiz de instalação definida para este serviço: `C:\Friozem\importacao_notas`.
  (nome técnico sugerido do serviço: `Friozem-ImportacaoNotas`.)

## C# / .NET 8 no Windows Server 2012 R2 — VERIFICADO

- **.NET 8 É suportado no Windows Server 2012 R2** (doc oficial de SO suportados do
  .NET 8). O servidor **não impede** a stack escolhida.
- Ressalva: 2012 R2 está em fase de **Extended Security Updates (ESU)**. .NET 8 roda,
  mas o SO é antigo — recomendável planejar upgrade de SO no médio prazo.
- **Runtime .NET 8 precisa ser instalado no servidor** (não vem por padrão). Publicar
  como **self-contained** (executável único com o runtime embutido) elimina essa
  dependência — recomendado, dado o foco em simplicidade de implantação.

## Divergência a decidir: C# vs. Python do prompt do servidor

O documento `PROMPT-IMPORTACAO-NOTAS` foi escrito presumindo **Python 3.10.11 + NSSM**.
Nossa decisão de projeto é **C#/.NET 8**. Não há impedimento técnico; é decisão de
padronização. Encaminhamento proposto:

- Manter **C#/.NET 8** (decisão já tomada pelo baixo consumo e evolução da equipe).
- Rodar o executável **sob NSSM**, como os demais serviços do servidor (preserva o
  padrão operacional: recuperação, rotação de logs, start atrasado). Alternativa é
  Windows Service nativo (`BackgroundService` + `sc.exe`), mas NSSM mantém a casa
  uniforme.

## Requisitos do servidor atendidos em C# (do prompt)

- Sem banco de dados, sem Web, sem IIS, sem porta de entrada. ✅ (agente só faz saída HTTP)
- Estado em **arquivos locais** com escrita atômica, lock e recuperação. ✅
- Caminhos absolutos, diretório de trabalho explícito. ✅
- Logs com data/hora+fuso, versão, id de execução, referência não sensível da nota,
  duração e resultado; rotação/retenção configuráveis. ✅
- Segredos fora do repositório, protegidos por ACL, nunca em args/logs. ✅
- Scripts PowerShell: diagnóstico, preparação de release, instalação idempotente,
  start/stop/status, publicação versionada, rollback, remoção. ✅
- Estrutura de diretórios: `app/releases/<versao>`, `app/current`, `config`, `data`,
  `logs`, `backups`, `tools`.

## Definições operacionais confirmadas pelo usuário

- **Depósito**: `JD` — **iddeposito**: `8`.
- **CNPJ Armazém**: `14.605.065/0001-75`.
- **Depositante / Empresa Proprietária**: `01.838.723/0098-50` — **Código**: `3509308`.
- **Reprocesso**: o colaborador consegue reprocessar após ajustar a falha informada.
- **Empresa não cadastrada** → enviar **I10** e reenviar a I50.
- **Outros erros** → apenas ajustar e enviar por e-mail (sem automação de correção).

## Solution

- Nome: **`KitWmsIntegrador`**.
