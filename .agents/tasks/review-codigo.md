# Puxada TSP: pastas Puxada/PuxadaTSP/raiz, leitor de ZIP e onda `TSP-AAAAMMDD-NN-NOME`

A fase código+testes (8 commits locais, `42a60d4`..`6bdab9b`) adiciona as pastas `Puxada` e `PuxadaTSP` (mais a raiz como origem), um leitor de ZIP seguro, a categoria `ZipInvalido`, o nome da onda TSP com sequencial do dia e reaproveitamento por hash, e as colunas Tipo/Origem/Onda no relatório, e-mail e telemetria. A puxada normal segue agrupando por transporte; na TSP há uma onda por ZIP. A revisão leu o diff e `verificacao-codigo.md` (Debug e Release: 0 avisos/0 erros, 584 aprovados e 9 ignorados, base anterior 487). Nada foi reexecutado.

Watch for: o prefixo `TSP-` no nome da onda não aparece nas mensagens literais do usuário, só em `ESCOPO-PUXADA-TSP.md` e no plano como decisão de 12/11 (possível, confirmar com o usuário antes do homolog). Em `numeroPlanejamento` o valor passa a ser o nome da onda, premissa ainda não validada no WMS (confirmed, já registrada como premissa aberta). Os hooks são locais e não versionados (confirmed).

**Verdict**: APPROVED

## High-level view

Tipo e Origem vêm só da pasta onde o arquivo foi depositado: `PuxadaTSP` sobe como TSP, `Puxada` e raiz sobem como PUXADA, com a raiz marcada como Origem `raiz` para análise no relatório. Não há detecção de pasta errada, como o usuário pediu.

O ZIP é lido por um componente que usa só o nome do arquivo da entrada (nunca o caminho interno), recusa `..`, caminho absoluto, unidade, ZIP aninhado e não-XML, e limita quantidade e tamanho por declaração e por bytes copiados. Os XMLs extraídos seguem o mesmo caminho do XML solto e o ZIP é movido para o mesmo `Processados\pacote_*\` só depois de todos os XMLs tratados, o que preserva o layout `NFE<chave>.XML`.

O nome da onda cabe em 30 caracteres (parte fixa de 16, NOME em no máximo 14), o sequencial reinicia por dia em `Estado\tsp\ondas.json` e o mesmo conteúdo (SHA-256) reaproveita a onda sem consumir sequencial.

Repositório protegido: `.gitignore` com `*.zip`/`*.ZIP`, hooks locais bloqueando `.xml` e `.zip`, nenhum `.xml`/`.zip` versionado, nenhum NuGet novo, `Habilitada` intocado, sem valores monetários.

<details>
<summary>Issues (3)</summary>

1. **Prefixo `TSP-` fora das mensagens literais** — o formato do usuário é data+sequencial+resumo; o prefixo vem da decisão de 12/11 registrada no escopo. Confirmar com o usuário que a onda no WMS deve levar `TSP-` (afeta a leitura no WMS e os 14 caracteres de NOME). Não bloqueia (likely).
2. **`numeroPlanejamento` = nome da onda na TSP** — o `ConstrutorI50` repete o argumento; validar no homolog e registrar na fase docs. Não bloqueia (confirmed).
3. **Hooks locais** — `pre-commit`/`pre-push` não vão ao repositório; replicar na máquina de quem fizer commit e citar na documentação. O `.gitignore` é a proteção versionada (confirmed).

</details>

<details>
<summary>Details</summary>

## Onda `TSP-AAAAMMDD-NN-NOME` e registro do sequencial

`NomeadorOndaTsp` limpa o nome (extensão, apóstrofos, palavras XML/XMLS, acentos, só A-Z0-9 e hífen) e corta o NOME para caber em 30 caracteres, sem hífen final. `XML'S SOROCABA.zip` vira `SOROCABA`, e sem nome sobra `TSP-AAAAMMDD-NN`. `RegistroOndasTsp` grava de forma atômica (temporário + move), preserva um `ondas.json` corrompido como `.corrompido-*` e poda hashes com mais de 90 dias. A data usa o relógio local do servidor, como o escopo pede. A onda é criada antes do envio e reaproveitada pelo hash no reprocesso, então uma falha no meio não gasta outro sequencial.

## Prefixo `TSP-` e premissa de `numeroPlanejamento`

O prefixo está em `ESCOPO-PUXADA-TSP.md` (item 6 e seção do formato) como decisão de 12/11 e como constante pública. Como as mensagens literais do usuário falam apenas em "data+num_seq+resumo", vale confirmar a decisão, pois muda o texto que o WMS recebe. Na TSP o orquestrador recebe `IntegrarAsync([nota], nomeDaOnda)`, e o `ConstrutorI50` usa o mesmo valor em `numeroPlanejamento`, sem mudar a interface; o relatório mostra o transporte da própria nota (ou `(sem)`).

## Leitor de ZIP e movimentação preservando o layout

Os XMLs extraídos passam por `LerXml` e são movidos um a um; o ZIP vai para `Processados` ao final (move falho entra em `_movePendentes` e repete sem reenviar ao WMS). ZIP inválido gera registro `ZipInvalido` com destino `Erro`. O XML repetido por chave dentro de ZIP é registrado como `ArquivoIgnorado` e não vai ao WMS; XML solto repetido mantém o comportamento antigo. A pasta temporária é apagada no `finally`. Entradas ignoradas dentro do ZIP aparecem só no diário/relatório (caminho inseguro como `ZipInvalido`, demais como `ExtensaoInvalida`).

## Cobertura

Há 97 testes novos, com ZIPs e XMLs sintéticos em `Path.GetTempPath()`: nome da onda, sequencial e virada de dia, hash, ZIP corrompido/aninhado/caminho perigoso/limites/repetido, classificação por pasta (raiz aceita e recusada), agrupamento TSP x PUXADA (SJC 2/2, SOROCABA 5/4), relatório e e-mail com Tipo/Origem, compatibilidade de diário e telemetria antigos e ponta a ponta. Não testado nesta fase: ZIP real no homolog, que depende do usuário e consta como pendência.

</details>

<details>
<summary>File map</summary>

Config e appsettings (Worker e deploy), `OpcoesPasta`, `OpcoesZip`, `.gitignore`; Core/Pacote (`NomeadorOndaTsp`, `RegistroOndasTsp`, `LeitorZipSeguro`, `EntradaArquivo`, `ClassificadorEntrada`, `ProcessadorLote`, `GerenciadorPacote`, `DiarioPacote`, `CategoriaErro`); Core/Relatorio, Email e Monitoramento (Tipo/Origem/Onda); Worker (`Program`, `Worker`); 10 arquivos de teste novos ou ampliados. Diff completo: `git diff 42a60d4^ HEAD`.

</details>
