# Plano de implementação: Plano B fase 2 (tela de devolução no Worker)

Base lida: brief `plano-b-fase2-brief.md`, `docs\ARQUITETURA-PLANO-B-I40.md` (seção 4 "Onde hospedar"),
`ConstrutorI40`, `OpcoesDevolucao`, `PayloadI40`, `LocalizadorXmlDevolucao`, `EnvioManualI40`,
`IClienteWms`/`RespostaWms`/`RetornoWms`, `Program.cs` e csproj do Worker, `deploy\publicar.ps1`,
`FakeClienteWms`, `docs\manuais\README.md` e `gerar-manuais.ps1`. Nenhum código foi alterado.
Esforço apenas em horas. Sem push, sem envio ao WMS, sem acesso ao SRVAPPALC02.

Estimativa de horas de IA (previsão antes de iniciar): 6,0 h no total (T1 1,0; T2 2,0; T3 1,0; T4 1,5; T5 0,5).
Horas humanas: a informar pelo usuário (não estimadas aqui).

## Decisões de projeto (com motivo)

D1. Hospedagem: manter `Microsoft.NET.Sdk.Worker` e acrescentar
`<FrameworkReference Include="Microsoft.AspNetCore.App" />` no `KitWmsIntegrador.Worker.csproj`.
Motivo: `Sdk.Web` troca implicit usings, regras de conteúdo (`wwwroot`, `appsettings` em publish) e
inclui `launchSettings`, sem ganho. O FrameworkReference dá o mesmo conjunto de assemblies e o publish
self-contained inclui o shared framework ASP.NET Core automaticamente (RID win-x64 já usado por
`deploy\publicar.ps1`). Nenhum pacote NuGet novo é necessário para o Kestrel.

D2. Kestrel em serviço hospedado isolado: nova classe `ServidorTelaDevolucao : BackgroundService`
(Worker) que, só se `Devolucao:Habilitada=true` e `Porta` válida, constrói um `WebApplication`
próprio (`WebApplication.CreateSlimBuilder()`, `UseUrls("http://0.0.0.0:<Porta>")`, logging do host
principal) e o executa. Motivo: o host principal continua `Host.CreateApplicationBuilder` (não muda o
ciclo de pacotes); falha de bind/exceção fica num try/catch dentro do `ExecuteAsync`, é logada e o
serviço retorna sem derrubar o Worker. Os serviços (`ServicoDevolucao`, opções) entram no
`WebApplication` por instância (`AddSingleton(instancia)`) vindas do `IServiceProvider` do host.
Com `Habilitada=false` o serviço nem cria o Kestrel (porta não abre).

D3. Lógica de negócio fora do Worker, em Core (testável sem HTTP): `ServicoDevolucao` em
`KitWmsIntegrador.Core\Devolucao`. Endpoints e HTML ficam no Worker.

D4. Testes de endpoint SEM pacote novo: o projeto de testes ganha `FrameworkReference
Microsoft.AspNetCore.App` e `ProjectReference` ao Worker; o teste sobe o mesmo `WebApplication`
(método estático `TelaDevolucaoApp.Criar(...)`, que `ServidorTelaDevolucao` também usa) em
`http://127.0.0.1:0` e chama com `HttpClient` real. Motivo: `Microsoft.AspNetCore.TestHost`/
`Mvc.Testing` seriam pacote novo; `WebApplicationFactory<Program>` ainda exigiria expor `Program`
(top-level statements com `return`), mexendo no entrypoint. Porta 0 (efêmera) evita conflito. Se o
implementador preferir TestServer, a justificativa precisa ser registrada e pinar
`Microsoft.AspNetCore.TestHost` em versão 8.0.x exata; o padrão do plano é NÃO adicionar.

D5. Auditoria: arquivo texto simples por dia `<Estado>\devolucao\devolucao_yyyyMMdd.log`
(Estado = `OpcoesPasta` resolvido, como o resto do Worker), uma linha por chave:
`data;ip;placa|-;chave;nota;status;transacao;mensagem`. Sem credenciais, sem JSON completo. Pasta
configurável em `Devolucao:PastaAuditoria` (vazio = `<Estado>\devolucao`). Falha ao gravar auditoria
só loga.

