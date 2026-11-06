# Implementation Plan — Capa ilustrada + Diagramas SVG nos manuais (KitWms Integrador)

Aprimorar os manuais humanos (Usuario e Tecnico) com (1) CAPA ilustrada com o nome do agente e
(2) DIAGRAMAS DE FLUXO em SVG inline, com a identidade Friozem, e regenerar HTML + PDF. E trabalho
de DOCUMENTACAO/APRESENTACAO.

## Decisoes de design (registradas aqui, uma a duas frases cada)

- **Abordagem de integracao = extensao do `gerar-manuais.ps1` por arquivos SVG dedicados + marcadores**
  (opcao "b" do pedido, nao a "a"). Os SVGs ficam em arquivos proprios sob
  `docs\manuais\assets\` e sao injetados pelo script em marcadores HTML-comentario no Markdown
  (ex.: `<!--DIAGRAMA:fluxo-usuario-->`). Motivo: SVGs grandes embutidos direto no `.md` poluem a
  fonte, atrapalham o diff e o conversor de Markdown (que processa `#`, `-`, `|`, ` ``` `) poderia
  mexer no conteudo SVG; mantendo o SVG fora e injetando por marcador o Markdown continua legivel e o
  SVG passa intacto. O HTML final continua **autocontido** (SVG inline no HTML, logo em base64, zero
  recurso externo).
- **A capa tambem vira marcador** (`<!--CAPA:usuario-->` / `<!--CAPA:tecnico-->`) resolvido pelo
  script, que monta o bloco HTML da capa (logo base64 + titulo + subtitulo + tipo + data + versao) e
  injeta no topo do corpo. Motivo: a capa precisa da logo base64 e da versao/data, que so o script
  conhece em tempo de geracao; deixa-la como marcador evita duplicar base64 no `.md` e centraliza a
  identidade visual no template.
- **Versao exibida na capa** = criterio do `deploy\publicar.ps1` via `Get-VersaoPadrao`
  (`yyyy.MM.dd-<hash git curto>`, hoje `2025.xx.xx-716414d`), com **fallback** `v1.0 - 2026-11`
  quando `git` nao responder. Motivo: o pedido manda derivar de git/short SHA ou do criterio do
  publicar.ps1 se existir — existe, entao reaproveitamos o mesmo criterio ja validado no projeto, em
  vez de inventar outro. **Data de geracao** = data atual (`Get-Date yyyy-MM-dd`).
- **A quebra de pagina apos a capa** usa `page-break-after: always` (CSS `@media print`), e os
  diagramas usam `width:100%` + `height:auto` (via `viewBox` + `preserveAspectRatio`) para nao serem
  cortados no A4. Motivo: e o mecanismo que o Edge headless (Chromium) respeita para o PDF, coerente
  com o `@page` ja existente no template.
- **MANUAL-AGENTE-IA permanece so Markdown** (sem capa, sem SVG, sem HTML/PDF) — inalterado.
- **Paleta/fontes dos SVGs** = identidade Friozem ja no template: azul `#17479E`, ciano `#54B6E7`,
  verde `#009A83`, Arial; vermelho de erro `#C0392B` e ambar de pendencia `#E8A317` (cores de status,
  coerentes com o relatorio/e-mail). Caixas arredondadas (`rx`), setas com `marker`, rotulos pt-BR.

## Restricoes rigidas (valem para todos os itens)

- **NAO** tocar em arquivos `.cs`, no diario (`docs\DIARIO*.md` / diario de bordo) nem no controle
  (`docs\CONTROLE*` / planilha de controle de etapas). **NAO** rodar `dotnet`.
- Branch **main**, **sem worktree**. Commits **pequenos, por arquivo nomeado**. **SEM push.**
- Preservar o comportamento ja validado do `gerar-manuais.ps1`: os diagramas apenas ILUSTRAM o que
  ja existe; nada de mudar o fluxo descrito em `docs\OPERACAO.md`.
- Verificacao de PDF usa o Microsoft Edge headless em
  `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe` (confirmado presente nesta maquina)
  com `--headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf`.

---

