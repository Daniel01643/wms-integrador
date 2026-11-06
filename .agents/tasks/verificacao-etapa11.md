# Verificação — Etapa 11 (Fechamento financeiro + deck final)

## Escopo
Tarefa de documentação/apresentação. Nenhum arquivo `.cs` alterado. Não foram tocados
`docs/diario/*`, `docs/controle/SEMANAL.md` nem `docs/controle/MENSAL.md`.

Arquivos produzidos/editados:
- `docs/CONTROLE-FINANCEIRO.md`
- `docs/apresentacao/deck.html`
- `docs/apresentacao/deck.pdf` (regenerado)
- `.agents/tasks/verificacao-etapa11.md` (este arquivo)

## Conferência dos números (coerência entre os dois documentos)
- Horas humanas: 29/10=2,0 + 30/10=5,0 + 03/11=3,0 + 04/11=4,5 + 05/11=4,75 + 06/11=6,0
  = **25,25 h** + 1,0 h (apresentação/treinamento, planejada) = **26,25 h**.
- Horas IA: 0,6 + 1,8 + 1,5 + 2,5 + 3,5 + 4,5 = **14,4 h** (estimada por tempo de sessão).
- Valor/hora humano: **R$ 28,70** (salário ÷ 220 h/mês; salário não exposto).
- Valor/hora IA (proporcional): **~R$ 0,95** (US$ 40/mês × R$ 5,20 ÷ 220 h ≈ R$ 0,95/h).
- **26,25 × 28,70 = R$ 753,38** (custo humano). ✔
- **14,4 × 0,95 = R$ 13,68** (custo IA, ilustrativo). ✔
- Total: 753,38 + 13,68 = **R$ 767,06 ≈ R$ 767**. ✔
- Câmbio de referência: R$ 5,20/US$ — marcado como atualizável no fechamento final.

Os mesmos números (horas, valor/hora, custos parciais e total) constam tanto em
`CONTROLE-FINANCEIRO.md` quanto em `deck.html` (slide de fechamento financeiro).

## Slides do deck
Estrutura final: 12 slides, `data-i` contínuo de 0 a 11, 12 `<section>` abertas e
12 `</section>` fechadas. Contadores de rodapé de 1/12 a 12/12 (o total também é
derivado em JS a partir de `slides.length`). Sem BOM; arquivo termina em `</html>`;
12 blocos base64 intactos, todos com 46748 caracteres (sem truncamento).

Slides adicionados (antes do slide de situação):
- `data-i="7"` — Linha do tempo e etapas (tabela de etapas por dia com duração; total
  26,25 h humanas + ~14,4 h IA; nota sobre monitoramento/resumo semanal e ciclo de
  release/rollback).
- `data-i="8"` — Desempenho em produção (KPIs ~87 ms médio, +800 integrações, 50–150x;
  tabela de robustez: placa, I10/I30=0, TLS 1.2, GetToken retry, não-XML, limpeza/subpastas).
- `data-i="9"` — Fechamento financeiro (R$ 753,38 humano, ~R$ 13,68 IA, ~R$ 767 total,
  com ressalvas: salário não exposto, IA como custo proporcional, câmbio atualizável).
- `data-i="10"` — Comparativo de custo em 3 colunas (tradicional ~R$ 80 mil / assistido
  por IA ~30–55% / este projeto ~R$ 767) com nota de rodapé de referência de mercado.

Slide alterado:
- `data-i="11"` — Situação do projeto e próximos passos. Atualizado para EM PRODUÇÃO
  (validado, serviço via NSSM, manuais, monitoramento). "Em acompanhamento": 401
  intermitente mitigado por retry, compatível com a conta de integração ainda em
  provisionamento (mesmos dados), deve estabilizar. "Próximos passos": conclusão do
  provisionamento da conta de produção (mesmos dados), acompanhamento da operação,
  atualização do SO do servidor. Nota: TLS 1.2 e GetToken resiliente já tornam o cliente
  robusto a 401 transitório. Sem expor senha.

Contadores de rodapé existentes (slides 2–7) atualizados de `/ 8` para `/ 12`; nav
counter inicial atualizado para `1 / 12`.

**Novo total de slides: 12.**

## deck.pdf
Regenerado via Edge headless:
`msedge.exe --headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf=...`
(Edge em `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`.)

- Arquivo regenerado (data de modificação recente), 274.857 bytes.
- Contagem de páginas: **12 objetos `/Type /Page`** = **12 slides**. ✔
