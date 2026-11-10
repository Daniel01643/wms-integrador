/*******************************************************/
/*** Vincula os itens da nota venda aos itens do     ***/
/*** DocCarga de mesmo numero (IDITEMDOCCARGA)        ***/
/*** Friozem - BRF (pre-processamento nota venda)    ***/
/*** Feito Friozem(Daniel) 10/11/2026                ***/
/*******************************************************/
declare
 pIdNFVenda       number := :pIdDoc1;
 pIdDepositante   number := :pIdDoc2;
 pIdDeposito      number := :pIdDep;
 vIdDocCarga      number;
begin

        for cr_doc in (
          select d.iddoccarga
            from doccarga d,
                 i_notavenda nv
           where nv.id = pIdNFVenda
             and ltrim(d.numero, '0') = ltrim(nv.numeronf, '0')
             and d.idempresa_proprietaria = pIdDepositante
        ) loop
          vIdDocCarga := cr_doc.iddoccarga;
        end loop;

        update i_notavenda nv
           set nv.iddoccarga = vIdDocCarga
         where nv.id = pIdNFVenda
           and vIdDocCarga is not null;

        for cr_itens in (
          select iv.id,
                 idc.iditemdoccarga
            from i_itemvenda iv,
                 itemdoccarga idc,
                 produto p
           where iv.idnotavenda = pIdNFVenda
             and idc.iddoccarga = vIdDocCarga
             and p.idproduto    = idc.idproduto
             and p.codigo       = iv.codproduto
        ) loop
          update i_itemvenda i
             set i.iditemdoccarga = cr_itens.iditemdoccarga
           where i.id = cr_itens.id;
        end loop;

exception
  when others then
     raise_application_error(-20400,'Erro inesperado no codigo customizado NVVINC (nota venda '||pIdNFVenda||'). '||sqlerrm );
end;
