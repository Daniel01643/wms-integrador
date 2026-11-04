# KitWmsIntegrador .NET 8 skeleton + offline NF-e XML reader with grouping by transport

The change lays down the KitWmsIntegrador solution: a `.NET 8` Worker Service (`KitWmsIntegrador.Worker`), a domain/logic library (`KitWmsIntegrador.Core`), and an xUnit project (`KitWmsIntegrador.Tests`). Core carries the real behavior for this phase — a streaming NF-e XML parser, a transport-number extractor, and a grouping step that turns notes into waves — while the WMS client, I50/I60 builders, orchestrator, state store, and notifier exist only as empty interfaces with TODOs. No HTTP, no WMS send, no file mutation in `C:\Envia xml`, no database. The Worker runs once, scans the input folder, and prints a summary (counts, distinct transports, wave-size distribution, pendencies). Watch for: the "scan-and-summarize runnable" is the Worker's only behavior right now rather than a separate tool (by design, not a defect); the no-transport fallback is exercised only against a synthetic XML because no non-BRF sample exists (confirmed, non-blocking); build/test/scan evidence is recorded by the coder and consistent (confirmed).

**Verdict**: APPROVED

## High-level view

The architecture is split so that `Core` holds domain models and the three implemented behaviors (parse, extract transport, group), and the Worker wires them through DI and drives a single pass. The future WMS-communication surface is reserved as empty interfaces (`IClienteWms`, `IConstrutorI50`, `IConstrutorI60`, `IStoreEstado`, `INotificador`, `IOrquestrador`), so the topology is visible without any network code present.

The parser is forward-only over `XmlReader` with DTD processing prohibited and no resolver, matching elements by `LocalName` so the NFe namespace/prefix doesn't matter. It extracts every field listed in MAPEAMENTO-DADOS.md — key, nNF, serie, mod, dhEmi, emitter, dest+address, N items (cProd/xProd/qCom/uCom/vProd/CFOP + rastro), total vNF, transp/vol, transportadora — and reads one file at a time via `yield return` to keep memory flat across the 1196-file folder.

The transport number comes from `infAdic/infCpl` via the regex `Transporte:\s*(\d+)`, kept as a string so leading zeros survive; a miss yields `null`. Grouping buckets notes by that string (ordinal) into waves (one note = one DocCarga, wave key = `nroagrupseparacao`) and routes transport-less notes into a `SemTransporte` pendency list without aborting the scan.

Configuration is bound from the `Entrada` section of appsettings plus environment variables (`Entrada__PastaEntrada`), defaulting to `C:\Envia xml` in the options class and appsettings — never hardcoded in domain code. Tests cover the extractor, the grouper, two real BRF XMLs (nNF, key, transport, items, volumes, transportadora), and an end-to-end synthetic no-transport pendency path.

<details>
<summary>Issues (3)</summary>

1. **Scan runnable is the Worker itself** — the scan-and-summarize is the Worker's `ExecuteAsync`, not a separate entry point; acceptable for this phase, but a watcher/continuous mode and any separate CLI are deferred. No action required now; note it when the next phase defines run modes.
2. **No-transport fallback only synthetic** — the pendency path is proven with a synthetic non-BRF XML (real data is 100% BRF with transport). Revisit with a real non-BRF sample before relying on the fallback in production, per the ressalvas in MAPEAMENTO-DADOS.md.
3. **Parse errors surfaced only as log warnings** — `VarredorPasta` collects per-file parse errors and the Worker logs them, but there's no structured pendency/error report yet. Fine for a scan phase; the error-report requirement (item B/relatório de erros) lands in a later phase.

</details>

<details>
<summary>Details</summary>

### Transport extraction preserves leading zeros

`ExtratorTransporte` is a static partial class with a `[GeneratedRegex]` for `Transporte:\s*(\d+)`, returning `match.Groups[1].Value` as a string. Keeping it a string is the correct call — `0130693549` would lose its leading zero as a number, and that value becomes the wave key (`nroagrupseparacao`). The grouper uses `StringComparer.Ordinal`, so `"0130693549"` and `"130693549"` are distinct keys, which is the intended behavior given the extractor never strips zeros. Null/empty/whitespace input returns `null`, and the "no `Transporte:`" case returns `null`, both covered by `ExtratorTransporteTests`.

### Streaming parse and forward-only subtree walking

