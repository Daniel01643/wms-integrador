# Plano da release 2026-11-16 (401 definitivo, tipos de separação I50, espera de coleta)

Fonte: `.agents/tasks/investigacao-401.md` + leitura do código (ClienteWmsHttp, ProcessadorLote, GerenciadorPacote,
FechadorPacote, OrganizadorArquivos, Orquestrador, Program.cs, SmokeTestWms, OpcoesWms/I50/Pacote, deploy\*.ps1,
appsettings, testes). Trabalho direto na `main` de `c:\wms-integrador`, sem push, commits locais em pt-BR.
Repositório público: nunca commitar `*.xml`, `*.zip`, dados reais, segredos, nem valores monetários.

**Decisão sobre o workflow:** não decompor em FEATs. B depende de um único componente coeso (cliente HTTP → guarda →
processador → gerenciador) e C/D são trocas de configuração; o loop existente (`loop-implementacao`, máx. 3 iterações,
verdict em `.agents/tasks/review-release-1611.json`) implementa este plano na ordem abaixo. Nada do tail é substituído;
o loop de docs/pacote (`review-docs-1611.json`) e o relatório `relatorio-release-1611.md` seguem como estão.

**Esforço (só horas):** estimativa IA ≈ 5 a 7 h (B ≈ 4 a 5 h, C ≈ 0,5 h, D ≈ 0,5 h, revisão/ajustes ≈ 1 h). Horas humanas: informadas pelo usuário, não estimadas aqui.

Comandos reais: `dotnet build KitWmsIntegrador.sln -c Release` e `dotnet test KitWmsIntegrador.sln -c Release`
(linha de base da release anterior: 905 aprovados, 9 ignorados, 0 falhas).

---

## 1. Fatos do código que ditam o design

- `ClienteWmsHttp` (singleton) tem `_tokenLock` (SemaphoreSlim) com dupla checagem; `ObterTokenAsync` faz 3 GetToken com
  backoff 0,5/1 s e, esgotado, `EnsureSuccessStatusCode()` lança `HttpRequestException`. Cache só guarda token bom.
- `Orquestrador` não captura exceção; `ProcessadorLote.ProcessarOndaAsync` captura `Exception` genérica e manda a nota para
  `Erro` (categoria `Outro`, motivo "Exceção ao integrar: ... 401 ..."). **É aqui que as 383 notas de 15/11 foram para Erro.**
- Após `ProcessarAsync`, `GerenciadorPacote` sempre vai para `Fechando` e fecha o pacote; `ProcessadorLote` move ZIPs
  incondicionalmente (`MoverZipTratado`).
- I51/I60 (`ProcessadorI60Separacao`, `ServicoSeparacaoI51`) tratam `HttpRequestException` como falha de comunicação com
  backoff (nunca rejeitam a nota). Por isso a exceção nova **deve herdar de `HttpRequestException`** para esses fluxos
  continuarem iguais (e passarem a não martelar o GetToken durante o cooldown).
- Erro vai para `Erro\<pacote>\<Categoria>\<arquivo>` com sidecar `<arquivo>.motivo.txt` (`OrganizadorArquivos`). O sidecar
  hoje não registra a origem (raiz/Puxada/PuxadaTSP).
- E-mail: `IEnviadorEmail` (devolve `NaoConfigurado` se `Email:Habilitado=false`), destinatários por
  `FonteDestinatariosFechamento` (txt a quente, fallback `Email:Destinatarios`). `DadosPacote(Identificador, Medicao)` em
  `Relatorio/MedicaoTempo.cs`; `EtapasPacote` em `Pacote/EtapasPacote.cs`.
- `SmokeTestWms.cs` é o molde da CLI; `Program.cs` despacha as CLIs antes de montar o host.
- `src\...\appsettings.json` traz `Wms:BaseUrl` com `:8888` (homolog) e `I50:TipoSeparacao = "RES;FEF;PLI"`,
  `Pacote:SilencioSegundos = 15`. `deploy\config-exemplo\appsettings.Production.json` tem Wms/Pacote/I50 parciais.
- `tests\...\Infra`: `FakeHttpMessageHandler` (responder `(req, n)`), `FakeClock`, `JwtFake.ComExpiracao`,
  `AmbientePacote` (molde de pacote ponta a ponta), `FakeClienteWms`.

---

## 2. Decisões de design (B)

