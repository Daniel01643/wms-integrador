# Subpasta por pacote em Processados/Erro + limpeza por subpasta (Etapa 7c)

Cada pacote passa a ter sua própria subpasta `pacote_<yyyyMMdd_HHmmss>` (horário local) em `Processados` e `Erro`, com o mesmo identificador usado no nome do relatório. O identificador é calculado uma única vez na abertura do pacote, propagado pelo diário e pelos moves incrementais, e reusado na retomada após queda. A limpeza de 10 dias passa a remover a subpasta de pacote vencida por inteiro (idade pela maior `LastWriteTimeUtc` do conteúdo), mantendo as proteções existentes e tratando arquivos soltos legados por arquivo. A revisão foi feita contra o commit de implementação `95c0098` (diff `HEAD~1..HEAD`), a tarefa autoritativa e o plano `plan-etapa7c.md`; a evidência de build/test está em `verificacao-etapa7c.md`.

Watch for: `docs/apresentacao/deck.html` aparece como modificado no working tree mas NÃO faz parte do commit de implementação — fora do escopo deste passo, não bloqueia (confirmed).

**Verdict**: APPROVED

## High-level view

O identificador do pacote nasce de uma única fonte: em `GerenciadorPacote`, no instante Ocioso→Coletando, `_idPacote = IdDe(agora)` usa exatamente o mesmo `agora` atribuído a `_inicio`. Esse mesmo id é passado ao `ProcessadorLote` (que o grava em cada `RegistroDiario` e o repassa ao move) e ao `FechadorPacote` (que o usa como nome do relatório). Não há dois carimbos independentes, então não existe o risco de 1s de diferença entre subpasta e relatório.

O `OrganizadorArquivos.Mover` ganhou um parâmetro obrigatório de id e compõe `Processados\<id>\` e `Erro\<id>\<sufixo>\`, com o sufixo seguindo as três modalidades de `OrganizacaoErro` (categoria/arquivo/plana) aninhadas sob a subpasta do pacote. Um `SanitizarId` defensivo remove caracteres inválidos de nome de pasta.

A retomada restaura `_idPacote` do primeiro registro do diário que o traga e cai num fallback calculado de `_inicio` para diários antigos sem o campo; `ReconciliarMove(registro, idFallback)` prioriza o id do próprio registro. Moves pré-queda e reconciliados pós-queda caem na mesma subpasta, sem reenvio ao WMS.

A limpeza opera por subpasta imediata de cada raiz: calcula a idade pela maior `LastWriteTimeUtc` do conteúdo e remove a subpasta inteira só quando toda ela está vencida. Todas as guardas originais (`Validar`, `EstaDentro`, `EhReparsePoint`, nunca a raiz, nunca Entrada/Estado/Relatórios) foram preservadas, e arquivos soltos legados na raiz continuam tratados por arquivo.

As restrições negativas da tarefa foram respeitadas: o commit toca apenas Pacote + testes + `OPERACAO.md`; Orquestrador, cliente WMS, flag I60 e a formatação de horário local não foram alterados, e `docs/diario`/`docs/controle` não foram tocados.

<details>
<summary>Issues (1)</summary>

1. **deck.html fora do escopo (não bloqueante)** — `docs/apresentacao/deck.html` está modificado no working tree porém não pertence ao commit de implementação `95c0098`; confirmar/descartar essa mudança em separado, não faz parte desta etapa.

</details>

<details>
<summary>Details</summary>

### Identificador único por pacote, calculado na abertura

A fonte do carimbo é `GerenciadorPacote`: no instante Ocioso→Coletando, `_inicio = agora` e `_idPacote = IdDe(agora)` partem do mesmo `agora`. `IdDe` é `"pacote_" + instante.ToLocalTime().ToString("yyyyMMdd_HHmmss", InvariantCulture)` — mesmo formato e mesma conversão de horário local do commit anterior (f29be7e), sem alteração na formatação (confirmed).

O ponto que a tarefa exige verificar — id idêntico entre subpastas e relatório, sem risco de 1s de diferença — está resolvido na origem: o relatório deixou de derivar o nome de `fim` e passa a receber o id do gerenciador. Em `FechadorPacote.FecharAsync`, `nomeBase = string.IsNullOrWhiteSpace(identificadorPacote) ? "pacote_" + carimbo(fim) : identificadorPacote`, e o nome do PDF anexo segue a mesma regra. Como o `GerenciadorPacote` sempre passa `_idPacote`, o caminho de `fim` só vale para chamadas legadas sem id (confirmed). `inicio`/`fim`/`periodo` continuam alimentando cabeçalho e corpo de e-mail (confirmed).

```
GerenciadorPacote (abre) ── _idPacote = IdDe(agora) ─┐
                                                     ├─ ProcessarAsync(foto, _idPacote) ─ RegistroDiario.IdentificadorPacote ─ Mover(...,id)
                                                     └─ FecharAsync(..., _idPacote) ───── nome do relatório/PDF