The parser avoids loading whole documents: `XmlReader` with `IgnoreWhitespace`/`IgnoreComments`, `DtdProcessing.Prohibit`, and `XmlResolver = null` (good hygiene — closes XXE/entity-expansion exposure even though this is trusted local input). The one subtle piece is `ParaCadaFilho`: callbacks that consume an element via `ReadElementContentAsString`/`ReadSubtree` leave the reader already positioned on the next node, so the helper only advances when the callback did *not* consume. Getting this wrong would silently skip siblings — e.g. drop items. The Xml1 test asserting exactly 2 items (and the second item's code/qty) is what makes this concrete and confirms multiple `det` siblings are all read.

`ParseArquivo` opens with `FileAccess.Read` + `FileShare.Read` and never writes, satisfying the no-mutation constraint at the file-handle level.

### Grouping into waves and pendency routing

`AgrupadorTransporte.Agrupar` enumerates the note sequence once — compatible with the streaming scan, so the whole folder is never materialized. Notes with a transport land in an ordinal-keyed dictionary that becomes one `Onda` per distinct transport; empty/null transport goes to `SemTransporte`. This is the "1 DocCarga per note, wave = transport number" rule modeled without any send. The grouper consuming the enumerable lazily matters: if it had called `.ToList()` on the scan up front, the low-memory property would be lost. It doesn't.

### Configuration, no hardcoded path in domain

`Program.cs` binds `OpcoesEntrada` from the `Entrada` section; the generic host already layers environment variables, so `Entrada__PastaEntrada` overrides without code changes. The `C:\Envia xml` default lives in the options class and appsettings.json, not in `VarredorPasta` or the parser — the domain code receives the path as a parameter. `appsettings.Development.json` only sets logging, so it won't silently redirect the input folder.

### Test coverage

Covered: extractor (zero preservation, no-space variant, no-pattern, null/empty/whitespace); grouper (multi-note wave, pendency routing, zero-preserving key); two real BRF XMLs asserting nNF, 44-digit key, transport, item count/fields, total, volumes, and transportadora CNPJ/name; and an end-to-end synthetic XML with no `Transporte:` that parses cleanly and lands in `SemTransporte`. The real XMLs are committed under `tests/.../Recursos` and copied to output via `CopyToOutputDirectory`, confirmed present in both `bin\Debug` and `bin\Release`.

Not tested: any real non-BRF XML (none exists — the synthetic stands in); a malformed/unparseable XML exercising the `VarredorPasta` error-collection path (the error list is produced by the Worker scan over real data, which reported 0 errors, so the catch branch is unexercised by an automated test). Neither gap is blocking for a read-only scan phase.

### Build / test / scan evidence (not re-run)

Per `RUN-EVIDENCE.md`: `dotnet build -c Release` succeeded with 0 warnings/0 errors across all three projects; `dotnet test` reported 14 passed / 0 failed; and the Worker scan over `C:\Envia xml` read 1196/1196 XMLs with 0 parse errors, 398 distinct transports, 0 pendencies, wave sizes 1–10. The distribution sums check out (143+45+58+70+21+38+15+3+3+2 = 398 waves; the weighted sum = 1196 notes), and the numbers match MAPEAMENTO-DADOS.md section 3b (1196 / 398 / wave up to 10). A grep for `HttpClient`/`System.Net.Http`/`GetToken`/SQL finds only the TODO comment in `IClienteWms` — no network or persistence code. Evidence is present and internally consistent, so per instructions the suites were not re-run.

</details>

<details>
<summary>File map</summary>

- `KitWmsIntegrador.sln` — solution grouping src/ (Worker, Core) and tests/.
- `src/KitWmsIntegrador.Core/Leitura/LeitorNotaFiscalXml.cs` — streaming NF-e parser (all mapped fields).
- `src/KitWmsIntegrador.Core/Leitura/ExtratorTransporte.cs` — regex transport extractor (string, leading zeros).
- `src/KitWmsIntegrador.Core/Leitura/VarredorPasta.cs` — folder scan, one file at a time, per-file error collection.
- `src/KitWmsIntegrador.Core/Agrupamento/*` — `AgrupadorTransporte`, `Onda`, `ResultadoAgrupamento` (waves + pendencies).
- `src/KitWmsIntegrador.Core/Modelos/*` — `NotaFiscal`, `ItemNota`, `EnderecoDestinatario`, `VolumeTransporte`.
- `src/KitWmsIntegrador.Core/{ClienteWms,Construtores,Estado,Notificacao,Orquestrador}/*` — empty interfaces with TODOs (no send).
- `src/KitWmsIntegrador.Worker/{Program,Worker}.cs` — DI wiring + single-pass scan/summary.
- `src/KitWmsIntegrador.Worker/Configuracao/OpcoesEntrada.cs`, `appsettings*.json` — configurable input folder.
- `tests/KitWmsIntegrador.Tests/*Tests.cs` + `Recursos/*.XML` — xUnit coverage + two real BRF XMLs.
- `.agents/tasks/RUN-EVIDENCE.md`, `scan-run.log` — recorded build/test/scan results.

Full diff: `git diff 4b825dc642cb6eb9a060e54bf8d69288fbee4904 HEAD`.

</details>
