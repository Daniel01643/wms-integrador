# Relatório: I60 Plano A (itens 3 a 9)

Data: 2026-11-09. Iteração: primeira (não existia `i60-review.json`). Nada foi enviado ao WMS e o
servidor SRVAPPALC02 não foi acessado. Sem push.

## Commits locais

| Commit | Conteúdo |
|---|---|
| d049f1a | Itens 3 e 4: `qtdeVolume` pela soma das caixas; `I60:TipoOperacaoNota` (mesmo arquivo, um commit só) |
| fcbde6b | Itens 5 e 6: retenção 60 dias; I60 só por env; `TipoOperacaoNota` no appsettings |
| 5ddd3f2 | Item 7: `ARQUITETURA-PLANO-B-I40.md` + SQL/README reescritos pelo orquestrador (não sobrescritos) |
| último | Itens 8 e 9 e este relatório |

## Arquivos alterados

- Código: `OpcoesI60.cs`, `ConstrutorI60.cs`, `Dtos\PayloadI60.cs`, `OpcoesLimpeza.cs`.
- Config: `src\KitWmsIntegrador.Worker\appsettings.json`, `deploy\config-exemplo\appsettings.Production.json`.
- Testes: `ConstrutorI60Tests.cs`, `OpcoesI60Tests.cs`, `LimpadorPastasTests.cs`,
  `LimpezaSubpastaTests.cs`, `PacoteEndToEndTests.cs`.
- Docs: `OPERACAO.md`, `INSTALACAO-SERVIDOR.md`, `ARQUITETURA.md`, `ENCERRAMENTO.md`,
  `CONTROLE-ESFORCO.md`, manuais técnico e do usuário (md e html), `ARQUITETURA-PLANO-B-I40.md`
  (novo), `diario\2026-11-09-i60.md` (novo), `wms-codigo-customizado\ROTEIRO-TESTE-HOMOLOG.md`
  (novo), comentários de `deploy\compartilhar-pasta.ps1` e `deploy\limpar-extensao-invalida.ps1`.

## Decisões de implementação

- `qtdeVolume` = soma de `Itens[].Quantidade` (mesma quantidade que a I50 envia por item e que o
  PL/SQL soma). Fração: arredonda ao inteiro mais próximo, meio para longe de zero
  (`Math.Round(..., AwayFromZero)`), porque o campo é inteiro (12 posições, 0 decimais).
  `qtdeTotalNota` segue sem arredondar. O PL/SQL grava a soma sem arredondar, então com fração a
  I60 pode divergir do DocCarga (documentado no código; caso incomum, pois a nota está em caixas).
- `I60:TipoOperacaoNota` (default `INE`) preenche `Tpsit` e `tipoOperacaoNota` com o mesmo valor,
  como nos JSONs do chamado. Valor vazio volta ao padrão. Override por `I60__TipoOperacaoNota`.
  O construtor recebe `IOptions<OpcoesI60>` como parâmetro opcional (não quebra os construtores
  existentes nos testes).
- I60 continua `Habilitada=false` em `OpcoesI60`, `appsettings.json` e no molde de produção.
  Ligar só por `I60__Habilitada=true` (roteiro de homolog).
- Retenção: 10 -> 60 dias em `OpcoesLimpeza`, `appsettings.json`, molde de produção (bloco
  `Limpeza` explícito) e docs. Testes: padrão 60, 59 dias preserva, 61 remove; idades dos testes
  antigos subiram (11->61, 15->65, 30->90 etc.).

## O que o manual diz sobre tipoOperacaoNota / Tpsit

Não há manual da I60 em PDF no repositório; a evidência são os documentos versionados:

- `docs\TEMPLATES-INTERFACES.md` linha 144: `"tipoOperacaoNota": "INE"` ("Fixo INE (Unidasul)");
  linha 150: `"Tpsit": "INE"` com a nota "IMB=gera embarque / INE=vincula sem gerar embarque";
  linha 231 (tabela de itens a confirmar): "`tipoOperacaoNota` / `Tpsit` ... `INE` ... IMB vs INE p/ BRF".