- [ ] 1. Criar a pasta de assets e o SVG do fluxo do Manual do Usuario (operacional, com icones/figuras e pouco texto).
      Desenhar em SVG inline autocontido, `viewBox="0 0 1000 H"`, `width="100%"` `height="auto"`,
      `preserveAspectRatio="xMidYMid meet"`, fontes Arial, paleta Friozem. Conteudo em caixas
      arredondadas ligadas por setas (marker de seta definido em `<defs>`):
      **Passo 0** "Notas chegam por E-MAIL" (icone envelope) -> **Colaborador salva os anexos XML**
      (destacar que ESTE e o unico passo humano) -> **Passo 1** "Salvar os XML na pasta de rede"
      (icone pasta compartilhada, rotulo `\\SRVAPPALC02\kitbrf`) -> **Passo 2** "O sistema le e
      integra no WMS" (icone engrenagem/servidor + nuvem WMS) -> **Passo 3** "Resultado" com duas
      setas: "Processados" (verde `#009A83`) e "Erro" (vermelho `#C0392B`) -> **Passo 4** "Relatorio
      (HTML+PDF) na pasta relatorios" + "E-mail de fechamento" (icone envelope de saida). Incluir um
      rotulo/realce visual deixando claro que o colaborador SO faz "salvar os XML na pasta" e o resto
      e automatico, e uma **legenda de status** curta: Integrada=verde, Pendente=ambar `#E8A317`, Nao
      integrada=vermelho. Sem texto fora de `<text>` do SVG; nenhum recurso externo (sem `<image href>`
      remoto, sem fontes web). Grounded em `docs\OPERACAO.md` (sec. 1 e 2) e no pedido do usuario
      (colaborador salva do e-mail na pasta).
      Files: `c:\wms-integrador\docs\manuais\assets\fluxo-usuario.svg`
      Verify: abrir o arquivo no Edge (`msedge.exe <caminho do .svg>`) e confirmar que renderiza sem
      erro e sem recurso faltando; conferir visualmente passos 0–4, as duas setas de resultado e a
      legenda de status. (A verificacao de pipeline/PDF ocorre no item 6.)