D6. Porta padrão: 8085 (já citada no documento de arquitetura). Escuta em todas as interfaces para
ser acessível na rede interna; proteção = regra de firewall restrita à sub-rede interna (documentar,
não aplicar). Sem login por decisão D do usuário.

## Formato do JSON do endpoint

GET `/` -> HTML único. Injeta no HTML, por substituição de marcador, `EnviarPlacaPadrao` e
`MaxChavesPorEnvio` (ou o HTML busca `GET /api/config`).
GET `/api/config` -> `{ "enviarPlacaPadrao": false, "maxChaves": 30 }`.
GET `/api/saude` -> `{ "status": "ok" }`.

POST `/api/devolucao/enviar`, `Content-Type: application/json`:

```json
{ "chaves": ["35251201838723009850550060156712411843547443"], "enviarPlaca": false, "placa": "" }
```

Resposta 200 (processou; erros por chave vêm no corpo):

```json
{
  "enviarPlaca": false,
  "total": 1,
  "enviadas": 1,
  "transacao": "4042207",
  "resultados": [
    { "chave": "3525...3547443", "nota": "15671241", "status": "Enviada", "sucesso": true,
      "mensagem": "Registrado com sucesso" }
  ]
}
```

`status` (enum string): `Enviada`, `ErroWms`, `XmlNaoEncontrado`, `ChaveInvalida`, `Duplicada`
(duplicadas são removidas antes; devolver o item `Duplicada` só se útil à tela; padrão: removidas
em silêncio e `total` conta as únicas), `ErroInterno`. `mensagem` em português para os
casos locais (ex.: "XML não encontrado na retenção de 60 dias", "Dígito verificador inválido");
para `ErroWms` é o texto do WMS sem alteração.
Respostas 400 com `{ "erro": "<texto>" }` para: corpo inválido, lista vazia, mais de 30 chaves
(`Devolucao:MaxChavesPorEnvio`, padrão 30), `enviarPlaca=true` sem placa. 503 com `{ "erro": ... }` se
`Devolucao:NomeMotorista` vazio e `enviarPlaca=true`, ou credenciais WMS ausentes. 500 genérico
(sem stack) para exceção não tratada, com log completo no servidor.
Limite do corpo: `MaxRequestBodySize` 64 KB (30 chaves cabem com folga).

## Impedimentos

Nenhum impedimento técnico concreto para a tela no Worker. Riscos registrados (nenhum bloqueia):

R1. .NET 8 no Windows Server 2012 R2: está na lista de SOs suportados do .NET 8 (já registrado em
`docs\AMBIENTE-SERVIDOR.md` e confirmado pela produção atual rodando o Worker self-contained). O
Kestrel usa transporte por sockets, sem HTTP.sys/IIS e sem `netsh urlacl`. Pré-requisitos do SO (UCRT
KB2999226 e atualização de 2014 KB2919355) já estão satisfeitos porque o Worker atual roda lá.
Risco residual: o 2012 R2 está fora do suporte padrão (só ESU); não verificado nesta etapa porque o
servidor não pode ser acessado. O primeiro teste real da tela no servidor fica para o usuário.
R2. Tamanho do pacote self-contained aumenta (assemblies do ASP.NET Core). Medir `dotnet publish -c
Release -r win-x64 --self-contained` antes e depois (em pasta temporária) e registrar o delta em MB
no relatório; não é bloqueio. Não usar trimming/ReadyToRun (já vetado em `publicar.ps1`).
R3. Duas instâncias de `ClienteWmsHttp` (Orquestrador singleton e `ServicoDevolucao` singleton) =
dois tokens JWT da mesma conta, pois o cache de token é por instância (`_token`/`_tokenLock`). O
histórico cita 401 intermitente com dois clientes. Mitigação adotada: aceitar (GetToken já tem
retry) e anotar em `docs\ARQUITETURA-PLANO-B-I40.md`; alternativa se aparecer 401: compartilhar a
instância (registrar `IClienteWms` como singleton) numa mudança separada.
R4. Porta aberta sem login na rede interna: mitigação = flag desligada por padrão, firewall restrito à
sub-rede, auditoria com IP de origem. Registrar nos manuais.
R5. Envio nunca é testado contra o WMS nesta etapa; os testes usam `FakeClienteWms`.

