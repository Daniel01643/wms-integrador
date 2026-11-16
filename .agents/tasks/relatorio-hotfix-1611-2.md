# Relatório do hotfix 2026.11.16-2 (--testar-token e CLIs)

## Causa raiz
A hipótese "o `--testar-token` usava a BaseUrl errada (homologação)" **não foi confirmada**. Fontes: `verificacao-hotfix-1611-2.md` e `git log`.

- O código antigo do `--testar-token` já lia `appsettings.json` e `appsettings.Production.json` ao lado do exe. Um listener local registrou `CONNECT friozem.assistsoftware.com.br:443`, tanto no build local antigo quanto no exe real do servidor (somente leitura, sem `Wms__BaseUrl`). A URL efetiva já era a de produção.
- No servidor, a variável de máquina, a de processo e o `AppEnvironmentExtra` (NSSM) tinham o mesmo usuário BRF e senha de 16 caracteres.
- Hipótese mais provável para o 401 anterior: credenciais obsoletas na sessão WinRM/shell do teste, que herda o ambiente ao abrir. Não há como provar retroativamente.
- **Divergências reais encontradas e corrigidas:**
  - `ConfiguracaoManual.Criar()` (`--reenfileirar-i60`, `--consultar-i51`, `--relatorio-i60`) ignorava `appsettings.Production.json` e os argumentos de linha de comando. Isso explica o incidente de 13/11, em que o `--reenfileirar-i60` gravou na pasta padrão.
  - `EnvioManualI40/I60` e `SmokeTestWms` também ignoravam o `appsettings.Production.json`.
  - O `--testar-token` lia `config\appsettings.Production.json`, que o serviço não lê.

## O que mudou
- Commit `1d787ae`: novo `ConfiguracaoAplicacao` (carregador único, ContentRoot = pasta do exe, ambiente Production por padrão, `email.settings.json` por último). O `Program.cs` e todas as CLIs passam a usá-lo. O `--testar-token` imprime `(host, porta N)` no fim da linha, sem usuário, senha, token ou caminho.
- `deploy\configurar-segredos.ps1`: grava também `AppEnvironmentExtra` do NSSM (`WMS__Usuario`/`WMS__Senha`), preserva as outras entradas, suporta `-WhatIf`, não imprime os valores e tem o parâmetro `-SemNssm`. Se a chave do serviço não existir, avisa e segue.
- Commit `236ee8e`: docs (`INSTALACAO-SERVIDOR.md`, `OPERACAO.md` §12.1 de rotação de senha, `MANUAL-TECNICO` em HTML e PDF).
- Commit `9c7d6ff`: checklist da release.

## Testes e build
- `dotnet build KitWmsIntegrador.sln -c Release`: 0 avisos, 0 erros.
- `dotnet test KitWmsIntegrador.sln -c Release`: 971 aprovados, 9 ignorados, 0 falhas. A linha de base era 966; foram acrescentados 5 testes em `ConfiguracaoAplicacaoTests`, e o `TestarTokenWmsTests` foi ajustado para a nova saída.
- Exe novo executado com cwd = `%USERPROFILE%`: `GetToken: sem resposta (timeout/rede) FALHA (friozem.assistsoftware.com.br, porta 443)`.
- `configurar-segredos.ps1`: o parse não deu erros e a função `Merge-EntradasWms` foi testada. O script **não** foi executado contra o HKLM nem no servidor.
- Observação: o passo de empacotamento (abaixo) informou que não rodou testes nem `--testar-token`. Os resultados acima vêm da verificação anterior.

## Pacote
- Arquivo: `c:\wms-integrador\artifacts\2026.11.16-2\KitWmsIntegrador-2026.11.16-2-win-x64.zip` (46.732.854 bytes), gerado com `deploy\publicar.ps1 -Versao 2026.11.16-2`.
- SHA-256: `6D555EAC9359F1E09EC390F09B0BE967B3D0B0216A461F3B0110A7C3D1F9B15F`
- Checklist: `c:\wms-integrador\.agents\tasks\checklist-release-2026-11-16-2.md`
- O `appsettings.json` do pacote aponta para a porta `:8888`. A URL de produção (porta 443) vem só do `appsettings.Production.json` do servidor. Ao rodar o `--testar-token`, confirmar que não há override `Wms__BaseUrl` e que host e porta impressos são os de produção.
- Nada foi executado no servidor e não houve push.

## Revisão
Veredito do revisor (`review-hotfix-1611-2.json`): **APPROVED**, sem achados. Documento: `review-hotfix-1611-2.md`.

## Esforço
- Horas humanas: informadas pelo usuário.
- Horas de IA: aprox. 2,0 h (estimativa pela duração da sessão do agente neste hotfix: investigação, correção, testes, docs, pacote e revisão).