| Decisão | Escolha | Motivo |
|---|---|---|
| Onde fica a regra | Novo `GuardaAutenticacaoWms` (singleton) usado só por `ClienteWmsHttp`; `ClienteWmsHttp` recebe a guarda e o alerta por parâmetros opcionais no fim do construtor (nulos = guarda interna padrão), para os testes e o `SmokeTestWms` atuais continuarem compilando | menor blast radius |
| Exceção | `AutenticacaoWmsIndisponivelException : HttpRequestException` com `Status` (int?), `EmCooldown` (bool), `ProximaTentativaUtc` | preserva I51/I60/devolução |
| Falhas que contam | Qualquer falha final do GetToken: 401, 403, 5xx, timeout, rede, 404 e corpo sem token | nota nunca vai para Erro por problema de autenticação/infra |
| Retries | `Wms:TokenTentativasImediatas` (padrão 2, backoff 0,5 s) por episódio; depois cooldown. Durante cooldown **zero** chamadas HTTP: lança imediatamente | acaba a tempestade (1.352 chamadas em 10 min) |
| Cooldown global | `Wms:CooldownTokenSegundos` = `"60;120;300;600;900"` (lista com `;`, último é o teto), índice = falhas consecutivas − 1; sucesso zera | pedido: 1, 2, 5, 10, 15 min |
| Single-flight | Mesmo `_tokenLock`; cooldown é checado dentro do lock | já serializa GetToken |
| Token com validade máxima (nota do usuário) | `Wms:ValidadeMaximaTokenMinutos` (padrão 3; 0 = só o exp do JWT). Token válido se `agora < min(exp − Margem, obtidoEm + ValidadeMax)`. `_tokenObtidoEm` guardado | pedido do usuário 16/11 |
| Renovação proativa falha | Se `agora < exp` do JWT (token ainda utilizável; sem exp conhecido conta como utilizável) usa o token atual, registra falha na guarda (cooldown) e **não aborta**; só lança quando não há token utilizável | pedido do usuário |
| 401 no POST | Mantém: invalida e renova uma vez. A renovação passa pelo cooldown; se o GetToken falhar, lança `AutenticacaoWmsIndisponivelException` (token foi invalidado, então não há fallback) | comportamento atual + abort seguro |
| Log do GetToken | Em falha: status, `WWW-Authenticate` (truncado 200), corpo truncado 200 sem caracteres de controle, request id (`X-Request-Id`/`Request-Id`/`X-Correlation-Id`/`traceparent`), tentativa. Em sucesso: validade real do JWT (exp − agora, minutos) e validade efetiva usada. Nunca token, Authorization, usuário ou senha | pedido 3 + nota |
| Alerta | Interface `IAlertaAutenticacaoWms` + `AlertaAutenticacaoWmsEmail` (usa `IEnviadorEmail`, `FonteDestinatariosFechamento`, respeita `Email:Habilitado`). Disparado pelo `ClienteWmsHttp` **fora** do `_tokenLock`, dentro de try/catch (falha de e-mail só loga). Um e-mail na transição Normal→Indisponível e um na volta (primeiro sucesso). Chave `Wms:AlertaAutenticacao` (padrão true) | uma vez por indisponibilidade |
| Aborto do pacote | `ProcessadorLote` captura `AutenticacaoWmsIndisponivelException` **antes** do `catch (Exception)`: nota atual **não** é registrada nem movida; flag compartilhada interrompe todas as ondas; propriedade `UltimoAbortoAutenticacao`. ZIP só é movido se todos os seus XMLs saíram da pasta temporária | arquivos ficam na entrada |
| Estado "aguardando autenticação" | `GerenciadorPacote` recebe `IEstadoAutenticacaoWms` (opcional; implementado pela guarda). Em Ocioso/Coletando com cooldown ativo: não abre pacote nem tira foto, loga uma vez "aguardando autenticação até HH:mm:ss" e expõe `AguardandoAutenticacao`/`ProximaTentativaAutenticacaoUtc` (sem novo valor no enum `EstadoPacote`) | evita churn de pacotes vazios |
| Pacote abortado | Fecha com o que já foi tratado (se 0 registros, `FecharAsync` já não gera relatório); arquivos restantes formam o próximo pacote depois do cooldown | retry automático |
| Contador no relatório | `EtapasPacote` ganha `FalhasGetToken` e `AbortadoPorAutenticacao` (opcionais); `DadosPacote` ganha `AutenticacaoWms?`; HTML/PDF/e-mail do fechamento mostram uma linha só quando > 0 | pedido 4; zero muda nada para relatórios existentes |
| Sidecar | `MotivoSidecar` ganha linha `Origem: <Puxada|PuxadaTSP|raiz>` (a origem já existe no `Ctx`) para o reprocesso futuro voltar ao lugar certo | notas já em Erro não têm; ver script |