## Itens (ordem de dependência; um commit por item, mensagens em português, Conventional Commits)

- [ ] 1. Tarefa 1: opções e construtor. `OpcoesDevolucao`: `TipoOperacaoNotaPadrao` = "IMP"; novas
      `TipoOperacaoNotaComPlacaPadrao`="ICD", propriedade `TipoOperacaoNotaComPlaca`, `EnviarPlacaPadrao`=false,
      `PastaAuditoria` (string), `MaxChavesPorEnvio`=30; `Porta` passa a `int` com padrão 8085 (avaliar impacto em
      `OpcoesDevolucaoTests`; manter `int?` se quebrar o contrato, mas default 8085). `IConstrutorI40`/`ConstrutorI40`:
      novas assinaturas `Construir(NotaFiscal nota, bool enviarPlaca, string? placa = null)` e `ConstruirJson(...)`;
      `enviarPlaca=false` -> `TipoOperacaoNota` = `Devolucao:TipoOperacaoNota`, `NomeMotorista` e
      `PlacaVeiculoPrevistoNota` em branco, sem exigir nada; `enviarPlaca=true` -> `TipoOperacaoNotaComPlaca`, placa
      obrigatória (`ArgumentException`), `NomeMotorista` obrigatório (`InvalidOperationException`). Remover a antiga
      assinatura `(nota, placa)` e ajustar chamadores. `EnvioManualI40`: `--placa` vira opcional e liga o envio da
      placa; sem `--placa` envia IMP; ajustar mensagens e o arquivo de log (`enviarPlaca=`). `appsettings.json` do Worker e
      `deploy\config-exemplo\appsettings.Production.json`: bloco `Devolucao` com `TipoOperacaoNota":"IMP"`,
      `TipoOperacaoNotaComPlaca":"ICD"`, `EnviarPlacaPadrao":false`, `Porta":8085`, `MaxChavesPorEnvio":30`,
      `PastaAuditoria":""`, `_comentario` explicando a regra da placa, o resultado do ICD no homolog e que `Habilitada`
      continua `false` (manter `I60:Habilitada=false`). Comentário de `InverterPartes`: validado, default false.
      Testes: atualizar `ConstrutorI40Tests`, `OpcoesDevolucaoTests`; novos casos: sem placa -> IMP + motorista/placa
      em branco + nada exigido; com placa -> ICD + placa maiúscula e trim + motorista; com placa sem placa -> exceção;
      com placa sem motorista -> exceção; sem placa e sem motorista não falha.
      Files: src\KitWmsIntegrador.Core\Configuracao\OpcoesDevolucao.cs, Construtores\IConstrutorI40.cs,
      Construtores\ConstrutorI40.cs, src\KitWmsIntegrador.Worker\EnvioManualI40.cs, appsettings.json,
      deploy\config-exemplo\appsettings.Production.json, tests\KitWmsIntegrador.Tests\ConstrutorI40Tests.cs,
      OpcoesDevolucaoTests.cs
      Verify: `dotnet build c:\wms-integrador\KitWmsIntegrador.sln` (0 avisos/0 erros) e
      `dotnet test c:\wms-integrador\KitWmsIntegrador.sln` verdes.

