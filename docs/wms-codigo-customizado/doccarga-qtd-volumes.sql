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
  
  -- total de caixas e quantidade de itens da doccarga  
  vQtdVolumes     NUMBER := 0;   
  vQtdItens       NUMBER := 0;   
  vMsgErro        VARCHAR2(2000);   
BEGIN   
  -- ===== CORAÇÃO =====  
  
  -- 1) soma as caixas dos itens da doccarga em importação  
  vMsgErro := 'Erro PtoFuncao QTDVOL-PRE:1 ';   
  BEGIN  
    SELECT COUNT(1),  
           NVL(SUM(NVL(i.qtd_informada_cxs, 0)), 0)  
      INTO vQtdItens, vQtdVolumes  
      FROM solwms.i_itemdoccarga i  
     WHERE i.iddoccarga = pIdDocCarga;  
  EXCEPTION  
    WHEN OTHERS THEN  
      vMsgErro := vMsgErro||'Erro não tratado ao somar as caixas dos itens do Documento de Carga '||pIdDocCarga||': '||SQLERRM;  
      raise_application_error(-20200, SUBSTR(vMsgErro,1,2000));  
  END;  
  
  -- 2) valida o resultado da soma  
  vMsgErro := 'Erro PtoFuncao QTDVOL-PRE:2 ';   
  IF vQtdItens = 0 THEN  
    vMsgErro := vMsgErro||'Documento de Carga '||pIdDocCarga||' não possui itens.';  
    raise_application_error(-20200, vMsgErro);  
  END IF;  
  IF vQtdVolumes <= 0 THEN  
    vMsgErro := vMsgErro||'Documento de Carga '||pIdDocCarga||' sem quantidade de caixas nos itens (qtd_informada_cxs).';  
    raise_application_error(-20200, vMsgErro);  
  END IF;  
  
  -- 3) grava o total no cabeçalho da doccarga  
  vMsgErro := 'Erro PtoFuncao QTDVOL-PRE:3 ';   
  BEGIN  
    UPDATE solwms.i_doccarga d  
       SET d.qtd_volumes = vQtdVolumes  
     WHERE d.iddoccarga = pIdDocCarga;  
  
    IF SQL%ROWCOUNT = 0 THEN  
      vMsgErro := vMsgErro||'Documento de Carga '||pIdDocCarga||' não encontrado para gravar QTD_VOLUMES.';  
      raise_application_error(-20200, vMsgErro);  
    END IF;  
  EXCEPTION  
    WHEN OTHERS THEN  
      IF SQLCODE = -20200 THEN  
        RAISE;  
      END IF;  
      vMsgErro := vMsgErro||'Erro não tratado ao gravar QTD_VOLUMES do Documento de Carga '||pIdDocCarga||': '||SQLERRM;  
      raise_application_error(-20200, SUBSTR(vMsgErro,1,2000));  
  END;  
  
  -- ===== FIM DO CORAÇÃO =====  
END;
