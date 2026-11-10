# Código Customizado WMS: QTD_VOLUMES do DocCarga

Arquivo: `doccarga-qtd-volumes.sql`

## Onde cadastrar
No WMS, em Código Customizado, no pré-processamento da importação do DocCarga, vinculado
somente ao nosso depositante. Outros depositantes não são afetados. Gravar primeiro no homolog.

## O que faz
Roda em tempo de execução, na importação de cada DocCarga (I50). Soma `qtd_informada_cxs`
dos itens daquela doccarga e grava o total em `i_doccarga.qtd_volumes`. Não divide por
embalagem, porque a nota já vem em caixas. A I60 envia o mesmo total, então a conferência
de volume entre DocCarga e nota de venda bate.

Mantém o esqueleto do código original (cabeçalho, `DECLARE` com os três parâmetros
`:pIdDoc1`, `:pIdDoc2`, `:pIdDoc3`, `BEGIN ... END`). Só o miolo, marcado como CORAÇÃO, é novo.

## O que não faz
Não altera itens, onda (`nroagrupseparacao`), observação, atributos nem quantidades.
Nada da lógica da Unidasul foi mantida.

## Confirmar no homolog
- Nome da coluna: `QTD_VOLUMES` ou `QTDVOLUMES`. Se for o segundo, trocar só na linha do `SET`.

## Como reverter
Desativar o código customizado no cadastro do depositante.
