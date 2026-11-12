# Relatório de Integração (HTML + PDF)

Além do alerta por e-mail, o time pediu um **arquivo de relatório** estruturado com os
erros de integração. A Etapa 8 refinou o layout (HTML e PDF) e a identidade visual.

Os dois formatos são gerados a partir do **mesmo modelo** (`ModeloRelatorio`), portanto
os números do resumo e a ordem das linhas são idênticos. Biblioteca de PDF adotada:
**PdfSharpCore 1.3.67** (MIT, versão fixa), sem novas dependências.

Nome dos arquivos: `erros_<yyyy-MM-dd_HHmm>.html` e `.pdf`, na pasta `Relatorio:PastaSaida`.

## Layout

1. **Cabeçalho** (12/11/2026): faixa azul `#17479E` com a **logo negativa** (branca) da Friozem,
   título **"Relatório de Integração"** em destaque e o subtítulo "KitWms Integrador"; logo abaixo,
   a barra tricolor (azul, ciano, verde) e uma **linha de informações** com três itens separados:
   `Pacote <id> · <PUXADA | TSP | PUXADA + TSP>`, `Gerado em dd/MM/yyyy HH:mm:ss` e
   `Coleta: <primeira gravação> a <última gravação>` (some quando não há período). Sem id de pacote
   (chamada legada), o primeiro item vira `Tipo: <tipo>`. O mesmo cabeçalho se repete em todas as
   páginas do PDF.
2. **Resumo**: notas processadas, integradas, integradas — revisar produto, pendentes,
   não integradas, arquivos ignorados, transportes e a linha "Por tipo". A antiga linha
   **"Notas por transporte: X: n notas · ..."** foi **removida** do resumo (12/11/2026): a mesma
   informação continua no cabeçalho de cada grupo da tabela ("Transporte X — n notas · ...").
3. **Tempo de integração** (nível B, ver seção abaixo). Fica no lugar da linha removida.
4. **Produtos criados por contenção (revisar cadastro)**: aparece **somente** quando há
   produto provisório. Lista nota, transporte e códigos de produto.
5. **Notas por transporte**: tabela agrupada por transporte, com linha de cabeçalho do grupo
   ("Transporte X — n notas · n integradas · n com problema"). Ordem: transporte, depois nota.

### Colunas

| Coluna | Conteúdo |
|--------|----------|
| Nota | Número da nota fiscal |
| Transporte | Número do transporte (DocCarga) |
| Horário | Horário do processamento, com fuso (`dd/MM/yyyy HH:mm:ss zzz`) |
| Status | Status único (ver abaixo); marca discreta "revisar produto: códigos" quando houver |
| ID de processamento | `Transação:` (campo `transacao` da **última** chamada da I50) e `Documento:` (primeiro `retornos[].idDocumento` não vazio da última chamada). Serve para o colaborador localizar o registro em tela no WMS |
| Mensagem | **Último erro resumido** (até 160 caracteres, termina com `...` se cortado). O histórico completo não entra no PDF |
| Integrada | `S` / `N` (mantido) |
| Tentativas | Quantidade de tentativas da I50 (1 a 3) |

### Status

| Status | Quando | Cor |
|--------|--------|-----|
| Integrada | I50 aceita pelo WMS | verde `#009A83` |
| Integrada — revisar produto | Aceita, mas houve I30 de contenção (produto provisório) | verde `#009A83` + marca "revisar produto" |
| Pendente | Importada, pendente na interface do WMS | âmbar `#F5A623` |
| Não integrada | Erro não recuperável ou 3 tentativas esgotadas | vermelho `#D32F2F` |

Âmbar e vermelho são cores semânticas, fora da paleta da marca. O Orquestrador continua
devolvendo `Integrada=true` com `Estado=Pendente` quando há produto a revisar; a distinção é
feita só na apresentação (`LinhaRelatorioErro.StatusDescricao`).

A lista de produtos provisórios contém **todos os itens enviados à I30**: o WMS não informa
quais foram de fato criados (a resposta é "já existe" ou sucesso).

## HTML

- **Autocontido**: CSS inline, sem recursos externos (nenhum `http(s)://`, `<link>` ou
  `<script src>`); logo, quando existir, vai em base64.
- Cabeçalho da tabela fixo (`position: sticky`).
- **Filtro/busca opcional** em JavaScript puro inline (texto livre e status). Os controles
  ficam ocultos (`hidden`) e só aparecem com JS; **sem JS todas as linhas continuam visíveis**.
- Cada linha com mensagem longa ou histórico tem `<details>` ("Ver detalhe") com a mensagem
  integral e o histórico completo das chamadas.
- Impressão: regras `@media print` (cabeçalho não fixo, controles ocultos).

## PDF

- **A4 paisagem** (842 × 595 pt), margens de 30 pt, fonte **Arial** com codificação Unicode.
- Quebra de linha dentro das células (por palavra; palavra maior que a coluna quebra por
  caractere). A altura da linha acompanha a maior célula.
- Paginação: o cabeçalho da tabela é **repetido** em cada página; o cabeçalho de grupo nunca
  fica órfão no fim da página; rodapé com "Página X de Y" e data/hora de geração
  (calculado em duas passadas para conhecer o total).

## Tempo de integração

Bloco medido de verdade pelo relógio do sistema (`IClock`), nunca estimado. Aparece no HTML, no PDF
e no e-mail de fechamento com **os mesmos textos** (um único objeto, `BlocoTempo`, é montado a
partir de `MedicaoTempo` e os três formatos só o diagramam).

**Resumo do pacote**

