/*
 ============================================================================
  Codigo Customizado WMS - DocCarga: QTD_VOLUMES
 ----------------------------------------------------------------------------
  Autor      : Friozem / Daniel
  Data       : 2026-11-09
  Finalidade : Apos a importacao do DocCarga (I50), somar a quantidade de
               caixas dos itens (solwms.i_itemdoccarga.qtd_informada_cxs) e
               gravar em solwms.i_doccarga.qtd_volumes. A I60 envia o mesmo
               valor, permitindo a associacao I50 x I60.

  Binds      : :pIdDoc1 = iddoccarga
               :pIdDoc2 = depositante
               :pIdDoc3 = deposito

  Regras     : - A nota ja vem em caixas: NAO dividir por embalagem.
               - Altera SOMENTE i_doccarga.qtd_volumes (nada de itens, onda,
                 observacao ou atributos).
               - Idempotente: o valor e recalculado a partir dos itens.

  CONFIRMAR NO HOMOLOG: nome exato da coluna (QTD_VOLUMES vs QTDVOLUMES) em
  solwms.i_doccarga. Este script usa QTD_VOLUMES (conforme informado).
 ============================================================================
*/
DECLARE
  pIdDocCarga    VARCHAR2(100) := :pIdDoc1;
  pIdDepositante VARCHAR2(100) := :pIdDoc2;
  pIdDeposito    VARCHAR2(100) := :pIdDoc3;
  vQtdVolumes    NUMBER        := 0;
BEGIN
  SELECT NVL(SUM(NVL(i.qtd_informada_cxs, 0)), 0)
    INTO vQtdVolumes
    FROM solwms.i_itemdoccarga i
   WHERE i.iddoccarga = pIdDocCarga;

  UPDATE solwms.i_doccarga d
     SET d.qtd_volumes = vQtdVolumes
   WHERE d.iddoccarga = pIdDocCarga;
END;
