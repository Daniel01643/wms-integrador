# Plano — Identidade visual Friozem nos manuais (HTML + PDF) + complementos

Tarefa de **documentação/apresentação** do KitWmsIntegrador. **NÃO** alterar código `.cs`, nem
`docs\diario\`, nem `docs\controle\`. Branch **main**. **NÃO** rodar `dotnet` (não há build/test;
a verificação é sobre HTML bem-formado/autocontido e PDFs > 0 bytes). Commits pequenos na main,
**um por arquivo nomeado**, **sem push**.

## Achados da exploração (fundamentam o plano)

### Paleta e tipografia (de `docs\apresentacao\deck.html`, `<style>` linhas ~8–121)
- Comentário oficial na linha 8: *"Identidade visual Friozem: azul #17479E, azul claro #54B6E7,
  verde #009A83, cinza #333333 / #C1C1C1. Tipografia: Arial."*
- Cores exatas: **azul `#17479E`**, **ciano `#54B6E7`**, **verde `#009A83`**, cinza texto
  `#333333`, cinza borda `#C1C1C1`, azul escuro de fundo `#0e2a5c`.
- Fonte: `font-family: Arial, Helvetica, sans-serif;` (linha 11).

### Cabeçalho / faixa / rodapé (deck.html)
- `.hd` (linha 17): faixa do cabeçalho `height:112px; background:#17479E; display:flex;
  align-items:center; justify-content:space-between; padding:0 56px;` — **título à esquerda,
  logo à direita**.
- `.hd h1` (linha 19): `color:#ffffff; font-size:34px; font-weight:700;`.
- `.hd img` (linha 20): `width:176px; height:auto; display:block;` (a logo do cabeçalho).
- `.bar` (linha 20): faixa de acento **ciano** sob o cabeçalho: `width:96px; height:5px;
  background:#54B6E7; margin:22px 56px 0;`.
- `.ft` (linha 22): rodapé `background:#eef2f8; color:#333333; font-size:12px;` com
  **`border-top:3px solid #009A83;`** (verde).
- Tabelas (`table.tbl`, linhas 93–102): **cabeçalho azul** (`thead th { background:#17479E;
  color:#fff; }`), **zebra** (`tbody tr:nth-child(even){ background:#f6f8fc; }`), bordas
  `1px solid #e1e7f0`.
- `@media print`/`@page` do deck (linhas ~108–116) usa `size:1280px 720px` (slides) — **NÃO
  reaproveitar**; os manuais precisam de `@page` A4 próprio (ver item C).

### LOGO em base64 (ponto crítico para o coder)
- A logo do cabeçalho aparece **8 vezes** no deck (uma por slide), sempre a **mesma** string.
- **Primeira ocorrência: linha 125** (`c:\wms-integrador\docs\apresentacao\deck.html`), no
  elemento da capa:
  `<img class="logo" src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAjAAAAE5CAYAAACOFho6...`
- Demais ocorrências: linhas **135, 169, 191, 220, 259, 287, 315** (dentro de `<div class="hd">`,
  em `<img src="data:image/png;base64,iVBOR...">`).
- A string base64 tem **46.748 caracteres** (PNG). Início: `iVBORw0KGgoAAAANSUhEUgAAAjAAAAE5CAYAAACO`
  Fim: `...11kAAAAASUVORK5CYII=`. O atributo completo é `src="data:image/png;base64,<BASE64>"`.
- **Como o coder reencontra/extrai**: ler a linha 125 do deck e capturar o grupo da regex
  `data:image/png;base64,([A-Za-z0-9+/=]+)` (todas as 8 ocorrências são idênticas; usar a 1ª).
  Guardar a string e reusá-la no template dos manuais (ver item C). Comando PowerShell de
  extração (sem internet/dotnet):
  ```powershell
  $deck = Get-Content 'docs\apresentacao\deck.html'
  $b64  = [regex]::Match($deck[124], 'data:image/png;base64,([A-Za-z0-9+/=]+)').Groups[1].Value
  ```

### Logo em arquivo (`docs\identidade\`)
- Existem PNGs utilizáveis: `docs\identidade\Logo negativo 02[2].png` (53 KB, logo **negativa**
  — clara, boa sobre o azul do cabeçalho) e `docs\identidade\LOGO_FRIOZEM_VERTICAL-03.png`
  (25 KB), além de `FRIOZEM_LOGOTIPO_02.jpg` (3,7 MB).
- **Decisão**: para garantir **igualdade exata com o deck** e **autocontenção** (sem recursos
  externos), o template dos manuais usa a **base64 do deck.html** (a mesma logo negativa do
  cabeçalho). Os PNGs ficam como referência; não é necessário embuti-los. (Registrado conforme
  pedido: a logo vem da base64 do deck; há PNG utilizável se algum dia se quiser regenerar.)

