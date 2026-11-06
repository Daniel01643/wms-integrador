# Verificação — Alinhamento do MANUAL-TECNICO ao servidor + regeneração HTML/PDF

Data: 2026-11-06
Branch: main (sem push)

## Objetivo

Alinhar `docs\manuais\MANUAL-TECNICO.md` à realidade do **servidor SRVAPPALC02** (produção), onde o
técnico opera via RDP a partir de `C:\Friozem\importacao_notas\tools\deploy`, removendo as menções a
estruturas que só existem no **repositório local / máquina de build** (`deploy\`, `artifacts\`) como
se fossem do servidor. A instalação completa continua em `docs\INSTALACAO-SERVIDOR.md`; o manual
técnico foca na **operação do dia a dia**.

Escopo: **documentação apenas**. Não foram tocados `.cs`, diário, controle; `dotnet` não foi
executado.

## Fontes conferidas antes de editar

- `docs\manuais\MANUAL-TECNICO.md` (estado anterior).
- `docs\INSTALACAO-SERVIDOR.md` (já atualizado para `tools\deploy` e fluxo RDP).
- `deploy\*.ps1` — parâmetros reais confirmados via `param()`:
  - `servico.ps1 -Acao {Start|Stop|Restart|Status}` (obrigatório).
  - `publicar-release.ps1 -ReleaseZip|-ReleaseDir -Versao -ManterUltimas(5) -CopiarScripts -PastaScriptsDestino`.
  - `rollback.ps1 [-Versao]`.
  - `configurar-segredos.ps1 -Fase {Homolog|Producao} -Escopo {Maquina|Arquivo} ...`.
  - `remover.ps1 [-LimparPastas] [-PreservarDados]`.
  - `instalar.ps1 ... -HabilitarTls12 -CopiarScripts -PastaScriptsDestino`.
  - `diagnostico.ps1` (WMS 8888 / SMTP 587). `publicar.ps1` (máquina de build → `artifacts\`).
- `docs\manuais\gerar-manuais.ps1` — pipeline MD→HTML autocontido (capa + diagramas SVG embutidos).

## Seções alteradas no MANUAL-TECNICO.md

1. **Introdução** — passou a enfatizar "operação do dia a dia" e a pasta de trabalho
   `C:\Friozem\importacao_notas\tools\deploy`; remissão à instalação completa no
   `INSTALACAO-SERVIDOR.md`; `C:\temp\deploy` só na 1ª instalação.
2. **Índice** — nova entrada `0. Ambiente: servidor x máquina de build (local)`.
3. **Nova seção 0 — Ambiente: servidor x máquina de build (local)** — descreve o servidor
   (`app\current`, `config\`, `data\` [Estado+telemetria], `logs\`, `backups\`, `tools\deploy`) e a
   entrada `C:\Friozem\kitbrf`; a máquina de build (`src\`, `deploy\`, `artifacts\`) como
   opcional/avançada só para gerar versão; e a tabela "Onde estou? → o que uso".
4. **Seção 1 (Dados do ambiente)** — linha dos scripts agora aponta `tools\deploy` como fonte de
   verdade; nova linha "Pasta de trabalho (RDP)"; subpastas citam `tools\deploy`; nota do nssm
   ajustada.
5. **Seção 2 (serviço)** — comandos precedidos de `cd C:\Friozem\importacao_notas\tools\deploy`.
6. **Seção 4 (WMS)** e **Seção 5 (SMTP)** — `configurar-segredos.ps1` rodado de `tools\deploy`.
7. **Seção 9 (publicar/rollback)** — `.zip` vem da máquina de build (`deploy\publicar.ps1` →
   `artifacts\`); `publicar-release.ps1`/`rollback.ps1`/`remover.ps1` rodados de `tools\deploy`.
8. **Seção 11 (diagnóstico/TLS)** — `diagnostico.ps1` de `tools\deploy`; TLS 1.2 remetido ao guia de
   instalação (passo de instalação/manutenção).
9. **Seção 12 (tabela "Quero fazer X")** — todos os comandos com caminho do servidor
   (`tools\deploy\...`) e config em `config\...`; incluídas as linhas "Remover o serviço" e
   "Habilitar TLS 1.2".

Não alterados: capítulo 14 (Guia rápido IA) e o link `./MANUAL-AGENTE-IA.md`. MANUAL-USUARIO e
MANUAL-AGENTE-IA não foram tocados.

## Config e segredos (confirmados no texto)

- `config\appsettings.Production.json` (Wms:BaseUrl, Email, Monitoramento, Pastas:Estado absoluto,
  Relatorio:PastaSaida).
- `config\email.settings.json` (SMTP 587, `UsarTls=false`, `RemetenteNome`).
- `config\destinatarios-fechamento.txt` e `config\destinatarios-resumo.txt` (editáveis a quente;
  `Email:CaminhoDestinatarios` / `Monitoramento:CaminhoDestinatarios`).
- `WMS__Usuario`/`WMS__Senha` em env de **máquina**.
- Estado/telemetria em `C:\Friozem\importacao_notas\data` (fora do share).

## Regeneração HTML + PDF

- `powershell -ExecutionPolicy Bypass -File docs\manuais\gerar-manuais.ps1` → OK.
  - Logo base64 extraída (46748 chars); versão da capa `2026.11.06-4c8cbce`.
  - Gerados `MANUAL-USUARIO.html` e `MANUAL-TECNICO.html`.
- HTML autocontido confirmado: sem `href="http`, `src="http` ou `<link rel=stylesheet>` externos.
  Capa e diagramas (SVG embutidos) preservados; nova seção 0 presente no índice e no corpo.
- PDF via Microsoft Edge headless
  (`C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`,
  `--headless --disable-gpu --no-pdf-header-footer --print-to-pdf`):
  - `docs\manuais\MANUAL-TECNICO.pdf` → **570406 bytes** (~557 KB), **> 0 bytes**.
  - Páginas (aprox.): **13**.

## Restrições respeitadas

- Nenhum `.cs`, diário ou controle alterado; `dotnet` não executado.
- Commits pequenos por arquivo na `main`, sem push.
