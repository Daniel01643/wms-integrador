# Plano de Implementação — Etapa 4: Construtores de payload I50, I60 e I10

Projeto: **KitWmsIntegrador** (.NET 8 / C#). Objetivo desta etapa: implementar os
**Construtores** que montam os objetos/DTOs das interfaces **I50 (DocCarga)**,
**I60 (nota/associação)** e **I10 (cadastro de empresa)** a partir dos dados já
parseados da `NotaFiscal`/`Onda`. **ESTA ETAPA NÃO FAZ CHAMADAS HTTP** — apenas
constrói objetos serializáveis (e, quando útil, o JSON) prontos para o
`IClienteWms.EnviarAsync(InterfaceWms, object, ...)` da Etapa 3. Sem orquestração,
máquina de estados, store, notificador ou movimentação de arquivos (Etapa 5+).

Base verde: `dotnet build KitWmsIntegrador.sln -c Debug` compila sem erros e
`dotnet test KitWmsIntegrador.sln` passa 30/30 (verificado nesta exploração).

---

## Decisões de design (grounded no código e nos docs)

- **Onde mora o quê.** Contratos (`IConstrutorI50/I60/I10`), DTOs tipados e
  implementações concretas ficam no projeto **Core**, namespace
  `KitWmsIntegrador.Core.Construtores` (os placeholders `IConstrutorI50.cs`/
  `IConstrutorI60.cs` já estão lá). A nova seção de config operacional/I50 fica no
  **Core** (`KitWmsIntegrador.Core.Configuracao`) junto do padrão de `OpcoesWms`, e é
  registrada/vinculada no **Worker** (`Program.cs` + `appsettings.json`), exatamente
  como `OpcoesEntrada`/`OpcoesWms` hoje. Motivo: o Core já abriga domínio, modelos e
  o cliente; o Worker é só host/DI.

- **Construtores produzem `object` (DTO tipado), não string.** `IClienteWms.EnviarAsync`
  já recebe `object payload` e serializa num ponto único com
  `JsonSerializerDefaults.Web` + `DefaultIgnoreCondition = WhenWritingNull`
  (ver `ClienteWmsHttp.JsonOpcoes`). Logo os construtores retornam DTOs com
  `[JsonPropertyName]` casando os nomes exatos do WMS; a serialização fica a cargo do
  cliente. Rejeitado: montar string JSON à mão — perderia tipagem e duplicaria a
  política de serialização. Para testes e inspeção, cada construtor também expõe um
  método auxiliar de serialização que usa **as mesmas** `JsonSerializerOptions` do
  cliente (extraídas para um ponto compartilhado — ver item 1).

- **Granularidade (regra central).** 1 `NotaFiscal` = 1 DocCarga = 1 I50. O campo
  `nroagrupseparacao` da I50 recebe o **número do transporte** (`NotaFiscal.NumeroTransporte`
  / `Onda.NumeroTransporte`, string com zeros à esquerda — já extraída do `infCpl`).
  Esse valor é o agrupador da onda. Depois, 1 I60 por nota referencia o DocCarga via
  `pedidos[]`/`numeroPedido`. I10 só é montada sob demanda (empresa destinatária não
  cadastrada), a partir do destinatário do XML. Os construtores recebem **uma nota por
  vez** (`NotaFiscal`); o construtor I50 também aceita, opcionalmente, o
  `numeroTransporte` já resolvido (default: `nota.NumeroTransporte`). A iteração sobre
  `Onda.Notas` é responsabilidade do Orquestrador (Etapa 5), não dos construtores.

- **Amarração I50 ↔ I60: `numeroPedido`.** O `numeroPedido` da I50 é derivado de forma
  determinística do **número da nota** (`NotaFiscal.Numero`) — BRIEFING diz que número
  de nota e DocCarga podem coincidir, e o teste de comunicação usou `numeroPedido`
  livre. Decisão: `numeroPedido = nota.Numero` (sem zeros à esquerda artificiais; é o
  valor bruto do `nNF`). A I60 referencia o mesmo valor em `pedidos[]`. Isso mantém I50
  e I60 amarrados por um único campo derivável da nota, sem estado externo.

- **Config operacional parametrizável, nunca hardcoded.** Depósito (`JD`/`8`), CNPJ do
  armazém (`14.605.065/0001-75`) e depositante/proprietária (`01.838.723/0098-50`,
  código `3509308`) vêm de uma seção de config nova com **defaults** iguais aos valores
  BRF/Friozem confirmados, mas sobrescrevíveis por `appsettings.json` + env (padrão
  `Secao__Campo`, igual a `Wms__BaseUrl`). A seção de I50 traz `tipoCarga`,
  `tipoSeparacao` (sep 1) e `tipoSeparacao2` (sep 2), também parametrizáveis — e os
  mesmos valores de separação são aplicados aos **itens** do I50. Rejeitado: literais no
  código dos construtores — violaria o pedido explícito do usuário.

- **Formatação determinística e cultura-invariante.** Datas no formato
  `DD/MM/YYYY HH:MM:SS` (`"dd/MM/yyyy HH:mm:ss"`, `CultureInfo.InvariantCulture`);
  decimais com ponto decimal (`ToString(formato, CultureInfo.InvariantCulture)`,
  ex. `"0.####"` ou casas fixas conforme campo); strings com zeros à esquerda
  preservados como string (transporte) ou formatados via `PadLeft` quando o WMS exigir
  largura fixa. Centralizo esses helpers numa classe estática interna
  (`FormatoWms`) no namespace dos construtores para evitar divergência entre I50/I60/I10.
  O Worker já usa `CultureInfo.InvariantCulture` em formatação numérica — segue o padrão.

- **Campos "a confirmar" ficam explícitos e parametrizáveis quando plausível.** Cada
  campo cuja semântica ainda não está 100% fechada (ver seção dedicada abaixo) recebe
  um **default proposto comentado** no DTO e, quando for valor de negócio (ex.:
  `tipoAcondicionamento`), vira campo de config com default — assim o ajuste futuro é
  por configuração, sem recompilar. Os comentários `// a confirmar:` marcam cada um.

- **I60 e I10 seguem template comentado (valores fixos), com CNPJ/armazém/depósito da
  config.** Conforme instrução do usuário na mensagem 12: I50 ganha a seção de config
  rica; I60 e I10 seguem o template com os fixos comentados, mas os CNPJs/armazém/
  depósito saem da **mesma** config operacional (não duplicar literais).

- **Sem dependências novas.** O Core hoje não tem `PackageReference`; os construtores
  usam só BCL (`System.Text.Json`, `System.Globalization`). Mantém o grafo enxuto e o
  publish self-contained leve (prioridade de baixo consumo — AMBIENTE-SERVIDOR.md).

---

## Campos "a confirmar" — defaults propostos

Cada um fica comentado no DTO/código e, quando é valor de negócio, exposto em config
com o default abaixo. Registrados aqui para a revisão e para a Etapa 5 não reinterpretar.

| Campo | Onde | Default proposto | Racional |
|---|---|---|---|
| `tipoAcondicionamento` (PC vs PP) | I50 item/cabeçalho (config) | **`"PC"`** | PC (pallet/caixa) é o acondicionamento mais comum em DC de alimentos; parametrizável para trocar p/ `PP` sem recompilar. Marcar `// a confirmar`. |
| `dataprev` / `DhprevEmbarque` | I50 | **data de emissão da nota** (`NotaFiscal.DataEmissao`) formatada `dd/MM/yyyy HH:mm:ss`; se nula, `null` (omitida) | Não há data de embarque no XML; usar emissão como previsão é o palpite menos arriscado. Omitir quando ausente (ignore-null). |
| `qtdMandante` (U vs E) | I50 item | **`"U"`** (unidade comercial do XML, `ItemNota.Unidade`) | Enviar a quantidade na unidade comercial (`qCom`/`uCom`) do XML é o dado disponível e confiável. `E` (embalagem) exigiria fator de conversão que não temos. Parametrizável. |
| `tipoOperacaoNota` / `Tpsit` (ex.: `INE`) | I60 | **`"INE"`** (fixo no template) | Valor observado no template manual do usuário; fixo como no template, comentado. |
| `pedidos[]` (nNF vs xPed) | I60 | **`numeroPedido` derivado do `nNF`** (= `NotaFiscal.Numero`), espelhando o `numeroPedido` da I50 | Amarra I60↔I50 por um único valor determinístico. `xPed` fica como alternativa documentada caso o WMS exija o pedido do cliente. |
| `Tpnota` | I60 | **fixo do template** (valor do template manual) | Segue template comentado. |
| `Tpsep` | I50 (itens + cabeçalho) | vem de `tipoSeparacao`/`tipoSeparacao2` da config | Pedido explícito: sep 1 e sep 2 configuráveis; aplicar aos itens. |
| `NroEmb` / `numeroEmbarque` | I50 | **omitido (null)** nesta etapa | Embarque é opcional (BRIEFING); onda nasce do `nroagrupseparacao`. Omitir via ignore-null. |

> Os nomes de propriedade JSON (`NroEmb`, `Tpsit`, `Tpnota`, `Tpsep`,
> `DhprevEmbarque`, `NOTAS_VENDA`, `PEDIDO`, `EMPRESA`, etc.) devem casar **exatamente**
> (incluindo caixa) via `[JsonPropertyName]`, conforme os templates do
> `<original_user_request>` — essa é a fonte de verdade dos nomes.

---

## Itens (ordenados por dependência; cada item deixa a solution compilável)

- [ ] 1. Extrair as `JsonSerializerOptions` do cliente para um ponto compartilhado e criar os helpers de formatação.
      Mover as opções hoje privadas em `ClienteWmsHttp` (`JsonSerializerDefaults.Web` + `DefaultIgnoreCondition = WhenWritingNull`) para um helper público estático `JsonWms` em `src/KitWmsIntegrador.Core/ClienteWms/JsonWms.cs` e fazer `ClienteWmsHttp` referenciá-lo (sem mudar comportamento). Criar `src/KitWmsIntegrador.Core/Construtores/FormatoWms.cs` com helpers estáticos: `Data(DateTimeOffset?)` → `"dd/MM/yyyy HH:mm:ss"` invariante (ou `null`), `Decimal(decimal, int casas)` e `Decimal(decimal)` com ponto invariante, `ZerosEsquerda(string, int largura)`.
      Files: src/KitWmsIntegrador.Core/ClienteWms/JsonWms.cs (novo), src/KitWmsIntegrador.Core/ClienteWms/ClienteWmsHttp.cs (editar: usar JsonWms), src/KitWmsIntegrador.Core/Construtores/FormatoWms.cs (novo)
      Verify: `dotnet build KitWmsIntegrador.sln -c Debug` compila sem erros; `dotnet test KitWmsIntegrador.sln` segue 30/30 (nenhuma regressão no cliente).

- [ ] 2. Criar a config operacional + config da I50 no Core.
      Criar `src/KitWmsIntegrador.Core/Configuracao/OpcoesOperacionais.cs` (`Secao = "Operacional"`) com `Deposito="JD"`, `IdDeposito=8`, `CnpjArmazem="14.605.065/0001-75"`, `CnpjDepositante="01.838.723/0098-50"`, `CodigoDepositante="3509308"` (todos com default BRF, sobrescrevíveis). Criar `src/KitWmsIntegrador.Core/Configuracao/OpcoesI50.cs` (`Secao = "I50"`) com `TipoCarga`, `TipoSeparacao` (sep 1), `TipoSeparacao2` (sep 2) e `TipoAcondicionamento="PC"` e `QtdMandante="U"` (defaults propostos, parametrizáveis). Documentar em XML-doc que nada é hardcoded nos construtores.
      Files: src/KitWmsIntegrador.Core/Configuracao/OpcoesOperacionais.cs (novo), src/KitWmsIntegrador.Core/Configuracao/OpcoesI50.cs (novo)
      Verify: `dotnet build KitWmsIntegrador.sln -c Debug` compila sem erros.

- [ ] 3. Definir os DTOs tipados das três interfaces com `[JsonPropertyName]` exatos.
      Criar os DTOs que espelham os templates do `<original_user_request>`: `src/KitWmsIntegrador.Core/Construtores/Dtos/PayloadI50.cs` (envelope `PEDIDO`, cabeçalho com `nroagrupseparacao`, `Tpsep`, `Tpnota`, `DhprevEmbarque`, `NroEmb`, depósito/armazém/depositante, e lista de itens com `Tpsep`/`qtdMandante`/`tipoAcondicionamento`), `PayloadI60.cs` (envelope `NOTAS_VENDA`, `chavenfe`, `pedidos[]`, `Tpsit`/`tipoOperacaoNota`, `Tpnota`) e `PayloadI10.cs` (envelope `EMPRESA`, CNPJ/nome/endereço do destinatário). Campos opcionais como `string?`/`decimal?` para herdar o ignore-null. Todos os nomes JSON casando exatamente os templates.
      Files: src/KitWmsIntegrador.Core/Construtores/Dtos/PayloadI50.cs (novo), src/KitWmsIntegrador.Core/Construtores/Dtos/PayloadI60.cs (novo), src/KitWmsIntegrador.Core/Construtores/Dtos/PayloadI10.cs (novo)
      Verify: `dotnet build KitWmsIntegrador.sln -c Debug` compila sem erros.

- [ ] 4. Substituir o placeholder `IConstrutorI50` pelo contrato real e implementá-lo.
      Reescrever `IConstrutorI50.cs` com `PayloadI50 Construir(NotaFiscal nota, string? numeroTransporte = null)` (default: `nota.NumeroTransporte`) e `string ConstruirJson(...)`. Implementar em `src/KitWmsIntegrador.Core/Construtores/ConstrutorI50.cs`: mapeia 1 nota → 1 DocCarga; `nroagrupseparacao = numeroTransporte` (string, zeros preservados); aplica `OpcoesI50`/`OpcoesOperacionais` (via `IOptions<>`) a cabeçalho e **itens** (tipoSeparacao, tipoAcondicionamento, qtdMandante); datas/decimais via `FormatoWms`; `ConstruirJson` serializa com `JsonWms.Opcoes`. Lançar `ArgumentException` se `numeroTransporte` vazio (caso B — nota sem transporte — é tratado pelo Orquestrador, não aqui).
      Files: src/KitWmsIntegrador.Core/Construtores/IConstrutorI50.cs (editar), src/KitWmsIntegrador.Core/Construtores/ConstrutorI50.cs (novo)
      Verify: `dotnet build KitWmsIntegrador.sln -c Debug` compila sem erros.

- [ ] 5. Substituir o placeholder `IConstrutorI60` pelo contrato real e implementá-lo.
      Reescrever `IConstrutorI60.cs` com `PayloadI60 Construir(NotaFiscal nota)` e `string ConstruirJson(NotaFiscal nota)`. Implementar em `src/KitWmsIntegrador.Core/Construtores/ConstrutorI60.cs`: 1 I60 por nota; `chavenfe = nota.ChaveNFe`; `pedidos[]` = [`numeroPedido` derivado de `nota.Numero`] (mesma regra de amarração do I50); `Tpsit`/`tipoOperacaoNota = "INE"` e demais fixos do template; CNPJ armazém/depósito da `OpcoesOperacionais`. Datas/decimais via `FormatoWms`.
      Files: src/KitWmsIntegrador.Core/Construtores/IConstrutorI60.cs (editar), src/KitWmsIntegrador.Core/Construtores/ConstrutorI60.cs (novo)
      Verify: `dotnet build KitWmsIntegrador.sln -c Debug` compila sem erros.

- [ ] 6. Criar o contrato `IConstrutorI10` e implementá-lo.
      Criar `src/KitWmsIntegrador.Core/Construtores/IConstrutorI10.cs` com `PayloadI10 Construir(NotaFiscal nota)` e `string ConstruirJson(NotaFiscal nota)`. Implementar em `src/KitWmsIntegrador.Core/Construtores/ConstrutorI10.cs`: monta `EMPRESA` a partir do destinatário do XML (`nota.DestinatarioCnpj`, `DestinatarioNome`, `nota.Endereco`). Só é usado no caso D (empresa não localizada) — o gatilho é do Orquestrador; aqui só a montagem. Valores fixos do template comentados; CNPJ armazém/depósito da config quando o template os exigir.
      Files: src/KitWmsIntegrador.Core/Construtores/IConstrutorI10.cs (novo), src/KitWmsIntegrador.Core/Construtores/ConstrutorI10.cs (novo)
      Verify: `dotnet build KitWmsIntegrador.sln -c Debug` compila sem erros.

- [ ] 7. Registrar config e construtores no DI do Worker e nos appsettings.
      Em `Program.cs`, adicionar `AddOptions<OpcoesOperacionais>().Bind(...)` e `AddOptions<OpcoesI50>().Bind(...)` (seguindo o padrão de `OpcoesEntrada`/`OpcoesWms`) e registrar `IConstrutorI50/I60/I10` como singletons. Em `appsettings.json`, acrescentar as seções `"Operacional"` (com os valores BRF confirmados) e `"I50"` (com `TipoCarga`, `TipoSeparacao`, `TipoSeparacao2`, `TipoAcondicionamento`, `QtdMandante`) com comentários. NÃO gravar segredos.
      Files: src/KitWmsIntegrador.Worker/Program.cs (editar), src/KitWmsIntegrador.Worker/appsettings.json (editar)
      Verify: `dotnet build KitWmsIntegrador.sln -c Debug` compila sem erros; `dotnet run --project src/KitWmsIntegrador.Worker -- --help` (ou start curto) não quebra na resolução do DI — alternativamente, um teste de DI (item 8) cobre a resolução.

- [ ] 8. Escrever os testes xUnit offline dos construtores.
      Adicionar `tests/KitWmsIntegrador.Tests/ConstrutorI50Tests.cs`, `ConstrutorI60Tests.cs`, `ConstrutorI10Tests.cs`, seguindo o estilo de `ClienteWmsHttpTests`/`AgrupadorTransporteTests` (`[Fact]`, `IOptions` via `Options.Create(...)`, sem rede). Cobrir: (I50) `nroagrupseparacao` = número do transporte com zeros preservados; 1 nota → 1 DocCarga; tipoSeparacao/tipoAcondicionamento/qtdMandante vindos da config aplicados ao cabeçalho e a cada item; data formatada `dd/MM/yyyy HH:mm:ss`; decimal com ponto; `numeroTransporte` vazio lança; override de config muda o payload. (I60) `chavenfe` e `pedidos[]` = número da nota; amarração bate com o `numeroPedido` do I50 para a mesma nota; `Tpsit="INE"`. (I10) CNPJ/nome/endereço do destinatário mapeados; monta a partir da nota. Serializar via `ConstruirJson` e asserir que os nomes JSON exatos aparecem (`"nroagrupseparacao"`, `"chavenfe"`, `"pedidos"`, `"NroEmb"` omitido quando null, etc.).
      Files: tests/KitWmsIntegrador.Tests/ConstrutorI50Tests.cs (novo), tests/KitWmsIntegrador.Tests/ConstrutorI60Tests.cs (novo), tests/KitWmsIntegrador.Tests/ConstrutorI10Tests.cs (novo)
      Verify: `dotnet test KitWmsIntegrador.sln` passa todos (30 anteriores + novos), 0 falhas.

- [ ] 9. Verificação final integrada e commit local.
      Rodar build Release e a suíte completa; conferir que nenhum segredo foi adicionado ao `appsettings.json` e que `bin/obj` seguem ignorados. Commitar localmente na branch atual (`main`) com mensagem `feat: construtores de payload I50/I60/I10 + config operacional e I50`. NÃO dar push, NÃO fazer chamadas HTTP.
      Files: (nenhum novo) — git add dos arquivos criados/editados acima
      Verify: `dotnet build KitWmsIntegrador.sln -c Release` compila sem erros; `dotnet test KitWmsIntegrador.sln` 0 falhas; `git status` limpo após o commit; `git log --oneline -1` mostra o novo commit.

---

## Fora de escopo (NÃO fazer nesta etapa)

- Chamadas HTTP / envio ao WMS; orquestração / máquina de estados (Etapa 5); store de
  estado; notificador; movimentação/escrita de arquivos em `C:\Envia xml` (somente
  leitura, e só nos testes já existentes). Não commitar segredos.

## Lacunas assumidas (decididas para não travar)

- Nomes JSON exatos saem dos templates do `<original_user_request>`; se algum nome
  divergir do que o WMS aceitar, o ajuste é trocar o `[JsonPropertyName]` — isolado nos
  DTOs. `numeroPedido = nNF` como amarração I50↔I60 (documentado, alternativa `xPed`
  registrada). Defaults da tabela "a confirmar" aplicados; todos parametrizáveis ou
  comentados para revisão futura.