- [ ] 2. Núcleo da tela em Core (sem HTTP): `ServicoDevolucao` (e `IServicoDevolucao`) em
      `Core\Devolucao\ServicoDevolucao.cs`, DTOs `SolicitacaoDevolucao`, `ResultadoChaveDevolucao`,
      `ResultadoLoteDevolucao`, enum `StatusChaveDevolucao`, `ValidadorChavesDevolucao` (normaliza: trim, remove
      não-dígitos só de espaços/quebras, dedup preservando ordem; reutiliza `LocalizadorXmlDevolucao.ValidarChave`).
      Fluxo: valida limite e placa; para cada chave única: valida DV -> `ChaveInvalida`; localiza XML
      (`LocalizadorXmlDevolucao(OpcoesPasta.CaminhoProcessados)`) -> `XmlNaoEncontrado`; `LeitorNotaFiscalXml.ParseArquivo`
      (erro de parse -> `ErroInterno` por chave); `ConstrutorI40.Construir(nota, enviarPlaca, placa)` por nota; junta as
      `NotaFiscalI40` válidas num único `PayloadI40` (até 30) e envia UMA vez por `IClienteWms.EnviarAsync(InterfaceWms.I40,
      payload)`; mapeia `RespostaWms.Retornos` por `Documento` (= número da nota; se o `documento` vier vazio ou não
      casar, usar a posição quando `Retornos.Count == notas.Count`, senão repetir o primeiro retorno/mensagem geral
      para todas) com `Sucesso == "S"` -> `Enviada`, senão `ErroWms` com a mensagem textual. Exceção de rede/WMS no envio
      -> todas as notas do lote viram `ErroWms` com a mensagem, sem derrubar. Chaves inválidas/não encontradas não
      bloqueiam as demais. Auditoria: `AuditoriaDevolucao` (classe pequena que grava a linha da D5; dependências
      `IClock`, `OpcoesPasta`, `OpcoesDevolucao`). Log estruturado de cada envio no `ILogger`.
      Files: src\KitWmsIntegrador.Core\Devolucao\ServicoDevolucao.cs, ValidadorChavesDevolucao.cs, AuditoriaDevolucao.cs,
      Modelos de resultado
      Verify: build da solução 0 avisos/0 erros.

- [ ] 3. Testes unitários do núcleo (junto do item 2, mesmo commit ou commit seguinte `test:`).
      Novo `ServicoDevolucaoTests.cs` e `ValidadorChavesDevolucaoTests.cs` usando `FakeClienteWms` (já em
      tests\...\Infra) e XMLs de `Recursos` copiados para uma pasta Processados temporária (padrão de
      `LocalizadorXmlDevolucaoTests`). Casos: DV inválido; duplicadas removidas; mais de 30 -> erro; mistura (1 ok, 1 sem
      XML, 1 chave inválida) gera um único envio com 1 nota e as demais com status local; placa desligada ->
      payload IMP sem placa; placa ligada sem placa -> erro; placa ligada -> ICD + placa; mapeamento por documento
      (2 notas, 1 sucesso e 1 erro); retorno único para o lote; exceção do cliente; auditoria grava linha sem segredo.
      Verify: `dotnet test c:\wms-integrador\KitWmsIntegrador.sln --filter "FullyQualifiedName~Devolucao"` verde.

