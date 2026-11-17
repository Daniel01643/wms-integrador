# Revisão: I51 com quantidade em embalagens

Diff revisado: `3251d40..HEAD` (commits 6136eaa e 0bfc28e). Veredito: APPROVED, sem bloqueantes.

## Evidência de verificação
`.agents/tasks/verificacao-i51-embalagem.md` registra build Release com 0 avisos e 0 erros, e testes com 1009 aprovados, 9 ignorados e 0 falhas (baseline 991/9, +18 testes). Não rodei build nem testes.

## Requisitos conferidos
- **Opção:** `I51:CampoQuantidadeSeparada` (Embalagem padrão | Produto) é validada em `OpcoesI51.Validar()`. Está em `appsettings.json` e em `deploy\config-exemplo\appsettings.Production.json`, com comentário do caso 5006566.
- **Fallback:** `SelecionadorQuantidadeI51` usa `qtdeEmbalagemSep`, depois a soma de `detalhes[].qtdeCaixaSep`, depois `qtdeProdutoSep`. O último caso gera Observacao na nota e aviso no log.
- **Parser:** `ParserI51` lê os dois campos novos como opcionais. Valor ausente, vazio ou não numérico vira null e não invalida o documento.
- **Casador:** `CasadorDocumentoNota` substitui `QtdeProdutoSep` pela quantidade escolhida antes de `CalculadorAjuste`. A comparação original × separada e a I60 passam a usar embalagens, então 18→180 não acontece. Com o mesmo dado, uma separação parcial (nota 11, emb 9) continua gerando ajuste 11→9.
- **Processador e construtor da I60:** `ProcessadorI60Separacao` e `ConstrutorI60` não foram alterados. Eles consomem `ajuste.Itens`, que já chega em embalagens, e os testes de `ProcessadorI60Separacao` cobrem o caso.
- **Órfãos:** guardam a quantidade já escolhida. Arquivos antigos, em unidades, são lidos como estão. Isso está documentado em `ModeloSeparacao.cs`, no escopo e no manual técnico.
- **Relatório I60:** a seção "Ajustes de quantidade" mostra o campo usado no HTML/e-mail e no PDF.
- **Testes:** os quatro cenários pedidos estão cobertos (conversão, parcial, emb ausente → soma qtdeCaixaSep, opção Produto), além de órfão, parser, validação e relatório.
- **Documentação:** `ESCOPO-I51-I60.md`, `OPERACAO.md` e `MANUAL-TECNICO.md` foram atualizados. HTML e PDF foram regenerados.
- **Arquivos proibidos:** o diff não contém `*.xml` nem `*.zip`, nem dados reais ou credenciais. `CONTROLE-ESFORCO.md`, `docs\controle\*` e `docs\diario\*` não foram alterados. Não há valores monetários nos textos adicionados.

## Observações (não bloqueantes)
- `OpcoesI51.cs`, `CasadorDocumentoNota.cs` e `ParserI51.cs` ganharam BOM UTF-8 na primeira linha. É só cosmético.
- O órfão não guarda se houve fallback para `qtdeProdutoSep`, então a Observacao do fallback não é gerada nesse caminho. Isso é um limite do formato com um único campo e não afeta a correção do valor.

## Achados bloqueantes
Nenhum.
