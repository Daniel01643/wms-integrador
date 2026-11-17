# Relatório: hotfix da I51 (quantidade separada por embalagem), 17/11/2026

## Problema
A I51 usava `qtdeProdutoSep` (unidades). Para produto cadastrado no WMS com conversão de embalagem, a quantidade separada vinha multiplicada. Caso 5006566: nota com 18 embalagens, 1 embalagem = 10 unidades. A I51 devolvia 180 e a I60 fazia o falso ajuste 18 → 180.

## Causa
A comparação entre a nota (unidade da NF-e/I50, em embalagens) e a quantidade separada era feita com campos de unidades diferentes. O campo `qtdeEmbalagemSep` da I51 não era lido.

## Correção
- Nova opção `I51:CampoQuantidadeSeparada`: `Embalagem` (padrão) ou `Produto` (comportamento antigo). Validada na partida; outro valor impede a inicialização.
- `ParserI51` lê `qtdeEmbalagemSep` e a soma de `detalhes[].qtdeCaixaSep` (ambos opcionais).
- `SelecionadorQuantidadeI51` e `CasadorDocumentoNota` resolvem a quantidade antes do cálculo de ajuste; a I60 passa a enviar embalagens.
- Órfãos guardam a quantidade já escolhida. Arquivos antigos (em unidades) são lidos como estão.
- O relatório diário da I60 mostra o campo usado na seção "Ajustes de quantidade".

## Fallback
Ordem: `qtdeEmbalagemSep` → soma de `qtdeCaixaSep` → `qtdeProdutoSep`. No último caso, a nota recebe Observacao e o log registra aviso. Com a opção `Produto`, vale o comportamento antigo.

## Testes
| | Passados | Ignorados | Falhas |
|---|---|---|---|
| Antes (baseline) | 991 | 9 | 0 |
| Depois | 1009 | 9 | 0 |

- 18 testes novos: produto com conversão (18/180/nota 18, sem ajuste), separação parcial (nota 11, emb 9 → ajuste 11→9), emb ausente/vazia/inválida, sem emb e sem detalhes, opção `Produto`, órfão, parser, validação da opção e relatório.
- `dotnet build` em Release: 0 avisos, 0 erros. O mesmo `dotnet test` foi repetido na geração do pacote (1009/9/0).
- Fonte: `verificacao-i51-embalagem.md`.

## Revisão
Veredito: **APPROVED**, sem achados (`review-i51-embalagem.json`).

## Pacote 2026.11.17-1
- Arquivo: `c:\wms-integrador\artifacts\2026.11.17-1\KitWmsIntegrador-2026.11.17-1-win-x64.zip` (46.745.901 bytes).
- SHA-256: `2FC35BD4729BEB2D3FD953A2C611E988B6043A2C1A9D6A664F7D8DC46919537C`
- Gerado com `deploy\publicar.ps1 -Versao 2026.11.17-1`. Manuais opcionais em `artifacts\2026.11.17-1\manuais.zip`. `artifacts\` é ignorada pelo git.

## Checklist de release
`.agents\tasks\checklist-release-2026-11-17-1.md` (commit `ec5737c`), com 8 itens: copiar, conferir hash, backup do config, conferir a opção, publicar (`-WhatIf` e real), verificar a partida da I51, verificar o caso 5006566 e rollback.
- Sem a chave no config, vale o padrão `Embalagem`. Se estiver `Produto`, trocar.
- Rollback para a 2026.11.16-3 traz de volta o risco do falso ajuste (essa versão não conhece a chave). Avaliar desligar a I60 nesse caso.

## Commits locais (a partir de origin/main)
- `6136eaa` fix: I51 usa quantidade em embalagens (qtdeEmbalagemSep) no ajuste da I60
- `0bfc28e` docs: quantidade separada da I51 em embalagens (escopo, operacao, manual tecnico, HTML/PDF e verificacao)
- `ec5737c` docs: checklist de release 2026.11.17-1 (I51 por embalagem), deploy nao executado

O branch `main` está 3 commits à frente de `origin/main`. Este relatório e `review-i51-embalagem.*` não estão commitados.

## Push e deploy
**NÃO houve push. NÃO houve deploy.** Nada foi copiado, publicado ou alterado no servidor. O pacote existe só localmente.

## Esforço (somente horas)
- Horas de IA: aprox. 1,0 h (estimativa pelo tempo de execução das sessões do agente; os commits dessa etapa vão de 14:23 a 14:36 de 17/11, e o início das sessões não foi registrado).
- Horas humanas: a informar pelo usuário.
- `docs\CONTROLE-ESFORCO.md`, `docs\controle\*` e `docs\diario\*` não foram alterados.
