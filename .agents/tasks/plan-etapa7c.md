# Plano — Etapa 7c: subpasta por pacote em Processados/Erro + limpeza por subpasta

Projeto KitWmsIntegrador (C#/.NET 8). Solução `c:\wms-integrador\KitWmsIntegrador.sln`.
Branch `main`, SEM worktree. Commits pequenos, só com arquivos nomeados (nunca `git add .`), sem push.
Comandos reais: `dotnet build c:/wms-integrador/KitWmsIntegrador.sln` e `dotnet test c:/wms-integrador/KitWmsIntegrador.sln`
(xUnit; a Etapa 7b fechou com a suíte verde; este plano mantém todos verdes + novos).

NÃO alterar: Orquestrador (decisões da Etapa 6), construtores, cliente WMS, flag I60 (`OpcoesI60.Habilitada=false`),
formatação de horário local já feita (commit f29be7e). A mudança é restrita à organização de pastas e à limpeza.
NÃO tocar em `docs/diario` nem `docs/controle`.

## Tarefa (resumo autoritativo)

Ao fechar um pacote, cada pacote deve ter sua PRÓPRIA SUBPASTA em Processados e em Erro, usando o MESMO
identificador do relatório do pacote (`pacote_<yyyyMMdd_HHmmss>` em horário LOCAL). Hoje os XMLs processados vão
SOLTOS para `Processados\` e os com erro para `Erro\<Categoria>\`; o identificador do relatório é calculado no
fechamento (`FechadorPacote`, a partir de `fim`). O movimento de arquivos é INCREMENTAL (cada nota é movida logo
após seu resultado, durante o processamento, ANTES do fechamento), então o identificador precisa existir ANTES de
mover o primeiro arquivo e ser idêntico ao do relatório. A limpeza de 10 dias passa a remover a subpasta de pacote
vencida por inteiro.

## O que foi lido (base das decisões)

- `Core\Pacote\OrganizadorArquivos.cs`: `Mover(arquivo, destino, categoria?, motivo)` monta o destino em
  `PastaDestino` usando `_opcoes.CaminhoProcessados` / `_opcoes.CaminhoErro` e, em Erro, aninha por
  `OrganizacaoErro` (PorCategoria → `Erro\<Categoria>`, PorArquivo → `Erro\<nome>`, Plana → `Erro`). Carimba
  `LastWriteTimeUtc` com `IClock` (base da limpeza). Nunca lança: devolve `ResultadoMovimento`. Grava sidecar
  `.motivo.txt` em Erro.
- `Core\Pacote\FechadorPacote.cs`: gera HTML+PDF com nome `pacote_<fim.ToLocalTime():yyyyMMdd_HHmmss>` (via
  `NomeUnico`), em `OpcoesRelatorio.PastaSaida`; nome do PDF anexo também derivado de `fim`. **Hoje o identificador
  nasce de `fim` (horário do fechamento).**
- `Core\Pacote\GerenciadorPacote.cs`: máquina Ocioso→Coletando→Processando→Fechando. `_inicio` é setado ao abrir o
  pacote (Ocioso→Coletando) = `agora` local do relógio. A foto é tirada ao fim da janela e processada por
  `ProcessadorLote.ProcessarAsync(foto, ct)`; `FecharAsync` chama `_fechador.FecharAsync(registros, inicio, fim, ct, periodo)`
  onde `inicio=_inicio` e `fim=_clock.UtcNow`. Retomada (`Retomar`) relê o diário, consolida, reconcilia moves e
  recalcula `_inicio` (hoje por `min(Horario)` ou `agora`).
- `Core\Pacote\ProcessadorLote.cs`: move cada arquivo logo após o resultado em `Finalizar` (streaming) chamando
  `_organizador.Mover(arquivo, destino, categoria, motivo)`. `ReconciliarMove(RegistroDiario)` e `RepetirMove`
  também chamam `Mover`. Registra no diário ANTES de mover. `_movePendentes` (ConcurrentDictionary) só em memória.
- `Core\Pacote\DiarioPacote.cs`: `RegistroDiario` (record) tem `Arquivo, XmlInvalido, NumeroNota, NumeroTransporte,
  Horario, Mensagem, Integrada, Tentativas, Status, IdTransacao, IdDocumento, ProdutosProvisorios, Historico,
  Categoria, Destino`. JSONL append+fsync; `Ler()` tolera linha truncada; `JsonStringEnumConverter`. Campo novo no
  record desserializa como default (null) para linhas antigas — compatível com retomada.
- `Core\Pacote\LimpadorPastas.cs`: `Limpar()` percorre recursivamente `CaminhoProcessados` e `CaminhoErro`, apaga
  arquivos com `LastWriteTimeUtc < limite` (por arquivo) e remove subpastas que ficaram vazias (nunca a raiz).
  Guardas de segurança: `_pastas.Validar()` recusa config perigosa; `EstaDentro` (prefixo + separador) impede apagar
  fora da raiz; `EhReparsePoint` ignora symlinks; nunca toca a Entrada nem `relatorios`.
- `Core\Configuracao\OpcoesPasta.cs`: `CaminhoProcessados/Erro/Estado` resolvidos contra a Entrada; `Validar(pastaRelatorios?)`
  já protege Estado e Relatórios de ficarem dentro de Processados/Erro. `OpcoesLimpeza.cs`: `Habilitada=true`,
  `DiasRetencao=10`.
- Testes existentes que fixam o LAYOUT de pastas (vão precisar de ajuste): `OrganizadorArquivosTests.cs`
  (esperam `Processados\a.xml`, `Erro\<Categoria>\a.xml`, etc.), `PacoteEndToEndTests.cs` (lê `_amb.Arquivos("Processados")`
  e `Arquivos(Path.Combine("Erro","ProdutoNaoCadastrado"))` — hoje usa `SearchOption.AllDirectories`, então encontra os
  arquivos mesmo com um nível a mais, MAS a asserção de igualdade exata do conjunto continua válida; a limpeza +11 dias
  precisa remover a subpasta inteira). `RelatorioRefinadoTests.cs` e `GeradorRelatorioTests.cs` tratam só do CONTEÚDO
  do relatório (não do nome do arquivo nem de pastas) → NÃO precisam de ajuste. `AmbientePacote.Arquivos(subpasta)`
  usa `SearchOption.AllDirectories`, então já varre as subpastas de pacote.
- `OpcoesPasta` não é alterada estruturalmente; os caminhos de pacote são compostos no `OrganizadorArquivos` a partir
  de `CaminhoProcessados`/`CaminhoErro` + o identificador recebido.

## Decisões (com justificativa — 1 a 2 frases cada)

- **D1. Quando calcular o identificador: na ABERTURA do pacote (Ocioso→Coletando), a partir de `_inicio` em horário
  local.** É o primeiro instante determinístico do pacote, existe antes de qualquer move incremental e é único por
  pacote. Formato idêntico ao atual: `"pacote_" + inicioLocal.ToString("yyyyMMdd_HHmmss")`. (Alternativa: usar o
  instante da foto; descartada porque a foto ocorre ~10 s depois da abertura e exigiria um 2º carimbo, sem ganho.)
  DECISÃO adicional: o relatório CONTINUA em `Relatorio:PastaSaida` (não dentro das subpastas de pacote); só o nome
  passa a vir do mesmo identificador, para o carimbo ser idêntico ao das subpastas.

- **D2. O identificador do relatório deixa de ser derivado de `fim` e passa a ser o identificador do pacote
  (recebido do gerenciador).** Resolve o descompasso: hoje o relatório usa `fim` e os moves aconteceriam antes de `fim`
  existir. Com o id vindo da abertura, subpastas e relatório têm exatamente o mesmo carimbo (sem risco de 1 s de
  diferença). O `CorpoEmailResumo`/cabeçalho do relatório continuam usando `inicio`/`fim`/`periodo` como hoje (datas
  humanas), só o NOME do arquivo e do PDF anexo passam a usar o identificador do pacote.

- **D3. Propagação do id:** `GerenciadorPacote` calcula `_idPacote` uma vez ao abrir; passa para
  `ProcessadorLote.ProcessarAsync(foto, _idPacote, ct)` (que o grava em cada `RegistroDiario` e o repassa ao
  `OrganizadorArquivos.Mover`) e para `FechadorPacote.FecharAsync(..., _idPacote)`.

- **D4. Composição dos caminhos (nas 3 modalidades, SEMPRE aninhadas sob a subpasta do pacote):**
  - Processados: `CaminhoProcessados\<id>\arquivo.xml`
  - Erro `PorCategoria`: `CaminhoErro\<id>\<Categoria>\arquivo.xml`
  - Erro `PorArquivo`: `CaminhoErro\<id>\<nome-sem-ext>\arquivo.xml`
  - Erro `Plana`: `CaminhoErro\<id>\arquivo.xml`
  O sidecar `.motivo.txt` acompanha o arquivo dentro da mesma subpasta (comportamento atual preservado). O id é
  sanitizado defensivamente (só o formato `pacote_<dígitos>_<dígitos>` é esperado; aplicar `string.Concat` removendo
  caracteres inválidos de nome de pasta como salvaguarda, embora o formato nunca os contenha).

- **D5. Retomada (reconciliação):** o id precisa sobreviver a uma queda para que os arquivos já movidos e os ainda a
  mover caiam na MESMA subpasta. Persistir o id no diário adicionando `IdentificadorPacote` ao `RegistroDiario`.
  Na retomada, `_idPacote` é restaurado do primeiro registro lido (que não seja nulo); se o diário for antigo/sem o
  campo, recalcular de `_inicio` como fallback (compatibilidade). `ReconciliarMove` passa `registro.IdentificadorPacote`
  (ou o `_idPacote` restaurado) ao `Mover`, garantindo a subpasta correta sem reenviar ao WMS. `_movePendentes` guarda
  o id junto (no record `MovePendente`) para que `RepetirMovesPendentes` use a subpasta certa.

- **D6. Limpeza por subpasta de pacote (não por arquivo solto):** a limpeza passa a operar no NÍVEL das subpastas
  imediatas de `Processados` e `Erro`. Para cada subpasta imediata (a subpasta de pacote), calcular a idade pela MAIOR
  (`Max`) `LastWriteTimeUtc` de todo o seu conteúdo (arquivos recursivos); se essa idade for maior que
  `DiasRetencao`, remover a subpasta INTEIRA (recursivo). Usar o máximo (e não o mínimo) evita apagar um pacote que
  contenha qualquer arquivo ainda dentro do prazo. Após remover os arquivos vencidos, remover diretórios de
  pacote/categoria que ficaram vazios. NUNCA apagar a raiz `Processados`/`Erro`; manter TODAS as guardas existentes
  (`Validar()`, `EstaDentro`, `EhReparsePoint`, nunca tocar Entrada/Estado/Relatórios). Retrocompatibilidade: arquivos
  SOLTOS diretamente na raiz (layout antigo, pré-7c) continuam sendo tratados por arquivo (idade individual), para que
  pacotes já gravados no formato antigo também sejam limpos.

- **D7. Não mexer na estrutura de `OpcoesPasta`** (sem nova opção de configuração): o identificador é um dado de
  runtime do pacote, não uma configuração. As modalidades de `OrganizacaoErro` permanecem as mesmas, só passam a ser
  aninhadas sob a subpasta do pacote.

## Plano ordenado (checklist)

- [ ] 1. **Adicionar o identificador do pacote ao `OrganizadorArquivos.Mover`.**
      Em `Core\Pacote\OrganizadorArquivos.cs`: acrescentar parâmetro `string identificadorPacote` a `Mover`
      (posição após `motivo`, ou sobrecarga — preferir parâmetro novo obrigatório e ajustar os chamadores no item 2).
      `PastaDestino` passa a compor: Processados → `Path.Combine(CaminhoProcessados, id)`; Erro → `Path.Combine(CaminhoErro, id, <sufixo por OrganizacaoErro>)`
      onde o sufixo é `<Categoria>` (PorCategoria), `<nome-sem-ext>` (PorArquivo) ou vazio (Plana). Adicionar helper
      privado `SanitizarId(string)` que remove `Path.GetInvalidFileNameChars()` (salvaguarda). Atualizar o doc-comment
      da classe/método para refletir a subpasta por pacote.
      Files: `src\KitWmsIntegrador.Core\Pacote\OrganizadorArquivos.cs`
      Verify: `dotnet build c:/wms-integrador/KitWmsIntegrador.sln` compila (os chamadores quebram até o item 2; se
      necessário, fazer itens 1+2 no mesmo passo de build). O build só precisa ficar verde ao fim do item 2.

- [ ] 2. **Propagar o id pelo `ProcessadorLote` e persistir no diário.**
      Em `Core\Pacote\DiarioPacote.cs`: adicionar `string? IdentificadorPacote` ao record `RegistroDiario` (no fim do
      construtor posicional; linhas antigas sem o campo desserializam como null — tolerado). Em
      `Core\Pacote\ProcessadorLote.cs`: `ProcessarAsync` ganha parâmetro `string identificadorPacote`; `Registro(...)`
      passa o id ao criar o `RegistroDiario`; `Finalizar`, `RepetirMove` e `ReconciliarMove` passam o id ao
      `OrganizadorArquivos.Mover`; o record interno `MovePendente` ganha o campo `IdentificadorPacote` para que
      `RepetirMovesPendentes` use a subpasta certa; `ReconciliarMove(RegistroDiario)` usa
      `registro.IdentificadorPacote` (fallback para um id passado pelo gerenciador quando o registro for antigo/nulo —
      ver item 3; manter a assinatura compatível adicionando uma sobrecarga `ReconciliarMove(registro, idFallback)`).
      Files: `src\KitWmsIntegrador.Core\Pacote\DiarioPacote.cs`, `src\KitWmsIntegrador.Core\Pacote\ProcessadorLote.cs`
      Verify: `dotnet build c:/wms-integrador/KitWmsIntegrador.sln` 0 erros/0 avisos novos.

- [ ] 3. **Calcular e propagar o id no `GerenciadorPacote`.**
      Em `Core\Pacote\GerenciadorPacote.cs`: novo campo `private string? _idPacote;`. Ao abrir o pacote
      (Ocioso→Coletando, onde `_inicio = agora`), calcular `_idPacote = "pacote_" + _inicio.Value.ToLocalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)`.
      Passar `_idPacote` em `ProcessarAsync(foto, _idPacote!, ct)` e em `FecharAsync` ao chamar
      `_fechador.FecharAsync(registros, inicio, fim, ct, _periodo, _idPacote!)`. Limpar `_idPacote = null` ao fechar o
      pacote (junto de `_inicio`/`_periodo`). Em `Retomar`: após consolidar, restaurar
      `_idPacote = registros.Select(r => r.IdentificadorPacote).FirstOrDefault(id => !string.IsNullOrEmpty(id))`;
      se nulo, recalcular de `_inicio` (fallback). Garantir que `_idPacote` esteja definido antes de qualquer
      `ReconciliarMove`/`ProcessarAsync` na retomada; passar o fallback para `ReconciliarMove`.
      Files: `src\KitWmsIntegrador.Core\Pacote\GerenciadorPacote.cs`
      Verify: `dotnet build c:/wms-integrador/KitWmsIntegrador.sln` 0/0. (A suíte pode ter testes de layout falhando até
      o item 5; não commitar antes do item 6.)

- [ ] 4. **Ajustar o `FechadorPacote` para usar o id recebido como nome do relatório.**
      Em `Core\Pacote\FechadorPacote.cs`: `FecharAsync` ganha parâmetro `string? identificadorPacote = null` (ao final,
      compatível). Se informado, usar `identificadorPacote` como base do nome (em vez de `"pacote_" + carimbo(fim)`) no
      `NomeUnico` e no `nomePdf` anexo; se nulo (chamada legada/sem id), manter o comportamento atual derivado de `fim`
      (retrocompatibilidade). `inicio`/`fim`/`periodo` continuam alimentando o `CorpoEmailResumo` e o cabeçalho do
      relatório como hoje. Atualizar o doc-comment.
      Files: `src\KitWmsIntegrador.Core\Pacote\FechadorPacote.cs`
      Verify: `dotnet build c:/wms-integrador/KitWmsIntegrador.sln` 0/0.

- [ ] 5. **Limpeza por subpasta de pacote.**
      Em `Core\Pacote\LimpadorPastas.cs`: reescrever a varredura para operar no nível das subpastas imediatas de cada
      raiz (`Processados`, `Erro`). Para cada subpasta imediata: calcular `idadeMax = Max(LastWriteTimeUtc)` dos
      arquivos recursivos (via `Directory.EnumerateFiles(sub, "*", AllDirectories)`, ignorando reparse points); se
      `idadeMax < limite`, apagar a subpasta inteira (`Directory.Delete(sub, recursive: true)`), contando os arquivos
      removidos. Arquivos SOLTOS diretamente na raiz (layout antigo) continuam tratados por arquivo (reaproveitar a
      lógica atual de idade individual). Preservar TODAS as guardas: `Habilitada`, `DiasRetencao>=0`, `Validar()`,
      `EstaDentro` (a subpasta precisa estar dentro da raiz), `EhReparsePoint`, nunca apagar a raiz em si. Logar cada
      subpasta removida (caminho + idade) e o total. Devolver `ResultadoLimpeza` com `ArquivosRemovidos`/`PastasRemovidas`.
      Files: `src\KitWmsIntegrador.Core\Pacote\LimpadorPastas.cs`
      Verify: `dotnet build c:/wms-integrador/KitWmsIntegrador.sln` 0/0; testes do item 8 (limpeza) passam.

- [ ] 6. **Ajustar os testes existentes que fixam o layout de pastas.**
      - `tests\KitWmsIntegrador.Tests\OrganizadorArquivosTests.cs`: `Criar()`/`Mover` passam um id de pacote fixo (ex.:
        `"pacote_20250601_120000"`); as asserções de caminho passam a esperar a subpasta do pacote: Processados →
        `Processados\pacote_.../a.xml`; Erro PorCategoria → `Erro\pacote_...\ProdutoNaoCadastrado\a.xml`; PorArquivo →
        `Erro\pacote_...\a\a.xml`; Plana → `Erro\pacote_...\a.xml`; colisão de nome idem dentro da subpasta.
      - `tests\KitWmsIntegrador.Tests\PacoteEndToEndTests.cs`: as asserções de `Arquivos("Processados")` e
        `Arquivos(Path.Combine("Erro","ProdutoNaoCadastrado"))` continuam válidas (varredura recursiva encontra os
        arquivos dentro da subpasta de pacote); acrescentar uma asserção de que existe UMA subpasta de pacote em
        `Processados` cujo nome bate com o nome-base do relatório HTML gerado (`pacote_<id>`), confirmando o requisito
        (iii). Garantir que a limpeza +11 dias esvazia `Processados`/`Erro` (remoção da subpasta inteira).
      - Verificar se `RelatorioRefinadoTests.cs`/`GeradorRelatorioTests.cs` precisam de algo — NÃO devem (tratam só do
        conteúdo do relatório). Deixar como estão.
      Files: `tests\KitWmsIntegrador.Tests\OrganizadorArquivosTests.cs`, `tests\KitWmsIntegrador.Tests\PacoteEndToEndTests.cs`
      Verify: `dotnet test c:/wms-integrador/KitWmsIntegrador.sln` — os testes adaptados verdes (demais inalterados).

- [ ] 7. **Testes novos de layout por pacote e de identificador compartilhado.**
      Criar `tests\KitWmsIntegrador.Tests\SubpastaPorPacoteTests.cs` (usa `AmbientePacote`, `OrquestradorFalso`,
      `FakeClock`):
      - (i) um pacote com 2 integradas → ambos em `Processados\<id>\` (um único `<id>` de subpasta), entrada vazia.
      - (ii) erro nas 3 modalidades de `OrganizacaoErro` (rodar o gerenciador 3x com `_amb.Pastas.OrganizacaoErro`
        diferente, ou testar direto o `OrganizadorArquivos` com os 3 valores) → arquivos em
        `Erro\<id>\<Categoria>\`, `Erro\<id>\<nome>\` e `Erro\<id>\` respectivamente.
      - (iii) o nome da subpasta de pacote (em Processados) é IGUAL ao nome-base do relatório gerado do mesmo pacote
        (comparar `Path.GetFileName(subpasta)` com `Path.GetFileNameWithoutExtension(relatorioHtml)`), provando o mesmo
        identificador.
      - (iv) dois pacotes consecutivos → duas subpastas distintas, cada uma com o nome do seu relatório.
      Files: `tests\KitWmsIntegrador.Tests\SubpastaPorPacoteTests.cs` (novo)
      Verify: `dotnet test c:/wms-integrador/KitWmsIntegrador.sln --filter SubpastaPorPacote` — todos verdes.

- [ ] 8. **Testes novos de limpeza por subpasta e de retomada reconciliando na subpasta.**
      - Criar `tests\KitWmsIntegrador.Tests\LimpezaSubpastaTests.cs` (temp dir + `FakeClock` + `File.SetLastWriteTimeUtc`):
        (a) subpasta `Processados\pacote_antigo\` com todos os arquivos >10 dias → removida inteira; (b) subpasta
        `Processados\pacote_novo\` com arquivos ≤10 dias → preservada; (c) subpasta com MISTURA (um arquivo velho + um
        recente) → PRESERVADA (idade pelo máximo); (d) subpasta de erro `Erro\pacote_antigo\ProdutoNaoCadastrado\`
        vencida → removida com a categoria dentro; (e) NÃO toca Entrada/Estado/Relatórios (montar essas pastas com
        arquivos antigos e conferir que seguem lá); (f) recusa config perigosa (Processados==Entrada); (g) arquivo
        SOLTO antigo na raiz (layout antigo) ainda é removido.
      - Estender `tests\KitWmsIntegrador.Tests\RetomadaDeduplicacaoTests.cs` (ou criar
        `RetomadaSubpastaTests.cs`): após queda, um arquivo com registro no diário contendo `IdentificadorPacote`
        é reconciliado para `Processados\<idDoRegistro>\` (NÃO para a raiz) sem nenhuma chamada ao orquestrador
        (`OrquestradorFalso.Chamadas` vazio); e os arquivos movidos antes da queda + os reconciliados após a queda
        compartilham a MESMA subpasta.
      Files: `tests\KitWmsIntegrador.Tests\LimpezaSubpastaTests.cs` (novo), `tests\KitWmsIntegrador.Tests\RetomadaSubpastaTests.cs` (novo ou estender `RetomadaDeduplicacaoTests.cs`)
      Verify: `dotnet test c:/wms-integrador/KitWmsIntegrador.sln --filter "LimpezaSubpasta|RetomadaSubpasta|RetomadaDeduplicacao"` — todos verdes.

- [ ] 9. **Suíte completa verde.**
      Rodar a suíte inteira para garantir que nenhum teste de pacote/relatório/e2e quebrou com a mudança de layout.
      Files: (nenhum)
      Verify: `dotnet build c:/wms-integrador/KitWmsIntegrador.sln` 0 erros/0 avisos; `dotnet test c:/wms-integrador/KitWmsIntegrador.sln` — TODOS verdes.

- [ ] 10. **Atualizar `docs\OPERACAO.md` (seções 1, 2, 3 e o exemplo de layout).**
      - Seção 1 (Pastas): tabela e descrição passam a mostrar `Processados\pacote_<yyyyMMdd_HHmmss>\<arquivo>.xml` e
        `Erro\pacote_<yyyyMMdd_HHmmss>\<Categoria>\<arquivo>.xml`; as modalidades `PorCategoria|PorArquivo|Plana`
        passam a ser aninhadas sob a subpasta do pacote; registrar que o identificador da subpasta é o MESMO do nome
        do relatório.
      - Seção 2 (Ciclo do pacote): documentar que o identificador do pacote é calculado UMA vez na abertura do pacote
        (horário local) e é usado tanto nas subpastas quanto no nome do relatório; a retomada reusa o identificador do
        diário para reconciliar os moves na subpasta correta.
      - Seção 3 (Limpeza): a limpeza de 10 dias remove a SUBPASTA de pacote vencida por inteiro (idade pelo
        `LastWriteTimeUtc` mais recente da subpasta), mantendo as protecções (nunca Entrada/Estado/Relatórios, nunca a
        raiz Processados/Erro, recusa config perigosa).
      - Atualizar o exemplo de layout/configuração para refletir as subpastas por pacote.
      NÃO tocar `docs\diario` nem `docs\controle`.
      Files: `docs\OPERACAO.md`
      Verify: `dotnet build` e `dotnet test` verdes; `Select-String -Path docs\OPERACAO.md -Pattern 'pacote_<yyyyMMdd_HHmmss>'` encontra os novos caminhos nas seções 1 e 3.

- [ ] 11. **Commit(s) — só arquivos nomeados, sem push.**
      Sugestão: (1) `feat(pacote): subpasta por pacote em Processados/Erro com identificador compartilhado com o relatorio`
      (src + testes), (2) `feat(limpeza): remover subpasta de pacote vencida por inteiro`, (3) `docs: layout de pastas
      por pacote`. Agrupar como fizer sentido mantendo cada commit buildável e verde.
      Files: (os alterados acima)
      Verify: `dotnet test c:/wms-integrador/KitWmsIntegrador.sln` verde; `git status --short` sem pendências de src/tests/docs
      fora do commit.

## Testes (mapa do requisito → onde é coberto)

- (i) processados em `Processados\pacote_<id>\` → item 7 (i) e PacoteEndToEnd (item 6).
- (ii) erro em `Erro\pacote_<id>\<Categoria>\` nas 3 modalidades → item 7 (ii).
- (iii) id da subpasta == id do relatório do mesmo pacote → item 7 (iii) e asserção extra no e2e (item 6).
- (iv) limpeza remove subpasta vencida por inteiro e preserva as dentro do prazo, sem tocar Entrada/Estado/Relatórios
  → item 8 (LimpezaSubpasta a–g).
- (v) retomada após queda reconcilia moves para a subpasta correta sem reenviar ao WMS → item 8 (RetomadaSubpasta).

## Riscos / pontos de atenção

- Mudança de assinatura de `OrganizadorArquivos.Mover` e `ProcessadorLote.ProcessarAsync` é interna ao Core; o
  `AmbientePacote` e os testes constroem esses objetos diretamente, então basta ajustar os chamadores de teste
  listados. NÃO há mudança nos construtores públicos (proibido pela tarefa).
- `RegistroDiario` ganha um campo nullable ao final do construtor posicional: linhas antigas do diário (sem o campo)
  continuam desserializando (null) e o gerenciador recalcula o id de `_inicio` na retomada — compatível.
- `FechadorPacote.FecharAsync` recebe o id como parâmetro OPCIONAL ao final, preservando chamadas legadas (o
  `GerenciadorPacote` passa o id; qualquer teste que chame sem id mantém o comportamento por `fim`).
- Limpeza: usar o `LastWriteTimeUtc` MÁXIMO da subpasta (não o mínimo) é a decisão que evita apagar um pacote com
  qualquer arquivo ainda no prazo; confirmar nos testes do item 8 (c).

## Contrato do loop (mantido)

Este plano NÃO reestrutura o workflow. O loop existente de implementação+revisão o executa; a condição de parada
continua sendo o arquivo `c:\wms-integrador\.agents\tasks\review-etapa7c.json` com `jsonPath` `verdict` = `APPROVED`,
escrito pelo revisor como último passo do loop.