| Linha | Definição |
|-------|-----------|
| Início do pacote | Abertura do pacote (primeiro arquivo detectado) |
| Fim do pacote | Início do fechamento (antes de gerar o relatório) |
| Duração total | Fim − início |
| Notas | Notas reais do pacote (sem arquivos ignorados) |
| Tempo médio por nota | Duração total ÷ notas (mesma conta de `TempoMedioPorNotaMs` da telemetria) |
| Notas por segundo | Notas ÷ duração total em segundos (duas casas) |

Com 0 notas ou duração 0, as duas últimas linhas são omitidas (nunca "NaN" nem divisão por zero).

**Etapas**

| Etapa | Onde é medida | Duração | Por nota |
|-------|---------------|---------|----------|
| Espera da janela de coleta | `GerenciadorPacote`: abertura até a foto da pasta | sim | - |
| Leitura do ZIP/XML | `ProcessadorLote`: fase de parse (abrir ZIP, ler XML) | sim | - |
| Integração no WMS | `ProcessadorLote`: início da 1ª nota até o registro da última (tempo de parede) | sim | soma das chamadas I50/I10/I30/I60 (`Orquestrador`) ÷ notas |

A coluna **"Por nota" de "Integração no WMS"** é o número comparável ao processo manual (hoje ~12 s
por nota na tela). Ela usa a soma das chamadas, e não a duração ÷ notas, porque as ondas podem
rodar em paralelo (`MaxTransportesConcorrentes`).

**Por onda (somente TSP)**: onda, notas, transportes distintos e tempo da onda (início da primeira
nota da onda até o registro da última). Na PUXADA esta tabela não aparece (pode haver centenas de
transportes; o detalhe fica nos grupos da tabela principal). Quando a tabela por onda existe, os
"chips"/parágrafo "Ondas da Puxada TSP" deixam de aparecer para não repetir os números.

**Formato**: `850 ms`, `4,2 s`, `45 s`, `1 min 25 s`, `2 min`, `1 h 05 min`; números em pt-BR
(`1.234`, `0,07`).

**Dados ausentes**: pacote retomado após uma queda não sabe a espera da janela nem a leitura (as
linhas somem); pacotes antigos/sem medição não mostram o bloco. Nada quebra.

**Limitação (etapa de relatório)**: o tempo de geração do próprio relatório não pode estar dentro
dele. Por isso o HTML, o PDF **e o e-mail** mostram só as etapas que terminaram antes de renderizar
(o e-mail poderia mostrar, mas ficaria diferente do relatório). A geração e a gravação do HTML+PDF
são medidas com `Stopwatch` no `FechadorPacote` e vão **só para a telemetria**
(`TempoRelatorioMs`). Uma nota curta sob o bloco avisa isso ao leitor.

## Identidade visual (Friozem)

Cores: `#17479E` (azul), `#54B6E7` (azul claro), `#009A83` (verde), `#333333` (texto),
`#C1C1C1` (bordas). Fonte Arial. Barra divisória curta sob títulos. Logo com largura mínima
de 3,5 cm e margem de proteção.

### Logo (embutida; `Relatorio:LogoPath` opcional)

A logo **negativa** (branca, fundo transparente, 560×313, 35 KB) vem **embutida no programa**
(`src/KitWmsIntegrador.Core/Relatorio/Recursos/logo-friozem-negativo.png`, recurso do assembly;
é a mesma imagem da apresentação e dos manuais). Ordem de resolução (`IdentidadeVisual.ObterLogo`):

1. `Relatorio:LogoPath` (ou `Relatorio__LogoPath`), se apontar para um PNG válido;
2. a logo embutida (padrão: `LogoPath` vazio);
3. só se nenhuma existir, o texto "Friozem".

Nada precisa ser copiado no servidor. Se usar `LogoPath`, prefira uma versão **negativa/branca**:
ela aparece sobre a faixa azul. O HTML continua **autocontido** (logo em base64); o PDF desenha a
logo sobre a faixa; o e-mail usa CID. Na assinatura do e-mail e no PDF do resumo semanal (fundo
branco) a logo vai sobre um **selo azul**, para ter contraste. A faixa usa `print-color-adjust:exact`
para não sumir na impressão. Contraste do título branco sobre `#17479E` ≈ 9:1; a logo tem `alt="Friozem"`.

### Título (mudança de 12/11/2026 — reversível)

O título passou de **"Relatório de Erros de Integração"** para **"Relatório de Integração"**, porque
o relatório cobre todas as notas, não só as com erro. Para reverter, basta voltar a constante
`IdentidadeVisual.Titulo` (em `src/KitWmsIntegrador.Core/Relatorio/IdentidadeVisual.cs`) e os testes
que citam o texto (`LogoEmbutidoTests`, `RelatorioRefinadoTests`, `RelatorioCabecalhoTempoTests`).
O nome dos arquivos (`pacote_<id>.html/.pdf`) não mudou.

## Como regenerar os exemplos

Os exemplos (`docs/exemplos/relatorio-erros-exemplo.html` e `.pdf`, e `email-pacote-exemplo.html`)
usam dados fictícios (3 transportes, 12 notas PUXADA e 2 ondas TSP, com medição de tempo sintética)
e são gravados por um teste que só roda com a variável ligada:

```powershell
$env:GERAR_EXEMPLOS='1'; dotnet test KitWmsIntegrador.sln --filter GerarExemplos
```

Os dados ficam em `tests/KitWmsIntegrador.Tests/ExemplosRelatorio.cs`.

## Decisões e fora de escopo

- `PreCondicaoOk` de I10/I30 **não** foi tratado nesta etapa (fora do escopo).
- O relatório não altera o fluxo nem a decisão do Orquestrador; apenas captura
  `transacao`, `idDocumento` e códigos de produtos provisórios.
- E-mail e movimentação de arquivos não foram alterados.
