# Verificacao FEAT-004: documentacao da release 2026.11.13-1

## O que mudou
- `docs\manuais\MANUAL-USUARIO.md`: coleta por silencio (15 s, teto de 3 min, copiar o lote de uma vez), placa (Puxada normal com placa, TSP sem), cartoes do resumo (inclui Transportes) e etapa "Coleta (janela de silencio)", retencao de 30 dias (FAQ e devolucao), homolog 13/11 (7 de 7), aviso imediato por e-mail, bloco Atencao do relatorio diario, devolucao com login simples (ultima alternativa, busca de 30 dias).
- `docs\manuais\MANUAL-TECNICO.md`: chaves `Pacote:ModoJanela/SilencioSegundos/JanelaMaximaSegundos` e como voltar a `Fixa`; retencao 30 dias e impactos; placa (8.2, tabela, secao 12); `I60:QtdeVolumeZero/QtdeTotalNotaZero`; pre-requisitos do WMS (EDI WEBAPI-I51 com WEBSERVI e evento interno); alerta imediato (`AlertaImediato`, `destinatarios-i60.txt`, `Estado\i51\alertas`, calibracao de `PadroesDocCargaEncerrado`); bloco Atencao; relatorio de integracao (10.1.1); login simples da devolucao; linhas de log novas; tabela da secao 12.
- `docs\apresentacao\apresentacao.html` (UTF-8 com BOM, 16 trocas pontuais, 14 slides mantidos): slides 3, 5, 6, 7 e 14. Slides de horas (9 a 13) sem alteracao.
- Regenerados: `MANUAL-USUARIO.html/.pdf`, `MANUAL-TECNICO.html/.pdf`, `apresentacao.pdf`.

## Comandos
- `powershell -NoProfile -ExecutionPolicy Bypass -File docs\manuais\gerar-manuais.ps1` (sem erro)
- Edge headless `--headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf=<pdf> file:///<html>` para os 3 PDFs
- Edge headless `--screenshot` (`?shot#N` para os slides)
- `Select-String '60 dias|sem placa'` em manuais e deck: so restam "TSP sem placa", "Placa/Puxada TSP sem placa", o historico "era 60 dias" e a linha de 11/11 da I40 (slide de horas, intocado)
- BOM do deck: 239,187,191

## Screenshots (conferidos visualmente)
- `.agents\tasks\shots-docs-1311\deck-slide-3.png`, `deck-slide-5.png`, `deck-slide-6.png`, `deck-slide-7.png`, `deck-slide-14.png`
- `.agents\tasks\shots-docs-1311\manual-usuario-coleta-placa.png`

## Observacoes
- Sem build/teste de codigo (so docs). Numero de testes citado (926 aprovados, 9 ignorados) vem do commit de integracao e3a398f.
- Alguns `**` multilinha antigos do Manual Tecnico seguem sem negrito no HTML (preexistentes, nao tocados).
- Nenhum valor monetario nem horas humanas nos textos.
## Conferencia de convergencia (iteracao 1)
- Todos os topicos de FEAT-004 conferidos contra o codigo (OpcoesPacote 15 s/180 s/ModoJanela, appsettings: placa true/false, retencao 30, AlertaImediato, PadroesDocCargaEncerrado, Porta 8085) e contra o plano: sem lacunas.
- Sem sobras de '60 dias' (so o historico 'era 60 dias') nem de 'Puxada normal sem placa'; BOM do deck 239,187,191; slides de horas sem diff.
- Regeneracao dos HTML dos manuais sem mudanca de conteudo (so o carimbo de versao com o hash do commit); HTML/PDF/apresentacao.pdf mais novos que as fontes.
- Nenhum valor monetario nem horas humanas inventadas.