Estados da guarda: `Normal` → (falha final) `Indisponivel` (cooldown n) → (cooldown expira) tentativa única: sucesso → `Normal`
(alerta de recuperação) ou falha → `Indisponivel` (cooldown n+1, sem novo alerta).

---

## 3. Itens (ordem de dependência)

- [ ] 1. **C: tipos de separação I50.** Trocar `TipoSeparacao` para `PIC;FEF;PLL` (`TipoSeparacao2` fica `AGL;FEF;PLL`).
      Arquivos: `src/KitWmsIntegrador.Core/Configuracao/OpcoesI50.cs` (default e XML-doc), `src/KitWmsIntegrador.Worker/appsettings.json`
      (seção I50 e comentário), `deploy/config-exemplo/appsettings.Production.json` (acrescentar `TipoSeparacao`/`TipoSeparacao2` na seção I50
      como valores explícitos, para ficar visível), `docs/TEMPLATES-INTERFACES.md` (linhas ~66 e ~98), `docs/exemplos/I50-enriquecida-exemplo.json`
      (3 ocorrências), `docs/MAPEAMENTO-DADOS.md` e `docs/manuais/*` se citarem (rodar `git grep -n -E "RES;|PLI|tipoSeparacao"` fora de `artifacts/` e `.agents/`; não editar
      `artifacts/` de releases antigas), `tests/KitWmsIntegrador.Tests/ConstrutorI50Tests.cs` (linhas ~33 e ~277: valores e asserts).
      `ConstrutorI50.cs` já usa `_i50.TipoSeparacao/2` no cabeçalho (l.67-68) e no item (l.88-89): não mudar o código, só provar com teste.
      Novos testes: (a) `OpcoesI50` default = `PIC;FEF;PLL`/`AGL;FEF;PLL`; (b) o `appsettings.json` do Worker carregado por `ConfigurationBuilder` bate com isso
      (seguir o padrão de `ConfiguracaoReleaseTests`/`PlacaTspConfiguracaoTests`); (c) o payload I50 tem os dois valores no cabeçalho **e** em todos os itens.
      Verify: `dotnet test KitWmsIntegrador.sln -c Release --filter "FullyQualifiedName~ConstrutorI50|FullyQualifiedName~Configuracao"` passa; commit `feat: tipos de separacao I50 PIC;FEF;PLL`.

- [ ] 2. **D: espera de coleta.** `SilencioSegundos` 15 → 20; `JanelaMaximaSegundos` fica 180 (o log de produção mostra lotes de 15 a 27 s e ~1 min no total, que cabem no teto;
      o silêncio é intervalo entre arquivos, não duração do lote); `EstabilizacaoSegundos` 3 e `ModoJanela=Silencio` inalterados.
      Arquivos: `OpcoesPacote.cs` (default e XML-doc), `Worker/appsettings.json` (valor e `_comentario`), `deploy/config-exemplo/appsettings.Production.json`
      (`SilencioSegundos` 20, comentário), `docs/OPERACAO.md` (l.~88, 229, 466), `docs/INSTALACAO-SERVIDOR.md` (l.~322), `docs/manuais/MANUAL-TECNICO.md/.html`
      (os manuais e o deck são atualizados no passo de docs; aqui só OPERACAO/INSTALACAO, que descrevem config), `tests/.../JanelaSilencioTests.cs` e demais testes que dependam do default 15
      (ajustar os que usam o default; os que fixam o valor ficam). Novo teste: default 20/180/3 e `appsettings.json` do Worker idêntico.
      Risco: se algum intervalo real entre arquivos passar de 20 s, o lote divide; mitigação é override `Pacote__SilencioSegundos` sem release (documentar).
      Verify: `dotnet test ... --filter "FullyQualifiedName~JanelaSilencio|FullyQualifiedName~GerenciadorPacote|FullyQualifiedName~Pacote"` passa; commit `feat: silencio de coleta 20 s`.

