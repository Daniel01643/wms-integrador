# Relatório da release 2026.11.13-1 (KitWms Integrador)

Nada foi enviado ao servidor SRVAPPALC02 nem ao GitHub. Commits locais na `main`; o push fica com o orquestrador.

## Entregáveis

- Pacote local: `c:\wms-integrador\artifacts\2026.11.13-1\KitWmsIntegrador-2026.11.13-1-win-x64.zip` (self-contained win-x64, 44,6 MB), SHA-256 `F097D770C9D0B4432BB2E9CB5614505C5E1918EAB5D7C8C762650DB8E565BC08`. Mais `manuais\` e `manuais.zip`. Pasta `artifacts\` ignorada pelo git (o zip não é versionado).
- Checklist: `c:\wms-integrador\.agents\tasks\checklist-release-2026-11-13.md` (publicação, config "pode aplicar agora" x "somente depois do OK do usuário", pastas Puxada/PuxadaTSP, rollback, verificação pós-deploy).

## O que mudou por FEAT

- **FEAT-001, janela de silêncio:** `Pacote:ModoJanela=Silencio` (padrão), fecha a coleta após 15 s sem arquivo novo, teto de 180 s; `Fixa` volta ao modo antigo. Lotes grandes deixam de ser divididos em vários pacotes. Log "Fim da coleta por ...". Rótulo do relatório: "Coleta (janela de silêncio)".
- **FEAT-002, defaults, placa e retenção:** `I60:QtdeVolumeZero` e `QtdeTotalNotaZero` = true; I51/I60/Devolução continuam desligadas; placa corrigida (Puxada normal COM placa `EnviarPlacaVeiculo=true`, TSP sem `EnviarPlacaVeiculoTsp=false`); retenção de 30 dias (`Limpeza:DiasRetencao`, `I51:DiasRetencaoEstado`), textos e testes ajustados.
- **FEAT-003, alerta imediato da I60:** varredura por estado a cada ciclo da I51 (rejeitada, ENC/IMP e pendente, resposta não reconhecida, atrasada), um e-mail em lote por ciclo, um aviso por nota e evento, arquivo de calibração `Estado\i51\alertas\AAAA-MM-DD.log`, opção `I60:AlertaImediato`, bloco "Atenção" no topo do relatório diário (HTML, e-mail e PDF).
- **FEAT-004, documentação:** manuais (MD, HTML, PDF) e deck (HTML, PDF) atualizados; slides de horas intocados.
- **FEAT-005, pacote e fechamento:** `docs\ENCERRAMENTO.md` (versão, itens da release, 60 para 30 dias, pendências), `docs\INSTALACAO-SERVIDOR.md` (subseção da release em §3.9), `docs\OPERACAO.md` (seção 11), checklist e este relatório. Os manuais não precisaram ser regenerados (nenhum insumo mudou).

## Verificação

- Cópia temporária (`$env:TEMP\kitwms-build-1311`, apagada no fim; o Worker local não foi parado): `dotnet build KitWmsIntegrador.sln -c Release` = 0 avisos, 0 erros; `dotnet test` Release = 926 aprovados, 9 ignorados (stress), 0 falhas (linha de base inicial: 891 aprovados).
- Pacote: zip lido sem extrair no servidor; contém `KitWmsIntegrador.Worker.exe` e `appsettings.json` com `I51/I60/Devolucao:Habilitada=false`, `QtdeVolumeZero=true`, `QtdeTotalNotaZero=true`, `AlertaImediato=true`, retenção 30/30, placa true/false, `ModoJanela=Silencio` 15/180.
- Revisões: `review-release-1311.json` APPROVED e `review-docs-1311.json` APPROVED.
- Evidências visuais: `.agents\tasks\alerta-i60-email.png`, `.agents\tasks\relatorio-i60-enc-nao-reconhecida.png`, `.agents\tasks\shots-docs-1311\`.

## Pendências

- Publicar no servidor seguindo o checklist (nenhuma ação remota foi feita).
- Ligar I51 e I60 juntas e a tela de devolução só com OK do usuário e dos pré-requisitos do WMS de produção (EDI WEBAPI-I51 com WEBSERVI e evento interno do retorno da separação).
- Texto real do ENC ainda desconhecido: calibrar `I60:PadroesDocCargaEncerrado` com o primeiro alerta.
- Com a retenção de 30 dias, a primeira limpeza no servidor apaga arquivos com mais de 30 dias.
- Em `git status` há alterações que não são desta release e não foram commitadas: `docs\CONTROLE-ESFORCO.md`, `docs\controle\*`, `docs\diario\2026-11-13.md` (registro de esforço, fora do escopo deste passo).

## Esforço (somente horas)

- Horas de IA, estimativa do plano: janela de silêncio 1,0; defaults/placa/retenção 0,7; alerta I60 1,8; documentação 2,0; pacote e checklist 0,8; revisões 1,0; total aproximado 7,3 h. O passo FEAT-005 (pacote, docs, checklist e relatório) levou cerca de 0,3 h de execução. Medição oficial fica no registro mestre `docs\CONTROLE-ESFORCO.md`, mantido pelo orquestrador.
- Horas humanas: a informar pelo usuário (não informadas até aqui; nada foi inventado).
