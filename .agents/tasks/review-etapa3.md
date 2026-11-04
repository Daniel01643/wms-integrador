# Cliente WMS HTTP: token cacheado, query obrigatória de 5 parâmetros e retry que separa transitório de rejeição de negócio

A Etapa 3 implementa a camada de comunicação HTTP do KitWmsIntegrador contra a API do SolWMS Assist. `ClienteWmsHttp` (no Core) obtém e cacheia o token JWT via Basic Auth, monta a query string obrigatória com os 5 parâmetros — inclusive `CNPJREQUISITANTE`/`CNPJDESTINO` vazios como chaves sem valor —, envia o POST com Bearer e desserializa a resposta em `RespostaWms`/`RetornoWms` tipados. A resiliência é um laço manual (sem Polly, mantendo o grafo de dependências enxuto) que distingue três classes: transitório (rede, timeout, 5xx) com backoff exponencial, 401-token com renovação e uma única repetição fora do contador, e rejeição de negócio (`sucesso="N"`) que volta ao chamador sem retry. A classificação SUCESSO/PENDENTE/REJEITADO é um helper derivado do texto da mensagem, sem embutir regra de negócio no modelo. Watch for: a renovação de token (GetToken) não tem retry para transitórios, ao contrário do POST — assimetria confirmada, não bloqueante; `Classificar` prioriza o texto "pendente na interface" sobre o flag `sucesso`, então um `sucesso="S"` com essa mensagem seria Pendente (confirmado, alinhado ao doc); nenhum segredo real commitado — a senha de produção não aparece em nenhum fonte (confirmado).

**Verdict**: APPROVED

## High-level view

O cliente mora no Core (`KitWmsIntegrador.Core.ClienteWms`), com o registro de DI, `IClock`→`SystemClock` e a seção `Wms` do appsettings no Worker — mesmo padrão da Etapa 1. O contrato `IClienteWms` é genérico por `InterfaceWms` (I50/I60/I10 → string `IDENTIFICADOR`), não um método por EDI, porque o endpoint de POST é único e só o parâmetro muda.

A resposta é desserializada fielmente (`transacao`, `tipoEDI`, `sucesso`, `retornos[]`) e a decisão de negócio fica fora do modelo: `Classificar` deriva SUCESSO/PENDENTE/REJEITADO a partir do texto, preservando a mensagem bruta para os relatórios da etapa de orquestração.

O token é cacheado em memória com renovação proativa por margem antes do `exp` do JWT, lido decodificando o payload base64url sem validar assinatura. Renovações concorrentes são serializadas por `SemaphoreSlim` com dupla verificação. Em 401, o token é invalidado e a requisição repetida uma única vez sem consumir tentativa de backoff.

O retry só cobre transitórios: `HttpRequestException`, timeout (`TaskCanceledException` sem cancelamento do chamador) e HTTP 5xx, com backoff exponencial até `MaxTentativas`. Erro de negócio e 4xx≠401 não repetem. A renovação do token sob 401 é a única exceção fora do contador.

Os segredos (Basic Auth) vêm só de `WMS__Usuario`/`WMS__Senha`; o appsettings carrega placeholders e um comentário, sem as chaves `Usuario`/`Senha`, e a senha real do briefing não aparece em nenhum fonte. O logging registra identificador, transação, `sucesso` e duração — nunca token, Authorization, credenciais ou corpo da nota.

<details>
<summary>Issues (3)</summary>

1. **GetToken sem retry transitório** — o laço de resiliência cobre só o POST; um 5xx ou blip de rede no `GET /Integracao/GetToken` lança imediatamente. Não bloqueante nesta etapa (a orquestração da Etapa 5 pode reprocessar), mas vale alinhar a política antes do envio em volume.
2. **Teste de 5xx incorre em backoff real** — `Enviar_500_FazRetryAteMaxTentativas` passa por `Task.Delay` reais (~0,5s+1s), tornando o teste mais lento sem necessidade. Considerar injetar o atraso (via `IClock`/função de delay) para zerá-lo em teste.
3. **`Classificar` faz o texto prevalecer sobre `sucesso`** — uma mensagem contendo "pendente na interface" classifica como Pendente mesmo com `sucesso="S"`. Alinhado ao doc e ao fato de que `sucesso="N"` não é falha total; registrar como contrato explícito para a Etapa 5 não reinterpretar.

</details>

<details>
<summary>Details</summary>

### Desserialização fiel dos 3 cenários e classificação como helper derivado

`RespostaWms`/`RetornoWms` são records com `[JsonPropertyName]` batendo o JSON real (`transacao`, `tipoEDI`, `sucesso`, `retornos`, `documento`, `idDocumento`, `mensagem`). Nada de regra de negócio no modelo: `Sucesso` é `string` ("S"/"N") preservada crua e `Mensagem` fica disponível na íntegra para os relatórios de pendência/erro. A classificação vive separada em `Classificar(RetornoWms)`:

