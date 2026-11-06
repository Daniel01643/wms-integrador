# Destinatários do e-mail de fechamento configuráveis por TXT editável a quente

Torna a lista de destinatários do e-mail de fechamento de pacote configurável por um arquivo TXT lido a quente, espelhando o mecanismo já usado pelo resumo semanal, mas com um fallback próprio. O parsing comum foi extraído para um helper puro (`LeitorListaEmails`), de modo que resumo e fechamento compartilham a leitura mas mantêm fallbacks distintos. O fechamento passa a resolver os destinatários por uma nova fonte (`FonteDestinatariosFechamento`) e os injeta via `DestinatariosOverride` na mensagem; quando nenhuma origem tem endereço, o e-mail não é enviado e o fechamento conclui normalmente. A lógica de decisão do pacote e do Orquestrador não mudou e a I60 continua desligada.

Watch for: nada bloqueante. A refatoração do resumo unificou três mensagens de aviso (ausente/vazio/ilegível) numa só — comportamento observável preservado, confirmed. O helper engole `IOException`/`UnauthorizedAccessException` silenciosamente e cai no fallback, o que é o comportamento desejado mas significa que um TXT ilegível por ACL se comporta como ausente sem distinção no log, possible.

**Verdict**: APPROVED

## High-level view

O parsing foi extraído para `LeitorListaEmails.Ler(caminho)`, um helper estático puro que devolve a lista encontrada (um e-mail por linha, `Trim`, ignora branco e `#`) ou lista vazia quando o arquivo está ausente/vazio/ilegível, sem decidir fallback. Isso elimina a duplicação: tanto o resumo quanto o fechamento usam o mesmo parsing e cada um aplica seu fallback por cima.

A nova `FonteDestinatariosFechamento` resolve o caminho a partir de `Email:CaminhoDestinatarios` (vazio = `<telemetria>\destinatarios-fechamento.txt`, mesma pasta base do resumo) e aplica a precedência TXT → `Email:Destinatarios` → vazio-com-aviso. O fechamento difere do resumo de propósito: o resumo cai em `ti@friozem.com.br`; o fechamento, quando tudo está vazio, não envia.

No `FechadorPacote`, a lista resolvida vai pela `DestinatariosOverride` da `MensagemEmail`; lista vazia faz o fechador logar e pular o envio retornando `true`, com o relatório HTML+PDF já gravado. A decisão de pacote e o Orquestrador não foram tocados.

A superfície de configuração nova é a chave `Email:CaminhoDestinatarios`, documentada em `appsettings.json` e no molde de produção com a nota de precedência. Os e-mails reais não são versionados: há `deploy/config-exemplo/destinatarios-fechamento.exemplo.txt` e o `.gitignore` cobre o `destinatarios-fechamento.txt` real.

A cobertura de teste exercita os cinco cenários pedidos (precedência, fallback, não-envio, parsing, releitura) em dois níveis — unidade da fonte e integração via `FechadorPacote` — e a evidência gravada pelo coder reporta build limpo e 323 testes verdes (313 baseline + 10 novos), com os testes do resumo intactos.

<details>
<summary>Issues (2)</summary>

1. **Aviso de TXT ilegível indistinto** (possible) — `LeitorListaEmails` trata `IOException`/`UnauthorizedAccessException` como lista vazia sem log, então um TXT bloqueado por ACL se comporta como ausente e só o aviso genérico da fonte aparece. Não bloqueante; considerar um log de debug no helper se diagnóstico de ACL virar necessidade.
2. **Avisos do resumo unificados** (confirmed) — a refatoração fundiu três mensagens (ausente/vazio/ilegível) numa só no `FonteDestinatariosResumo`. O plano permitiu explicitamente; sem ação necessária, registrado para rastreabilidade.

</details>

<details>
<summary>Details</summary>

### Helper de parsing: captura de exceção estreita

O helper captura `IOException` e `UnauthorizedAccessException` (em vez do `catch (Exception)` genérico do resumo original) e cai em lista vazia. O efeito colateral é que um arquivo ilegível por ACL retorna vazio sem um log próprio no helper; o aviso que o operador vê vem da fonte ("ausente/vazio"), sem distinguir "não consegui ler" de "não existe". Em produção isso raramente importa porque o ACL de `config\` é estável, mas vale como nota (possible).

### Fonte de fechamento e preservação do fallback do resumo

Além da precedência própria do fechamento, o ponto a proteger é que o contrato público do `FonteDestinatariosResumo` (`DestinatarioPadrao`, `NomeArquivoPadrao`, `Caminho`, `Ler()`) não mudou ao passar a usar o helper: o resumo continua caindo no mínimo `ti@friozem.com.br` e seus testes continuam verdes conforme a evidência. Os dois fallbacks ficam separados — fechamento devolve vazio para sinalizar não-envio, resumo devolve o padrão — sem duplicação de parsing.

### Roteamento no FechadorPacote sem tocar a decisão de pacote

```
FechadorPacote.Fechar
  ├─ PoliticaEnvioEmail.Deve(...)  (inalterado)
  ├─ _destinatarios.Ler()          (nova fonte)
  │     ├─ lista vazia → LogInformation + return true (relatório já salvo, NÃO envia)
  │     └─ lista com itens → MensagemEmail(..., DestinatariosOverride: lista) → EnviarAsync
  └─ (demais ramos inalterados)
