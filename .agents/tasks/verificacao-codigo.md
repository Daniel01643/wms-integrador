# Verificação da fase código + testes (Puxada TSP)

Branch `main`, commits locais (sem push). Nada enviado ao WMS, SRVAPPALC02 não acessado, nenhum segredo impresso, nenhum NuGet novo.

## Resultados

| Comando | Resultado |
|---|---|
| `dotnet build KitWmsIntegrador.sln -c Debug` | 0 avisos, 0 erros |
| `dotnet build KitWmsIntegrador.sln -c Release` | 0 avisos, 0 erros |
| `dotnet test KitWmsIntegrador.sln -c Debug` | 584 aprovados, 9 ignorados, 0 falhas |
| `dotnet test KitWmsIntegrador.sln -c Release` | 584 aprovados, 9 ignorados, 0 falhas |

Base anterior: 487 aprovados + 9 ignorados (todos seguem verdes). Novos: 97 testes.

## Commits (ordem)

1. `chore: ignora *.zip` (`.gitignore`: `*.zip` e `*.ZIP`)
2. `feat(config)`: `Pastas:Puxada`, `Pastas:PuxadaTsp`, `Pastas:AceitarRaiz` (padrão true), `Zip:MaxArquivos` (1000) e `Zip:MaxTamanhoMb` (500), validação, appsettings (Worker e deploy), o Worker cria `Puxada` e `PuxadaTSP`
3. `feat(modelo)`: `Tipo`, `Origem`, `Onda`, `Zip` no `RegistroDiario` e na `LinhaRelatorioErro` (campos novos no fim, com padrão; diário antigo lê normal)
4. `feat(tsp)`: `NomeadorOndaTsp` e `RegistroOndasTsp` (`Estado\tsp\ondas.json`)
5. `feat(zip)`: `LeitorZipSeguro` e `CategoriaErro.ZipInvalido` (acrescentada no fim do enum)
6. `feat(puxada)`: classificação por pasta, ZIP no `ProcessadorLote`, onda única por ZIP na TSP
7. `feat(relatorio)`: Tipo/Origem/onda no HTML, PDF, e-mail de fechamento, telemetria e resumo semanal
8. `test(tsp)`: ponta a ponta

## Hooks e .gitignore (tarefa 1)

- `.gitignore` tem `*.xml`, `*.XML`, `*.zip`, `*.ZIP`. `git check-ignore -v teste.zip` aponta a regra.
- Hooks `.git/hooks/pre-commit` e `pre-push` (não versionados, alterados só nesta máquina) agora filtram `\.(xml|zip)$` (LF, mensagem "arquivo XML/ZIP").
- Teste do hook em repositório temporário descartável: `git add -f a.zip` + `git commit` foi bloqueado (saída 1) com a mensagem esperada; a pasta foi apagada.
- `git ls-files` não lista nenhum `.xml` nem `.zip`.

## Decisões de comportamento (conferir)

