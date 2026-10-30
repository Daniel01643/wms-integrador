# Mapeamento de dados — entrada real (`C:\Envia xml`)

Levantamento feito sobre os arquivos reais em 2026-10-30. Base para o cruzamento
XML × planilha e para a montagem de I50/I60.

## Panorama da pasta

- **1196** arquivos `.XML` (NFe autorizadas, layout nacional 4.00).
- **2** planilhas `.xls` (Excel 97-2003 binário): `Puxada SP 17.12.xls`,
  `Puxada SP 18.12.xls`.
- Alguns `.zip` (`XML's 1.zip`, `XML's 2.zip`) — provavelmente lotes compactados.

## 1. Estrutura do XML da NFe (campos úteis)

Namespace: `http://www.portalfiscal.inf.br/nfe`, versão 4.00. Envelope `nfeProc`.
Campos relevantes para I50/I60 e para o cruzamento:

| Campo WMS / uso            | Caminho no XML                                  | Exemplo                          |
|----------------------------|-------------------------------------------------|----------------------------------|
| **Chave NFe**              | `infNFe/@Id` (sem prefixo "NFe") / `protNFe/.../chNFe` | `35251201838723...604491612` |
| **Número da nota (nNF)**   | `ide/nNF`                                        | `15665807`                       |
| Série                      | `ide/serie`                                      | `6`                              |
| Modelo                     | `ide/mod`                                        | `55`                             |
| Emissão                    | `ide/dhEmi`                                      | `2025-12-15T08:29:34-03:00`      |
| Emitente CNPJ / nome       | `emit/CNPJ`, `emit/xNome`                        | `01838723009850` / BRF S.A.      |
| Destinatário CNPJ / nome   | `dest/CNPJ`, `dest/xNome`                        | `28288914000119` / FREE MAN ...  |
| **Pedido do cliente**      | `det/prod/xPed`                                  | `0774534`                        |
| Item — código produto      | `det/prod/cProd`                                 | `175894`                         |
| Item — EAN                 | `det/prod/cEAN`                                  | `17891515645813`                 |
| Item — descrição           | `det/prod/xProd`                                 | KIT ESPERAN ...                  |
| Item — **qtd comercial**   | `det/prod/qCom` + `uCom`                         | `12.0000` CX                     |
| Item — valor               | `det/prod/vProd`                                 | `5710.80`                        |
| Lote / validade            | `det/prod/rastro/nLote`, `dVal`                  | `3755261` / `2026-01-16`         |
| Valor total da nota        | `total/ICMSTot/vNF`                              | `12644.40`                       |
| Volumes / peso             | `transp/vol/qVol`, `pesoL`, `pesoB`             | `36` / `275.640` / `302.675`     |
| Transportadora             | `transp/transporta/xNome`                        | FRIOZEM LOGISTICA LTDA           |

Observações:
- Uma NFe tem **N itens** (`det nItem="1..n"`). A quantidade de produtos (soma/linha)
  é o que precisa **bater** com o DocCarga.
- O campo `infAdic/infCpl` tem texto livre com "Transporte: 0130693549" e "PED CLI",
  mas não é confiável como chave estruturada.

## 2. Estrutura da planilha "Puxada" (BRF / SAP)

Relatório SAP "Acompanhamento de Ordens Faturadas" (transação ZSD0199). Layout:

- **Linhas 1-8**: cabeçalho de relatório (título, empresa BRF S.A., data/hora, transação).
- **Linha 9**: **cabeçalho das colunas** (24 colunas).
- **Linha 11 em diante**: dados. Cada linha = **1 item de nota** (granularidade de item,
  não de nota — a mesma nota aparece em várias linhas, uma por material).

### Colunas (linha 9)

| Col | Nome                                | Uso no projeto                         |
|-----|-------------------------------------|----------------------------------------|
| 3   | No. Ordem de Venda                  | ordem de venda (SAP)                   |
| 4   | Qt. Ordem de venda                  |                                        |
| 5   | Qt. Fornecida                       |                                        |
| 6   | No. Remessa                         |                                        |
| 7   | **No. Transporte**                  | **agrupamento da onda** (decidido)     |
| 8   | Corte                               |                                        |
| 9   | **No. Nota Fiscal**                 | **chave de cruzamento com `nNF`**      |
| 10  | Material                            | código do produto (≈ `cProd`)          |
| 11  | Desc. Material                      | descrição                              |
| 12  | Sigla                               | sigla/unid do produto                  |
| 13  | **Qt. Nota Fiscal**                 | quantidade (deve bater com `qCom`)     |
| 14  | Unid. Medida                        | CX etc.                                |
| 15  | Motivo de Recusa                    |                                        |
| 16  | Valor do item da Nota Fiscal        |                                        |
| 17  | Peso liquido do item                |                                        |
| 18  | Peso bruto do item                  |                                        |
| 19  | NBM                                 | ≈ NCM                                  |
| 20  | No. Documento Nota Fiscal           | nº documento (DocCarga?)               |
| 21  | Local Expedição                     |                                        |
| 22  | Placa                               |                                        |
| 23  | Cliente                             | código do cliente                      |
| 24  | Nome Cliente                        |                                        |