```csharp
if (mensagem.Contains("pendente na interface", StringComparison.OrdinalIgnoreCase))
    return ResultadoWms.Pendente;
if (string.Equals(retorno.Sucesso, "S", StringComparison.OrdinalIgnoreCase))
    return ResultadoWms.Sucesso;
return ResultadoWms.Rejeitado;
```

Os três JSON do TESTE-COMUNICACAO.md estão reproduzidos literais nos testes: SUCESSO (`sucesso="S"`) → `Sucesso`; PENDENTE com a ressalva "Empresa não localizada" → `Pendente`; estrutura mal-formada → `Rejeitado`. A ordem das regras é o detalhe que importa: o texto "pendente na interface" é avaliado antes do flag `sucesso`, então um retorno com `sucesso="S"` que ainda trouxesse essa frase viraria Pendente. É coerente com o achado do doc (um `sucesso="N"` pendente não é falha total), mas é uma decisão de precedência que a Etapa 5 precisa herdar sem reinterpretar — daí valer deixá-la explícita no contrato.

### Query string obrigatória com os 5 parâmetros, inclusive os 2 vazios

A montagem segue o contrato do doc, onde faltar `CNPJREQUISITANTE`/`CNPJDESTINO` causava 404:

```csharp
sb.Append("?IDENTIFICADOR=").Append(Uri.EscapeDataString(identificador.ParaIdentificador()));
sb.Append("&REQUISITANTE=").Append(Uri.EscapeDataString(_opcoes.Requisitante));
sb.Append("&CNPJREQUISITANTE").Append(ValorOpcional(_opcoes.CnpjRequisitante));
sb.Append("&DESTINO=").Append(Uri.EscapeDataString(_opcoes.Destino));
sb.Append("&CNPJDESTINO").Append(ValorOpcional(_opcoes.CnpjDestino));
```

`ValorOpcional` emite a chave sem `=` quando o valor é vazio e `=valor` escapado quando configurado — exatamente o formato `&CNPJREQUISITANTE&...&CNPJDESTINO` do Postman. O teste `Enviar_TodosOsCincoParametrosNaOrdemEsperada` fixa a string inteira (`?IDENTIFICADOR=WEBAPI-I50-Json&REQUISITANTE=...%2F...&CNPJREQUISITANTE&DESTINO=...&CNPJDESTINO`), e o `[Theory]` cobre o mapeamento I50/I60/I10 do `IDENTIFICADOR`. O CNPJ escapa a barra para `%2F`, que é o comportamento correto do servidor observado.

### Cache de token: margem antes do `exp` e renovação serializada

`TokenValido` renova quando `_clock.UtcNow >= exp - margem` (margem configurável, default 60s); sem `exp` legível, o token é tratado como válido e a renovação fica por conta do 401. `GarantirTokenAsync` serializa a renovação com `SemaphoreSlim` e faz dupla verificação dentro do lock, evitando GetTokens concorrentes redundantes. O `exp` sai de `JwtHelper.ObterExpiracao`, que decodifica só o segundo segmento base64url e lê o claim — sem validar assinatura, o que é adequado porque só precisamos do instante de expiração, não de confiar no emissor. `FakeClock` prova os dois caminhos: dois envios seguidos com token válido fazem um único GetToken; avançar o relógio além de `exp - margem` dispara um novo.

### Retry: transitório × 401 × rejeição de negócio

O laço trata cada classe de forma distinta e os testes amarram o comportamento por contagem de requisições capturadas:

- **401**: invalida o token, marca `jaRenovouPorToken`, faz `tentativa--` (não consome backoff) e repete. Um segundo 401 cai no `EnsureSuccessStatusCode` e lança — `Enviar_401Persistente` confirma exatamente 2 POSTs e que o segundo usou o token novo.
- **5xx**: backoff exponencial até `MaxTentativas` (`Enviar_500` → 3 POSTs; `Enviar_500DepoisSucesso` → 2 e retorna a resposta).
- **Rede/timeout**: `HttpRequestException` e `TaskCanceledException` com `!cancellationToken.IsCancellationRequested` (idioma padrão para separar timeout de cancelamento do chamador) entram no backoff.
- **4xx≠401**: `Enviar_400` confirma uma única chamada e exceção, sem retry.
- **Rejeição de negócio** (`sucesso="N"`): volta ao chamador tipada, sem retry — os testes PENDENTE e REJEITADO confirmam 2 requisições (token + POST).

A assimetria a observar: o laço só existe no POST. `ObterTokenAsync` faz `EnsureSuccessStatusCode` direto, então um 5xx ou blip de rede no GetToken lança sem repetir. Para credenciais inválidas (401 no GetToken) isso é o certo — não adianta repetir —, mas para um 5xx transitório do endpoint de token o envio falha na primeira. Não bloqueia a etapa, porém a política de resiliência fica incompleta nesse flanco.

### Segredos: só de env, nunca no repo nem no log