- [ ] 3. **B1: núcleo de autenticação no cliente WMS.** Criar em `src/KitWmsIntegrador.Core/ClienteWms/`: `AutenticacaoWmsIndisponivelException.cs`,
      `GuardaAutenticacaoWms.cs` (+ `IEstadoAutenticacaoWms`), `IAlertaAutenticacaoWms.cs`, `AlertaAutenticacaoWmsEmail.cs`. Alterar `OpcoesWms.cs`
      (`ValidadeMaximaTokenMinutos=3`, `TokenTentativasImediatas=2`, `CooldownTokenSegundos="60;120;300;600;900"` com `ListaCooldown` parseada e validada, `AlertaAutenticacao=true`)
      e `ClienteWmsHttp.cs` conforme a tabela da seção 2: `GarantirTokenAsync` (limite de validade, cooldown, fallback do token utilizável), `ObterTokenAsync` (diagnóstico, tentativas imediatas,
      lançar a exceção nova), `TokenValido`, `_tokenObtidoEm`, alerta fora do lock, método público `TestarTokenAsync(CancellationToken)` que faz **um** GET sem cache/cooldown e devolve
      (status, ok, validade em minutos) para a CLI do item 5.
      A guarda: thread-safe (lock simples), usa `IClock`, expõe `Bloqueada(agora)`, `ProximaTentativaUtc`, `FalhasConsecutivas`, `TotalFalhasGetToken`, `Estado`; `RegistrarFalha` devolve se é a primeira da indisponibilidade; `RegistrarSucesso` devolve se houve recuperação.
      Registro DI em `Program.cs` (singletons de guarda/alerta; `IEstadoAutenticacaoWms` aponta para a mesma guarda) e `RegistroClienteWms.cs` (continua usando `ActivatorUtilities`).
      Testes novos `tests/KitWmsIntegrador.Tests/AutenticacaoWmsTests.cs` (FakeHttpMessageHandler + FakeClock + alerta fake que conta chamadas):
      tempestade 401 com N chamadas de envio produz **1** episódio (`TokenTentativasImediatas` GetToken) e depois **0** chamadas durante o cooldown; após o cooldown, 1 única tentativa; recuperação zera cooldown e dispara 1 alerta de recuperação;
      1 alerta de indisponibilidade mesmo com várias falhas; escada 60/120/300/600/900 e teto; 403/5xx/timeout/rede/corpo sem token também lançam a exceção nova; token nunca aparece no log (logger de captura);
      validade máxima: renova aos 3 min (relógio falso), respeita exp menor que 3 min, `0` desliga o limite; falha na renovação com token ainda válido pelo exp **não** lança e usa o token atual, tentando de novo só após o cooldown;
      POST 401 renova uma vez (teste existente em `ClienteWmsHttpTests` continua passando); concorrência: 10 chamadas paralelas com token expirado geram 1 GetToken (single-flight).
      Verify: `dotnet test ... --filter "FullyQualifiedName~AutenticacaoWms|FullyQualifiedName~ClienteWmsHttp|FullyQualifiedName~ClienteI51|FullyQualifiedName~ProcessadorI60Separacao"` passa; build sem avisos; commit `feat: guarda de autenticacao do WMS (cooldown, validade do token, alerta)`.

- [ ] 4. **B2: pacote aborta sem mandar para Erro.** `ProcessadorLote.cs`: capturar a exceção nova em `ProcessarOndaAsync` (antes do `catch (Exception)`), flag de aborto, `UltimoAbortoAutenticacao` (reiniciada a cada `ProcessarAsync`), pular ondas/notas restantes, não mover ZIP com XML pendente; `MotivoSidecar` ganha `Origem`.
      `GerenciadorPacote.cs`: parâmetro opcional `IEstadoAutenticacaoWms? autenticacao`, gate em Ocioso/Coletando logo depois de `RepetirMovesPendentes`, propriedades `AguardandoAutenticacao`/`ProximaTentativaAutenticacaoUtc`, tratamento do aborto (fecha com o que foi tratado, volta a Ocioso, mantém `_diario` consistente) e snapshot do contador de falhas na abertura.
      `EtapasPacote.cs`, `MedicaoTempo.cs` (`DadosPacote`), `FechadorPacote.cs`, `RenderizadorHtml.cs`, `RenderizadorPdf.cs`, `CorpoEmailResumo.cs`: linha "Falhas de autenticação (GetToken): N" e "Pacote interrompido: notas aguardam na entrada" só quando > 0.
      Testes (novo `tests/.../Autenticacao401PacoteTests.cs`, usando `Orquestrador` real + `ClienteWmsHttp` com `FakeHttpMessageHandler`, seguindo `AmbientePacote`/`PacoteEndToEndTests`): pacote com várias notas e GetToken sempre 401 → **nenhum** arquivo em `Erro` nem em `Processados`, todos os XMLs continuam na entrada, 1 chamada GetToken por episódio; ciclos seguintes dentro do cooldown não abrem pacote nem chamam o WMS; relógio avança além do cooldown e GetToken volta 200 → o pacote é processado inteiro (todas em `Processados`); alerta de indisponibilidade 1x e de recuperação 1x; POST 401 + GetToken 200 renova uma vez e a nota integra; 401 no meio do pacote: notas já integradas ficam em `Processados`, restantes na entrada; ZIP com falha não é movido; relatórios existentes inalterados quando o contador é 0 (testes atuais de relatório/e-mail passam sem edição, exceto os que constroem `DadosPacote`).
      Risco registrado: ao reprocessar um ZIP mantido, XMLs já integrados são reenviados e o WMS responde "já existe" (vai para `Processados`, idempotente). XML solto da PuxadaTSP consome um novo sequencial de onda no retry (`RegistroOndasTsp.ObterOuCriar("", "")`); o implementador verifica se dá para evitar sem mexer no contrato e, se não der, documenta.
      Verify: `dotnet test KitWmsIntegrador.sln -c Release` completo, 0 falhas; commit `feat: falha de autenticacao aborta o pacote sem mover notas para Erro`.