- [ ] 4. Tarefa 2: host Kestrel e página. Worker.csproj: `FrameworkReference Microsoft.AspNetCore.App`.
      Novos arquivos em `src\KitWmsIntegrador.Worker\TelaDevolucao\`: `TelaDevolucaoApp.cs` (método estático
      `WebApplication Criar(IServicoDevolucao servico, OpcoesDevolucao opcoes, ILoggerFactory lf, string urls)` que mapeia
      `GET /`, `GET /api/config`, `GET /api/saude`, `POST /api/devolucao/enviar`, com try/catch que devolve 500 genérico;
      `Content-Security-Policy: default-src 'self'; style-src 'unsafe-inline'; script-src 'unsafe-inline'`,
      `Cache-Control: no-store`), `ServidorTelaDevolucao.cs` (BackgroundService da D2), `PaginaDevolucao.html`
      como `EmbeddedResource` (lida em memória, sem arquivo solto no publish; csproj `<EmbeddedResource
      Include="TelaDevolucao\PaginaDevolucao.html" />`). Program.cs: registrar `AddSingleton<IServicoDevolucao,
      ServicoDevolucao>()`, `AddSingleton<AuditoriaDevolucao>()` e `AddHostedService<ServidorTelaDevolucao>()`.
      HTML: único, autocontido, `lang="pt-BR"`, Arial, paleta #17479E/#54B6E7/#009A83, logo base64 extraída de
      `docs\apresentacao\apresentacao.html` (linha 125, mesmo método do `gerar-manuais.ps1`), `<label for>` em cada campo,
      `:focus-visible` com contorno de 3 px, contraste mínimo 4,5:1, tudo operável por teclado, `aria-live="polite"` na área de
      resultado, `<table>` com `<caption>` e `<th scope>`. Interação: textarea ou input de chave onde Enter adiciona à
      lista (também cola várias linhas), lista com botão remover por item, contador "n de 30", validação de 44 dígitos + DV
      (módulo 11) no JS, remoção de duplicadas, checkbox "Enviar placa do veículo" (estado inicial = `enviarPlacaPadrao` de
      `/api/config`), campo placa só visível/obrigatório quando marcado, botão Enviar desabilitado durante a chamada, tabela
      de resultado (chave, nota, status em texto + cor, mensagem). Sem fetch externo, sem CDN, sem fontes externas.
      Files: src\KitWmsIntegrador.Worker\KitWmsIntegrador.Worker.csproj, Program.cs, TelaDevolucao\*.cs,
      TelaDevolucao\PaginaDevolucao.html
      Verify: `dotnet build c:\wms-integrador\KitWmsIntegrador.sln -c Debug` e `-c Release` (0 avisos/0 erros);
      `dotnet test` verdes (nada quebrou); com `Devolucao:Habilitada=false` o Worker sobe sem abrir a porta (conferido no teste do
      item 5).

- [ ] 5. Tarefa 3 (parte HTTP): testes de endpoint. Em `tests\KitWmsIntegrador.Tests\KitWmsIntegrador.Tests.csproj`
      acrescentar `<FrameworkReference Include="Microsoft.AspNetCore.App" />` e `ProjectReference` ao Worker (sem
      pacote novo). Novo `TelaDevolucaoEndpointTests.cs`: sobe `TelaDevolucaoApp.Criar` em `http://127.0.0.1:0` com
      `ServicoDevolucao` real + `FakeClienteWms` + pasta Processados temporária; testa `GET /` (200, `text/html`, contém
      `<label`, não contém "http://" ou "https://" de recursos externos), `GET /api/config`, `GET /api/saude`,
      `POST /api/devolucao/enviar` (sucesso IMP sem placa; placa ligada sem placa -> 400; 31 chaves -> 400; chave inválida e
      sem XML no resultado 200; JSON malformado -> 400) e que `ServidorTelaDevolucao` com `Habilitada=false` não abre
      porta. Verify: `dotnet test` completo verde; registrar contagem (hoje 400 aprovados/9 ignorados + novos). Se o
      csproj de testes acusar conflito de versão ao referenciar o Worker (exe), registrar e ajustar com
      `FrameworkReference` apenas, não com pacote.

- [ ] 6. Tarefa 4: documentação e manuais. `docs\ARQUITETURA-PLANO-B-I40.md`: resultados reais do teste (tabela das 3
      tentativas, dados do brief), decisões A-D, estado da fase 2, riscos R1-R4 acima, endpoints e JSON.
      `docs\ROTEIRO-TESTE-I40.md`: como testar a tela nesta máquina (`dotnet run --project
      src\KitWmsIntegrador.Worker` com `Devolucao__Habilitada=true`, `Devolucao__Porta`, `Pastas__Entrada=C:\wms-teste\entrada`,
      credenciais por `Read-Host -AsSecureString` convertidas só em memória, `WMS__BaseUrl` homolog
      `https://friozem.assistsoftware.com.br:8888/wmsapi/api`, abrir `http://localhost:<porta>/`; nunca imprimir senha).
      `docs\manuais\MANUAL-USUARIO.md`: seção "Devolução: tela de chaves" (passo a passo simples, o que cada status
      significa, quando marcar a placa). `docs\manuais\MANUAL-TECNICO.md`: habilitar, porta, firewall, auditoria, flags,
      isolamento. `docs\OPERACAO.md` e `docs\INSTALACAO-SERVIDOR.md`: regra de firewall restrita à rede interna (comando de
      exemplo `New-NetFirewallRule` com `-RemoteAddress` placeholder, documentado, NÃO executado; no 2012 R2 o cmdlet existe)
      e como ligar em produção (editar `appsettings.Production.json` ou env do NSSM, reiniciar serviço), sem ligar nada.
      `docs\diario\2026-11-10.md`: resultado do teste da I40 e a fase 2; horas humanas "a informar pelo usuário",
      horas de IA reais. Sem valores monetários. Regenerar:
      `powershell -NoProfile -ExecutionPolicy Bypass -File c:\wms-integrador\docs\manuais\gerar-manuais.ps1` e os PDFs via Edge
      headless (comandos do `docs\manuais\README.md`; alternativa de impressão manual se der 0 bytes).
      Verify: HTML/PDF regenerados existem e têm tamanho > 0; `Select-String` por `R\$|US\$|custo|valor/hora` nos
      arquivos tocados não encontra nada novo.

