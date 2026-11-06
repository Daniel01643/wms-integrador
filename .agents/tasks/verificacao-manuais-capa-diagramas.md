# Verificação — Capa ilustrada + Diagramas SVG nos manuais

Iteração: **primeira** (não havia `review-manuais.json`). Branch `main`, sem worktree, sem push.
Trabalho de documentação/apresentação: nenhum `.cs`, diário ou controle foi tocado; `dotnet` não
foi executado.

## O que rodou

1. `powershell -ExecutionPolicy Bypass -File docs\manuais\gerar-manuais.ps1`
   - Saída: `Logo base64 extraida (46748 chars).`
   - `Versao da capa: 2026.11.06-716414d | Data de geracao: 2026-11-06`
   - `Gerado: ...MANUAL-USUARIO.html` e `Gerado: ...MANUAL-TECNICO.html`
2. Microsoft Edge headless (`--headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf`)
   para `MANUAL-USUARIO.pdf` e `MANUAL-TECNICO.pdf`.

## Resultados verificados

### HTML autocontidos (nenhum recurso externo)

| Arquivo | `<link>` | `<script src>` | `url(http)` | `href/src http` | logo base64 |
|---|---|---|---|---|---|
| MANUAL-USUARIO.html | 0 | 0 | 0 | 0 | 2 (cabeçalho + capa) |
| MANUAL-TECNICO.html | 0 | 0 | 0 | 0 | 2 (cabeçalho + capa) |

### Capa e SVGs presentes/inline

| Arquivo | `class="capa"` | `<svg` | `<figure class="diagrama">` | placeholders restantes | marcadores restantes | capa é 1º bloco do corpo |
|---|---|---|---|---|---|---|
| MANUAL-USUARIO.html | 1 | 1 | 1 | 0 | 0 | sim |
| MANUAL-TECNICO.html | 1 | 2 | 2 | 0 | 0 | sim |

- Capa contém: nome do agente **Integrador Notas Fiscais KIT → WMS** (`&rarr;`), subtítulo
  *Integração automática de notas fiscais com o WMS*, tipo (*Manual do Usuário* / *Manual Técnico*),
  **data de geração** (2026-11-06) e **versão** (`2026.11.06-716414d`, critério
  `yyyy.MM.dd-<hash git>` do `deploy\_comum.ps1`; fallback `v1.0 - 2026-11` quando git não responde).
- Diagramas SVG inline, paleta Friozem, `width:100%`/`height:auto` (não cortados no A4), Arial,
  caixas arredondadas, setas e rótulos em pt-BR. Nenhum recurso externo (sem `<image href>` remoto,
  sem fonte web, sem Mermaid).

### PDFs regenerados (> 0 bytes)

| Arquivo | Tamanho (bytes) | Páginas |
|---|---|---|
| MANUAL-USUARIO.pdf | 216574 | 6 (`/Count 6`) |
| MANUAL-TECNICO.pdf | 489005 | 13 (`/Count 8` na primeira árvore; 13 objetos `/Type /Page`) |

Ambos os PDFs foram reescritos nesta execução (LastWrite 2026-11-06 11:02) e têm tamanho > 0.
A 1ª página é a capa (CSS `.capa { page-break-after: always; }`).

### Escopo preservado

- `git status --short` lista apenas: `docs/manuais/MANUAL-USUARIO.{md,html,pdf}`,
  `docs/manuais/MANUAL-TECNICO.{md,html,pdf}`, `docs/manuais/README.md`,
  `docs/manuais/gerar-manuais.ps1` e os novos `docs/manuais/assets/*.svg`.
- Nenhum `.cs`, diário (`DIARIO*`) ou controle alterado. `MANUAL-AGENTE-IA.md` permanece só MD
  (sem capa/SVG/HTML/PDF).

## Arquivos criados/alterados

- `docs/manuais/assets/fluxo-usuario.svg` (novo)
- `docs/manuais/assets/arquitetura-tecnico.svg` (novo)
- `docs/manuais/assets/ciclo-pacote.svg` (novo)
- `docs/manuais/gerar-manuais.ps1` (capa + diagramas via marcadores, CSS, versão)
- `docs/manuais/MANUAL-USUARIO.md` / `MANUAL-TECNICO.md` (marcadores de capa/diagrama)
- `docs/manuais/MANUAL-USUARIO.{html,pdf}` / `MANUAL-TECNICO.{html,pdf}` (regenerados)
- `docs/manuais/README.md` (menção a capa + diagramas + pasta assets)
