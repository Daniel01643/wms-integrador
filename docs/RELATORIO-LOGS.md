# Relatório de erros e pendências (HTML + PDF)

Além do alerta por e-mail, o time pediu um **arquivo de relatório** estruturado com os
erros de integração. A Etapa 8 refinou o layout (HTML e PDF) e a identidade visual.

Os dois formatos são gerados a partir do **mesmo modelo** (`ModeloRelatorio`), portanto
os números do resumo e a ordem das linhas são idênticos. Biblioteca de PDF adotada:
**PdfSharpCore 1.3.67** (MIT, versão fixa), sem novas dependências.

Nome dos arquivos: `erros_<yyyy-MM-dd_HHmm>.html` e `.pdf`, na pasta `Relatorio:PastaSaida`.

## Layout

1. **Cabeçalho**: logo (ou texto "Friozem" na cor da marca), título "Relatório de Erros de
   Integração", data/hora de geração e barra divisória curta (azul, azul claro, verde).
2. **Resumo**: notas processadas, integradas, integradas — revisar produto, pendentes,
   não integradas, transportes, e a lista de notas por transporte.
3. **Produtos criados por contenção (revisar cadastro)**: aparece **somente** quando há
   produto provisório. Lista nota, transporte e códigos de produto.
4. **Notas por transporte**: tabela agrupada por transporte, com linha de cabeçalho do grupo
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

## Identidade visual (Friozem)

Cores: `#17479E` (azul), `#54B6E7` (azul claro), `#009A83` (verde), `#333333` (texto),
`#C1C1C1` (bordas). Fonte Arial. Barra divisória curta sob títulos. Logo com largura mínima
de 3,5 cm e margem de proteção.

### Logo (`Relatorio:LogoPath`)

Vazio por padrão. Para usar o logo oficial, copie o PNG para `docs/identidade/` (ou outro
caminho) e configure `appsettings.json` seção `Relatorio`:

```json
"Relatorio": { "PastaSaida": "relatorios", "LogoPath": "docs/identidade/logo-friozem.png" }
```

Ou por variável de ambiente: `Relatorio__LogoPath`. Se o caminho estiver vazio, não existir ou
não for um PNG válido, o cabeçalho usa o texto "Friozem" na cor da marca, sem erro.

## Como regenerar os exemplos

Os exemplos (`docs/exemplos/relatorio-erros-exemplo.html` e `.pdf`) usam dados fictícios
(3 transportes, 12 notas) e são gravados por um teste que só roda com a variável ligada:

```powershell
$env:GERAR_EXEMPLOS='1'; dotnet test KitWmsIntegrador.sln --filter GerarExemplos
```

Os dados ficam em `tests/KitWmsIntegrador.Tests/ExemplosRelatorio.cs`.

## Decisões e fora de escopo

- `PreCondicaoOk` de I10/I30 **não** foi tratado nesta etapa (fora do escopo).
- O relatório não altera o fluxo nem a decisão do Orquestrador; apenas captura
  `transacao`, `idDocumento` e códigos de produtos provisórios.
- E-mail e movimentação de arquivos não foram alterados.