### Estado atual dos manuais (estrutura)
- **MANUAL-USUARIO.md** (9 seções): 1 O que o sistema faz · 2 Como depositar os XMLs ·
  3 O que acontece depois · 4 Onde ver o resultado · 5 Como ler o relatório (status) · 6 E-mail
  de fechamento · 7 Boas práticas · 8 Como reenviar nota corrigida · 9 FAQ. Já cita: status
  **Integrada**, **Integrada — revisar produto**, **Pendente**, **Não integrada**; XmlInvalido;
  `pacote_<data_hora>.html/.pdf`; share `\\SRVAPPALC02\kitbrf` (e IP `\\200.200.200.213\kitbrf`);
  usuário grava/lê mas **não apaga**. **Sem nada de IA** (manter assim).
- **MANUAL-TECNICO.md** (13 seções): 1 Ambiente · 2 Serviço · 3 Logs · 4 Usuário/senha WMS ·
  5 SMTP · 6 Endpoint WMS · **7 Destinatários (fechamento e resumo semanal)** · 8 Resumo/janela/
  limpeza · 9 Publicar/rollback · 10 Estado/telemetria · 11 Não sobe/TLS · 12 Tabela "Quero
  fazer X" · 13 Segurança. **A seção 7 está desatualizada** (ver A1).
- **MANUAL-AGENTE-IA.md** (7 seções): fluxo, mapa de arquivos, diagnóstico, verificação,
  guarda-rails, propor alterações, referências. **Permanece só em Markdown** (consumo por IA) —
  **não** gerar HTML/PDF dele.
- **manuais\README.md**: tabela de 3 manuais (links `.md`) + referências. Precisa listar as
  versões MD/HTML/PDF (item D).

### Estado atual do e-mail (VERDADE — de `docs\OPERACAO.md` §4 e §4.1, já implementado)
- **Dois** TXT de destinatários, independentes, editáveis a quente (1 e-mail por linha, `#`
  comenta, linhas em branco ignoradas, **relidos a cada envio**, sem reiniciar o serviço):
  - **Fechamento do pacote**: chave `Email:CaminhoDestinatarios`, **default
    `<telemetria>\destinatarios-fechamento.txt`**. Precedência: (1) TXT se tiver ≥1 e-mail →
    (2) `Email:Destinatarios` (lista `;` no JSON) → (3) **não envia** o e-mail, com **aviso no
    log**, mas o **fechamento conclui** (HTML+PDF gerados).
  - **Resumo semanal**: chave `Monitoramento:CaminhoDestinatarios`, default
    `<telemetria>\destinatarios.txt` (modelo `docs/exemplos/destinatarios-resumo.exemplo.txt`).
    Fallback **diferente**: cai no mínimo `ti@friozem.com.br` (não "não-envia").
- `<telemetria>` fica em `data\` (fora do share `kitbrf`). **Recomendação de servidor**: apontar
  **ambas** as chaves para `C:\Friozem\importacao_notas\config\` (ex.:
  `config\destinatarios-fechamento.txt` e `config\destinatarios-resumo.txt`), porque os defaults
  do código caem em `<telemetria>` (dentro de `data\`, fora do share). As listas REAIS não são
  versionadas (`.gitignore` cobre `destinatarios-fechamento.txt`, `destinatarios.txt`).
- Editar `Email:Destinatarios` no JSON é apenas **FALLBACK** do fechamento.

### Ferramentas do ambiente (verificado)
- **pandoc: NÃO instalado.** → conversão Markdown→HTML **sem pandoc e sem internet**: usar um
  **script PowerShell conversor** próprio (subconjunto de Markdown usado nos manuais). Justifica-se
  porque os manuais usam um Markdown previsível (headings `#`/`##`/`###`, tabelas GFM com `|`,
  blocos cercados ```` ``` ````, listas `-`/`1.`, citações `>`, código inline `` ` ``, **negrito**
  `**`, links `[texto](url)`). PowerShell 5.1 disponível.
- **Microsoft Edge (x86) presente** em `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`
  → geração de PDF viável (headless). Chrome (x64) também presente como fallback.

---

## Itens do plano (ordenados por dependência)