```

A lista entra pelo parâmetro nomeado `DestinatariosOverride:` — correto, pois é o último parâmetro opcional do record, depois de `ImagemEmbutida`. O ramo de lista vazia retorna `true` após logar, preservando o fechamento (relatório HTML+PDF já gravado antes deste ponto) — comportamento fail-safe que a tarefa exige: destinatário faltando não quebra o fechamento. Nenhuma alteração na política de envio, no Orquestrador ou na lógica de decisão de pacote; a I60 permanece desligada.

### Superfície de configuração e segredos

A única chave nova é `Email:CaminhoDestinatarios` (string, default vazio), documentada no `_comentario` de `appsettings.json` e no molde `deploy/config-exemplo/appsettings.Production.json`, ambos com a nota de precedência (TXT > `Email:Destinatarios`) e o comportamento de não-envio quando ambos vazios. O molde de produção aponta o caminho absoluto em `config\`, simétrico ao `Monitoramento:CaminhoDestinatarios` do resumo.

Os e-mails reais não são versionados: o `.gitignore` ganhou `destinatarios-fechamento.txt` no mesmo bloco de `destinatarios.txt`/`ultimo-resumo.txt`, e o molde versionado `deploy/config-exemplo/destinatarios-fechamento.exemplo.txt` traz o formato, a precedência e o fallback específicos do fechamento sem endereços reais além do `ti@friozem.com.br` de exemplo já público nos moldes. Trust boundary coberto: nenhuma lista real de destinatários passa a ser versionada por esta mudança.

### Cobertura de teste

Os cinco cenários pedidos estão cobertos em dois níveis. `FonteDestinatariosFechamentoTests` (6 casos) cobre precedência do TXT, fallback com TXT ausente e com TXT vazio, vazio-total, parsing (comentários/branco/trim) e releitura após edição. `FechadorPacoteDestinatariosTests` (4 casos) exercita o roteamento fim-a-fim via `AmbientePacote` + `GravadorEmail`: precedência com `DestinatariosOverride` igual ao TXT (requisito i), fallback para `Email:Destinatarios` (ii), não-envio com fechamento concluído — pacote volta a Ocioso e HTML+PDF gerados (iii) — e releitura entre dois pacotes (v). O requisito de parsing (iv) está no teste de unidade.

A infra de teste foi ajustada nas duas montagens do `FechadorPacote` (`AmbientePacote` e `AmbienteStress`), cada uma com um `Email:Destinatarios` de fallback para que os testes pré-existentes que assumem envio continuem emitindo. A evidência gravada em `verificacao-email-fechamento.md` reporta build `-warnaserror` com 0 avisos/0 erros e `dotnet test` com 323 aprovados (313 baseline + 10 novos), 9 ignorados (stress, igual ao baseline). Não reexecutei as suítes; o veredito se apoia nessa evidência conforme instruído.

Não testado explicitamente: o ramo de `IOException`/`UnauthorizedAccessException` do helper (TXT ilegível por ACL). Não é bloqueante — é um caminho de borda de I/O que recai no mesmo fallback já coberto pelo cenário de TXT ausente.

### Docs e higiene de commit

`OPERACAO.md` (nova seção 4.1) e `INSTALACAO-SERVIDOR.md` descrevem os dois TXTs, a precedência e o fallback do fechamento, distinguindo-o do fallback do resumo. `docs/diario` e `docs/controle` não foram tocados. Os commits são pequenos e por arquivo nomeado na `main`, sem push (não há `origin/main`).

</details>

<details>
<summary>Arquivos alterados</summary>

- `src/KitWmsIntegrador.Core/Monitoramento/LeitorListaEmails.cs` — novo helper puro de parsing da lista de e-mails.
- `src/KitWmsIntegrador.Core/Monitoramento/FonteDestinatariosResumo.cs` — passa a usar o helper; contrato e fallback preservados.
- `src/KitWmsIntegrador.Core/Pacote/FonteDestinatariosFechamento.cs` — nova fonte com fallback TXT → `Email:Destinatarios` → vazio.
- `src/KitWmsIntegrador.Core/Configuracao/OpcoesEmail.cs` — nova chave `CaminhoDestinatarios`.
- `src/KitWmsIntegrador.Core/Pacote/FechadorPacote.cs` — resolve destinatários pela fonte e roteia via `DestinatariosOverride`; não-envio preserva o fechamento.
- `src/KitWmsIntegrador.Worker/Program.cs` — registra `FonteDestinatariosFechamento` no DI.
- `src/KitWmsIntegrador.Worker/appsettings.json` — chave `CaminhoDestinatarios` + nota de precedência.
- `deploy/config-exemplo/appsettings.Production.json` — molde com caminho absoluto e nota de virada.
- `deploy/config-exemplo/destinatarios-fechamento.exemplo.txt` — molde versionado do TXT (sem e-mails reais).
- `.gitignore` — ignora o `destinatarios-fechamento.txt` real.
- `docs/OPERACAO.md`, `docs/INSTALACAO-SERVIDOR.md` — documentam os dois TXTs e os fallbacks.
- `tests/.../FonteDestinatariosFechamentoTests.cs`, `tests/.../FechadorPacoteDestinatariosTests.cs` — testes novos.
- `tests/.../Infra/AmbientePacote.cs`, `tests/.../Stress/AmbienteStress.cs` — infra ajustada para a nova dependência.

Diff completo: `git diff bda9e17..HEAD`.

</details>
