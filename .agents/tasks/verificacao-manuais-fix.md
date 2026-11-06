# Correção de 3 problemas visuais nos manuais — verificação

Escopo: apenas documentação (`docs\manuais`). Não foram tocados `.cs`, diário ou
controle; `dotnet` não foi executado. Branch `main`, sem worktree, sem push.

## Problema 1 — Logo "cortada" na capa

Diagnóstico: a logo Friozem embutida (PNG 560×313) é **branca**. Na capa, o fundo é
branco, então o lettering "FRIOZEM" + símbolo sumiam no branco (só o "ZEM", de tom
levemente diferente, aparecia). Não era corte de CSS: cabeçalho `.hd` mostra a logo
inteira porque tem fundo azul `#17479E` (como a capa do deck).

Correção (`gerar-manuais.ps1`): a logo da capa passou a ficar sobre um **painel azul**
(`.capa-logo-panel`, fundo `#17479E`, borda inferior ciano), igual à capa do deck.
`.capa-logo` agora usa `width:280px; height:auto; object-fit:contain` — marca inteira,
proporcional, sem corte/distorção. `Build-Capa` envolve o `<img>` no painel.

## Problema 2 — Faixa `.hd` duplicada acima da capa

Correção (`gerar-manuais.ps1`): a capa deixou de ser injetada no corpo (`<main>`) e
passou a ser o **primeiro elemento** da página, antes de `<header class="hd">`. Com
`.capa { page-break-after: always }`, a página 1 é só a capa; a faixa `.hd` + `.bar` +
conteúdo começam na página 2. Verificado: cada HTML tem `capa=1` e `hd=1`, com a capa
antes do `.hd`.

## Problema 3 — Fluxo do usuário: erro não fechava no e-mail

Correção (`assets\fluxo-usuario.svg`): adicionada seta vermelha de **Erro → Passo 4**
(contornando por baixo dos resultados) e ajustada a seta verde **Processados → Passo 4**.
Legenda "notas com erro/pendencia tambem entram no fechamento". Assim, tanto
"Processados" quanto "Erro" desembocam no "Relatorio (HTML+PDF) + E-mail de fechamento".
Paleta/estilo e legenda de status mantidos. SVG validado como XML bem-formado.

## Regeneração e evidências

- `powershell -ExecutionPolicy Bypass -File docs\manuais\gerar-manuais.ps1` → OK
- PDFs via Edge headless (`--headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf`):
  - `MANUAL-USUARIO.pdf`: 217.300 bytes, 5 páginas
  - `MANUAL-TECNICO.pdf`: 489.561 bytes, 12 páginas
- Conferência visual (screenshots Edge): capa com logo Friozem inteira sobre painel
  azul; página 1 = capa limpa (faixa `.hd` só depois); no `fluxo-usuario`, Erro conecta
  ao Passo 4 (e-mail/relatório).
- HTML autocontidos (logo/SVG em base64/inline; sem recursos externos).

## Arquivos alterados

- `docs\manuais\gerar-manuais.ps1`
- `docs\manuais\assets\fluxo-usuario.svg`
- `docs\manuais\MANUAL-USUARIO.html` / `.pdf` (regenerados)
- `docs\manuais\MANUAL-TECNICO.html` / `.pdf` (regenerados)