## 3. Cruzamento XML × planilha

- **Chave**: `No. Nota Fiscal` (col 9 da planilha) ↔ `ide/nNF` do XML. Confirmado no
  exemplo: XML `nNF = 15665807`; planilha tem `15666469`, `15666470` (mesma faixa).
- **Agrupamento da onda**: `No. Transporte` (col 7). Notas com o mesmo No. Transporte
  entram no mesmo DocCarga/onda.
- **Validação de quantidade**: somar `Qt. Nota Fiscal` (col 13) por nota na planilha e
  conferir com a soma de `qCom` dos itens do XML — a regra de "a quantidade de produtos
  precisa bater".

## 3b. DESCOBERTA: o XML já tem o No. Transporte (a planilha é dispensável)

Investigação de 2026-10-30 sobre os 1196 XMLs reais:

- **100% dos XMLs** (1196/1196) têm o padrão `Transporte: <numero>` dentro de
  `infNFe/infAdic/infCpl`. Exemplo: `Transporte: 0130693549` (10 dígitos, com zeros
  à esquerda).
- Notas diferentes compartilham o mesmo número de transporte — comportamento esperado
  de agrupamento de onda. Nos XMLs: **398 transportes distintos** para 1196 notas,
  com ondas de até **10 notas** (confirma "1 DocCarga para N notas").
- **Cruzamento validado contra a planilha `Puxada SP 17.12`**: das **629 notas
  distintas** da planilha, o `No. Transporte` (col 7, lido como valor bruto) **bateu
  com o `Transporte:` do XML em 629/629 — zero divergências, zero notas ausentes**.

### Consequência (decisão de arquitetura)

**O agrupamento da onda pode vir só do XML**, via `infCpl` → `Transporte:`. Com isso:

- A **planilha deixa de ser necessária** para o agrupamento da onda.
- O **problema da notação científica some** (não dependemos mais das colunas numéricas
  grandes do .xls).
- O agente fica **mais simples e robusto**: lê só XMLs, sem acoplar ao formato/versão
  do relatório SAP de um cliente terceiro (ao qual não temos acesso para padronizar).

### Padrão de leitura do transporte (do XML)

- Localizar em `infNFe/infAdic/infCpl` (texto livre).
- Regex: `Transporte:\s*(\d+)` → captura o número (manter zeros à esquerda como string).
- Agrupar notas por esse número = onda. `nroagrupseparacao` da I50 deriva daqui.

### Ressalvas a validar antes de descartar a planilha de vez

- Confirmar o padrão nos lotes dos `.zip` e em remessas de **outros clientes/emitentes**
  (aqui só vimos BRF S.A.). O `infCpl` é texto livre do emitente: se outro cliente não
  escrever "Transporte:", o parser precisa de fallback.
- A planilha ainda pode ser útil para **conferência de quantidade** (col 13
  `Qt. Nota Fiscal`) e como segunda fonte de validação — mas não como fonte primária
  da onda.

## 4. Notação científica na planilha (relevância reduzida)

> Com a descoberta 3b, este problema deixa de ser bloqueante: não dependemos mais das
> colunas numéricas grandes da planilha para o agrupamento. Mantido abaixo como
> referência, caso a planilha volte a ser usada para conferência.


Os números grandes estão formatados como **notação científica** no `.xls`:

- `No. Transporte` aparece como `1,31E+08` (deveria ser `131000000...`).
- `No. Ordem de Venda` como `5,5E+08`; `No. Remessa` como `3,47E+08`;
  `No. Documento Nota Fiscal` como `4,06E+08`.
- `No. Nota Fiscal` (col 9) aparece **inteiro** (`15666469`) — essa, a chave, está OK.

Implicação: ler o `.Text` da célula traz o valor **arredondado/truncado**, inútil como
chave. É preciso ler o **valor numérico bruto** da célula (`.Value2`) e formatá-lo como
inteiro sem notação científica, OU pedir o relatório SAP exportado com essas colunas
como texto. **Decisão pendente** — ver seção 6.

## 5. Volume e desempenho

- ~1196 XMLs e planilhas de ~840 linhas (≈ item). O parser precisa ser eficiente, mas
  o volume é modesto para qualquer stack.

## 6. Pendências que este mapeamento levanta

- **Notação científica**: confirmar se lemos `.Value2` (valor bruto) e formatamos, ou
  se o relatório pode ser exportado com colunas-chave como texto. Impacta o
  `No. Transporte` (agrupamento da onda) — é bloqueante para agrupar corretamente.
- **Granularidade item × nota**: a planilha é por item; agrupar por `No. Nota Fiscal`
  para reconstruir a nota e por `No. Transporte` para a onda.
- **`No. Documento Nota Fiscal` (col 20)**: verificar se é o DocCarga ou outro doc.
- **Relação `xPed` (XML) × `No. Ordem de Venda` (planilha)**: confirmar se amarram.
- **Zips**: definir se o agente também descompacta `XML's *.zip` ou se ignora.