`OpcoesWms.Usuario`/`Senha` só são preenchidos por `WMS__Usuario`/`WMS__Senha`; o `appsettings.json` traz a seção `Wms` com BaseUrl/Requisitante/Destino/CNPJs vazios/timeouts e um `_comentario_credenciais`, **sem** as chaves `Usuario`/`Senha`. Uma busca pela senha real do briefing (`b$qNg...`) não retorna nada em nenhum fonte; o único literal sensível nos testes é `senha-de-teste` com o usuário `BLUEFOOD` (de homolog, já público no briefing), aceitável em teste. O Basic Auth é montado em `ObterTokenAsync` e aplicado ao header; o log de sucesso registra `identificador`, `transacao`, `sucesso` e `duracaoMs` — não há log de token, header Authorization, credenciais nem corpo da nota. Os logs de warning de retry registram status/identificador/tentativa, também sem material sensível. `SmokeTestWms` lê credenciais de env e aborta com código 2 se ausentes, sem imprimi-las.

### Escopo

A entrega fica contida na camada HTTP: contrato, modelos tipados, `OpcoesWms`, `IClock`/`SystemClock`, `JwtHelper`, `ClienteWmsHttp`, registro no DI do Worker e um smoke test manual acionado por `--smoke-wms` (fora do fluxo padrão e de CI). O `Worker` continua só varrendo/agrupando — nenhum construtor I50/I60, orquestração, store de estado, notificador por e-mail ou movimentação de arquivos foi tocado, conforme o "fora de escopo" do plano. As três novas `PackageReference` do Core (Http, Options, Logging.Abstractions) são o mínimo para `IHttpClientFactory`/`IOptions`/`ILogger` e não arrastam Polly.

### Cobertura de testes

O relatório `test-report-etapa3.md` registra build OK (0 avisos/0 erros) e 30/30 testes passando (14 anteriores + 16 novos), com `ClienteWmsHttpTests.cs` entre os arquivos da suíte. Conforme instrução, a suíte não foi reexecutada. Os testes do cliente, lidos em `ClienteWmsHttpTests.cs`, cobrem: desserialização e classificação dos 3 cenários; query string exata e por `InterfaceWms`; Basic no GetToken e Bearer no POST (com o base64 esperado de `BLUEFOOD:senha-de-teste`); 401 renova+repete uma vez e 401 persistente desiste após 2 POSTs; 4xx≠401 sem retry; 5xx até `MaxTentativas` e 5xx-depois-sucesso; cache de token com relógio válido e renovação após a margem; serialização da sobrecarga de objeto. Os fakes (`FakeHttpMessageHandler`, `FakeClock`, `JwtFake`) mantêm tudo sem rede e sem assinatura real.

Não testado: GetToken retornando transitório (o flanco sem retry acima); JWT sem `exp` em tempo de execução (`JwtFake.SemExpiracao` existe mas não há teste que exercite o caminho "válido até 401"); corpo de resposta vazio/`null` caindo no `new RespostaWms()` de `DesserializarResposta`. Nenhum é bloqueante para esta camada, mas o primeiro reforça o Issue 1.

</details>

<details>
<summary>File map</summary>

- `src/KitWmsIntegrador.Core/ClienteWms/IClienteWms.cs` — contrato genérico por `InterfaceWms` + helper `Classificar` (substitui a interface vazia).
- `src/KitWmsIntegrador.Core/ClienteWms/ClienteWmsHttp.cs` — implementação HTTP: token cacheado, query, retry e logging seguro.
- `src/KitWmsIntegrador.Core/ClienteWms/InterfaceWms.cs` — enum I50/I60/I10 + mapeamento para `WEBAPI-I*-Json`.
- `src/KitWmsIntegrador.Core/ClienteWms/{RespostaWms,RetornoWms,ResultadoWms}.cs` — modelos tipados + enum de classificação.
- `src/KitWmsIntegrador.Core/ClienteWms/OpcoesWms.cs` — opções da seção `Wms`; `Usuario`/`Senha` documentados como env-only.
- `src/KitWmsIntegrador.Core/ClienteWms/JwtHelper.cs` — leitura do claim `exp` (base64url, sem validar assinatura).
- `src/KitWmsIntegrador.Core/Tempo/{IClock,SystemClock}.cs` — abstração de relógio para cache de token testável.
- `src/KitWmsIntegrador.Core/KitWmsIntegrador.Core.csproj` — +Http/+Options/+Logging.Abstractions.
- `src/KitWmsIntegrador.Worker/Program.cs` — DI do cliente (`AddHttpClient`, `IClock`) + branch `--smoke-wms`.
- `src/KitWmsIntegrador.Worker/SmokeTestWms.cs` — smoke test manual (GetToken+POST reais), fora de CI.
- `src/KitWmsIntegrador.Worker/appsettings.json` — seção `Wms` com placeholders, sem segredos.
- `tests/KitWmsIntegrador.Tests/ClienteWmsHttpTests.cs` — 16 testes do cliente.
- `tests/KitWmsIntegrador.Tests/Infra/{FakeHttpMessageHandler,FakeClock,JwtFake}.cs` — fakes de rede/relógio/JWT.

Diff completo: `git diff 6d958c1 5e79eb2`.

</details>
