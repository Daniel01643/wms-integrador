# Codigo Customizado WMS: QTD_VOLUMES do DocCarga

Arquivo: `doccarga-qtd-volumes.sql`

## Onde cadastrar
No WMS, em Codigo Customizado, no pre/pos-processamento da importacao do DocCarga, vinculado SOMENTE ao depositante da integracao. Outros depositantes nao sao afetados. Binds: `:pIdDoc1` = iddoccarga, `:pIdDoc2` = depositante, `:pIdDoc3` = deposito. Gravar primeiro no homolog.

## O que faz
Soma `solwms.i_itemdoccarga.qtd_informada_cxs` (NVL para nulos) dos itens do doccarga e grava em `solwms.i_doccarga.qtd_volumes`. Nao divide por embalagem, pois a nota ja vem em caixas. Idempotente: rodar 2x gera o mesmo resultado.

## O que NAO faz
Nao altera itens, onda (`nroagrupseparacao`), observacao, atributos nem quantidades. Nao reutiliza a logica do codigo da Unidasul (apenas o estilo e os binds).

## CONFIRMAR NO HOMOLOG
- Nome da coluna: `QTD_VOLUMES` vs `QTDVOLUMES` em `i_doccarga`.
- Se o codigo roda antes ou depois de os itens estarem gravados em `i_itemdoccarga` (a soma depende dos itens ja existentes).

## Como reverter
Desativar (ou descadastrar) o codigo customizado do depositante. Valores ja gravados em `qtd_volumes` permanecem; se necessario, corrigir manualmente.

## Query de conferencia
```sql
SELECT d.iddoccarga,
       d.qtd_volumes,
       NVL(SUM(i.qtd_informada_cxs), 0) AS soma_cxs,
       CASE WHEN NVL(d.qtd_volumes, -1) = NVL(SUM(i.qtd_informada_cxs), 0)
            THEN 'OK' ELSE 'DIVERGENTE' END AS status
  FROM solwms.i_doccarga d
  LEFT JOIN solwms.i_itemdoccarga i ON i.iddoccarga = d.iddoccarga
 GROUP BY d.iddoccarga, d.qtd_volumes
 ORDER BY d.iddoccarga;
```
