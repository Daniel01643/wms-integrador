declare
 pIdNFVenda       number := :pIdDoc1;
 pIdDepositante   number := :pIdDoc2;
 pIdDeposito      number := :pIdDep;
begin
  null;
exception
  when others then
     raise_application_error(-20400,'Erro inesperado no codigo customizado NVVINC teste. '||sqlerrm );
end;
