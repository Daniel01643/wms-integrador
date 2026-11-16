# Deck atualizado para o estado de 16/11/2026 (slides 3, 4, 6, 7, 11, 14 e PDF)

Dois commits locais sobre `origin/main` (55e0a84): 3222aa0 (deck, PDF, capturas, relatório) e d38e7ff (ajuste do relatório). O deck passa a refletir a release 2026.11.16-3 em produção, I51/I60 e devolução ligadas, retenção de 30 dias, teto de coleta de 180 s, causa do 401 confirmada pelo fornecedor e projeto travado como finalizado. O slide 14 foi reescrito como "Estado em 16/11/2026: projeto finalizado".

Watch for: (1) o rodapé do slide 7 ainda diz "horas humanas a informar", enquanto o fechamento do dia já registra 4,00 h humanas (possível, não bloqueia, e o slide de horas foi mantido de propósito); (2) o texto "Reaberto só para novos ajustes" no slide 14 é ambíguo (confirmado, cosmético); (3) o IP alternativo http://200.200.200.213:8085/ do slide 14 não foi verificado por esta revisão (possível).

**Verdict**: APPROVED

## High-level view

A edição é cirúrgica: o diff do HTML tem 44 inserções e 41 remoções, concentradas em frases dos slides 3, 4, 6, 7 e 11, num bloco CSS novo (`compact-fim`) e no slide 14. O BOM EF BB BF continua no início do arquivo e os acentuados seguem corretos no deck.

Os fatos pedidos aparecem corretos: Puxada normal com placa e TSP sem placa, tipoSeparacao PIC;FEF;PLL e tipoSeparacao2 AGL;FEF;PLL, silêncio de 20 s com teto de 180 s, retenção de 30 dias, I51/I60 ligadas com alerta ENC→IMP e relatório às 12:00, devolução ligada com link SRVAPPALC02:8085, causa do 401 (3 servidores atrás de balanceador, token por envio, usuário BRF) e a ressalva de que as 383 notas não foram reprocessadas pelo integrador. O projeto consta como finalizado em 16/11/2026, com pendências fora do escopo.

Os slides de horas e `CONTROLE-ESFORCO.md`, `docs/controle/*` e `docs/diario/*` não aparecem nos dois commits. Não há valores monetários no deck, senhas, XML ou ZIP versionados. Os commits estão em pt-BR e nada foi publicado (main está 2 commits à frente de origin/main).

<details>
<summary>Issues (3)</summary>

1. **Rodapé de horas do slide 7 defasado (possível)**: diz "horas humanas a informar", mas o dia fechou com 4,00 h humanas. Atualizar só se o usuário quiser; foi mantido por regra (slides e números de horas intocados).
2. **Redação "Reaberto só para novos ajustes" (confirmado)**: no slide 14 soa como "já foi reaberto". Trocar por "Só será reaberto em caso de novos ajustes".
3. **IP alternativo da tela de devolução (possível)**: confirmar que 200.200.200.213:8085 é o endereço correto do servidor antes de distribuir o deck.

</details>

<details>
<summary>Details</summary>

## Edição pontual e preservação do arquivo

O HTML começa com EF BB BF. O diff sem contexto mostra apenas as linhas esperadas: frases dos slides 3 (teto de 180 s), 4 (401 com token por envio), 6 (devolução no ar), 7 (chaves e situação da I51/I60), 11 (GetToken, retenção, nota de rodapé) e o bloco do slide 14. O CSS novo `.compact-fim` só afeta o slide 14. No slide 14 saíram os itens antigos de "mais de 800 integrações", "580 testes" e a nota de rodapé sobre o 401 e o Windows Server 2012. Isso é coerente com a reescrita pedida, mas o dado do servidor deixou de constar no slide.

## Varredura de termos desatualizados

"60 dias", "10 s", "desligad", "enviadas": nenhum resultado. "sem placa" aparece na linha 293 (Puxada TSP sem placa, correto), na 596 (idem, correto) e na 481 (histórico de 11/11: tela da I40 sem placa, fato daquela data). "BLUEFOOD" aparece em duas linhas do slide 14: a conta antiga que alternava 401/200 e a pendência com a Assist. Ambas coerentes. Nenhum "R$" ou "US$" no deck. Não há senha no deck.

## Slides de horas e controles

Nenhuma linha das tabelas de horas (slides 8, 12, 13 em numeração do arquivo) entrou no diff, e os documentos de controle não aparecem na lista de arquivos dos commits. Único ponto a notar é o rodapé do slide 7 (Issue 1).

## Capturas e PDF

Existem slide3, 4, 6, 7, 11, 11b e 14 em `shots-deck-1611/`, cobrindo todos os slides alterados. Vi 4, 7, 11 e 14: sem texto estourado e com rodapé e numeração inteiros. O slide 14 usa cerca de 70% da altura, sem cortes. O PDF mudou de 318933 para 317432 bytes no commit, consistente com regeneração; não foi regenerado nesta revisão.

## Higiene dos commits

Autor e mensagens em pt-BR ("docs: atualiza o deck com o estado de 16/11/2026 (projeto finalizado)"). Nenhum *.xml ou *.zip rastreado. Sem push.

</details>

<details>
<summary>Arquivos</summary>

docs/apresentacao/apresentacao.html (slides 3, 4, 6, 7, 11, 14 e CSS), apresentacao.pdf (regenerado), .agents/tasks/relatorio-deck-1611.md e shots-deck-1611/*.png (evidência).

</details>
