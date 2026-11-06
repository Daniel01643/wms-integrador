# Capa ilustrada e diagramas SVG inline nos manuais KitWms Integrador

Esta mudanca adiciona uma capa ilustrada com identidade Friozem na primeira pagina dos manuais humanos (Usuario e Tecnico) e tres diagramas de fluxo em SVG inline: um fluxo operacional no Manual do Usuario e, no Manual Tecnico, a arquitetura de implantacao e o ciclo do pacote/orquestrador reativo. A abordagem escolhida mantem os SVGs em arquivos dedicados sob `assets\` e os injeta por marcadores HTML-comentario (`<!--CAPA:...-->` / `<!--DIAGRAMA:...-->`) resolvidos em tempo de geracao, preservando o Markdown-fonte legivel e o HTML final autocontido. Os HTML e PDF dos dois manuais foram regenerados; o MANUAL-AGENTE-IA permanece so Markdown, sem capa nem SVG. Watch for: nada bloqueante — a capa e precedida pela faixa de cabecalho padrao `.hd` (elemento pre-existente, nao introduzido por este diff) na primeira pagina (possible, cosmetico).

**Verdict**: APPROVED

## High-level view

A capa resolve exatamente o que o pedido original pediu: nome do agente "Integrador Notas Fiscais KIT → WMS" com a seta via entidade `&rarr;`, subtitulo "Integracao automatica de notas fiscais com o WMS", tipo correto por arquivo ("Manual do Usuario" / "Manual Tecnico"), data de geracao e versao (criterio `yyyy.MM.dd-<hash git>` com fallback), logo Friozem em base64, faixa azul da identidade, e `page-break-after: always` empurrando o conteudo para a pagina 2. Confirmei nos dois HTML que a capa aparece uma vez em cada e com o tipo certo.

Os tres diagramas cobrem o conteudo exigido: o fluxo do Usuario deixa explicito que o colaborador SO salva os XML na pasta de rede `\\SRVAPPALC02\kitbrf` e o resto e automatico (realce visual + rotulo "UNICO passo humano"), com os ramos Processados (verde) / Erro (vermelho) e a legenda de status Integrada/Pendente/Nao integrada. O diagrama tecnico de arquitetura mostra o servidor SRVAPPALC02, o servico NSSM Friozem-ImportacaoNotas, o share SMB com Processados/Erro/relatorios dentro, `data\` fora do share, `config\` com segredos/ACL e as saidas HTTPS (WMS) e SMTP. O ciclo do pacote mostra Ocioso → deteccao → foto → processamento por transporte → I50/I10/I30 com limite de 3 tentativas → classificacao colorida → move arquivo → relatorio+e-mail+telemetria → volta a Ocioso.

O mecanismo de injecao preserva o HTML autocontido: zero `<link>`, zero `<script src>`, zero `url(http...)` e zero `href/src` http externos nos dois HTML; os SVGs ficam inline e a logo em base64. Nao ha Mermaid nem recurso remoto.

As restricoes de escopo foram respeitadas: nenhum `.cs`, diario ou controle alterado; commits pequenos por arquivo nomeado na branch `main`; sem push. A evidencia de regeneracao de PDF (> 0 bytes) esta registrada e foi corroborada por spot-check direto.

<details>
<summary>Issues (1)</summary>

1. **Faixa de cabecalho acima da capa** — a primeira pagina mostra a faixa `.hd` (titulo + logo) e a barra divisoria antes de `<section class="capa">`; e um elemento pre-existente do template, nao introduzido por este diff, e cosmetico. Se o objetivo for a capa totalmente limpa, ocultar `.hd`/`.bar` quando houver capa (fora do escopo deste trabalho).

</details>

<details>
<summary>Details</summary>

### Capa resolvida por marcador, com versao derivada do git

A capa e montada por `Build-Capa` e injetada no lugar de `<!--CAPA:usuario-->` / `<!--CAPA:tecnico-->`. O tipo do manual e derivado do nome do marcador (`tecnico` → "Manual Tecnico", senao "Manual do Usuario"), o que confirmei no HTML renderizado: `capa-tipo` traz "Manual do Usuario" no Usuario e "Manual Tecnico" no Tecnico (confirmed). O titulo usa a entidade `&rarr;` para a seta, presente uma vez em cada HTML (confirmed). A versao vem de `Get-VersaoManual`, que replica o criterio `yyyy.MM.dd-<hash git curto>` do `deploy\_comum.ps1` e cai no fallback `v1.0 - 2026-11` quando `git` nao responde; a evidencia registra `2026.11.06-716414d` (confirmed).

A injecao protege o HTML cru do conversor de Markdown via placeholder com caractere NUL e um `codeStore`-like, reinjetando depois da conversao e removendo o wrapper `<p>` que o conversor coloca em volta. Ha uma rede de seguranca que reinjeta mesmo se o placeholder nao estiver dentro de `<p>`. Nos dois HTML nao sobrou nenhum placeholder `HTMLBLOCK` nem marcador `CAPA/DIAGRAMA` nao resolvido (confirmed), entao o mecanismo fechou sem vazamento.

### Posicao da capa na pagina 1

A capa ocupa a primeira pagina e `.capa { page-break-after: always; min-height: 90vh }` garante que o conteudo seguinte vai para a pagina 2. A ordem real no corpo e `<header class="hd">` (faixa azul com titulo + logo) → `<div class="bar">` → `<main class="bd">` → `<section class="capa">`. O `.hd` nao e `position: fixed`/`running` (confirmei a ausencia de ambos no CSS), ou seja, e um elemento estatico de topo que ja existia no template antes deste diff — a capa nao foi inserida acima dele. Resultado: na pagina 1 aparecem a faixa de cabecalho e a barra, e logo abaixo a capa. E cosmetico e pre-existente (possible); nao e regressao deste trabalho.

### Fluxo do Usuario: unico passo humano explicito

O `fluxo-usuario.svg` cobre Passo 0 (notas por e-mail) → Passo 1 destacado como "UNICO passo humano" (salvar XML em `\\SRVAPPALC02\kitbrf`) → Passo 2 automatico (le e integra no WMS) → Passo 3 Resultado com duas setas, Processados em verde `#009A83` e Erro em vermelho `#C0392B` → Passo 4 (relatorio HTML+PDF + e-mail de fechamento). Ha o realce no topo "O colaborador SO faz UMA coisa: salvar os XML na pasta. Todo o resto e automatico." e a legenda de status com os tres quadrados coloridos (Integrada=verde, Pendente=ambar `#E8A317`, Nao integrada=vermelho) (confirmed). Isso atende diretamente ao ponto do usuario de que "o colaborador salva do email na pasta, nao muda o que fizemos, somente para o SVG e importante".