- [ ] 1. **A1 — Atualizar MANUAL-TECNICO.md §7 (destinatários) para os DOIS TXT editáveis a quente.**
      Reescrever a seção 7 "Destinatários de e-mail (fechamento e resumo semanal)" para descrever
      **dois** arquivos TXT independentes, editáveis a quente (1 e-mail/linha, `#` comenta, relido
      a cada envio, **sem reiniciar**): **fechamento** via `Email:CaminhoDestinatarios`
      (default `<telemetria>\destinatarios-fechamento.txt`) com precedência **TXT → `Email:Destinatarios`
      (apenas FALLBACK no JSON) → não-envio-com-aviso (fechamento conclui mesmo assim)**; e
      **resumo semanal** via `Monitoramento:CaminhoDestinatarios` (default `<telemetria>\destinatarios.txt`;
      fallback mínimo `ti@friozem.com.br`). Incluir **nota de servidor**: apontar **ambas** as
      chaves para `C:\Friozem\importacao_notas\config\` (ex.: `config\destinatarios-fechamento.txt`
      e `config\destinatarios-resumo.txt`), pois os defaults caem em `<telemetria>` (dentro de
      `data\`, fora do share). Atualizar também a **tabela §12** ("Quero fazer X"): a linha
      "Destinatários do fechamento" deve passar a citar o TXT `destinatarios-fechamento.txt`
      (editar o TXT = vale no próximo envio, **não reinicia**) e manter `Email:Destinatarios` como
      fallback. Não mexer em `.cs`.
      Files: `docs\manuais\MANUAL-TECNICO.md`
      Verify: abrir o arquivo e confirmar que §7 descreve as duas chaves (`Email:CaminhoDestinatarios`
      e `Monitoramento:CaminhoDestinatarios`), a precedência TXT→JSON→não-envio para o fechamento,
      que o JSON `Email:Destinatarios` está rotulado como FALLBACK, e a nota de `config\`. Conferir
      coerência com `docs\OPERACAO.md` §4.1.

- [ ] 2. **A2 — Adicionar capítulo "Guia rápido: usar a IA de apoio" ao MANUAL-TECNICO.md.**
      Novo capítulo curto com: link relativo **`./MANUAL-AGENTE-IA.md`** (funciona em MD e, depois,
      no HTML gerado — ver item 5) e os **guarda-rails** resumidos do manual do agente: não
      versionar/expor segredos (WMS/SMTP); **não ligar a I60** (`I60:Habilitada` segue `false`);
      validar mudanças de código com **build + test** no repositório (fora de produção); **não
      rodar teste de carga em produção/homolog sem OK** do responsável; preferir **config antes de
      código**; separar diagnóstico de ação (execução no servidor fica com o técnico humano).
      Acrescentar a entrada no Índice do manual.
      Files: `docs\manuais\MANUAL-TECNICO.md`
      Verify: o Índice lista o novo capítulo; o corpo contém o link `./MANUAL-AGENTE-IA.md` e os
      cinco guarda-rails; nenhum segredo real aparece.

- [ ] 3. **B — Revisar MANUAL-USUARIO.md para coerência com o estado atual (SEM IA).**
      Revisão de coerência (não reescrita): confirmar/ajustar os **status de linha** exatos
      (**Integrada**, **Integrada — revisar produto**, **Pendente**, **Não integrada**),
      **XmlInvalido** como categoria de erro/contador (não é status de linha), relatório
      `pacote_<yyyyMMdd_HHmmss>.html`/`.pdf`, share `\\SRVAPPALC02\kitbrf` (e IP), e a regra de que
      o usuário **grava e lê mas não apaga**. Corrigir qualquer divergência encontrada. **Não
      inserir nenhuma menção a IA** (requisito explícito).
      Files: `docs\manuais\MANUAL-USUARIO.md`
      Verify: ler o arquivo e confirmar os quatro status, a menção a XmlInvalido como
      categoria/contador, o formato do nome do relatório, o share/IP e a regra de permissão;
      confirmar **ausência** de qualquer menção a IA/agente. Coerência com `docs\OPERACAO.md` §1–§2
      e `MANUAL-AGENTE-IA.md` §1.

- [ ] 4. **C — Criar o conversor Markdown→HTML com o template Friozem e gerar os HTML dos manuais.**
      Criar um script PowerShell autocontido (sem pandoc, sem internet, sem dotnet) em
      `docs\manuais\gerar-manuais.ps1` que: (a) extrai a **logo base64** da linha 125 de
      `docs\apresentacao\deck.html` via regex `data:image/png;base64,([A-Za-z0-9+/=]+)`; (b)
      converte o subconjunto de Markdown dos manuais (h1/h2/h3, tabelas GFM, blocos cercados
      ```` ``` ````, listas, citações `>`, código inline, **negrito**, links `[t](u)`) em HTML; (c)
      aplica o **template Friozem** com CSS **embutido no `<head>` e inline**, **tudo autocontido,
      sem recursos externos**: cabeçalho faixa azul `#17479E` com `<h1>` do título à esquerda e a
      **logo base64** (`width:176px`) à direita; faixa de acento ciano `#54B6E7` (`.bar`); fonte
      `Arial, Helvetica, sans-serif`; `h1/h2/h3` em azul; **tabelas** com cabeçalho azul + zebra
      (`#f6f8fc`) + bordas suaves; **blocos de código/comando** com fundo suave (ex.: `#f1f5fb`/
      `#eef2f8`) e fonte monoespaçada; **rodapé** "Friozem | Integrador WMS"; e **`@page` A4** com
      **número de página no PDF** (`@bottom-right { content: counter(page) }` ou equivalente
      `@page`), margens A4. O link `./MANUAL-AGENTE-IA.md` do MANUAL-TECNICO deve ser **reescrito
      para `./MANUAL-AGENTE-IA.md`** apontando ao MD (funciona a partir da pasta `manuais\`). Rodar
      o script para gerar `docs\manuais\MANUAL-USUARIO.html` e `docs\manuais\MANUAL-TECNICO.html`.
      Depende dos itens 1–3 (converte o MD já finalizado).
      Files: `docs\manuais\gerar-manuais.ps1`, `docs\manuais\MANUAL-USUARIO.html`,
      `docs\manuais\MANUAL-TECNICO.html`
      Verify: abrir cada HTML e confirmar que é **bem-formado e autocontido** — a logo está
      embutida como `data:image/png;base64,` (sem `<img src="http...">` nem `file:`), não há
      `<link rel="stylesheet">` nem `<script src=...>` externos, as cores `#17479E`/`#54B6E7`/
      `#009A83` aparecem no CSS, e `MANUAL-TECNICO.html` contém o link `./MANUAL-AGENTE-IA.md`.
      Checagem PowerShell: `(Get-Content MANUAL-USUARIO.html -Raw) -match 'data:image/png;base64'`
      = True e `-notmatch 'src="https?://'` para recursos externos.

- [ ] 5. **PDF — Gerar os PDFs via Edge headless (com plano B).**
      Para cada HTML, rodar:
      ```powershell
      & 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe' --headless=new --disable-gpu `
        --no-pdf-header-footer --print-to-pdf="docs\manuais\MANUAL-USUARIO.pdf" `
        "docs\manuais\MANUAL-USUARIO.html"
      ```
      e idem para `MANUAL-TECNICO`. Usar **caminhos absolutos** nos argumentos (o headless resolve
      relativo ao perfil, não ao cwd). Numeração de página vem do CSS `@page` (por isso
      `--no-pdf-header-footer`). **Plano B** (se o Edge falhar/der timeout): não falhar a tarefa —
      deixar os HTML prontos e documentar no README (item 6) o passo manual **"Abrir o HTML no
      Edge/Chrome → Imprimir → Salvar como PDF (A4, margens padrão, sem cabeçalho/rodapé do
      navegador)"**; registrar a falha nos `findings`. (Opcional de robustez: tentar o Chrome x64
      em `C:\Program Files\Google\Chrome\Application\chrome.exe` com os mesmos flags antes do plano
      B.)
      Files: `docs\manuais\MANUAL-USUARIO.pdf`, `docs\manuais\MANUAL-TECNICO.pdf`
      Verify: `(Get-Item docs\manuais\MANUAL-USUARIO.pdf).Length -gt 0` e idem para o técnico;
      confirmar extensão `.pdf` e tamanho > 0. Se o Edge falhar, confirmar que os HTML existem e
      que o passo manual está documentado no README.

