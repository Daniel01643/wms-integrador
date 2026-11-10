/*********************************************************/   
/*** Execução do pré-processamento Importa doccarga BRF/Friozem ***/   
/*** Preenche QTD_VOLUMES com a soma das caixas dos itens ***/   
/*** Feito Friozem(Daniel) 09/11/2026                    ***/   
/*********************************************************/   
DECLARE   
  -- parâmetros recebidos do processo de importação  
  pIdDocCarga     VARCHAR2(100) := :pIdDoc1;   
  pIdDepositante  VARCHAR2(100) := :pIdDoc2;   
  pIdDeposito     VARCHAR2(100) := :pIdDoc3;   
  
  -- total de caixas da doccarga  
  vQtdVolumes     NUMBER := 0;   
BEGIN   
  -- ===== CORAÇÃO: soma as caixas dos itens da doccarga em importação =====  
  SELECT NVL(SUM(NVL(i.qtd_informada_cxs, 0)), 0)  
    INTO vQtdVolumes  
    FROM solwms.i_itemdoccarga i  
   WHERE i.iddoccarga = pIdDocCarga;  
  
  -- grava o total no cabeçalho da doccarga  
  UPDATE solwms.i_doccarga d  
     SET d.qtd_volumes = vQtdVolumes  
   WHERE d.iddoccarga = pIdDocCarga;  
  -- ===== FIM DO CORAÇÃO =====  
END;
