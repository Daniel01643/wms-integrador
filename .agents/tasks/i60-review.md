# Revisão I60 Plano A (segunda passada)

Alterações revisadas: commits locais de 0077629..HEAD (6d1e536) em main e o relatório `.agents/tasks/i60-plano-a.md`. Os três achados da primeira passada (query de conferência, marca 'CONFIRMAR NO HOMOLOG', decisão de soma zero) foram tratados. Nenhum build ou teste foi reexecutado. Foram feitos apenas greps de verificação.

Atenção: o relatório registra `dotnet build` limpo (0 avisos, 0 erros) e `dotnet test` com 367 aprovados, 9 ignorados e 0 falhas. Isso é mais do que o mínimo aceito (testes compilam + build limpo). A última iteração (docs e comentário SQL) declara que build e testes não foram reexecutados, o que é honesto e coerente: só mudaram docs.

Watch for: soma de caixas com fração gera divergência I60 x DocCarga (confirmado, documentado); `INV` sem evidência (confirmado, pergunta registrada); PL/SQL aborta importação com soma zero (confirmado, pendente de decisão do usuário, não bloqueante).

**Verdict**: APPROVED

## Visão geral

O PL/SQL altera apenas `solwms.i_doccarga.qtd_volumes` com a soma de `NVL(qtd_informada_cxs,0)` dos itens, sem divisão por embalagem. Usa `:pIdDoc1/2/3` e é idempotente, porque recalcula e sobrescreve. Não toca onda, itens, observação nem atributos. Tem cabeçalho, e a linha do `SET` leva a marca 'CONFIRMAR NO HOMOLOG'. O README traz a query de conferência somente leitura.

O `ConstrutorI60` calcula `qtdeVolume` pela soma de `Itens[].Quantidade` e não usa `qVol`. A regra de arredondamento (inteiro mais próximo, `AwayFromZero`) está no código, no DTO e no relatório, com a ressalva de divergência. Os testes cobrem vários itens e `qVol` divergente. `TipoOperacaoNota` é configurável em `OpcoesI60` (default INE, coerente com `TEMPLATES-INTERFACES.md` linhas 144 e 150). O relatório cita arquivo e linha, não adivinha `INV` e registra a pergunta em aberto.

A I60 segue com `Habilitada=false` no `appsettings.json` e no molde de produção, e `I60__Habilitada=true` está documentada. A retenção de 60 dias aparece em `OpcoesLimpeza`, `appsettings.json`, molde de produção e docs. Não restam "10 dias" de retenção em ARQUITETURA, OPERACAO e INSTALACAO-SERVIDOR. Os testes foram ajustados (59 preserva, 61 remove). O Plano B está só em documento (nenhum código), com mapeamento, componentes e recomendação justificada, reuso, riscos, perguntas e estimativa em horas. ARQUITETURA, ENCERRAMENTO e diário foram atualizados, e as horas humanas estão como 'a informar pelo usuário'. O roteiro de homolog tem as seções 0 a 6.

Os transversais estão ok. Não há valor monetário adicionado (o único acerto no grep foi "customizado", falso positivo). Não há segredo impresso, nenhum envio ao WMS e nenhum push. Os commits são pequenos e descritivos.

<details>
<summary>Issues (3)</summary>

1. **Soma zero/sem itens aborta a importação** (confirmed, não bloqueante) — `QTDVOL-PRE:2` levanta erro e bloqueia o DocCarga. Confirmar com o usuário que isso é desejado ou trocar por aviso.
2. **`INV` sem evidência** (confirmed, não bloqueante) — default INE mantido e configurável. Confirmar com o fornecedor ou testar `I60__TipoOperacaoNota=INV` em homolog.
3. **Fração na soma** (confirmed, não bloqueante) — a I60 arredonda e o PL/SQL não, então podem divergir. Caso incomum com nota em caixas, já documentado.

</details>

<details>
<summary>Details</summary>

## Divergência de arredondamento entre I60 e DocCarga

`CalcularQtdeVolume` arredonda a soma para inteiro (meio para longe de zero), e o PL/SQL grava a soma crua. Se alguma nota tiver caixa fracionada, o campo da I60 não será igual ao do DocCarga. O comentário em `ConstrutorI60` e o relatório documentam isso. Não é bloqueante, porque `qtd_informada_cxs` vem em caixas inteiras.

## Aborto da importação no PL/SQL

O bloco lança `raise_application_error(-20200)` quando não há itens ou a soma é menor ou igual a zero. É conservador (falha rápido), mas pode barrar uma nota legítima. A decisão está registrada no README e nas perguntas em aberto.

## Verificação possível

Não há como confirmar nesta passada que os testes rodaram. A evidência é o registro do coder. O relatório não alega verificação de homolog, e o PL/SQL e o nome da coluna continuam como 'CONFIRMAR NO HOMOLOG'.

</details>

<details>
<summary>Mapa de arquivos</summary>

Código: `OpcoesI60.cs`, `OpcoesLimpeza.cs`, `ConstrutorI60.cs`, `PayloadI60.cs`. Config: `appsettings.json` do Worker e `appsettings.Production.json` de exemplo. Testes: `ConstrutorI60Tests`, `OpcoesI60Tests`, `LimpadorPastasTests`, `LimpezaSubpastaTests`, `PacoteEndToEndTests`. Docs: `wms-codigo-customizado` (SQL, README, ROTEIRO), `ARQUITETURA-PLANO-B-I40.md`, `ARQUITETURA.md`, `ENCERRAMENTO.md`, `CONTROLE-ESFORCO.md`, diário, manuais, OPERACAO e INSTALACAO-SERVIDOR. Diff completo: `git diff 0077629 HEAD`.

</details>
