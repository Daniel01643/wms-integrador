# Revisão das docs da I51/I60 (commits 6996b24 e 5ce845f)

Escopo: docs/apresentacao e docs/manuais (mais ESCOPO, OPERACAO, ARQUITETURA, ROTEIRO e exemplo do relatório). Sem rebuild. Evidência: verificacao-docs-i51-i60.md, appsettings.json e design-i51-i60.md.

**Verdict**: APPROVED

## Resumo
O deck ganhou 2 slides (6 e 7), CSS próprio `compact-i51a/b`, rodapés renumerados para n / 14 e ajustes de uma linha nos slides 5 e 14. Os manuais ganharam seções novas (Usuário 10 e 11; Técnico 8.5 e 8.6). O conteúdo bate com o código e o design. Os pontos abaixo são menores e não bloqueiam.

## Checagens pedidas
- Fidelidade: chaves `I51:Habilitada` e `I60:Habilitada` (false), identificador `WEBAPI-I51`, intervalo de 5 min, atraso de 24 h, relatório às 12:00, TODOS/UNICO, INE, ENC para IMP com erro registrado, ondas TSP, `Devolucao:Habilitada=false` e visão por pacote como seção do relatório diário conferem com o appsettings.json e com o design. "839 testes aprovados" confere com verificacao-i51-i60.md. O texto declara que não houve teste no WMS real (confirmada).
- BOM: apresentacao.html começa com EF BB BF (confirmada).
- Edições cirúrgicas: diff do deck de 120 inclusões e 23 remoções, sem mudança de fins de linha. Só os slides novos, os rodapés/contador, a nota do slide 5 e duas linhas do slide 14 mudaram (confirmada).
- Slide de comparação de horas: só o número do rodapé mudou (11 / 12 para 13 / 14). Slides de esforço (9 e 11) intactos (confirmada).
- Valores monetários e horas humanas: nenhuma ocorrência de R$, US$, custo ou valor/hora nas linhas adicionadas. O slide 7 diz que as horas humanas ficam "a informar". As únicas horas nas docs novas são o prazo de atraso (24 h) (confirmada).
- I40: nos manuais está em seções próprias (Usuário 11, Técnico 8.6 "removível") e como "última alternativa". No deck aparece como box no slide 6 e uma linha no slide 14 (ver achado 1).
- Proibidos: nenhum arquivo de docs/CONTROLE-ESFORCO.md, docs/controle ou docs/diario nos dois commits de docs (o fechamento 00f020c, anterior, é fora do escopo). Nenhum *.xml ou *.zip rastreado (confirmada).

<details>
<summary>Issues (3)</summary>

1. **I40 sem slide próprio** (possible, baixo) — no deck a I40 é um box dentro do slide 6 mais uma linha no slide 14, e não uma seção separada. Remover é simples, mas se "removível" valer também para o deck, considerar um slide dedicado.
2. **Fluxo "como antes" com I51 desligada** (likely, baixo) — o slide 7 diz que com a I51 desligada a I60 "sai junto da I50, como antes". Isso só vale se `I60:Habilitada=true`, e o padrão é false. Acrescentar essa condição.
3. **PDFs não reinspecionados** (possible) — a revisão confiou na verificação visual registrada (slides 6, 7 e 14, páginas dos manuais). Não houve nova leitura dos PDFs.

</details>