# Verificação do pacote da release 2026.11.16-1 (para o revisor)

Trabalho direto na `main`, commits locais, sem push. Nada foi copiado nem executado no servidor.

## O que rodou (16/11/2026, máquina de build)

| Passo | Comando | Resultado |
|---|---|---|
| Build | `dotnet build c:\wms-integrador\KitWmsIntegrador.sln -c Release` e `--no-incremental` | Compilação com êxito, **0 avisos, 0 erros** |
| Testes | `dotnet test c:\wms-integrador\KitWmsIntegrador.sln -c Release` | **966 aprovados, 9 ignorados (stress), 0 falhas** (total 975) |
| Pacote | `powershell -File deploy\publicar.ps1 -Versao 2026.11.16-1` | `artifacts\2026.11.16-1\KitWmsIntegrador-2026.11.16-1-win-x64.zip`, 46.732.548 bytes, 339 arquivos |
| SHA-256 do zip | `Get-FileHash -Algorithm SHA256` | `256560BB0C023FC436D05AB698D52836ED91474875FA0E13ADFE64F2D978906E` |
| Manuais | `docs\manuais\gerar-manuais.ps1` + Edge headless (PDF); deck em PDF pelo Edge | HTML e PDF regenerados; copiados para `artifacts\2026.11.16-1\manuais` e `manuais.zip` (SHA-256 `125D61C00D4D54B18CC7AC5F784926EDFE0AFACCFC1ABC48A0FEE9B45836C37A`) |

O `appsettings.json` dentro do zip foi lido sem extrair e confere: `I50:TipoSeparacao=PIC;FEF;PLL`, `TipoSeparacao2=AGL;FEF;PLL`,
`EnviarPlacaVeiculo=true`, `EnviarPlacaVeiculoTsp=false`, `Pacote` 20/180/3 (`ModoJanela=Silencio`), `Limpeza:DiasRetencao=30`,
`I51/I60/Devolucao:Habilitada=false`, `Wms:TokenTentativasImediatas=2`, `CooldownTokenSegundos=60;120;300;600;900`,
`ValidadeMaximaTokenMinutos=3`, `AlertaAutenticacao=true`. `artifacts\` é ignorado pelo git (`git check-ignore artifacts`): zip fora do repositório.

## Documentos alterados (edições pontuais, UTF-8 com BOM nos `.md` e no deck)
`docs\ENCERRAMENTO.md`, `docs\OPERACAO.md` (§11 e novo §12.1 runbook do 401), `docs\INSTALACAO-SERVIDOR.md`,
`docs\manuais\MANUAL-USUARIO.md`, `MANUAL-TECNICO.md` (novo §8.7), `MANUAL-AGENTE-IA.md`, HTML/PDF dos dois manuais,
`docs\apresentacao\apresentacao.html` e `.pdf` (somente slides com mudança visível ao usuário; slides de horas intocados).
Os HTML gerados pelo script seguem sem BOM, como nas versões anteriores. Não foram editados `docs\CONTROLE-ESFORCO.md`, `docs\controle\*` nem `docs\diario\*`.

## Entregáveis
`.agents\tasks\checklist-release-2026-11-16.md` e `.agents\tasks\relatorio-release-1611.md`.