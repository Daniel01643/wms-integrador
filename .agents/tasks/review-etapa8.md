# Relatório HTML/PDF refinado (Etapa 8): paisagem, agrupado por transporte, identidade Friozem

O commit 7c3622b substitui o HTML/PDF mínimo por um modelo único (`ModeloRelatorio`) consumido por dois renderizadores. Resumo no topo, agrupamento por transporte, seção de produtos criados por contenção, status único, PDF A4 paisagem com cabeçalho repetido e "Página X de Y". O Orquestrador passa a capturar IdTransacao, IdDocumento e ProdutosProvisorios apenas como dados de saída. Evidência do coder presente (129 testes verdes, inspeção visual de HTML e PDF descrita com limitações declaradas). Não re-executei build/testes; conferi o diff, os testes presentes e os exemplos.

Watch for: nenhum bloqueante. Pontos menores: a lista de produtos provisórios é "todos os itens enviados à I30" (confirmed, documentado); `docs/identidade/` está untracked (confirmed); PDF não verificado em Adobe/Chrome (declarado pelo coder).

**Verdict**: APPROVED

## High-level view

A captura no Orquestrador é aditiva: variáveis locais atualizadas após cada I50 e propriedades `init` novas no record, sem tocar em nenhum `if/continue` de decisão nem em `PreCondicaoOk`. O construtor posicional não mudou, então os 109 testes originais seguem válidos.

A distinção "Integrada — revisar produto" é resolvida só na apresentação (`Integrada` prevalece sobre `Estado=Pendente` quando há produtos provisórios), preservando o estado híbrido do Orquestrador. Os quatro status são únicos por linha e idênticos em HTML, PDF e resumo, pois ambos partem do mesmo modelo.

O HTML é autocontido (CSS e JS inline, logo em base64, sem `http(s)`, `<link>` ou `<script src>`). Os controles de filtro nascem `hidden` e só o JS os revela; todas as linhas estão no HTML estático. O PDF faz layout em duas passadas, o que viabiliza "Página X de Y", repete o cabeçalho da tabela e evita cabeçalho de grupo órfão. O logo é opcional: ausente ou inválido cai para o texto "Friozem" sem erro, nos dois formatos.

<details>
<summary>Issues (3)</summary>

1. **Produtos provisórios = todos os itens da I30** — a lista pode incluir produtos que já existiam (confirmed; documentado no relatório e em RELATORIO-LOGS.md). Não bloqueante.
2. **`docs/identidade/` não versionado** — pasta untracked no working tree; versionar o logo oficial ou manter fora de propósito (confirmed). Não bloqueante.
3. **PDF só validado via Windows.Data.Pdf** — Adobe/Chrome não testados (confirmed na evidência). Não bloqueante.

</details>

<details>
<summary>Details</summary>

## Captura de transação e produtos sem alterar a decisão do Orquestrador

O diff em `Orquestrador.cs` só adiciona `(idTransacao, idDocumento) = Capturar(respI50)` logo após cada I50, o acúmulo de códigos distintos no ramo I30 e o initializer nos cinco `return`s. A linha de `PreCondicaoOk` não aparece no diff e está registrada como fora de escopo na documentação. Valores vêm da última I50, que é a origem da mensagem exibida. Testes novos cobrem sucesso, não-integrada e o caminho I30 com produtos distintos.

## Status híbrido resolvido na apresentação

`LinhaRelatorioErro.EstaIntegrada` dá precedência ao flag `Integrada` sobre `Status`, de modo que Estado=Pendente com Integrada=true e produtos vira "Integrada — revisar produto". O mapeador cobre isso em teste. Uma nota não integrada que tenha criado produtos provisórios mostra a marca "revisar produto" dentro do status "Não integrada" e entra na seção de contenção, o que é coerente com o plano (cadastro provisório existe).

## HTML autocontido com filtro opcional

CSS, JS e imagem inline; escape de `&`, `<`, `>` e de aspas em atributos (`data-*`). Mensagem resumida em 160 caracteres, com `<details>` trazendo mensagem integral e histórico. Cores da paleta Friozem mais âmbar e vermelho semânticos (suposição do plano). Impressão desativa sticky e expande os details.

## PDF paisagem paginado

Dimensões 297x210 mm com guarda explícita se `Orientation` não trocar. Rodapé com "Página X de Y" calculado após a primeira passada. Acentos e travessão via Arial com `PdfFontEncoding.Unicode`; teste extrai o texto por ToUnicode. Teste com 80 linhas confirma mais de uma página. Cabeçalho da tabela repetido quando uma página nova continua a tabela. Falha de leitura do logo cai para texto.

## Cobertura de testes

20 novos testes (129 no total segundo a evidência): HTML autocontido, filtro e linhas estáticas, agrupamento e ordem, resumo, seção de produtos, details, acentuação, logo com e sem arquivo, status único, resumo de mensagem, quebra de linha, mapeador, PDF paisagem, paginação e extração de texto. Exemplos regenerados em `docs/exemplos` (HTML e PDF com data de 04/11) e sem URLs externas. RELATORIO-LOGS.md atualizado (LogoPath, comando de regeneração, PreCondicaoOk fora de escopo). Sem novos pacotes NuGet: apenas `InternalsVisibleTo` no csproj (o BOM do csproj foi removido, trivial).

</details>

<details>
<summary>File map</summary>

Orquestrador.cs / ResultadoIntegracaoNota.cs: captura de ids e produtos. LinhaRelatorioErro, StatusLinhaRelatorio, MapeadorRelatorio, ModeloRelatorio, ResumoMensagem, IdentidadeVisual, TextoPdf: modelo e utilitários. RenderizadorHtml / RenderizadorPdf / GeradorRelatorio / IGeradorRelatorio: renderização. OpcoesRelatorio / appsettings.json: LogoPath. Core.csproj: InternalsVisibleTo. Testes: OrquestradorCapturaTests, RelatorioRefinadoTests, PdfTextoHelper, ExemplosRelatorio. Docs: RELATORIO-LOGS.md e exemplos. Diff completo: `git show 7c3622b`.

</details>
