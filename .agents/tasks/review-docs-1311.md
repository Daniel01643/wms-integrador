# Documentação da release 2026.11.13-1: manuais, deck, HTML/PDF

Os manuais e o deck cobrem os temas de FEAT-004 com as chaves reais e batem com o plano e a configuração. Não achei bloqueio.

**Verdict**: APPROVED

Watch for: nada bloqueante. O número de testes (926 aprovados) vem do commit de integração e3a398f, não foi reexecutado (confirmed, a evidência declara isso).

## High-level view

A janela de silêncio (`Pacote:ModoJanela/SilencioSegundos/JanelaMaximaSegundos`, 15 s e teto de 180 s, volta para `Fixa` por configuração) está nos dois manuais e no slide 3. A placa segue a decisão do plano: Puxada normal com placa, Puxada TSP sem. A retenção de 30 dias aparece no FAQ, na devolução e no slide de próximos passos. O único "60 dias" que resta é o histórico "era 60 dias".

A I51 como gatilho da I60 traz o resultado do homolog de 13/11 (7 de 7 associadas), os defaults `I60:QtdeVolumeZero/QtdeTotalNotaZero` e os pré-requisitos do WMS (EDI `WEBAPI-I51` com `WEBSERVI` e o evento interno). O alerta imediato está documentado com `AlertaImediato`, `destinatarios-i60.txt`, `Estado\i51\alertas` e `PadroesDocCargaEncerrado`, mais o bloco Atenção do relatório. A devolução com login simples tem seção própria (8.6, "Login simples") como última alternativa.

<details>
<summary>Issues (1)</summary>

1. **Alterações não commitadas** — `docs/CONTROLE-ESFORCO.md`, `docs/controle/MENSAL.md` e `SEMANAL.md` estão modificados no working tree, e `docs/diario/2026-11-13.md` está sem rastreio. Os commits de documentação (a5f32b3, 4c9d82a) não os tocam (confirmed por `git diff 5f621dd HEAD`). Informativo, não bloqueante.

</details>

<details>
<summary>Details</summary>

## Verificações feitas

- Os commits a5f32b3 e 4c9d82a alteram só manuais (MD, HTML e PDF), `apresentacao.html` e `apresentacao.pdf`, os screenshots e os arquivos de tarefa em `.agents`. Não há *.xml nem *.zip rastreados e nenhuma credencial.
- `docs/CONTROLE-ESFORCO.md`, `docs/controle/*` e `docs/diario/*` não aparecem no diff entre origin/main e HEAD.
- O BOM do deck está preservado (239,187,191). O diff do deck tem 16 trocas pontuais e só toca os slides 3, 5, 6, 7 e 14. Os slides de horas não aparecem no diff.
- Busca por `60 dias`, `R$` e `US$` nos MD e no deck: só o histórico "era 60 dias" em MANUAL-TECNICO.md:711. Não há valor monetário.
- Seções do Manual Técnico conferidas por grep: 7 de 7, `WEBSERVI`, `destinatarios-i60.txt`, `PadroesDocCargaEncerrado`, `ModoJanela` e as seções 8.6 e "Login simples".
- Screenshots abertos: `deck-slide-6.png` (I51→I60, ENC, alerta imediato e a caixa "Última alternativa" com login simples e busca de 30 dias, rodapé 6 / 14) e `manual-usuario-coleta-placa.png` (janela de silêncio, placa e relatório com cabeçalho e status). Layout íntegro e texto correto.
- A evidência do coder (`verificacao-docs-1311.md`) está presente. Ela lista comandos, regeneração dos HTML e PDFs e conferência de convergência.

## Não verificado

Não reexecutei a geração nem os PDFs, como pedido. Também não abri os demais screenshots (slides 3, 5, 7 e 14).

</details>

<details>
<summary>Arquivos</summary>

Manuais MD/HTML/PDF, apresentacao.html/pdf e os screenshots em `.agents\tasks\shots-docs-1311`.

</details>
