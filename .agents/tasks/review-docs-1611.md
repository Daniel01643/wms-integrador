# Revisão de docs e pacote da release 2026.11.16-1 (2ª passada)
Evidência lida: `verificacao-pacote-1611.md` (build 0 avisos/0 erros, 966 aprovados, 9 ignorados, 0 falhas; appsettings do zip conferido). Build e testes não foram rerodados. Spot-check: SHA-256 do zip recalculado = `256560BB0C023FC436D05AB698D52836ED91474875FA0E13ADFE64F2D978906E`, igual ao checklist e ao relatório.

## Bloqueio anterior resolvido
`docs\apresentacao\apresentacao.html:601` agora diz "(perguntas ao fornecedor a enviar)", coerente com o checklist §7 e o relatório. O commit cb12981 alterou só essa linha, o PDF do deck e o arquivo de verificação. A linha 621 do deck também não afirma envio. O deck mantém BOM e não contém valores monetários.

## Conferido sem problema
- Checklist completo: pacote + SHA-256, backup de config, deploy, aplicar agora x somente após OK, conferência de Production.json (I50 TipoSeparacao e chaves de placa), `--testar-token` antes e depois, reprocesso das 383 notas com -WhatIf só após token OK e OK do usuário, pastas Puxada/PuxadaTSP, retenção de 30 dias, rollback, verificação pós-deploy.
- Valores: PIC;FEF;PLL, AGL;FEF;PLL, SilencioSegundos=20, JanelaMaximaSegundos=180.
- Relatório: causa raiz, correção, separação, espera de coleta, caminho do checklist, horas de IA; sem valores monetários nem horas humanas inventadas.
- Slides de horas, `docs\CONTROLE-ESFORCO.md`, `docs\controle` e `docs\diario` não alterados. Nenhum `*.xml`/`*.zip`/segredo/log com notas commitado; `artifacts\` ignorado; árvore de trabalho limpa para `docs`. Sem deploy no servidor.

## Veredito
APPROVED. Nenhum bloqueio restante.
