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

## Erros (raise_application_error -20200)
Seguem o padrão dos códigos customizados da casa (`vMsgErro` com ponto de função):

| Código | Quando |
|---|---|
| `QTDVOL-PRE:1` | Falha não tratada ao somar as caixas dos itens |
| `QTDVOL-PRE:2` | Doccarga sem itens, ou com soma de caixas igual a zero |
| `QTDVOL-PRE:3` | Doccarga não encontrada ao gravar, ou falha não tratada no UPDATE |

Qualquer um desses erros aborta a importação daquele DocCarga. O integrador recebe a
mensagem do WMS e a nota aparece como não integrada no relatório do pacote, com o motivo.

## O que não faz
Não altera itens, onda (`nroagrupseparacao`), observação, atributos nem quantidades.
Nada da lógica da Unidasul foi mantida.

## Confirmar no homolog
- Nome da coluna: `QTD_VOLUMES` ou `QTDVOLUMES`. Se for o segundo, trocar só na linha do `SET`.

## Query de conferência (somente leitura)
Depois da importação da I50, compara o total gravado com a soma dos itens. A coluna
`diferenca` deve ser 0. Trocar `qtd_volumes` por `qtdvolumes` se o nome da coluna for o outro.

```sql
SELECT d.iddoccarga,
       d.qtd_volumes,
       SUM(NVL(i.qtd_informada_cxs, 0))                  AS soma_caixas_itens,
       d.qtd_volumes - SUM(NVL(i.qtd_informada_cxs, 0))  AS diferenca
  FROM solwms.i_doccarga d
  JOIN solwms.i_itemdoccarga i ON i.iddoccarga = d.iddoccarga
 WHERE d.iddoccarga = :iddoccarga
 GROUP BY d.iddoccarga, d.qtd_volumes;
```

## Decisão pendente: soma zero ou sem itens
O código aborta a importação (`QTDVOL-PRE:2`) quando não há itens ou a soma de caixas é
zero. Isso é mais que gravar QTD_VOLUMES: bloqueia o DocCarga. Confirmar com o usuário que
é o desejado; se não for, trocar o `raise_application_error` por gravar 0 e seguir.

## Como reverter
Desativar o código customizado no cadastro do depositante.
