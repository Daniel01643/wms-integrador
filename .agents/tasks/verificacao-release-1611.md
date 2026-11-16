# Verificação da release 16/11/2026 (itens B, C e D)

Iteração 1 (sem `review-release-1611.json` no início). Trabalho direto na `main`, commit local, sem push.

## Comandos e resultados

| Comando | Resultado |
|---|---|
| `dotnet build c:\wms-integrador\KitWmsIntegrador.sln -c Release` | Compilação com êxito, **0 avisos, 0 erros** |
| `dotnet test c:\wms-integrador\KitWmsIntegrador.sln -c Release` | **966 aprovados, 9 ignorados, 0 falhas** (total 975; linha de base 926 + 9 → 40 testes novos) |

Observação do ambiente local: numa das rodadas intermediárias o Smart App Control do Windows bloqueou o carregamento do
`KitWmsIntegrador.Worker.dll` recém-compilado (`0x800711C7`, evento CodeIntegrity 3077), derrubando todos os testes que
carregam o Worker. Não é falha de código: uma recompilação (hash novo da DLL) foi aceita e a suíte completa passou.

## O que foi implementado

### B. 401 definitivo
- `AutenticacaoWmsIndisponivelException : HttpRequestException` (Status, EmCooldown, ProximaTentativaUtc): I51/I60/devolução
  continuam tratando como falha de comunicação.
- `GuardaAutenticacaoWms` (+ `IEstadoAutenticacaoWms`): estado global, cooldown `Wms:CooldownTokenSegundos`
  (`60;120;300;600;900`, último = teto, inválido cai no padrão), contador `TotalFalhasGetToken`.
- `ClienteWmsHttp`: qualquer falha final do GetToken (401/403/404/5xx/timeout/rede/corpo sem token) lança a exceção nova
  após `Wms:TokenTentativasImediatas` (2); durante o cooldown **nenhuma** chamada HTTP; single-flight pelo `_tokenLock`
  existente; `Wms:ValidadeMaximaTokenMinutos` (3; 0 desliga); renovação proativa que falha com token ainda dentro do `exp`
  usa o token atual; POST 401 continua renovando uma vez. Log de diagnóstico (status, WWW-Authenticate, request id,
  corpo truncado a 200, sem caracteres de controle), nunca token/Authorization/usuário/senha. `TestarTokenAsync` faz um
  GET sem cache/cooldown.
- Alerta: `IAlertaAutenticacaoWms` + `AlertaAutenticacaoWmsEmail` (reusa `IEnviadorEmail` e `FonteDestinatariosFechamento`,
  respeita `Email:Habilitado` e `Wms:AlertaAutenticacao`); disparado fora do lock, um na indisponibilidade e um na recuperação.
- `ProcessadorLote`: captura a exceção nova antes do `catch (Exception)`; a nota não é registrada nem movida; flag interrompe
  as demais ondas; `UltimoAbortoAutenticacao`; ZIP com XML não tratado fica na entrada. Sidecar `.motivo.txt` ganha `Origem:`.
- `GerenciadorPacote`: parâmetro opcional `IEstadoAutenticacaoWms`; com cooldown ativo não abre pacote (log único
  "Aguardando autenticação do WMS até HH:mm:ss"), `AguardandoAutenticacao`/`ProximaTentativaAutenticacaoUtc`; contador de
  falhas e flag de interrupção vão para `EtapasPacote` → `DadosPacote`.
- Relatório HTML/PDF (linha de informações do cabeçalho) e e-mail de fechamento: "Falhas de autenticação (GetToken): N" e
  aviso de pacote interrompido, **só quando > 0** (relatórios existentes inalterados; testes antigos passam sem edição).
- CLI `--testar-token` (`TestarTokenWms.cs`): imprime só `GetToken: HTTP <código> OK|FALHA`; exit 0/1; credenciais ausentes
  → 1 sem rede. Handler TLS extraído para `RegistroClienteWms.CriarHandler()` (mesmo comportamento).
- `deploy\reprocessar-erro-401.ps1` (não havia script de reprocesso em `deploy\*.ps1`; molde de guardas do
  `limpar-extensao-invalida.ps1`): `-WhatIf`, `-Force`, `-DestinoPadrao Puxada|PuxadaTSP|Raiz`, só sidecars com `\b401\b`
  na mensagem e XML irmão existente, volta para `Origem:` ou destino padrão, nome único, sidecar renomeado
  `.reprocessado-<carimbo>`, idempotente, log em `Estado\logs` só com nomes/contagens.

### C. I50
`OpcoesI50.TipoSeparacao` = `PIC;FEF;PLL` (default e `Worker\appsettings.json`), `TipoSeparacao2` = `AGL;FEF;PLL`;
explícitos em `deploy\config-exemplo\appsettings.Production.json`. `ConstrutorI50` já usava as opções no cabeçalho e no item
(sem mudança de código). Docs: `TEMPLATES-INTERFACES.md`, `MAPEAMENTO-DADOS.md`, `docs\exemplos\I50-enriquecida-exemplo.json`.

### D. Espera de coleta
`Pacote:SilencioSegundos` 15 → 20 (default, appsettings, config-exemplo, `OPERACAO.md`, `INSTALACAO-SERVIDOR.md`).
`JanelaMaximaSegundos` 180 e `EstabilizacaoSegundos` 3 mantidos (a investigação não traz evidência para mudá-los).
Manuais e deck ficam para o passo de documentação.

## Testes novos
- `AutenticacaoWmsTests` (fake HTTP handler + FakeClock): tempestade 401 com 50 envios → 1 GetToken e 0 POST; escada
  60/120/300/600/900 com teto e sem novo alerta; recuperação com 1 alerta de cada; 403/404/500/503, timeout, rede e corpo
  sem token lançam a exceção nova; log sem segredos e com diagnóstico; POST 401 renova uma vez; 10 chamadas paralelas → 1
  GetToken; renovação proativa com fallback; validade máxima 3 min e 0 desligado; `TestarTokenAsync`; alerta por e-mail
  respeitando `Email:Habilitado`, `Wms:AlertaAutenticacao` e destinatários.
- `Autenticacao401PacoteTests` (Orquestrador e `ClienteWmsHttp` reais): nenhuma nota em Erro e todas na entrada; ciclos no
  cooldown sem chamada; recuperação processa o pacote inteiro; 401 no meio do pacote (integradas em Processados, demais na
  entrada, contador no relatório e no e-mail); POST 401 renova e integra; ZIP mantido e processado depois; sidecar com Origem.
- `TestarTokenWmsTests`, `ConfiguracaoReleaseI50PacoteTests` (defaults, appsettings real, payload com os dois tipos no
  cabeçalho e em todos os itens, config-exemplo), `ReprocessarErro401ScriptTests` (PowerShell real em pasta temporária;
  ignorado se não houver PowerShell).

## Riscos conhecidos
- XMLs soltos da PuxadaTSP interrompidos por falha de autenticação consomem um novo sequencial de onda no retry (o
  registro de ondas já trata XML solto como "nunca reaproveitado"; mudar isso alteraria o contrato do registro).
- ZIP mantido na entrada: no retry, XMLs dele já integrados são reenviados e o WMS responde "já existe" (vão para Processados).
- Notas antigas em Erro (15/11) não têm `Origem:`; o script usa `-DestinoPadrao` e avisa. Reprocessar só com OK do usuário.
