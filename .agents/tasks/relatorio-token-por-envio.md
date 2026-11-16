# Relatório: token por envio (release 2026.11.16-3)

## Contexto
A Assist (fornecedor do WMS) orientou uma solicitação de token a cada envio e confirmou 3 servidores em balanceamento, sem tempo limite de token. Commits: `192a319` e `e8086be`.

## Decisões de projeto
- **Token por envio:** com `Wms:TokenPorEnvio=true` (padrão), cada POST (I50, I60, I40, I51) faz o próprio `GetToken`. Com `false`, volta o cache antigo.
- **Afinidade/cookie:** `UseCookies=false` no handler. O cookie de afinidade do balanceador vale só para o par `GetToken + POST` do mesmo envio, então envios diferentes não ficam presos a um servidor.
- **Retry em conexão nova:** `GetToken` com 401 repete até `Wms:TokenTentativasImediatas` (3) vezes, com HttpClient/handler novo, `Connection: close` e sem o cookie anterior, para poder cair em outro servidor. Falha de GetToken não manda nota para `Erro`.
- **Teto (throttle):** `Wms:MaxGetTokenPorMinuto` = 300 (não 120), janela deslizante de 60 s. Acima do teto a chamada espera, não falha a nota. `0` desliga.
- **Diagnóstico:** cada GetToken que falha é logado com `status`, `latenciaMs` e `servidor` (`nome#hash`); o resumo do pacote lista as rejeições por servidor para levar à Assist.
- **`--testar-token --vezes N`:** amostra N chamadas (máximo 20, 2 s entre elas), imprime só `GetToken: HTTP <código> ... (host, porta)`, sem usuário, senha ou token.
- Manuais com o link da tela I40 (`http://SRVAPPALC02:8085/` e `http://200.200.200.213:8085/`), HTML/PDF regerados.

## Resultados
- `dotnet build KitWmsIntegrador.sln -c Release`: 0 avisos, 0 erros.
- `dotnet test KitWmsIntegrador.sln -c Release`: 991 aprovados, 9 ignorados, 0 falhas (total 1000). A linha de base era 971/9; foram acrescentados 20 testes em `TokenPorEnvioTests.cs`. Fonte: `verificacao-token-por-envio.md`. O commit seguinte (`e8086be`) mudou só um comentário XML, e o empacotamento compilou sem erro. Não rodei os testes de novo no empacotamento.
- Sobrecarga de GetToken + POST: cerca de **+120 ms por nota** (188 notas ≈ 23 s) e o número de chamadas ao WMS dobra. É o valor documentado em `docs\OPERACAO.md` §12.1. Não medi de novo neste passo, e a medição em produção fica para o item 7 do checklist.
- Pacote gerado com `deploy\publicar.ps1 -Versao 2026.11.16-3`. Não há campo de versão no código, então não houve alteração de arquivo nem commit de versão.

## Pacote
- `c:\wms-integrador\artifacts\2026.11.16-3\KitWmsIntegrador-2026.11.16-3-win-x64.zip` (46.743.505 bytes; `artifacts\` não é versionado)
- SHA-256: `F2001E00C85B67ED2C9AE3A3FF5836736A09CB36F0173696F3A4668C283275F3`
- Checklist: `c:\wms-integrador\.agents\tasks\checklist-release-2026-11-16-3.md`
- Nada foi executado no servidor, sem push e sem deploy.

## Esforço (somente horas)
- Horas humanas: informado pelo usuário.
- Horas de IA: aprox. 2,5 h (estimativa pela duração das sessões do agente: implementação, testes, documentação, revisão). O empacotamento, o checklist e este relatório levaram cerca de 0,2 h, dentro desse total.
