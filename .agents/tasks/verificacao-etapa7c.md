# Verificação — Etapa 7c (subpasta por pacote em Processados/Erro + limpeza por subpasta)

Primeira iteração (não existia `review-etapa7c.json`). Implementado conforme `plan-etapa7c.md`.

## Comandos executados (reais)

```
dotnet build c:/wms-integrador/KitWmsIntegrador.sln
dotnet test  c:/wms-integrador/KitWmsIntegrador.sln
```

## Resultado

- **Build**: `Compilação com êxito. 0 Aviso(s) 0 Erro(s)`.
- **Testes**: `Aprovado! – Com falha: 0, Aprovado: 259, Ignorado: 9, Total: 268`.
  - Os 9 ignorados são os cenários de stress (`CenariosStress`), que já eram `[Skip]` antes desta etapa.
  - Antes da etapa a suíte tinha 242 testes aprovados; foram adicionados 10 novos testes
    (SubpastaPorPacote: 6 — um `[Theory]` com 3 casos; LimpezaSubpasta: 9; RetomadaSubpasta: 3) e
    ajustados os existentes de layout, totalizando 259 aprovados + 9 skip.

## O que mudou (código de produção)

- `OrganizadorArquivos.Mover(... , string identificadorPacote)`: compõe a subpasta do pacote
  `Processados\<id>\` e `Erro\<id>\<sufixo-por-OrganizacaoErro>\` (PorCategoria|PorArquivo|Plana),
  com `SanitizarId` defensivo.
- `DiarioPacote.RegistroDiario`: novo campo opcional `IdentificadorPacote` (linhas antigas = null).
- `ProcessadorLote`: `ProcessarAsync(foto, identificadorPacote, ct)` propaga o id para o diário e para
  o move (streaming); `MovePendente` guarda o id; `ReconciliarMove(registro, idFallback)` usa o id do
  registro (ou o fallback) — sem reenviar ao WMS.
- `GerenciadorPacote`: calcula `_idPacote` UMA vez na abertura (horário local), propaga para
  `ProcessarAsync` e `FechadorPacote.FecharAsync`, e restaura o id do diário na retomada.
- `FechadorPacote.FecharAsync(..., identificadorPacote?)`: usa o id como nome do relatório HTML/PDF e
  do anexo (fallback para o comportamento antigo por `fim` quando nulo).
- `LimpadorPastas`: remove a subpasta de pacote vencida por INTEIRO (idade pela MAIOR
  `LastWriteTimeUtc`); arquivos soltos na raiz (layout antigo) ainda por arquivo; preserva todas as
  guardas (nunca a raiz, nunca Entrada/Estado/Relatórios, recusa config perigosa, ignora reparse).

## Testes (mapa requisito → cobertura)

- (i) processados em `Processados\pacote_<id>\` → `SubpastaPorPacoteTests.Processados_VaoParaSubpastaUnicaDoPacote` + `PacoteEndToEndTests`.
- (ii) erro em `Erro\pacote_<id>\<Categoria>\` nas 3 modalidades → `SubpastaPorPacoteTests.Erro_FicaAninhadoSobASubpastaDoPacote_Nas3Modalidades` (Theory) + `OrganizadorArquivosTests`.
- (iii) id da subpasta == id do relatório → `SubpastaPorPacoteTests.NomeDaSubpasta_IgualAoNomeBaseDoRelatorio` + `DoisPacotes_GeramSubpastasDistintas` + asserção no `PacoteEndToEndTests`.
- (iv) limpeza remove subpasta vencida por inteiro e preserva as dentro do prazo sem tocar Entrada/Estado/Relatórios → `LimpezaSubpastaTests` (a–g + raiz).
- (v) retomada reconcilia moves na subpasta sem reenviar ao WMS → `RetomadaSubpastaTests` (3 casos, incluindo fallback de diário antigo).

## Exemplo real de árvore (copiado de execução de teste)

Pacote com dois arquivos (`a` integrada; `b` com produto não cadastrado, `OrganizacaoErro=PorCategoria`),
`FakeClock` em 2025-01-10 12:00:00Z → id local `pacote_20250110_090000`:

```text
Processados\pacote_20250110_090000\a.xml
Erro\pacote_20250110_090000\Outro\b.xml
Erro\pacote_20250110_090000\Outro\b.xml.motivo.txt
relatorios\pacote_20250110_090000.html
relatorios\pacote_20250110_090000.pdf
```

A subpasta em `Processados`, a subpasta em `Erro` e o nome do relatório compartilham exatamente o mesmo
identificador `pacote_20250110_090000`.
