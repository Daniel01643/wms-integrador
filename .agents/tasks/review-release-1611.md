# Revisão da release 16/11/2026 (commit c9efdab)

Veredito: APROVADO. Nenhum bloqueio.

## Evidência de verificação
`.agents/tasks/verificacao-release-1611.md` e a mensagem do commit registram `dotnet build -c Release` com 0 avisos e 0 erros, e `dotnet test -c Release` com 966 aprovados, 9 ignorados e 0 falhas. Não reexecutei build nem testes.

## Critérios
1. **GetToken nunca manda nota para Erro.** `ClienteWmsHttp.ObterTokenAsync` lança `AutenticacaoWmsIndisponivelException` (herda de `HttpRequestException`) em qualquer falha final. `ProcessadorLote.ProcessarOndaAsync` captura essa exceção antes do `catch (Exception)`, não registra nem move a nota e interrompe as ondas. O ZIP só é movido se não restar XML não tratado. Testado em `Autenticacao401PacoteTests`.
2. **Single-flight e cooldown.** `_tokenLock` com dupla checagem. A guarda aplica a escada `Wms:CooldownTokenSegundos`, e durante o cooldown não há chamada HTTP. O `GerenciadorPacote` não abre pacote enquanto a guarda está bloqueada. Tentativas imediatas, escada e validade do token são configuráveis.
3. **Diagnóstico do 401.** `LogarFalhaGetToken` registra status, WWW-Authenticate, request id e corpo truncado em 200 caracteres sem caracteres de controle. Não registra token, Authorization, usuário nem senha. Há teste de log.
4. **Alerta de e-mail.** `RegistrarFalha` devolve true só na primeira falha, e `RegistrarSucesso` devolve true só na recuperação. O disparo ocorre fora do lock, em try/catch, e respeita `Wms:AlertaAutenticacao` e `Email:Habilitado`. O contador "Falhas de autenticação (GetToken)" aparece no relatório e no e-mail só quando é maior que 0.
5. **`--testar-token`.** Despachado em `Program.cs`. Imprime só `GetToken: HTTP <código> OK|FALHA`, com exit 0 ou 1, e credenciais ausentes dão 1 sem chamar a rede.
6. **`deploy\reprocessar-erro-401.ps1`.** Tem `-WhatIf` (`SupportsShouldProcess`) e só considera sidecars com `\b401\b` na mensagem e XML irmão existente. Usa nome único e renomeia o sidecar, o que o torna idempotente. As guardas de caminho estão presentes e o log só contém nomes e contagens.
7. **I50.** `OpcoesI50` e o `appsettings.json` do Worker têm `PIC;FEF;PLL` e `AGL;FEF;PLL`. `ConstrutorI50` não mudou e usa as opções no cabeçalho e nos itens. Há teste de payload. A única referência restante a `RES;FEF;PLI` está em texto histórico de docs e no teste que verifica a ausência do valor antigo. O exemplo `deploy/config-exemplo/appsettings.Production.json` define `TipoSeparacao`/`TipoSeparacao2` iguais ao novo padrão e `EnviarPlacaVeiculo=true`, `EnviarPlacaVeiculoTsp=false`. Não há outro exemplo de Production versionado.
8. **Pacote.** `SilencioSegundos=20` e `JanelaMaximaSegundos=180` no default, no appsettings e no exemplo.
9. **Higiene.** O commit não contém `*.xml` nem `*.zip`. Não há valores monetários no diff. `docs/CONTROLE-ESFORCO.md` e `docs/controle` não aparecem no diff.
10. **POST 401.** `PostCoreAsync` mantém `jaRenovouPorToken`: invalida o token e renova uma vez, passando pelo cooldown. Há teste em `AutenticacaoWmsTests` e `Autenticacao401PacoteTests`.

## Observação sem bloqueio
A adição de BOM UTF-8 em `appsettings.json` e `GerenciadorPacote.cs` é inócua para o provedor JSON do .NET, e a suíte passa.
