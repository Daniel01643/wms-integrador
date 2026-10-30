# Plano de Implementação — Etapa 3: Cliente WMS (comunicação HTTP)

Projeto: **KitWmsIntegrador** (.NET 8 / C#). Objetivo desta etapa: implementar a camada de
comunicação HTTP com a API REST do SolWMS Assist — token com cache, envio genérico por
IDENTIFICADOR, resposta tipada, resiliência, configuração, logging seguro e testes sem rede.
NÃO reimplementar leitura/parse/agrupamento (já commitado, build verde, 14 testes).

## Decisões de design (grounded no código e nos docs)

- **Onde mora o quê.** Contrato (`IClienteWms`), modelos de resposta (`RespostaWms`,
  `RetornoWms`, enum de classificação), opções (`OpcoesWms`), abstração de relógio
  (`IClock`) e implementação concreta (`ClienteWmsHttp`) ficam todos no projeto
  **Core** (`KitWmsIntegrador.Core`), namespace `KitWmsIntegrador.Core.ClienteWms` e
  sub-namespaces. Motivo: o Core já abriga as interfaces de extensão (`IOrquestrador`,
  `IStoreEstado`, `INotificador`) e os modelos de domínio; manter o cliente lá preserva o
  padrão e deixa o Worker apenas como host/DI. O registro no DI e os `appsettings` ficam no
  **Worker**, igual ao padrão atual de `OpcoesEntrada` + `Program.cs`.
- **`IClienteWms` genérico por IDENTIFICADOR, não um método por interface.** O endpoint de
  POST é o mesmo para I50/I60/I10; só muda o `IDENTIFICADOR` na query (confirmado em
  TESTE-COMUNICACAO.md e BRIEFING.md). O núcleo é `EnviarAsync(identificador, payload, ct)`.
  O `identificador` é um `enum InterfaceWms { I50, I60, I10 }` mapeado para a string
  `WEBAPI-I50-Json`/`...`, evitando strings mágicas no chamador. Rejeitado: sobrecargas
  `EnviarI50/EnviarI60` — duplicariam código sem ganho, já que o corpo vem pronto do
  Orquestrador (etapa futura). O token é tratado internamente; não há método público de token.
- **Payload como `object` serializado dentro do cliente.** `EnviarAsync` recebe `object payload`
  e o cliente serializa com `System.Text.Json`. Motivo: os construtores de I50/I60 (Etapa 4)
  produzirão objetos/anônimos; serializar num único ponto garante `Content-Type` e opções de
  serialização consistentes. Sobrecarga adicional que aceita `string jsonBruto` para o smoke
  test manual e para testes com JSON literal.
- **Resposta tipada com desserialização fiel + classificação como helper derivado.**
  `RespostaWms` espelha o JSON (`transacao`, `tipoEDI`, `sucesso`, `retornos[]`). A regra de
  negócio NÃO é decidida aqui (é do Orquestrador, Etapa 5). Exponho um helper
  `ClassificarRetorno(RetornoWms)` → `enum ResultadoWms { Sucesso, Pendente, Rejeitado }`
  derivado do texto da mensagem, deixando `Mensagem` bruta disponível. Classificação por texto
  (TESTE-COMUNICACAO.md): contém "pendente na interface" → Pendente; `sucesso="S"` → Sucesso;
  caso contrário → Rejeitado.
- **Cache de token com `IClock` injetável.** O JWT tem claim `exp`. Faço cache do token em
  memória (campo + `SemaphoreSlim` para serializar renovação concorrente) e renovo quando
  faltar menos que a margem configurável (`MargemRenovacaoTokenSegundos`) para o `exp`, ou
  proativamente/sob 401. `exp` é lido decodificando o payload do JWT (parse do segundo
  segmento base64url como JSON e leitura do campo `exp` em epoch) — sem dependência externa de
  validação, pois só precisamos do tempo de expiração, não validar assinatura. `IClock` (com
  `SystemClock` default) permite testar expiração com relógio fake.
- **Resiliência com Polly? Não — handler manual mínimo.** Prioridade do projeto é baixo consumo
  e implantação simples (AMBIENTE-SERVIDOR.md, self-contained em Server 2012 R2). A política de
  retry é específica (distinguir transitório × 401-token × rejeição de negócio) e pequena;
  implemento um laço de retry explícito dentro de `ClienteWmsHttp` com backoff exponencial, sem
  adicionar o pacote Polly. Isso mantém o grafo de dependências enxuto (hoje o Core não tem
  nenhum PackageReference) e torna a lógica 100% testável via `HttpMessageHandler` fake.
  Transitório = `HttpRequestException`/timeout (`TaskCanceledException` não-cancelado pelo
  chamador) / HTTP 5xx. 401 → renova token e repete UMA vez (não conta como tentativa normal de
  backoff). Rejeição de negócio (`sucesso="N"`) e 4xx≠401 → NÃO repete.
- **`HttpClient` via `IHttpClientFactory` (named client "Wms").** Segue a prática padrão do
  host genérico; `BaseAddress` e `Timeout` configurados no registro. `ClienteWmsHttp` recebe
  `IHttpClientFactory` (ou `HttpClient` tipado) + `IOptions<OpcoesWms>` + `ILogger` + `IClock`.
- **Segredos.** Basic Auth (`WMS__Usuario`/`WMS__Senha`) vem de env/user-secrets; placeholders
  em `appsettings.json`, valores reais NÃO commitados. `.gitignore` já ignora `bin/obj`; não há
  arquivo de segredo no repo. Nunca logar token, Authorization, credenciais ou corpo da nota.

## Itens (ordenados por dependência; cada item deixa a solution compilável)

- [ ] 1. Criar o enum `InterfaceWms` e os modelos de resposta tipados.
      `InterfaceWms { I50, I60, I10 }` com mapeamento para `WEBAPI-I50-Json` etc.
      `RespostaWms` (record: `Transacao`, `TipoEDI`, `Sucesso`, `IReadOnlyList<RetornoWms> Retornos`),
      `RetornoWms` (record: `Documento`, `IdDocumento`, `Sucesso`, `Mensagem`), todos com atributos
      `[JsonPropertyName]` batendo com o JSON real (`transacao`, `tipoEDI`, `sucesso`, `retornos`,
      `documento`, `idDocumento`, `mensagem`). Enum `ResultadoWms { Sucesso, Pendente, Rejeitado }`.
      Seguir o estilo `record` + XML-doc dos modelos existentes em `Modelos/`.
      Files:
        c:\wms-integrador\src\KitWmsIntegrador.Core\ClienteWms\InterfaceWms.cs
        c:\wms-integrador\src\KitWmsIntegrador.Core\ClienteWms\RespostaWms.cs
        c:\wms-integrador\src\KitWmsIntegrador.Core\ClienteWms\RetornoWms.cs
        c:\wms-integrador\src\KitWmsIntegrador.Core\ClienteWms\ResultadoWms.cs
      Verify: `dotnet build c:\wms-integrador\KitWmsIntegrador.sln -v minimal` — compila sem erros.

- [ ] 2. Criar a abstração de relógio `IClock` + `SystemClock` em Core.
      `IClock { DateTimeOffset UtcNow { get; } }`; `SystemClock` retorna `DateTimeOffset.UtcNow`.
      Namespace `KitWmsIntegrador.Core.Tempo` (ou `.Infra`). Usada pelo cache de token e pelos testes.
      Files:
        c:\wms-integrador\src\KitWmsIntegrador.Core\Tempo\IClock.cs
        c:\wms-integrador\src\KitWmsIntegrador.Core\Tempo\SystemClock.cs
      Verify: `dotnet build c:\wms-integrador\KitWmsIntegrador.sln -v minimal` — compila.

- [ ] 3. Criar `OpcoesWms` (seção de configuração "Wms") no Core.
      Propriedades: `const string Secao = "Wms"`, `BaseUrl`, `Requisitante`, `Destino`,
      `CnpjRequisitante`, `CnpjDestino`, `Usuario`, `Senha`, `int TimeoutSegundos` (default 30),
      `int MaxTentativas` (default 3), `int MargemRenovacaoTokenSegundos` (default 60). XML-doc
      deixando explícito que `Usuario`/`Senha` vêm de env (`WMS__Usuario`/`WMS__Senha`) e NUNCA do
      appsettings/código. Seguir o estilo de `OpcoesEntrada.cs`.
      Files:
        c:\wms-integrador\src\KitWmsIntegrador.Core\ClienteWms\OpcoesWms.cs
      Verify: `dotnet build c:\wms-integrador\KitWmsIntegrador.sln -v minimal` — compila.

- [ ] 4. Substituir o placeholder `IClienteWms` pelo contrato real.
      Remover a interface vazia e definir:
        `Task<RespostaWms> EnviarAsync(InterfaceWms identificador, object payload, CancellationToken ct = default);`
        `Task<RespostaWms> EnviarAsync(InterfaceWms identificador, string jsonBruto, CancellationToken ct = default);`
        `ResultadoWms Classificar(RetornoWms retorno);` (helper de classificação por texto).
      Manter a XML-doc explicando que token é interno/cacheado e que regra de negócio é do
      Orquestrador. NÃO expor método público de token.
      Files:
        c:\wms-integrador\src\KitWmsIntegrador.Core\ClienteWms\IClienteWms.cs
      Verify: `dotnet build c:\wms-integrador\KitWmsIntegrador.sln -v minimal` — compila (nenhum
      consumidor referencia a interface vazia hoje, então não quebra nada).

- [ ] 5. Implementar `ClienteWmsHttp` (token com cache + envio + resiliência).
      Classe concreta implementando `IClienteWms`. Responsabilidades:
        (a) **Token**: método privado `GarantirTokenAsync` — se não há token em cache ou falta
            menos que `MargemRenovacaoTokenSegundos` para o `exp`, chama
            `GET {BaseUrl}/Integracao/GetToken` com header `Authorization: Basic base64(Usuario:Senha)`,
            desserializa `{ "token": "<JWT>" }`, decodifica o `exp` do JWT e guarda token+expiração.
            Serializar renovações concorrentes com `SemaphoreSlim`.
        (b) **Query string**: montar com os 5 parâmetros SEMPRE presentes, inclusive os 2 vazios:
            `?IDENTIFICADOR=<...>&REQUISITANTE=<Requisitante>&CNPJREQUISITANTE&DESTINO=<Destino>&CNPJDESTINO`
            (observando que `CNPJREQUISITANTE`/`CNPJDESTINO` entram como chave sem valor; usar os
            valores de `OpcoesWms` quando configurados). Escapar valores adequadamente.
        (c) **Envio**: `POST {BaseUrl}/integracao/Post{query}` com `Authorization: Bearer <JWT>`,
            `Content-Type: application/json`, corpo = payload serializado (System.Text.Json).
            Desserializar a resposta em `RespostaWms`.
        (d) **Resiliência**: laço até `MaxTentativas` com backoff exponencial para transitórios
            (HttpRequestException, timeout via TaskCanceledException não solicitado pelo chamador,
            HTTP 5xx). Em 401: renovar token e repetir UMA vez fora do contador de backoff. NÃO
            repetir em 4xx≠401 nem em resposta 2xx com rejeição de negócio.
        (e) **Classificar**: implementar o helper por texto (contém "pendente na interface" →
            Pendente; `sucesso="S"` → Sucesso; senão Rejeitado), case-insensitive.
        (f) **Logging**: logar método, identificador, `transacao`, `sucesso`, duração em ms e uma
            referência não-sensível. NUNCA logar token, Authorization, credenciais ou corpo da nota.
      Construtor recebe `HttpClient` (tipado via factory), `IOptions<OpcoesWms>`,
      `ILogger<ClienteWmsHttp>`, `IClock`.
      Files:
        c:\wms-integrador\src\KitWmsIntegrador.Core\ClienteWms\ClienteWmsHttp.cs
        (opcional) c:\wms-integrador\src\KitWmsIntegrador.Core\ClienteWms\JwtHelper.cs  (decodificar exp)
      Verify: `dotnet build c:\wms-integrador\KitWmsIntegrador.sln -v minimal` — compila.

- [ ] 6. Registrar o cliente no DI do Worker + opções + appsettings.
      Em `Program.cs`: `AddOptions<OpcoesWms>().Bind(configuration.GetSection(OpcoesWms.Secao))`;
      `AddHttpClient<IClienteWms, ClienteWmsHttp>(...)` configurando `BaseAddress` = `OpcoesWms.BaseUrl`
      e `Timeout` = `TimeoutSegundos`; registrar `IClock`→`SystemClock` como singleton. Adicionar a
      seção `"Wms"` em `appsettings.json` com placeholders (BaseUrl de homolog, Requisitante
      `02.559.193/0001-40`, Destino `14.605.065/0001-75`, CnpjRequisitante/CnpjDestino vazios,
      TimeoutSegundos 30, MaxTentativas 3, MargemRenovacaoTokenSegundos 60) e **sem** Usuario/Senha
      (comentar/documentar que vêm de `WMS__Usuario`/`WMS__Senha`). NÃO alterar o fluxo atual do
      Worker (continua só varrendo/agrupando nesta etapa).
      Files:
        c:\wms-integrador\src\KitWmsIntegrador.Worker\Program.cs
        c:\wms-integrador\src\KitWmsIntegrador.Worker\appsettings.json
      Verify: `dotnet build c:\wms-integrador\KitWmsIntegrador.sln -v minimal` — compila; opcional
      `dotnet run --project c:\wms-integrador\src\KitWmsIntegrador.Worker` falha só por falta de
      pasta de entrada, não por DI (validação de container não quebra).

- [ ] 7. Preparar o projeto de testes para testar HTTP sem rede.
      Garantir `ProjectReference` para Core (já existe). Adicionar um helper `FakeHttpMessageHandler`
      (fila/func de respostas programáveis, captura das requisições enviadas) e um `FakeClock`
      (implementa `IClock`, permite avançar o tempo) em uma pasta `Infra/` do projeto de testes.
      Criar um gerador de JWT de teste (header.payload.signature com `exp` controlável, base64url) —
      não precisa de assinatura válida, só do claim `exp`.
      Files:
        c:\wms-integrador\tests\KitWmsIntegrador.Tests\Infra\FakeHttpMessageHandler.cs
        c:\wms-integrador\tests\KitWmsIntegrador.Tests\Infra\FakeClock.cs
        c:\wms-integrador\tests\KitWmsIntegrador.Tests\Infra\JwtFake.cs
      Verify: `dotnet build c:\wms-integrador\KitWmsIntegrador.sln -v minimal` — compila.

- [ ] 8. Escrever os testes unitários do cliente (sem rede).
      Em `ClienteWmsHttpTests.cs`, cobrir (usando os JSON reais do TESTE-COMUNICACAO.md):
        - Desserialização SUCESSO (`sucesso="S"`) → `Classificar` = `Sucesso`.
        - Desserialização PENDENTE (mensagem "Foi importado e esta pendente na interface. Ressalvas :
          {Destinataria - Empresa não localizada...}") → `Classificar` = `Pendente`.
        - Desserialização ERRO/REJEITADO ("Mensagem EDI com estrutura mal-formada. Processo
          Abortado.") → `Classificar` = `Rejeitado`.
        - Montagem da query string: exatamente os 5 parâmetros, inclusive `CNPJREQUISITANTE` e
          `CNPJDESTINO` vazios, e `IDENTIFICADOR` correto por `InterfaceWms`.
        - 401 → renova token (segundo GetToken) e repete o POST UMA vez, depois sucesso.
        - NÃO-retry em erro de negócio (`sucesso="N"` rejeitado) — handler chamado uma única vez.
        - NÃO-retry em 4xx≠401 (ex.: 400) — uma chamada; retry em 5xx até `MaxTentativas`.
        - Cache de token: dois `EnviarAsync` consecutivos com token válido → GetToken chamado só
          uma vez (usando `FakeClock`); após avançar o relógio além de `exp - margem` → novo GetToken.
        - Header `Authorization: Basic ...` no GetToken e `Bearer <jwt>` no POST (assert no request
          capturado pelo `FakeHttpMessageHandler`).
      Files:
        c:\wms-integrador\tests\KitWmsIntegrador.Tests\ClienteWmsHttpTests.cs
      Verify: `dotnet test c:\wms-integrador\KitWmsIntegrador.sln -v minimal` — todos os testes
      passam (14 existentes + novos do cliente), 0 falhas.

- [ ] 9. (OPCIONAL) Modo smoke test manual do Worker (GetToken real + POST), fora de CI.
      Adicionar um caminho de execução acionado por argumento/flag (ex.: `--smoke-wms`) ou variável
      de ambiente, que lê credenciais de env, faz um GetToken real e um POST de PEDIDO vazio contra
      o homolog e imprime `transacao`/`sucesso`/classificação. Deixar claro em comentário que NÃO
      roda em testes/CI e exige `WMS__Usuario`/`WMS__Senha`. Não deve alterar o comportamento padrão
      do Worker nem rodar automaticamente.
      Files:
        c:\wms-integrador\src\KitWmsIntegrador.Worker\Program.cs (branch condicional)
        (opcional) c:\wms-integrador\src\KitWmsIntegrador.Worker\SmokeTestWms.cs
      Verify: `dotnet build` compila; execução manual local com env setado retorna
      `transacao`/`sucesso` (não executar em CI). `dotnet test` continua verde.

## Fora de escopo (manter placeholders como estão)

Construtores I50/I60 a partir do XML (Etapa 4), `IOrquestrador`/máquina de estados (Etapa 5),
`IStoreEstado`, `INotificador`, watcher contínuo e movimentação de arquivos.

## Verificação final

`dotnet test c:\wms-integrador\KitWmsIntegrador.sln -v minimal` → build verde e 100% dos testes
passando (os 14 originais + os novos do cliente). Nenhum segredo commitado; `appsettings.json` só
com placeholders e sem `Usuario`/`Senha`.

## Lacunas / suposições

- O `exp` do JWT é assumido presente (o briefing afirma que há claim `exp`). Se ausente em runtime,
  o cliente deve tratar como "sem expiração conhecida" e renovar sob 401 — já coberto pela lógica de
  401. Testes não dependem de assinatura válida, só do claim.
- Concorrência (nº de requisições simultâneas) é decisão do Orquestrador (Etapa 5); aqui o cliente é
  thread-safe para uso concorrente (renovação de token serializada por `SemaphoreSlim`).
