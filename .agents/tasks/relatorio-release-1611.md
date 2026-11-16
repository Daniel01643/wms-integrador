# Relatório final da release 2026.11.16-1

Nada foi enviado ao servidor SRVAPPALC02 nem ao GitHub. Commits locais na `main`; o deploy e o push ficam com o usuário.
Checklist de publicação: `c:\wms-integrador\.agents\tasks\checklist-release-2026-11-16.md`.

## Pacote
- `c:\wms-integrador\artifacts\2026.11.16-1\KitWmsIntegrador-2026.11.16-1-win-x64.zip` (self-contained win-x64, 46.732.548 bytes, 339 arquivos).
- SHA-256: `256560BB0C023FC436D05AB698D52836ED91474875FA0E13ADFE64F2D978906E`. Manuais: `manuais.zip`, SHA-256 `125D61C00D4D54B18CC7AC5F784926EDFE0AFACCFC1ABC48A0FEE9B45836C37A`.
- Build Release: 0 avisos, 0 erros. Testes Release: 966 aprovados, 9 ignorados (stress), 0 falhas (linha de base de 13/11: 926 aprovados).
- Substitui a 2026.11.13-1, que nunca foi publicada e está incluída nesta.

## Causa raiz do 401
- **Causa mais provável: estado de autenticação do lado do WMS/Assist.** Em 15/11 o `GET Integracao/GetToken` foi rejeitado em janelas contínuas
  (cerca de 10 min, 17:27 a 17:38, e outra curta por volta de 18:34) e voltou sozinho, sem mudança nossa. Nenhum POST recebeu 401, a latência
  do 401 foi de 6 a 9 ms (rejeição na borda de autenticação) e uma sonda de leitura no dia seguinte devolveu 200 com a mesma credencial.
- **Descartado do nosso lado:** credencial e codificação (ASCII), divergência de configuração, URL de homolog em produção, concorrência de GetToken,
  cache de token, TLS e uso simultâneo do usuário em produção (o usuário confirmou que só o integrador o usa).
- **Agravante nosso (corrigido):** sem circuit breaker, cada nota refazia 3 GetToken (1.352 GetToken 401 em cerca de 10 min) e o pacote inteiro ia para
  `Erro` (383 notas de 15/11).
- **Depende do fornecedor (Assist):** por que o usuário é rejeitado nessas janelas (bloqueio por tentativas, sessão única, replicação/manutenção,
  usuário compartilhado entre homolog `:8888` e produção). Perguntas prontas em `docs\OPERACAO.md` §12.1. Não confirmado; o integrador não elimina essa causa.

## Correção no integrador
- Falha do GetToken (401, 403, 5xx, timeout, rede, resposta sem token) lança `AutenticacaoWmsIndisponivelException` e **nunca manda a nota para `Erro`**:
  o pacote é interrompido, as notas integradas vão para `Processados` e as demais ficam na entrada (ZIP com XML pendente não é movido).
- Cooldown global `Wms:CooldownTokenSegundos` = `60;120;300;600;900` (1, 2, 5, 10, 15 min) depois de `Wms:TokenTentativasImediatas` = 2 tentativas; sem chamadas
  ao WMS durante o cooldown; single-flight; sucesso zera. O serviço não abre pacote enquanto aguarda ("Aguardando autenticação do WMS até HH:mm:ss").
- Token com validade máxima de 3 min (`Wms:ValidadeMaximaTokenMinutos`); renovação que falha com token ainda válido pelo `exp` não aborta.
- Diagnóstico no log (status, `WWW-Authenticate`, id da requisição, corpo truncado a 200 caracteres), sem token, usuário ou senha.
- Alerta por e-mail na indisponibilidade e na recuperação (`Wms:AlertaAutenticacao`); relatório e e-mail do pacote mostram "Falhas de autenticação (GetToken): N" só quando > 0.
- CLI `KitWmsIntegrador.Worker.exe --testar-token` (um GET; imprime só `GetToken: HTTP <código> OK|FALHA`; exit 0/1).
- `deploy\reprocessar-erro-401.ps1` (com `-WhatIf`, `-Force`, `-DestinoPadrao`) para devolver à entrada as notas que foram para `Erro` por 401, inclusive as 383 de 15/11, só com OK do usuário e depois de `--testar-token` OK.
- Efeito visível: em falha de login as notas ficam na pasta de entrada por alguns minutos (antes iam para `Erro`); descrito nos manuais, no deck e no runbook.

## Tipos de separação da I50
`I50:TipoSeparacao` = `PIC;FEF;PLL` e `I50:TipoSeparacao2` = `AGL;FEF;PLL`, enviados no cabeçalho e em todos os itens (era `RES;FEF;PLI`). Provado por testes de
configuração e de payload; o checklist manda conferir que o `appsettings.Production.json` do servidor não os sobrescreve nem inverte as chaves de placa
(Puxada normal COM placa `EnviarPlacaVeiculo=true`; TSP SEM placa `EnviarPlacaVeiculoTsp=false`).

## Valores de espera de coleta
`Pacote:SilencioSegundos` = 20 (era 15), `Pacote:JanelaMaximaSegundos` = 180 e `Pacote:EstabilizacaoSegundos` = 3, `ModoJanela=Silencio`. O log de produção mostrou lotes de 15 a 27 s de
duração total (cerca de 1 min no pior caso), que cabem no teto; o silêncio é o intervalo entre arquivos. Se um lote ainda dividir, subir `SilencioSegundos` ou usar
`Pacote__SilencioSegundos` sem nova release.

## Documentação
`docs\ENCERRAMENTO.md`, `docs\OPERACAO.md` (§12.1 runbook: sintomas, `--testar-token`, cooldown, alerta, reprocesso, quando acionar a Assist, perguntas), `docs\INSTALACAO-SERVIDOR.md`,
manuais de usuário e técnico (MD, HTML, PDF), manual do agente de IA e deck (HTML e PDF, só onde há mudança visível; slides de horas intocados).
Revisão do código: `review-release-1611.json` com verdict APPROVED. Verificação do pacote: `verificacao-pacote-1611.md`.

## Pendências
Publicar no servidor (usuário); reprocessar as 383 notas (OK do usuário); enviar as perguntas à Assist; usuário exclusivo de produção; ligar I51/I60 e a tela de devolução só com OK;
texto do ENC; trocar senhas que apareceram em chat.

## Esforço (somente horas)
- **Horas de IA (estimadas pelo tempo de execução das sessões de 16/11/2026):** aprox. 1,5 h neste passo de pacote e documentação (build/testes, empacotamento, checklist,
  runbook, manuais, deck e relatório), mais a implementação, os testes e a revisão do código registrados nos commits anteriores do dia (investigação às 10:38 e código às 11:38).
  Soma estimada do dia: aprox. 4 a 5 h de IA.
- **Horas humanas:** não informadas para 16/11/2026; nenhum valor foi inventado. O registro mestre (`docs\CONTROLE-ESFORCO.md`) não foi alterado por este passo.