- [ ] 5. **B3: CLI `--testar-token`.** Novo `src/KitWmsIntegrador.Worker/TestarTokenWms.cs` (molde `SmokeTestWms.cs`): carrega `appsettings.json`, `appsettings.Production.json` (opcional, também de `config\` ao lado do exe se existir, como o serviço) e variáveis de ambiente; monta `ClienteWmsHttp` com o handler TLS 1.2/1.3 de `RegistroClienteWms` (extrair o handler para método estático reutilizável sem mudar o comportamento); chama `TestarTokenAsync`; imprime **só** `GetToken: HTTP <código> <OK|FALHA>` (e a validade em minutos no sucesso), nunca token/credenciais; exit 0 em 200 com token válido, 1 em qualquer outra coisa (inclui credencial ausente, com a mensagem "credenciais ausentes"). Despachar em `Program.cs` junto das demais CLIs. Documentar em `docs/OPERACAO.md`.
      Testes: `tests/.../TestarTokenWmsTests.cs` (saída e exit code com handler fake: 200 → 0; 401 → 1; timeout → 1; saída não contém token nem senha).
      Verify: `dotnet test ... --filter "FullyQualifiedName~TestarToken"` passa; `dotnet run --project src/KitWmsIntegrador.Worker -- --testar-token` sem credenciais imprime a mensagem e sai com 1 (não chama rede); commit `feat: CLI --testar-token`.

- [ ] 6. **B4: reprocesso seguro das notas em Erro por 401.** Não existe script de reprocesso em `deploy\*.ps1` (há só `limpar-extensao-invalida.ps1`, que serve de molde de guardas). Criar `deploy/reprocessar-erro-401.ps1` (PowerShell 4/5.1, compatível com Server 2012 R2, instalado automaticamente por `instalar.ps1`): parâmetros `-Entrada` (padrão `C:\Friozem\kitbrf`), `-PastaErro` (`Erro`), `-DestinoPadrao` (`Puxada`|`PuxadaTSP`|`Raiz`, padrão `Puxada`), `-Padrao` (padrão `401`), `-Force`, `-WhatIf`; varre `<Entrada>\Erro\**\*.xml.motivo.txt`; só considera sidecar cujo texto casa `401` (palavra inteira, `\b401\b`) **e** cujo XML irmão existe; destino = linha `Origem:` do sidecar (novos) ou `-DestinoPadrao` (antigos; o resumo avisa que as notas antigas não registram a origem e que TSP só é correta com `-DestinoPadrao PuxadaTSP`); move o XML para a pasta de destino com nome único (nunca sobrescreve), renomeia o sidecar para `.motivo.txt.reprocessado-<aaaammdd_HHmmss>` (auditoria, limpeza pela retenção); guardas iguais às de `limpar-extensao-invalida.ps1` (nada fora de `<Entrada>`, recusa raiz de drive, ignora reparse points, resolve com `GetFullPath`); idempotente (sem sidecar casando, informa e sai 0); log em `<Entrada>\Estado\logs\reprocessar-erro-401-<data>.log` (sem conteúdo de nota: só nomes de arquivo e contagens); resumo final (encontrados, movidos, ignorados) e confirmação s/n sem `-Force`. Nunca roda sozinho nem é chamado pelo serviço; uso só com OK do usuário.
      Teste automatizado do script em `tests/.../ReprocessarErro401ScriptTests.cs` com pasta temporária e `pwsh`/`powershell` (marcar `Skip` quando o interpretador não existir): 3 sidecars (um com 401, um com outro motivo, um 401 sem XML) → `-WhatIf` não move nada; execução move só o primeiro; segunda execução não faz nada; destino por `Origem:`; colisão de nome gera nome único.
      Verify: `dotnet test ... --filter "FullyQualifiedName~ReprocessarErro401"` passa e `powershell -File deploy\reprocessar-erro-401.ps1 -Entrada <pasta temporária> -WhatIf` roda sem alterar nada; commit `feat: script reprocessar-erro-401.ps1`.

- [ ] 7. **Configuração, documentação técnica e fechamento do loop.** Ajustar `Worker/appsettings.json` (Wms: `ValidadeMaximaTokenMinutos` 3, `TokenTentativasImediatas`, `CooldownTokenSegundos`, `AlertaAutenticacao`, comentários) e `deploy/config-exemplo/appsettings.Production.json` (Wms com as mesmas chaves, Pacote 20/180, I50 PIC); texto técnico em `docs/OPERACAO.md` (GetToken 401, cooldown, alerta, `--testar-token`, reprocesso, estado "aguardando autenticação", validade do token), `docs/ARQUITETURA.md` (guarda/aborto), `docs/INSTALACAO-SERVIDOR.md` (chaves novas), `docs/TEMPLATES-INTERFACES.md`/`MAPEAMENTO-DADOS.md` (item 1). Manuais de usuário/técnico, deck e checklist da release (`checklist-release-2026-11-16.md`, incluindo a linha `Pacote:SilencioSegundos` 20 e as chaves `Wms:*`) ficam para o passo seguinte de docs + pacote. Registrar as horas de IA em `docs/CONTROLE-ESFORCO.md` (só horas; horas humanas só se o usuário informar).
      Verify: `dotnet build KitWmsIntegrador.sln -c Release` com 0 avisos/0 erros e `dotnet test KitWmsIntegrador.sln -c Release` com 0 falhas; `git status` sem `*.xml`/`*.zip`/segredos; o revisor escreve `.agents/tasks/review-release-1611.json` com `verdict`.

---

## 4. Riscos e pontos de atenção

1. **Validade máxima de 3 min aumenta a frequência de GetToken** (um a cada 3 min com tráfego). O fallback do token ainda válido pelo exp e o cooldown limitam o efeito; se o exp do JWT for menor que 3 min, vale o exp. `0` desliga.
2. **Comportamento visível novo:** em falha de autenticação as notas ficam na entrada por minutos (era: iam para Erro). Autorizado pelo pedido do usuário; a mensagem ao usuário está no alerta e no log.
3. **Notas antigas em Erro (383 de 15/11):** sidecar sem `Origem`. O script usa `-DestinoPadrao`; reprocessar só com OK do usuário, depois de publicar a release.
4. **E-mail de alerta depende de `Email:Habilitado` e destinatários;** sem eles só há log (`Warning`). O alerta é disparado fora do lock para um SMTP lento não travar o token.
5. **Compatibilidade:** `AutenticacaoWmsIndisponivelException` herda de `HttpRequestException`; testes de I51/I60/devolução precisam seguir verdes. Construtores novos só com parâmetros opcionais.
6. **Silêncio 20 s:** se existirem pausas reais de upload maiores que 20 s dentro do mesmo lote, o lote ainda pode dividir; o teto de 180 s não resolve isso. Override por env sem release.
7. **Causa raiz no WMS não resolvida pelo integrador:** a correção torna o 401 inofensivo (sem perda, sem tempestade, com alerta), mas as perguntas ao fornecedor (`investigacao-401.md`) continuam abertas.
8. `Wms:BaseUrl` do `appsettings.json` versionado aponta para `:8888` (homolog); produção sobrescreve em `appsettings.Production.json`. Não alterar nesta release.

## 5. Ordem e dependências

1 e 2 independentes (config + testes). 3 antes de 4 (a exceção/guarda são usadas pelo processador e pelo gerenciador). 5 depende de 3 (`TestarTokenAsync`). 6 independente do código, mas depende da linha `Origem:` do item 4 para o caminho completo. 7 por último.