- [ ] 6. **D — Atualizar docs\manuais\README.md com as versões de cada manual.**
      Atualizar a tabela/listagem para refletir, por manual: **Usuário** (MD + HTML + PDF),
      **Técnico** (MD + HTML + PDF) e **Agente de IA** (apenas MD — consumo por IA, sem HTML/PDF),
      cada um com breve descrição e **público-alvo**. Incluir uma nota curta de **como regenerar**
      (rodar `gerar-manuais.ps1` e, se necessário, o passo manual de "Imprimir como PDF" do plano
      B). Links relativos para os arquivos gerados.
      Files: `docs\manuais\README.md`
      Verify: o README lista os três formatos para Usuário e Técnico e só MD para o Agente, com
      público-alvo; os links relativos batem com os arquivos efetivamente gerados em
      `docs\manuais\`.

## Commits
Um commit por arquivo nomeado, na **main**, **sem push**. Ordem sugerida: `MANUAL-TECNICO.md`
(itens 1–2) → `MANUAL-USUARIO.md` (item 3) → `gerar-manuais.ps1` + os dois `.html` (item 4) →
os dois `.pdf` (item 5) → `README.md` (item 6). Não versionar segredos nem listas reais de
destinatários (já cobertas pelo `.gitignore`).

## Restrições reafirmadas
- Não alterar `.cs`, `docs\diario\`, `docs\controle\`. Não rodar `dotnet`. Branch main. Sem push.
- MANUAL-AGENTE-IA.md fica **só** em Markdown; **não** gerar HTML/PDF dele.
- MANUAL-USUARIO **sem** qualquer menção a IA.
- Verificação = HTML bem-formado/autocontido (logo embutida, sem recursos externos) + PDFs > 0 bytes.