- [ ] 2. Criar o SVG de ARQUITETURA/implantacao do Manual Tecnico.
      SVG inline autocontido, mesmas convencoes do item 1. Conteudo: uma caixa grande "SERVIDOR
      SRVAPPALC02 (Windows Server 2012 R2)" contendo: o Servico NSSM `Friozem-ImportacaoNotas`; a
      pasta local `C:\Friozem\kitbrf` (compartilhada SMB = icone de rede/usuarios depositando XML);
      subpastas `Processados\`, `Erro\`, `relatorios\` DENTRO do share; e `data\` (Estado + telemetria)
      FORA do share (deixar claro visualmente o limite do share). Setas de SAIDA do servidor:
      **HTTPS -> WMS** (`friozem.assistsoftware.com.br`, icone nuvem/WMS) e **SMTP ->**
      (`smtp.friozem.com.br:587`, icone envelope). Indicar a pasta `config\` (segredos, ACL restrita).
      Grounded em `docs\manuais\MANUAL-TECNICO.md` sec. 1 e `docs\OPERACAO.md` (caminhos, portas,
      Estado/telemetria em `data\`).
      Files: `c:\wms-integrador\docs\manuais\assets\arquitetura-tecnico.svg`
      Verify: abrir no Edge e confirmar renderizacao sem erro; conferir caixa do servidor, limite do
      share (`data\` fora), setas HTTPS/WMS e SMTP e a caixa `config\`.

- [ ] 3. Criar o SVG do CICLO DO PACOTE + ORQUESTRADOR REATIVO do Manual Tecnico.
      SVG inline autocontido, mesmas convencoes. Fluxo em caixas/setas: **Ocioso** -> **detecta XML**
      -> **janela de coleta** -> **FOTO do pacote** -> **processa POR TRANSPORTE (sequencial)** ->
      para cada nota: **envia I50**; se "empresa nao localizada" -> **I10 e reenvia**; se "produto nao
      cadastrado" -> **I30 e reenvia**; **ate 3 tentativas** (mostrar o limite e os caminhos de erro)
      -> **classifica** Integrada (verde) / Pendente (ambar) / Nao integrada (vermelho) -> **move o
      arquivo** -> ao fim do pacote: **gera relatorio + envia e-mail + grava telemetria** -> volta a
      **Ocioso**. Icones simples (envelope, pasta, servidor, engrenagem, nuvem/WMS) desenhados em SVG.
      Grounded em `docs\OPERACAO.md` sec. 2 e nas categorias de erro (EmpresaNaoLocalizada,
      ProdutoNaoCadastrado, TentativasEsgotadas).
      Files: `c:\wms-integrador\docs\manuais\assets\ciclo-pacote.svg`
      Verify: abrir no Edge e confirmar renderizacao sem erro; conferir o loop Ocioso->...->Ocioso, os
      ramos I50/I10/I30, o limite de 3 tentativas e as 3 classificacoes coloridas.

- [ ] 4. Estender o `gerar-manuais.ps1` para resolver os marcadores de CAPA e DIAGRAMA e injetar os SVGs/capa no HTML.
      Mudancas, todas dentro do script (sem mexer na logica ja validada de conversao de Markdown):
      (a) nova funcao `Get-VersaoManual` que tenta `git rev-parse --short HEAD` a partir da raiz do
      repo (replicando o criterio de `deploy\_comum.ps1::Get-VersaoPadrao`, formato
      `yyyy.MM.dd-<hash>`) e, em falha/sem git, retorna o fallback `v1.0 - 2026-11`; (b) nova funcao
      `Read-AssetSvg($nome)` que le `assets\<nome>.svg` como UTF-8 e devolve o markup (sem reescrever);
      (c) nova funcao `Build-Capa($tipoManual, $logoBase64, $versao, $dataGeracao)` que monta o bloco
      HTML da capa (`<section class="capa">`: logo base64, titulo `Integrador Notas Fiscais KIT &rarr;
      WMS` — usar a entidade `&rarr;` para a seta, subtitulo `Integracao automatica de notas fiscais
      com o WMS`, tipo `Manual do Usuario`/`Manual Tecnico`, data e versao); (d) no fluxo de
      `Convert-Manual`, ANTES de converter o Markdown, substituir os marcadores
      `<!--CAPA:usuario-->`/`<!--CAPA:tecnico-->` pela capa e `<!--DIAGRAMA:fluxo-usuario-->`,
      `<!--DIAGRAMA:arquitetura-tecnico-->`, `<!--DIAGRAMA:ciclo-pacote-->` pelos SVGs, protegendo-os
      para o conversor nao tocar (p.ex. guardar como placeholder, converter o resto, e reinjetar o HTML
      cru depois — mesma tecnica dos `codeStore`); (e) estender o CSS do `Build-HtmlDocument` com:
      `.capa { ... }` (identidade Friozem: faixa azul `#17479E`, logo, titulo grande, subtitulo, tipo,
      data/versao, sobrio) e `@media print { .capa { page-break-after: always; } }`; e
      `figure.diagrama { max-width:100%; margin:18px 0; } figure.diagrama svg { width:100%; height:auto;
      display:block; } @media print { figure.diagrama { page-break-inside: avoid; } }`. NAO alterar a
      extracao da logo (linha 125 do deck) nem a chamada ao Edge (fora do script).
      Files: `c:\wms-integrador\docs\manuais\gerar-manuais.ps1`
      Verify: `powershell -ExecutionPolicy Bypass -File docs\manuais\gerar-manuais.ps1` roda sem erro e
      imprime "Gerado: ...MANUAL-USUARIO.html" e "...MANUAL-TECNICO.html" (confirma que os marcadores
      resolveram sem quebrar o pipeline). Verificacao de conteudo no item 6.

- [ ] 5. Inserir os marcadores de capa e diagrama nos Markdown dos manuais (sem reescrever o conteudo existente).
      No `MANUAL-USUARIO.md`: adicionar `<!--CAPA:usuario-->` como primeira linha do corpo (apos o H1
      de titulo, que o script ja remove do corpo) e `<!--DIAGRAMA:fluxo-usuario-->` dentro da secao
      "O que o sistema faz" (ou logo apos ela), como ilustracao do fluxo operacional.
      No `MANUAL-TECNICO.md`: adicionar `<!--CAPA:tecnico-->` no topo do corpo;
      `<!--DIAGRAMA:arquitetura-tecnico-->` na secao 1 (Dados do ambiente) e
      `<!--DIAGRAMA:ciclo-pacote-->` perto da explicacao do ciclo/orquestrador (referenciar
      `OPERACAO.md`). Os marcadores sao comentarios HTML, entao sao invisiveis no Markdown puro e so
      ganham efeito ao passar pelo script. NAO alterar `MANUAL-AGENTE-IA.md`.
      Files: `c:\wms-integrador\docs\manuais\MANUAL-USUARIO.md`,
      `c:\wms-integrador\docs\manuais\MANUAL-TECNICO.md`
      Verify: `grep` confirma que os 2 marcadores de capa e os 3 de diagrama estao presentes; releitura
      rapida confirma que nenhum texto existente foi removido. (Render real no item 6.)

