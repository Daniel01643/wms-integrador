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
exception
  when others then
     raise_application_error(-20400,'Erro inesperado no codigo customizado NVVINC C1. '||sqlerrm );
end;