- `docs\chamado\RESUMO-CHAMADO.md` linhas 48 a 51: pergunta ao fornecedor sobre IMB (associação
  automática, gera embarque), INE (vincula sem embarque) e IMP (só importa / associação manual).
  Linhas 58 e 59: anexos com Tpsit=IMB e Tpsit=IMP.
- `docs\chamado\I60-associacao-automatica-IMB.json` (linhas 37 e 46) e
  `I60-associacao-manual-IMP.json` (linhas 37 e 46): `tipoOperacaoNota`/`Tpsit` iguais a IMB e IMP.
- `docs\chamado\respostas-homolog.txt`: respostas reais do homolog para IMB e IMP.
- Busca por `INV` (palavra inteira, md/txt/json em `docs` e `deploy`, fora da apresentação):
  **nenhuma ocorrência**.

Conclusão: os valores documentados são IMB, INE e IMP. `INV` não tem evidência no repositório, então
o default continuou `INE` e o valor é configurável. Recomendação: confirmar `INV` com o fornecedor ou
testar `I60__TipoOperacaoNota=INV` em homolog; se aceito e associar, definir como padrão em release.

## Verificação

| Comando | Resultado |
|---|---|
| `dotnet build c:\wms-integrador\KitWmsIntegrador.sln` | Sucesso, 0 avisos, 0 erros |
| `dotnet test ...\KitWmsIntegrador.Tests.csproj` | Rodou (não bloqueado): 367 aprovados, 0 falhas, 9 ignorados (stress), total 376 |

- Grep: nenhum default de I60 ligado (`Habilitada=false` em `OpcoesI60.cs`, `appsettings.json` e no
  molde `appsettings.Production.json`; os `true` do grep são de `Limpeza`/`Email`/`Monitoramento`).
- Grep: `DiasRetencao` 60 em código, appsettings, molde e `OPERACAO.md`; não restam "10 dias" de
  retenção em src, deploy, OPERACAO, INSTALACAO-SERVIDOR, ARQUITETURA e manuais. Restam menções
  históricas em diários, planos antigos e `controle\MENSAL.md` (registro do que foi feito na época).
- Nenhum valor monetário adicionado; nenhum segredo impresso.

## Perguntas em aberto

1. Valor correto de `tipoOperacaoNota`/`Tpsit` na I60 (INV, IMB, INE ou IMP) para a BRF.
2. Nome da coluna do PL/SQL (`QTD_VOLUMES` ou `QTDVOLUMES`), a confirmar no homolog.
3. Se o WMS fizer conferência de caixa por item, o teste de homolog mostrará; tratar então.
4. Plano B: perguntas P1 a P9 em `docs\ARQUITETURA-PLANO-B-I40.md` (inversão de emitente/destinatário,
   CFOP, `tipoOperacaoNota` da I40, porta, duplicidade, entre outras).
5. PL/SQL aborta a importação (QTDVOL-PRE:2) com doccarga sem itens ou soma zero: confirmar com o usuário que é desejado.
6. Horas humanas da etapa: a informar pelo usuário.

## Horas

- Horas de IA: aprox. 1,6 h (1,5 h da etapa + 0,1 h da iteração de revisão).

## Iteração de revisão (i60-review.json)

- Achado 1: query de conferência somente leitura adicionada ao README do código customizado e citada (opcional) no roteiro.
- Achado 2: marca 'CONFIRMAR NO HOMOLOG' na linha do SET do SQL.
- Achado 3: decisão pendente registrada no README e nas perguntas em aberto (sem mudar comportamento).
- Achados 4 e 5: informativos, sem ação. Alteração só de docs/comentário SQL: build e testes não reexecutados.
- Horas humanas: a informar pelo usuário (não inventadas).
- Estimativa do Plano B (implementação): 8,0 h humanas e 6,5 h de IA.
