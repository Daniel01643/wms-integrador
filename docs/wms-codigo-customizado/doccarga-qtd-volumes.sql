/*********************************************************/   
/*** Execução do pós-processamento Importa doccarga BRF/Friozem ***/   
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
  vLinhas         NUMBER := 0;   
  vMsgErro        VARCHAR2(2000);   
BEGIN   
  -- ===== CORAÇÃO =====  
  -- As instruções rodam por EXECUTE IMMEDIATE: se houver erro de coluna, tabela ou  
  -- permissão, a mensagem real do Oracle aparece no log do WMS com o código QTDVOL-POS.  
  
  -- 1) soma as caixas dos itens da doccarga importada  
  vMsgErro := 'Erro PtoFuncao QTDVOL-POS:1 ';   
  BEGIN  
    EXECUTE IMMEDIATE  
      'SELECT COUNT(1), NVL(SUM(NVL(i.qtd_informadacx, 0)), 0) '||  
      '  FROM solwms.itemdoccarga i '||  
      ' WHERE i.iddoccarga = :1'  
      INTO vQtdItens, vQtdVolumes  
      USING pIdDocCarga;  
  EXCEPTION  
    WHEN OTHERS THEN  
      vMsgErro := vMsgErro||'Erro ao somar as caixas dos itens do Documento de Carga '||pIdDocCarga||': '||SQLERRM;  
      raise_application_error(-20200, SUBSTR(vMsgErro,1,2000));  
  END;  
  
  -- 2) valida o resultado da soma  
  vMsgErro := 'Erro PtoFuncao QTDVOL-POS:2 ';   
  IF vQtdItens = 0 THEN  
    vMsgErro := vMsgErro||'Documento de Carga '||pIdDocCarga||' não possui itens.';  
    raise_application_error(-20200, vMsgErro);  
  END IF;  
  IF vQtdVolumes <= 0 THEN  
    vMsgErro := vMsgErro||'Documento de Carga '||pIdDocCarga||' sem quantidade de caixas nos itens.';  
    raise_application_error(-20200, vMsgErro);  
  END IF;  
  
  -- 3) grava o total no cabeçalho da doccarga  
  vMsgErro := 'Erro PtoFuncao QTDVOL-POS:3 ';   
  BEGIN  
    EXECUTE IMMEDIATE  
      'UPDATE solwms.doccarga d '||  
      '   SET d.qtd_volumes = :1 '||  
      ' WHERE d.iddoccarga = :2'  
      USING vQtdVolumes, pIdDocCarga;  
    vLinhas := SQL%ROWCOUNT;  
  EXCEPTION  
    WHEN OTHERS THEN  
      vMsgErro := vMsgErro||'Erro ao gravar QTD_VOLUMES do Documento de Carga '||pIdDocCarga||': '||SQLERRM;  
      raise_application_error(-20200, SUBSTR(vMsgErro,1,2000));  
  END;  
  
  IF vLinhas = 0 THEN  
    vMsgErro := vMsgErro||'Documento de Carga '||pIdDocCarga||' não encontrado para gravar QTD_VOLUMES.';  
    raise_application_error(-20200, vMsgErro);  
  END IF;  
  
  -- ===== FIM DO CORAÇÃO =====  
END;