### Arquitetura e ciclo do pacote (Tecnico)

`arquitetura-tecnico.svg` tem a caixa do servidor SRVAPPALC02 (Windows Server 2012 R2), o servico NSSM `Friozem-ImportacaoNotas`, o share SMB `C:\Friozem\kitbrf` com `Processados\`/`Erro\`/`relatorios\` dentro (limite do share em tracejado, com nota "FORA do share" na caixa `data\`), a `config\` com segredos (SMTP/WMS) e ACL restrita, e as setas de saida HTTPS→WMS (`friozem.assistsoftware.com.br`) e SMTP (`smtp.friozem.com.br:587`) (confirmed).

`ciclo-pacote.svg` mostra o loop Ocioso → detecta XML → janela de coleta → FOTO do pacote → processa POR TRANSPORTE (sequencial) → bloco "Por nota — ate 3 tentativas" com Envia I50, ramo "empresa nao localizada" → I10 e reenvia, ramo "produto nao cadastrado" → I30 e reenvia, com o rotulo "reenvia (limite: 3 tentativas)" → Classifica (Integrada/Pendente/Nao integrada coloridos) → Move o arquivo → "Ao fim do pacote: gera relatorio + envia e-mail + grava telemetria" → volta a Ocioso. O limite de 3 tentativas e os caminhos de erro I10/I30 estao visiveis (confirmed).

### HTML autocontido e sem dependencias externas

Nos dois HTML: `<link>` = 0, `<script src>` = 0, `url(http...)` = 0, `href/src="http...` = 0 (confirmed por busca direta). SVGs inline (1 no Usuario, 2 no Tecnico), cada um em `<figure class="diagrama">`, logo em base64. A unica ocorrencia de URL nos SVGs e `xmlns="http://www.w3.org/2000/svg"`, que e declaracao de namespace XML e nao faz requisicao de rede. Sem Mermaid.

### Evidencia de PDF regenerado

A evidencia em `verificacao-manuais-capa-diagramas.md` registra os PDFs regenerados: MANUAL-USUARIO.pdf 216574 bytes (6 paginas) e MANUAL-TECNICO.pdf 489005 bytes. Corroborei por `Get-Item`: ambos existem, com `Length` > 0 e LastWrite 06/11/2026 11:02, coerente com a regeneracao (confirmed). Conforme instruido, nao reexecutei `dotnet` nem o `gerar-manuais.ps1`.

### Escopo preservado

`git diff --stat` entre o commit base (716414d) e HEAD lista apenas arquivos sob `docs/manuais/` (3 SVGs novos, `gerar-manuais.ps1`, os dois `.md`, os dois HTML, os dois PDF, README) mais o arquivo de verificacao em `.agents/tasks/`. Nenhum `.cs`, diario (`DIARIO*`) ou controle alterado (confirmed). `MANUAL-AGENTE-IA.md` continua so MD, sem `CAPA:` e sem `<svg` (confirmed). Commits pequenos e nomeados, um por arquivo, na ordem do plano; branch `main`; `git status` nao mostra push (confirmed).

</details>

<details>
<summary>Mapa de arquivos</summary>

- `docs/manuais/assets/fluxo-usuario.svg` (novo) — fluxo operacional do Usuario com unico passo humano e legenda de status.
- `docs/manuais/assets/arquitetura-tecnico.svg` (novo) — arquitetura de implantacao no SRVAPPALC02.
- `docs/manuais/assets/ciclo-pacote.svg` (novo) — ciclo do pacote / orquestrador reativo com I50/I10/I30 e limite de 3 tentativas.
- `docs/manuais/gerar-manuais.ps1` — funcoes `Get-VersaoManual`/`Read-AssetSvg`/`Build-Capa`, resolucao de marcadores, CSS da capa e dos diagramas.
- `docs/manuais/MANUAL-USUARIO.md` — marcadores `<!--CAPA:usuario-->` e `<!--DIAGRAMA:fluxo-usuario-->`.
- `docs/manuais/MANUAL-TECNICO.md` — marcadores de capa e dos dois diagramas tecnicos.
- `docs/manuais/MANUAL-USUARIO.{html,pdf}` / `MANUAL-TECNICO.{html,pdf}` — regenerados com capa e diagramas.
- `docs/manuais/README.md` — mencao a capa, diagramas SVG e pasta assets.

Diff completo: `git diff 716414d HEAD -- docs/manuais`
</details>