```

O teste `NomeDaSubpasta_IgualAoNomeBaseDoRelatorio` compara `Path.GetFileNameWithoutExtension(relatorio)` com `Path.GetFileName(subpasta)`, provando a identidade pela observação externa, e `DoisPacotes_GeramSubpastasDistintas` confirma o pareamento em dois pacotes (confirmed).

### Composição dos caminhos e as 3 modalidades de Erro

`OrganizadorArquivos.PastaDestino` compõe `Processados\<id>\` e, para Erro, `Erro\<id>\` seguido do sufixo por `OrganizacaoErro`: `<Categoria>` (PorCategoria), `<nome-sem-ext>` (PorArquivo) ou vazio (Plana) — a categoria/sufixo fica aninhada sob a subpasta do pacote, como exigido (confirmed). O `Mover` recebe o id como parâmetro obrigatório, o que força todos os chamadores a fornecê-lo e elimina o risco de um move cair solto na raiz por esquecimento.

O teste `Erro_FicaAninhadoSobASubpastaDoPacote_Nas3Modalidades` é um `[Theory]` com os 3 valores de `OrganizacaoErro`, verificando o caminho exato esperado em cada modalidade (confirmed).

### Retomada reconcilia na subpasta certa sem reenviar ao WMS

`Retomar` restaura `_idPacote = registros.Select(r => r.IdentificadorPacote).FirstOrDefault(id => !string.IsNullOrEmpty(id)) ?? IdDe(inicio)`, com `inicio = min(Horario)` quando há registros. `ReconciliarMove(registro, idFallback)` prioriza `registro.IdentificadorPacote` e só usa o fallback para diários antigos sem o campo (confirmed). Assim, arquivos movidos antes da queda e os reconciliados depois caem na mesma subpasta.

Três testes cobrem o requisito: reconciliação para `Processados\<idDoRegistro>\` com `OrquestradorFalso.Chamadas` vazio; move pré e pós-queda na mesma subpasta única; e diário antigo (id nulo) usando o fallback, também sem chamar o WMS (confirmed). O campo novo `IdentificadorPacote` entra ao fim do construtor posicional de `RegistroDiario` como `string? = null`, então linhas antigas do diário desserializam com null e acionam o fallback — compatível com a retomada (confirmed).

### Limpeza por subpasta com as guardas preservadas

`LimparRaiz` enumera apenas filhos imediatos de `Processados`/`Erro`: cada subpasta de pacote vai para `LimparSubpastaDePacote` e os arquivos soltos na raiz (layout antigo) para `RemoverArquivoSeVencido`. A idade da subpasta é o `Max(LastWriteTimeUtc)` dos arquivos recursivos; a remoção inteira (`Directory.Delete(sub, recursive: true)`) só ocorre quando `idadeMax < limite`, de modo que um pacote com qualquer arquivo ainda no prazo é preservado inteiro (confirmed).

As proteções do código base foram mantidas sem regressão: `Habilitada`, `DiasRetencao >= 0`, `_pastas.Validar()` antes de qualquer remoção, `EstaDentro(sub, raiz)` (prefixo `raiz + separador`, que a raiz em si nunca satisfaz), `EhReparsePoint` em subpasta e arquivos, e o escopo restrito a `CaminhoProcessados`/`CaminhoErro` — Entrada, Estado e Relatórios nunca são enumerados (confirmed). A subpasta vazia também é removida, o que limpa resíduos de categoria.

`LimpezaSubpastaTests` cobre a–g mais `RaizNuncaERemovida`: remoção integral por vencimento, preservação dentro do prazo, preservação por mistura (idade pelo máximo), subpasta de Erro com categoria aninhada, Entrada/Estado/Relatórios intactos, recusa de config perigosa, arquivo solto legado removido e raiz preservada (confirmed).

### Restrições negativas da tarefa

O commit `95c0098` toca 18 arquivos: `Pacote/*` (produção), testes e `docs/OPERACAO.md` mais os dois artefatos de tarefa. Orquestrador, construtores públicos, cliente WMS, flag I60 e a formatação de horário local não aparecem no diff (confirmed). As mudanças de assinatura são todas em métodos internos do Core (`Mover`, `ProcessarAsync`, `ReconciliarMove`, `FecharAsync`), não em construtores. `docs/diario` e `docs/controle` não foram tocados (confirmed).

`docs/apresentacao/deck.html` aparece como ` M` no `git status` mas o `git diff HEAD~1 HEAD` para esse arquivo é vazio — é uma modificação de working tree alheia ao commit de implementação, portanto fora do escopo autoritativo (o diff do último commit) e sem efeito no veredito (confirmed).

### Documentação

`OPERACAO.md` atualizou a seção 1 (tabela de pastas e as 3 modalidades agora aninhadas sob `pacote_<yyyyMMdd_HHmmss>`, mais o exemplo de árvore), a seção 2 (id calculado uma vez na abertura, usado em subpasta e relatório, reusado na retomada) e a seção 3 (limpeza por subpasta inteira pela maior data, com as proteções), conforme o item 10 do plano (confirmed).

### Evidência de build/test

`verificacao-etapa7c.md` registra `dotnet build` (0 avisos/0 erros) e `dotnet test` (0 falhas, 259 aprovados, 9 ignorados — os `CenariosStress` pré-existentes). O mapa requisito→teste da evidência bate com os arquivos de teste lidos nesta revisão. Build e suíte não foram reexecutados nesta revisão, conforme instruído (confirmed).

</details>

<details>
<summary>Mapa de arquivos</summary>

Produção (`src/KitWmsIntegrador.Core/Pacote/`):
- `OrganizadorArquivos.cs` — `Mover` recebe id obrigatório; compõe subpasta do pacote nas 3 modalidades; `SanitizarId`.
- `DiarioPacote.cs` — `RegistroDiario` ganha `string? IdentificadorPacote = null`.
- `ProcessadorLote.cs` — propaga id por `ProcessarAsync`/`ReconciliarMove`/`MovePendente`/moves.
- `GerenciadorPacote.cs` — `_idPacote` calculado uma vez na abertura; propagado e restaurado na retomada.
- `FechadorPacote.cs` — usa o id como nome do relatório/PDF (fallback por `fim` quando nulo).
- `LimpadorPastas.cs` — limpeza por subpasta de pacote inteira; soltos legados por arquivo; guardas preservadas.

Testes (`tests/KitWmsIntegrador.Tests/`):
- `SubpastaPorPacoteTests.cs` (novo) — casos i, ii (3 modalidades), iii, iv.
- `LimpezaSubpastaTests.cs` (novo) — caso iv (limpeza a–g + raiz).
- `RetomadaSubpastaTests.cs` (novo) — caso v (reconciliação, mesma subpasta, fallback legado).
- `OrganizadorArquivosTests.cs`, `PacoteEndToEndTests.cs`, `ProcessadorLoteTests.cs`, `LimpadorPastasTests.cs`, `RetomadaDeduplicacaoTests.cs`, `Infra/AmbientePacote.cs` — ajustes de layout/assinatura.

Docs:
- `docs/OPERACAO.md` — seções 1/2/3 + exemplo de layout.

Fora do escopo do commit: `docs/apresentacao/deck.html` (modificação de working tree, não commitada).

Diff completo: `git diff HEAD~1 HEAD`.

</details>
