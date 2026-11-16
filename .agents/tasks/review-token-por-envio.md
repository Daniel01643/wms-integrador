# Token por envio: GetToken a cada POST, nova tentativa em conexão nova, teto por minuto e diagnóstico por servidor

O commit 192a319 (mais e8086be, ajuste de comentário XML) faz cada POST ao WMS (I40, I51 PostBruto, I60 e demais) pedir um GetToken próprio quando `Wms:TokenPorEnvio=true` (padrão). O token e o cookie de afinidade do balanceador valem só para aquele envio. Com `TokenPorEnvio=false` o cache antigo segue intacto. Falha de GetToken (401, rede, timeout) nunca vira erro de nota: após `TokenTentativasImediatas=3`, abre o cooldown global e lança `AutenticacaoWmsIndisponivelException`. A evidência do coder (build 0 avisos/0 erros; 991 aprovados, 0 falhas, 9 ignorados; +20 testes em `TokenPorEnvioTests.cs`) está em `verificacao-token-por-envio.md`. Não reexecutei a suíte, conforme pedido.

Watch for: (1) a 1ª tentativa de GetToken usa a conexão do pool; só da 2ª em diante usa conexão nova com `Connection: close` (possible, não bloqueante, coerente com o item 4). (2) `MaxGetTokenPorMinuto` padrão 300, acima do teto a chamada espera em vez de falhar (decisão registrada, confirmed). (3) PDF/HTML dos manuais regerados, binários não inspecionados.

**Verdict**: APPROVED

## High-level view

O par GetToken+POST de um envio usa o mesmo cookie de afinidade, repassado manualmente porque o handler passou a `UseCookies=false`. Envios diferentes não compartilham cookie, então não ficam presos a um servidor.

Falha de GetToken segue a escada de cooldown já existente, com proteção contra envios paralelos escalarem a escada várias vezes. Notas permanecem na entrada.

O diagnóstico identifica o servidor por nome do cookie e hash curto, mais Server/X-*, status e latência, sem token, usuário ou senha. O relatório do pacote ganha contagem por servidor. A CLI `--testar-token --vezes N` (padrão 1, máx. 20, 2 s) amostra servidores com conexão nova.

<details>
<summary>Issues (2)</summary>

1. **Primeira tentativa de GetToken no pool** — a tentativa 1 pode reaproveitar conexão persistente (possible); aceitável, pois o cookie não é enviado e as seguintes usam conexão nova.
2. **Binários dos manuais** — PDFs regerados não inspecionados (possible); verificar visualmente antes do deploy.

</details>

<details>
<summary>Details</summary>

## Conferência dos itens 1-6

Item 1: `ObterTokenPorEnvioAsync` é chamado no laço de `PostAsync` quando `TokenPorEnvio`; I51 PostBruto e I40 têm teste dedicado (`PostBrutoI51_EI40_TambemUsamTokenPorEnvio`). Cache preservado em `GarantirTokenAsync` (teste `TokenPorEnvioFalse_MantemOCacheAntigo`), e os testes antigos de cache fixam `TokenPorEnvio=false`.

Item 2: cookie anexado só no POST do mesmo envio; `CookieDeAfinidade_NaoVazaEntreEnvios` cobre.

Item 3: tentativa ≥2 usa `HttpClient` novo (fábrica registrada em `RegistroClienteWms`), sem cookie e com `ConnectionClose`; 3 tentativas, depois guarda global (`RegistrarFalha`) e exceção; teste de 3×401 confirma que as notas ficam. Um 401 no POST também pede outro token e repete uma vez.

Item 4: `EsperarTetoGetTokenAsync` espera a janela e registra warning com o limite, uma vez por espera.

Item 5: `LogarFalhaGetToken` registra status, WWW-Authenticate, corpo truncado a 200, servidor, detalhe, latência e host, sem Authorization/usuário/senha; teste `Diagnostico_TemServidorStatusELatencia_SemSegredos`.

Item 6: `FalhasPorBackend` calculado por diferença desde a abertura do pacote e impresso na linha "Falhas de autenticação (GetToken)" do HTML/e-mail (`TextoFalhasPorBackend`, com HtmlEncode no e-mail); `RelatorioDoPacote_ContaFalhasPorServidor`. A CLI `--vezes` tem `Math.Clamp(1,20)` e intervalo de 2 s, com testes.

## Configuração, docs e restrições

`appsettings.json` e `deploy/config-exemplo/appsettings.Production.json` trazem `TokenPorEnvio`, `MaxGetTokenPorMinuto` e `TokenTentativasImediatas=3`. Alterados: OPERACAO.md (§12.1), ARQUITETURA.md, investigacao-401.md, Manual Técnico e Usuário (md, html, pdf). Nenhum arquivo de `docs/CONTROLE-ESFORCO.md`, `docs/controle/*` ou `docs/diario/*` no diff. Busca por R$/US$, chaves e segredos no diff sem ocorrências; nenhum xml/zip. HEAD está à frente de origin/main sem push e não houve deploy.

Observação cosmética: `GerenciadorPacote.cs` perdeu o BOM UTF-8.

</details>

## File map

<details>
<summary>Files</summary>

Core/ClienteWms (ClienteWmsHttp, GuardaAutenticacaoWms, BackendWms, OpcoesWms), Core/Pacote e Relatorio e Email (contagem por servidor), Worker (RegistroClienteWms, TestarTokenWms, appsettings, CLIs manuais), tests (TokenPorEnvioTests + ajustes), docs e manuais. Diff completo: `git diff fbc0246 HEAD`.

</details>