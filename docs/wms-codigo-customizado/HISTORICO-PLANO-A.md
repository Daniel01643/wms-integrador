# Histórico técnico do Plano A (I60 com associação automática)

Período: 09/11 e 10/11/2026, WMS de homolog. Status: **PAUSADO, aguardando o fornecedor.**
A I60 segue desligada em produção (`I60:Habilitada=false`); nada foi para produção.

Objetivo: a I60 (nota de venda) associar automaticamente ao DocCarga criado pela I50, sem tela extra.

## 1. Arquivos envolvidos

| Arquivo | Papel | Situação |
|---|---|---|
| `doccarga-qtd-volumes.sql` | POS-processamento do DocCarga: soma `itemdoccarga.qtd_informadacx` e grava `doccarga.qtd_volumes`; `EXECUTE IMMEDIATE`, erros `QTDVOL-POS:1/2/3` (-20200) | **Ativo em homolog**; roda sem erro. Não resolveu a I60 |
| `notavenda-vincula-itemdoccarga.sql` | PRE da nota venda: preenche `i_notavenda.IDDOCCARGA` e `i_itemvenda.IDITEMDOCCARGA` (busca DocCarga por `numero` = `numeronf` e `idempresa_proprietaria`; itens por `produto.codigo` = `codproduto`) | **ORA-20901** em todas as versões |
| `teste-a-notavenda-minimo.sql` | Só `declare` dos parâmetros `:pIdDoc1`, `:pIdDoc2`, `:pIdDep` + `null` | **Passou** (sem ORA-20901) |
| `teste-c1-so-busca-doccarga.sql` | Isolamento: só a busca do DocCarga | Preparado, **não executado** |
| `teste-c2-busca-e-cabecalho.sql` | Isolamento: busca + `update i_notavenda set iddoccarga` | Preparado, **não executado** |
| `ROTEIRO-TESTE-HOMOLOG.md` | Roteiro de teste I50 + I60 | Referência |
| Worker `--enviar-i60 <xml>` | Envia só a I60, mostra o JSON, grava envio e resposta em `bin\...\envio-manual-i60\` | Ferramenta |
| Opções `I60:TipoOperacaoNota`, `I60:QtdeVolumeZero` | Padrão `INE`; `qtdeVolume = 0` | Ferramenta de teste |

## 2. Tentativas e mensagens

### 2.1 Código customizado do DocCarga (QTD_VOLUMES)
1. **PRE**: as tabelas `i_` não serviam (mensagem: "não possui itens").
2. **POS** (tabelas da base), primeira versão: **ORA-00900**.
3. **POS** com `EXECUTE IMMEDIATE` e exceptions `QTDVOL-POS`: roda e o DocCarga sobe sem erro. Nome
   da coluna de caixas confirmado no projeto planejador: `qtd_informadacx` (sem sublinhado antes de `cx`).

### 2.2 I50
"Registrado com sucesso". Placa desligada só no teste (`I50__EnviarPlacaVeiculo=false`).

### 2.3 I60 com `qtdeVolume` = soma das caixas
> Total de volume(s) do pedido(doccarga): 0, não confere com a qtdvolume da nota venda: N

Mesmo com o DocCarga assentado há mais de 5 minutos (nota 15671241), o erro persistiu. A conferência
não lê o `QTD_VOLUMES` gravado. Hipótese: conta volumes físicos (`itemdoccarga.IDVOLUME`), que só
existem após a separação.

### 2.4 I60 com `qtdeVolume = 0` (`I60:QtdeVolumeZero`)
A conferência de volume **passa**. `qtdeTotalNota` = soma também passa. Próximo bloqueio:

> Produto X. Quantidade caixa do(s) N pedido(s) não confere com a quantidade da nota venda 0

O JSON mostra `qtdeProduto` = `qtdeProdutoConfirmada` = `qtdeEmbalagem` = 2 (nota 15671241, produto
175888, embalagem 1 para 1).

### 2.5 Timing
Logo após a I50, a I60 também pode receber:

> Há pedido(s)(Doccarga) da nota venda que não existe no WMS

(DocCarga ainda pendente na interface.)

### 2.6 Tipo de operação
O usuário definiu `INE` (padrão). O teste com `IMP` não aconteceu (a I50 voltou "já existe").

### 2.7 Associação manual
Pela tela do WMS **funcionou** (nota 15671286), com dados coerentes. O problema é só o caminho automático.

### 2.8 Análise das tabelas (planilha do usuário; produção = homolog)
- `itemvenda` (base): `IDITEMDOCCARGA` preenchido nas notas associadas.
- `i_itemvenda` (interface): `IDITEMDOCCARGA` vazio.
- `i_notavenda`: `IDDOCCARGA` vazio e `NROPEDIDO` preenchido.

A I60 não tem campo para o vínculo item a item. **Hipótese principal: falta o vínculo `IDITEMDOCCARGA`.**

### 2.9 Configuração do depositante
"Geral - NotaVenda - ChecaItensVenda" = NÃO foi testada: mesmo erro. Não controla essa conferência;
recomendado voltar para SIM.

### 2.10 PRE da nota venda (`notavenda-vincula-itemdoccarga.sql`)
Versões testadas, **todas com ORA-20901** (o WMS recusa antes de executar): com blocos internos;
plana; padrão Unidasul; padrão Havita com `:pIdDep`; sem acentos; sem comentários `--`; buscas
simplificadas sem a tabela `empresa`; busca do `IDDOCCARGA` na `doccarga` e do `IDITEMDOCCARGA` na
`itemdoccarga`.

O **teste A mínimo** (só `declare` dos parâmetros + `null`) **passou** sem ORA-20901. Logo o ponto de
cadastro e os parâmetros estão corretos; a causa está no miolo e **não foi isolada** (testes C1/C2
preparados e não executados). O usuário notou que subrotinas (`begin` dentro de `begin`) parecem causar
o erro; o exemplo Havita (`update i_notavenda set situacao='IMC'...`, `exception when others then
raise_application_error(-20400,...)`) foi usado como molde.

## 3. O que foi descartado e por quê

| Descartado | Por quê |
|---|---|
| Gravar `QTD_VOLUMES` e esperar a I60 ler | A conferência não lê esse campo (erro persiste com DocCarga assentado) |
| Hipótese de fator de conversão pela embalagem | Descartada pelo usuário: embalagem 1 para 1, `qtdeProduto` = `qtdeEmbalagem` = 2 |
| Mexer em "ChecaItensVenda" | Mesmo erro com NÃO; voltar para SIM |
| Código no PRE do DocCarga | Tabelas `i_` sem itens; movido para POS |
| Mais variações sintáticas do PRE da nota venda | Todas ORA-20901; sem o fornecedor não há como ver o motivo |
| `IMP` | Não testado (I50 "já existe"); `INE` é o padrão definido pelo usuário |

## 4. Decisão

Plano A **pausado**: depende do fornecedor. O usuário segue para o **Plano B** (devolução pela I40;
ver `docs/ARQUITETURA-PLANO-B-I40.md` e `docs/diario/2026-11-10.md`).

## 5. Perguntas para o fornecedor

Contexto: depositante BRF, WMS de homolog, I50 sobe com sucesso e a associação manual pela tela funciona.

1. **Total de volumes do pedido.** Qual campo ou rotina alimenta o "total de volume(s) do pedido
   (doccarga)" conferido na I60? Por que ele não lê `doccarga.qtd_volumes` (mesmo gravado e assentado
   há mais de 5 minutos)? Ele conta `itemdoccarga.IDVOLUME` (volumes físicos pós-separação)?
2. **Vínculo item a item.** Como a I60 deve vincular o item da nota venda ao item do DocCarga?
   `i_itemvenda.IDITEMDOCCARGA` não existe no layout da I60, e na associação manual o `itemvenda` da base
   fica com `IDITEMDOCCARGA` preenchido. Qual é o caminho suportado para o vínculo via interface?
3. **ORA-20901 no pré-processamento da nota venda.** Por que o código customizado de pré-processamento
   da nota venda retorna ORA-20901 (o WMS recusa antes de executar), se o esqueleto mínimo
   (`declare` dos parâmetros + `null`) passa? **Anexar** `notavenda-vincula-itemdoccarga.sql`
   e `teste-a-notavenda-minimo.sql`. Existe restrição de sintaxe, de tabelas acessíveis ou do
   validador (por exemplo, `for ... loop`, `update` em `i_` ou acesso a `doccarga`/`itemdoccarga`)?
4. **Associação automática INE.** Existe forma suportada de associação automática com `INE` para o
   depositante BRF, ou o correto é `IMP` + associação manual?