- [ ] 6. Regenerar HTML e PDF dos dois manuais humanos e validar.
      Rodar `powershell -ExecutionPolicy Bypass -File docs\manuais\gerar-manuais.ps1` para regenerar
      `MANUAL-USUARIO.html` e `MANUAL-TECNICO.html`; depois gerar os PDFs com o Edge headless:
      `& 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe' --headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf='C:\wms-integrador\docs\manuais\MANUAL-USUARIO.pdf' 'C:\wms-integrador\docs\manuais\MANUAL-USUARIO.html'`
      e o equivalente para o TECNICO. Confirmar que os 4 arquivos (2 HTML + 2 PDF) existem e que os
      **PDFs tem > 0 bytes**; se possivel, estimar a contagem de paginas (p.ex. contar ocorrencias de
      `/Type /Page` no PDF via leitura binaria) e confirmar que a 1a pagina e a capa (o HTML comeca com
      `<section class="capa">`). Conferir no HTML que os tres `<svg` de diagrama estao inline.
      MANUAL-AGENTE-IA NAO e tocado.
      Files: `c:\wms-integrador\docs\manuais\MANUAL-USUARIO.html`,
      `c:\wms-integrador\docs\manuais\MANUAL-TECNICO.html`,
      `c:\wms-integrador\docs\manuais\MANUAL-USUARIO.pdf`,
      `c:\wms-integrador\docs\manuais\MANUAL-TECNICO.pdf` (regenerados)
      Verify: `Get-Item` dos 4 arquivos com `Length` dos PDFs > 0; `Select-String '<svg'` nos 2 HTML
      conta >= 1 no Usuario e >= 2 no Tecnico; `Select-String 'class="capa"'` presente nos 2 HTML.

- [ ] 7. Atualizar o README dos manuais se necessario (capa + diagramas + pasta assets).
      Acrescentar uma linha curta descrevendo que os HTML/PDF agora trazem CAPA e DIAGRAMAS SVG e que
      os fontes dos diagramas vivem em `docs\manuais\assets\*.svg`, injetados por marcadores no passo
      de geracao. NAO alterar as instrucoes de regeneracao (continuam validas). Nao mexer em diario/
      controle.
      Files: `c:\wms-integrador\docs\manuais\README.md`
      Verify: releitura confirma a mencao a capa/diagramas/assets e que o passo a passo de regeneracao
      segue correto.

- [ ] 8. Commits pequenos, um por arquivo nomeado, na branch main (sem push).
      Fazer commits separados e nomeados, na ordem: os 3 SVGs (`assets\fluxo-usuario.svg`,
      `assets\arquitetura-tecnico.svg`, `assets\ciclo-pacote.svg`), depois `gerar-manuais.ps1`, depois
      `MANUAL-USUARIO.md` e `MANUAL-TECNICO.md`, depois os HTML/PDF regenerados, depois `README.md`.
      Usar `git add <arquivo especifico>` (nunca `git add .`). **NAO** `git push`. **NAO** incluir
      arquivos de diario/controle nem qualquer `.cs`. Mensagens em pt-BR, escopo `docs(manuais):`.
      Files: (apenas operacoes git sobre os arquivos acima)
      Verify: `git status --short` nao lista arquivos `.cs`, de diario ou de controle como alterados
      por este trabalho; `git log --oneline -n 8` mostra os commits pequenos esperados; `git status`
      confirma que nada foi enviado (sem push) e que a branch e `main`.

## Lacunas / suposicoes

- O "nome do servidor" no SVG do Usuario usa `\\SRVAPPALC02\kitbrf` (consistente com
  `MANUAL-USUARIO.md`); o IP alternativo `\\200.200.200.213\kitbrf` pode entrar como nota pequena, mas
  nao e obrigatorio no desenho.
- A contagem de paginas do PDF e **best-effort** (parsing simples de `/Type /Page`); se nao for
  confiavel no ambiente, basta confirmar PDF > 0 bytes e abrir para inspecao visual (plano B do
  README: imprimir como PDF pelo navegador).
- Caso `git` nao esteja disponivel no PATH durante a geracao, a capa cai no fallback de versao
  `v1.0 - 2026-11` por design (sem falhar o pipeline).
