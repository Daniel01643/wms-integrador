# Verificação — Etapa 8

## O que rodou
- `dotnet build KitWmsIntegrador.sln`: 0 avisos, 0 erros.
- `dotnet test KitWmsIntegrador.sln`: **129 aprovados, 0 falhas** (109 originais + 20 novos).
- Exemplos regenerados com `GERAR_EXEMPLOS=1 ... --filter GerarExemplos`.

## Verificação visual
- HTML: Edge headless (screenshot 1500 px). Visto: cabeçalho com "Friozem" + título + barra, 6 cartões de resumo
  com cores, chips de notas por transporte, seção de produtos por contenção, controles de filtro (revelados por JS),
  3 grupos por transporte com contadores, etiquetas verde/âmbar/vermelho, "Integrada — revisar produto" com
  códigos, coluna ID de processamento (Transação/Documento), mensagem longa cortada em ~160 chars com "Ver detalhe".
- PDF: Edge headless exibe cinza em branco para PDF; renderizado com `Windows.Data.Pdf` (WinRT) em PNG.
  Visto: 2 páginas, 1122×793 px (paisagem A4), cabeçalho e rodapé "Página 1 de 2"/"Página 2 de 2", cabeçalho da tabela
  repetido na página 2, quebra de linha (horário, status, produtos, mensagem longa), cores de status.
  Não foi possível testar em Adobe/Chrome PDF viewer.

## Acentuação
- HTML: UTF-8 literal, teste assert.
- PDF: Arial + `PdfFontEncoding.Unicode`; renderizado visualmente com acentos e travessão corretos; teste
  extrai o texto via ToUnicode e confirma `áéíóúãõç ÁÉÍÓÚÃÕÇ —`. Sem IFontResolver próprio (resolver padrão bastou).

## Colunas finais
Nota, Transporte, Horário, Status, ID de processamento (Transação + Documento), Mensagem (último erro, ≤160), Integrada (S/N), Tentativas.

## Status
Integrada (verde), Integrada — revisar produto (verde + marca), Pendente (âmbar), Não integrada (vermelho).

## Campos novos capturados
`ResultadoIntegracaoNota.IdTransacao` (transacao da última I50), `IdDocumento` (1º retornos[].idDocumento não vazio),
`ProdutosProvisorios` (códigos distintos enviados à I30), `RevisaoProdutoPendente`. Decisões do Orquestrador intactas.

## Paginação do PDF
A4 paisagem, margens 30 pt, 2 passadas (layout, desenho). Exemplo de 12 notas: 2 páginas; com 80 linhas: >1 página,
cabeçalho repetido e "Página X de Y" verificados por teste.