- [ ] 7. Tarefa 5: verificação visual. Subir o Worker localmente com `Devolucao__Habilitada=true`,
      `Devolucao__Porta=<livre, ex. 8095>`, `Pastas__Entrada=<temp vazia>` e SEM `WMS__Usuario`/`WMS__Senha` (só renderiza;
      nenhum envio). Screenshot 1280x800 com `& 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe' --headless=new
      --disable-gpu --window-size=1280,800 "--screenshot=<png>" http://localhost:<porta>/` em pasta temporária; ler o PNG
      e conferir layout/contraste/logo. Segundo screenshot com checkbox de placa marcado: usar uma query `?placa=1` que só
      pré-marca (aceitar no JS) OU `Devolucao__EnviarPlacaPadrao=true` na subida (preferido: sem código extra). Se a página
      permitir, um terceiro com resultado simulado só se vier de dados fake do HTML (não implementar mock em produção).
      Encerrar o processo (`Stop-Process` do PID iniciado) e apagar PNGs e pastas temporárias.
      Verify: PNG lido, itens conferidos; porta liberada; `git status` sem arquivos temporários.

- [ ] 8. Fechamento: `dotnet build c:\wms-integrador\KitWmsIntegrador.sln -c Debug` e `-c Release` (0 avisos/0 erros);
      `dotnet test` (contar aprovados/ignorados); conferir `Devolucao:Habilitada=false` e `I60:Habilitada=false` em
      `src\KitWmsIntegrador.Worker\appsettings.json` e `deploy\config-exemplo\appsettings.Production.json` (grep dos
      dois valores, inclusive `I60`); `dotnet list package` sem pacote novo; medir o delta de publish (R2). Escrever o
      relatório final `c:\wms-integrador\.agents\tasks\plano-b-fase2.md` (arquivos alterados, build/testes, screenshots
      conferidos, o que falta para produção, horas de IA gastas).

## Ordem de commits (locais, pequenos, nunca push)

1. `feat(devolucao): placa opcional, IMP por padrão e ICD com placa na I40` (item 1 inclui testes e config).
2. `feat(devolucao): ServicoDevolucao com lote, validação de chave e auditoria` (item 2).
3. `test(devolucao): testes do ServicoDevolucao e do validador de chaves` (item 3).
4. `feat(worker): tela web de devolução em Kestrel isolado (flag desligada)` (item 4).
5. `test(worker): testes de endpoint da tela de devolução` (item 5).
6. `docs(devolucao): arquitetura, roteiro, manuais, operação e diário` (item 6, com HTML/PDF regenerados).
7. `docs: relatório da fase 2` (itens 7-8). Cada commit deve compilar e passar nos testes.

## Pontos para o implementador

- Arquivos do repo têm acentos em UTF-8; preservar a codificação ao editar (alguns saem com mojibake no console, o
  arquivo está correto). Usar `fs_write`/`str_replace`, não `Set-Content` sem `-Encoding utf8`.
- Nenhum valor monetário em qualquer doc; esforço só em horas (regra do projeto).
- Não imprimir `WMS__Usuario`/`WMS__Senha`; não gravar segredo na auditoria.
- Não alterar `Devolucao:Habilitada` nem `I60:Habilitada` (ambos `false`).