- Tipo e Origem vêm só da pasta: `PuxadaTSP` sobe como TSP; `Puxada` e raiz sobem como PUXADA. Sem detecção de pasta errada.
- Raiz (`AceitarRaiz=true`): XML e ZIP soltos continuam PUXADA normal, Origem `raiz`. Com `AceitarRaiz=false` ficam parados na entrada.
- PuxadaTSP aceita só ZIP; qualquer outro arquivo definitivo vira `ExtensaoInvalida` com motivo "a pasta PuxadaTSP aceita apenas ZIP".
- Nome da onda TSP: `TSP-AAAAMMDD-NN-NOME`. O prefixo `TSP-` vem da decisão de 12/11 em `docs/ESCOPO-PUXADA-TSP.md` (seção 2, item 6 e seção do formato). A parte fixa tem 16 caracteres, então o NOME é cortado em 14 e o total nunca passa de 30 (limite do `nroagrupseparacao`). Sem NOME: `TSP-AAAAMMDD-NN` (15). Sequencial >99 usa 3 dígitos e o NOME encolhe.
- `XML'S SOROCABA.zip` → `TSP-<data>-01-SOROCABA`; `XML'S SJC.zip` → `TSP-<data>-02-SJC` (cobertos em teste).
- Reprocesso: mesmo conteúdo (SHA-256 do ZIP) reaproveita a onda sem consumir sequencial; mesmo nome com conteúdo diferente ganha novo NN; virada de dia reinicia em 01; `ondas.json` corrompido não derruba (preserva o antigo como `.corrompido-*`).
- Chamada ao orquestrador na TSP: `IntegrarAsync([nota], nomeDaOnda)`, 1 DocCarga por nota. O `ConstrutorI50` repete o valor também em `numeroPlanejamento` (premissa a validar no homolog).
- Nota TSP sem transporte sobe normalmente; no relatório a coluna Transporte mostra `(sem)`.
- Puxada normal inalterada: onda = número do transporte; nota sem transporte continua pendência.
- Layout preservado: XMLs vão para `Processados\pacote_*\NFE<chave>.XML` (a busca da devolução I40 achou o XML em teste); o `.zip` vai para a mesma pasta depois que todos os XMLs dele foram tratados; ZIP inválido vai para `Erro\pacote_*\ZipInvalido\` com `.motivo.txt`. `Estado\tmp\<guid>` é apagado no `finally`.
- Leitor de ZIP: só usa o NOME do arquivo (descarta o caminho interno), recusa `..`, caminho absoluto, unidade, `\\servidor`; ZIP aninhado e não-XML viram `ExtensaoInvalida` (sem abrir); caminho perigoso vira `ZipInvalido` só no relatório; limites por tamanho declarado e por bytes copiados. Observação: o .NET já corta a leitura no tamanho declarado, então o teste da "bomba que mente" confirma que nunca se grava mais que o limite (a contagem de bytes é a segunda barreira).
- XML repetido (mesma chave) vindo de ZIP na mesma foto: enviado uma vez; o repetido vira registro `ArquivoIgnorado` em Processados, com status "Arquivo ignorado (XML repetido)". XML solto repetido mantém o comportamento antigo (o WMS barra).
- Relatório/e-mail: colunas Tipo e Origem; TSP agrupada por onda ("Onda ... (TSP) — N notas · M transportes"); PUXADA primeiro, TSP depois; etiqueta discreta `raiz` só nas notas da raiz; resumo com "Por tipo", "Notas da raiz" e ondas; telemetria e resumo semanal ganharam `NotasPuxada`, `NotasTsp`, `NotasRaiz` (linhas antigas leem 0).
- `Habilitada` não foi alterado (I60 e Devolução seguem `false`). Nenhum valor monetário adicionado.

## Cobertura nova (arquivos de teste)

`OpcoesZipTests`, `OpcoesPastaValidacaoTests` (ampliado), `DiarioPacoteTests` (ampliado), `ClassificarPastasTests`, `NomeadorOndaTspTests`, `RegistroOndasTspTests`, `LeitorZipSeguroTests`, `ProcessadorLoteTspTests`, `RelatorioTipoOrigemTests`, `PacoteTspEndToEndTests`.

Todos os ZIPs/XMLs dos testes são sintéticos, gerados em `Path.GetTempPath()` com `System.IO.Compression` (emitente BRF CNPJ 01.838.723/0098-50, série 6, `Transporte: NNNNNNNNNN` no `infCpl`) e apagados no `Dispose`. Os `Recursos\*.XML` não são lidos.

Checklist do plano: nome da onda (`XML'S SOROCABA.zip`/`XML'S SJC.zip`, prefixo, corte em 14, vazio, acentos) | sequencial e virada de dia | reprocesso por hash | ZIP inválido | não-XML | caminho perigoso | aninhado | repetido em dois ZIPs | tipo por pasta | raiz aceita/recusada | agrupamento TSP x PUXADA (SJC 2/2, SOROCABA 5/4, uma onda por ZIP) | relatório/e-mail com Tipo e Origem | compatibilidade de diário e telemetria antigos. Todos cobertos.

## Pontos para o revisor

- O hook é local (não versionado); o `.gitignore` é o que vai ao repositório.
- Não foi feita a fase de docs (tarefas 10 a 13 do plano), conforme o escopo desta etapa.
- O arquivo `docs/ESCOPO-PUXADA-TSP.md` e `.agents/tasks/plano-puxada-tsp.md` aparecem como alterado/não rastreado no `git status` por trabalho anterior ao código; não foram tocados nesta fase.
