# Verificação visual: deck e manuais da I51/I60

Data: 13/11/2026. Edge headless; PDFs convertidos em PNG (Windows.Data.Pdf) e conferidos por página. As imagens ficam em `.agents/tasks/shots-docs-i51/` (não versionadas).

## Deck (`docs/apresentacao/apresentacao.html`, UTF-8 com BOM preservado)
- Edições cirúrgicas: 2 slides novos (6: fluxo I50 → separação → I51 → I60, ENC → IMP, I40 por último; 7: relatório diário 12:00, chaves `I51:Habilitada`/`I60:Habilitada`, situação), CSS `compact-i51a`/`compact-i51b`, rodapés renumerados (n / 14), contador do nav 1 / 14.
- Slide 5: removida a frase "I60 descontinuada"; slide 14 (antigo 12): I51/I60 em acompanhamento e nos próximos passos; I40 como última alternativa.
- Slide de comparação de horas (agora 13): conteúdo intacto; mudou apenas o número do rodapé (11 / 12 → 13 / 14). Slides de esforço (9 e 11) não alterados; nenhuma hora humana inventada.
- `apresentacao.pdf` regenerado: 14 páginas. Conferidos slides 6, 7 e 14 (sem estouro de rodapé nem truncamento) e a página 6 do PDF.

## Manuais
- Manual do Usuário: seções 10 (I51/I60/relatório diário) e 11 (I40, última alternativa, removível). PDF com 8 páginas; página 8 conferida.
- Manual Técnico: 8.3 atualizada, 8.5 (I51 → I60: chaves, TODOS/UNICO, estado, INE, ENC → IMP, ondas TSP, relatório 12:00, decisão da visão por pacote, comandos) e 8.6 (I40, seção removível), linhas novas na tabela da seção 12. PDF com 18 páginas; página 12 conferida (tabela de chaves sem corte).
- Manual do Agente de IA: parágrafo curto no fluxo (somente Markdown).
- Corrigido negrito quebrado em duas linhas no texto novo (o conversor trabalha por linha).

## Não verificado
- Nada contra o WMS real (homolog pendente); o texto afirma isso.
- Horas: não registradas aqui; horas humanas a informar.
- A I40 entrou nos manuais (Usuário seção 11, Técnico 8.6) a pedido do usuário, só como última alternativa; confirmar com o usuário.