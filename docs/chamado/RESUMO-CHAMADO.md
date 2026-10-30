# Chamado — Integração I60 (WebAPI REST) BLUEFOOD/BRF

Ambiente: homologação `friozem.assistsoftware.com.br:8888/wmsapi/api`.
Usuário: BLUEFOOD. REQUISITANTE: `01.838.723/0098-50` (BRF). DESTINO:
`14.605.065/0001-75`. IDENTIFICADOR: `WEBAPI-I60-Json`.

## Situação

A cadeia de integração de uma nota de saída (cadastro de produto I30 → empresa I10 →
pedido/DocCarga I50 → nota I60) está implementada. As interfaces **I30, I10 e I50
retornam sucesso ("Registrado com sucesso")**. A **I60 não conclui** por erros de
**conferência de quantidade/volume** entre o DocCarga (I50) e a nota (I60), e precisamos
de orientação sobre quais campos alimentam essas conferências.

## Erros reais capturados na I60

### 1. Conferência de VOLUME (I60 com qtdeVolume = 36)
```
Operação abortada. Total de volume(s) do(s) pedido(s)(doccarga): 0 ,
não confere com a qtdvolume da nota venda : 36 !
```
O DocCarga (I50) foi criado e a nota tem qtdeVolume=36, mas o WMS lê 0 volumes no
doccarga. **Qual campo da I50 define o "total de volumes do doccarga"?**

### 2. Conferência de CAIXA por item (I60 com qtdeVolume = 0)
```
Operação abortada. Produto 175894. Quantidade caixa do(s) 12 pedido(s)
não confere com a quantidade da nota venda 0
```
Ao zerar o qtdeVolume, o erro muda para quantidade de caixa por item. **Qual campo da
I60 (item) é a "quantidade de caixa" comparada com a I50, e qual campo da I50?**

### 3. Duplicidade (reenvio da mesma nota)
```
Nota [15665807] já existe, TpAcao precisa ser UPD!
```
Confirma que o WMS bloqueia nota duplicada e orienta tpacao="U". (Esperado; só
registrando o comportamento.)

## Perguntas

1. Qual campo da I50 alimenta o "total de volumes do doccarga" conferido contra o
   `qtdeVolume` da I60? É a soma de `qtdeEmbalagem` dos itens? Um campo de cabeçalho?
2. Na conferência por item, qual campo da I60 é a "quantidade de caixa" e qual da I50?
   Como `quantidadeProduto`/`qtdeEmbalagem` (I50) e `qtdeProduto`/`qtdeEmbalagem` (I60)
   devem se corresponder?
3. Com `qtdMandante="E"` na I50 (caixas), como a I60 deve informar quantidade para bater?
4. **Associação ao DocCarga**: diferença prática entre `Tpsit`/`tipoOperacaoNota` =
   **IMB** (associação automática, gera embarque), **INE** (vincula sem embarque) e
   **IMP** (só importa a nota / associação manual). Para cada um, quais conferências de
   quantidade se aplicam? (Anexados 2 JSONs: IMB e IMP.)
5. Os 5 parâmetros podem ir na query string OU no envelope `{"header":{},"body":{}}`?
6. "Importado e pendente na interface": precisa de processamento posterior? Como
   consultar se saiu de pendente para efetivado?

## Anexos (nesta pasta)

- `I60-associacao-automatica-IMB.json` — payload com Tpsit=IMB.
- `I60-associacao-manual-IMP.json` — payload com Tpsit=IMP.
- `respostas-homolog.txt` — respostas reais do homolog aos dois envios.
