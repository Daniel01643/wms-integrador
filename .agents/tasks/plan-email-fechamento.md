# Plano de implementação — destinatários do e-mail de FECHAMENTO configuráveis por TXT

Objetivo: tornar os destinatários do e-mail de **fechamento de pacote** configuráveis por um arquivo
TXT editável a quente (um e-mail por linha, `#` comenta, relido a cada envio), igual ao mecanismo já
usado pelo **resumo semanal** (`FonteDestinatariosResumo` + `Monitoramento:CaminhoDestinatarios`),
mas com **fallback diferente**: TXT → `Email:Destinatarios` (comportamento atual) → não-envio com
aviso. Sem reiniciar o serviço, sem editar JSON. A I60 permanece desligada; nenhuma lógica de decisão
de pacote/Orquestrador muda — só o **roteamento de destinatários** do e-mail.

## Decisões de projeto (tomadas a partir do código lido)

1. **Helper comum de parsing + fonte dedicada (helper-comum E fonte-dedicada, não uma só).**
   O parsing (aparar espaços, ignorar linha em branco e `#`, reler a cada chamada, tratar
   ausente/ilegível) é idêntico ao de `FonteDestinatariosResumo.Ler()`. Mas os **fallbacks** são
   distintos: o resumo cai em `ti@friozem.com.br`; o fechamento cai em `Email:Destinatarios` e,
   se vazio, **não envia**. Logo: extrair um helper **só de parsing** `LeitorListaEmails` que lê um
   caminho e devolve a lista encontrada (vazia quando ausente/vazio/ilegível, sem decidir fallback);
   cada `Fonte` aplica o seu próprio fallback por cima. Isso elimina a duplicação de parsing e
   **preserva os dois comportamentos de fallback**. `FonteDestinatariosResumo` passa a usar o helper
   internamente sem mudar seu contrato público (`DestinatarioPadrao`, `NomeArquivoPadrao`, `Caminho`,
   `Ler()`), então os testes do resumo continuam válidos. Alternativa descartada: só uma fonte
   genérica parametrizada por fallback — acoplaria dois comportamentos num tipo só e tornaria os
   testes do resumo mais frágeis; o ganho não compensa.

2. **Caminho padrão do TXT de fechamento: mesmo critério de pasta base do resumo.**
   `OpcoesEmail` não tem conceito de pasta base. O resumo resolve o default contra
   `RegistradorTelemetria.PastaBase` (= `<Estado>/telemetria`) quando `CaminhoDestinatarios` está
   vazio. `FechadorPacote` **já recebe** `RegistradorTelemetria`. Então `FonteDestinatariosFechamento`
   resolve o default contra `RegistradorTelemetria.PastaBase` com nome
   `destinatarios-fechamento.txt`, exatamente como o resumo resolve o seu — "mesmo critério de pasta
   base". Em produção o operador aponta `Email:CaminhoDestinatarios` para o caminho absoluto em
   `config\` (idêntico ao que `Monitoramento:CaminhoDestinatarios` já faz). O nome default difere do
   resumo (`destinatarios.txt`) para os dois arquivos poderem coexistir na mesma pasta base.

3. **Roteamento via `DestinatariosOverride`.** `MensagemEmail.DestinatariosOverride` e
   `EnviadorEmailSmtp` já aplicam o override quando `{ Count: > 0 }`, senão usam
   `OpcoesEmail.ListaDestinatarios`. O `FechadorPacote` passará a lista resolvida pela nova fonte via
   `DestinatariosOverride`. Quando a fonte não achar ninguém (TXT vazio e `Email:Destinatarios`
   vazio), o `FechadorPacote` **loga aviso e não chama `EnviarAsync`** — sem quebrar o fechamento
   (retorna `true`, relatório já gravado).

4. **Nenhuma mudança em `EnviadorEmailSmtp`, `PoliticaEnvioEmail`, Orquestrador ou lógica de pacote.**

## Convenções confirmadas no código

- Fakes de e-mail de teste: `GravadorEmail` em `tests/.../Infra/AmbientePacote.cs` (captura
  `MensagemEmail`, permite assertar `DestinatariosOverride`) e `FakeEnviadorEmail` interno em
  `EnviadorResumoSemanalTests.cs`. Sem SMTP/WMS reais.
- `FechadorPacote` é construído em DUAS infras de teste: `tests/.../Infra/AmbientePacote.cs` e
  `tests/.../Stress/AmbienteStress.cs`. Ambas precisam passar a nova dependência.
- `.gitignore` já ignora `email.settings.json`, `destinatarios.txt`, `ultimo-resumo.txt`,
  `deploy/config/`. Os moldes versionados ficam em `deploy/config-exemplo/` e
  `docs/exemplos/`.
- Comandos reais: `dotnet build KitWmsIntegrador.sln` e
  `dotnet test tests/KitWmsIntegrador.Tests/KitWmsIntegrador.Tests.csproj` (raiz `c:\wms-integrador`).
- Baseline atual: **313 testes verdes** + os novos.

---

## Itens (ordenados por dependência)

- [ ] 1. Criar o helper de parsing comum `LeitorListaEmails` (classe estática) que lê um caminho de
      arquivo e devolve a lista de e-mails encontrada (um por linha, `Trim`, ignora linha em branco e
      iniciada por `#`), relendo o arquivo a cada chamada. Arquivo ausente/vazio/ilegível devolve
      lista **vazia** (sem log, sem fallback — quem decide fallback é a fonte). Assinatura:
      `public static IReadOnlyList<string> Ler(string caminho)`. Sem dependências de DI; parsing puro.
      Files: `src/KitWmsIntegrador.Core/Monitoramento/LeitorListaEmails.cs`
      Verify: `dotnet build KitWmsIntegrador.sln` compila sem erros.

- [ ] 2. Refatorar `FonteDestinatariosResumo.Ler()` para usar `LeitorListaEmails.Ler(Caminho)` e
      aplicar por cima o fallback existente (lista vazia → `DestinatarioPadrao` com
      `LogWarning`; exceção/ausência tratadas via lista vazia do helper, preservando as três mensagens
      de aviso atuais — ausente, vazio, ilegível podem ser unificadas em um único aviso "vazio/ausente"
      desde que o log continue existindo). Manter públicos `DestinatarioPadrao`, `NomeArquivoPadrao`,
      `Caminho` e `Ler()` inalterados em assinatura e semântica observável.
      Files: `src/KitWmsIntegrador.Core/Monitoramento/FonteDestinatariosResumo.cs`
      Verify: `dotnet test ...KitWmsIntegrador.Tests.csproj --filter FonteDestinatariosResumoTests` —
      todos os testes do resumo (incluindo `UmPorLinha_IgnoraComentariosEVazias_ComTrim`,
      `Ausente_RetornaPadraoMinimo`, `Vazio_RetornaPadraoMinimo`, `Recarrega_AposEditarOArquivo`)
      continuam verdes.

- [ ] 3. Adicionar a opção `CaminhoDestinatarios` em `OpcoesEmail` (string, default `string.Empty`),
      com doc-comment: vazio = `<telemetria>\destinatarios-fechamento.txt`; o TXT tem **precedência**
      sobre `Destinatarios`. Não alterar `ListaDestinatarios`/`EstaConfigurado`.
      Files: `src/KitWmsIntegrador.Core/Configuracao/OpcoesEmail.cs`
      Verify: `dotnet build KitWmsIntegrador.sln` compila.

- [ ] 4. Criar `FonteDestinatariosFechamento` espelhando `FonteDestinatariosResumo`, porém com
      fallback próprio. Dependências (DI): `IOptions<OpcoesEmail>`, `RegistradorTelemetria` (para a
      pasta base) e `ILogger<FonteDestinatariosFechamento>`. Const `NomeArquivoPadrao =
      "destinatarios-fechamento.txt"`. Propriedade `Caminho` = `OpcoesEmail.CaminhoDestinatarios` se
      preenchido, senão `Path.Combine(telemetria.PastaBase, NomeArquivoPadrao)`. Método
      `public IReadOnlyList<string> Ler()`: usa `LeitorListaEmails.Ler(Caminho)`; se a lista do TXT
      tiver itens, devolve-a (precedência); senão devolve `OpcoesEmail.ListaDestinatarios` (fallback 1,
      comportamento atual); se também vazio, devolve lista vazia + `LogWarning`
      ("sem destinatários de fechamento: TXT ausente/vazio e Email:Destinatarios vazio; e-mail não
      será enviado"). Reler a cada chamada (sem cache).
      Files: `src/KitWmsIntegrador.Core/Pacote/FonteDestinatariosFechamento.cs`
      (ou `.../Email/`; usar `.../Pacote/` por ser consumida pelo `FechadorPacote`).
      Verify: `dotnet build KitWmsIntegrador.sln` compila.

- [ ] 5. Alterar `FechadorPacote` para receber `FonteDestinatariosFechamento` no construtor e, no
      trecho de envio (após `PoliticaEnvioEmail.Deve`), chamar `fonte.Ler()`; se a lista estiver
      **vazia**, logar informação ("sem destinatários; e-mail do pacote não enviado") e **pular** o
      `EnviarAsync` (sem alterar o retorno `true` nem a gravação do relatório); se tiver itens, montar
      a `MensagemEmail` passando a lista em `DestinatariosOverride`. Nenhuma outra lógica muda.
      Files: `src/KitWmsIntegrador.Core/Pacote/FechadorPacote.cs`
      Verify: `dotnet build KitWmsIntegrador.sln` compila (os testes vêm nos itens 9–10).

- [ ] 6. Registrar `FonteDestinatariosFechamento` no DI e garantir que o `FechadorPacote` a receba
      (o contêiner resolve por construtor; só adicionar o `AddSingleton`). Colocar o registro junto ao
      bloco Email, antes do registro de `FechadorPacote`.
      Files: `src/KitWmsIntegrador.Worker/Program.cs`
      Verify: `dotnet build KitWmsIntegrador.sln` compila; `dotnet run --project
      src/KitWmsIntegrador.Worker` resolve o host sem erro de DI (ou cobrir via build + testes dos
      itens 9–10, que constroem o `FechadorPacote`).

- [ ] 7. Ajustar as DUAS infras de teste que constroem `FechadorPacote` para passar a nova fonte.
      Em `AmbientePacote.CriarGerenciador`, criar `new FonteDestinatariosFechamento(
      Options.Create(OpcoesDeEmail), telemetria, NullLogger<...>.Instance)` e passá-la ao
      `FechadorPacote`. Em `AmbienteStress`, idem com `Options.Create(new OpcoesEmail())`.
      Files: `tests/KitWmsIntegrador.Tests/Infra/AmbientePacote.cs`,
      `tests/KitWmsIntegrador.Tests/Stress/AmbienteStress.cs`
      Verify: `dotnet build KitWmsIntegrador.sln` compila o projeto de testes.

- [ ] 8. Criar os testes de unidade de `FonteDestinatariosFechamento` espelhando
      `FonteDestinatariosResumoTests` (mesmo padrão de pasta temporária/`Dispose`). Cobrir o item (iv)
      parsing e os fallbacks da fonte: (a) TXT com e-mails → retorna-os (precede `Email:Destinatarios`);
      (b) TXT ausente/vazio e `Email:Destinatarios` preenchido → retorna `Email:Destinatarios`;
      (c) TXT ausente/vazio e `Email:Destinatarios` vazio → retorna lista vazia; (d) parsing ignora
      linha em branco e `#` e apara espaços; (e) relê após editar o arquivo (chama `Ler()` duas vezes
      com conteúdo diferente).
      Files: `tests/KitWmsIntegrador.Tests/FonteDestinatariosFechamentoTests.cs`
      Verify: `dotnet test ...KitWmsIntegrador.Tests.csproj --filter FonteDestinatariosFechamentoTests`
      — todos verdes.

- [ ] 9. Adicionar testes de integração do `FechadorPacote` (via `AmbientePacote` + `GravadorEmail`,
      sem SMTP/WMS) para os requisitos (i), (ii), (iii) e (v):
      (i) TXT de fechamento existe com e-mails → `GravadorEmail.Enviadas` contém a mensagem com
      `DestinatariosOverride` igual à lista do TXT (e diferente de `Email:Destinatarios`);
      (ii) TXT ausente/vazio e `OpcoesDeEmail.Destinatarios` preenchido → `DestinatariosOverride` igual
      a `Email:Destinatarios`; (iii) TXT vazio e `Email:Destinatarios` vazio → nada enviado
      (`Enviadas` vazio) e o fechamento **conclui** (pacote volta a Ocioso / relatório HTML+PDF
      gerado); (v) relido a cada envio: fechar um pacote, editar o TXT, fechar um segundo pacote e
      conferir que a 2ª mensagem reflete a edição. Expor um gancho no `AmbientePacote` para apontar o
      caminho do TXT (ex.: preencher `OpcoesDeEmail.CaminhoDestinatarios` para um arquivo dentro de
      `Raiz`) de modo que a fonte resolva para ele nos testes.
      Files: `tests/KitWmsIntegrador.Tests/FechadorPacoteDestinatariosTests.cs`
      (ajuste mínimo em `tests/KitWmsIntegrador.Tests/Infra/AmbientePacote.cs` se precisar do gancho de
      caminho).
      Verify: `dotnet test ...KitWmsIntegrador.Tests.csproj --filter FechadorPacoteDestinatariosTests`
      — todos verdes.

- [ ] 10. Rodar a suíte completa e confirmar a ausência de regressões (em especial os testes do
      resumo, item 2).
      Files: — (sem alteração de código)
      Verify: `dotnet build KitWmsIntegrador.sln` e
      `dotnet test tests/KitWmsIntegrador.Tests/KitWmsIntegrador.Tests.csproj` — **313 testes
      anteriores + os novos**, todos verdes.

- [ ] 11. Criar o molde versionado do TXT de fechamento (SEM e-mails reais), espelhando
      `deploy/config-exemplo/destinatarios-resumo.exemplo.txt`: cabeçalho explicando o formato
      (um e-mail por linha, `#` comenta, linhas em branco ignoradas, relido a cada envio) e a
      precedência/fallback específicos do fechamento (TXT → `Email:Destinatarios` → não-envio com
      aviso), além da instrução de copiar para
      `C:\Friozem\importacao_notas\config\destinatarios-fechamento.txt` e apontar
      `Email:CaminhoDestinatarios`.
      Files: `deploy/config-exemplo/destinatarios-fechamento.exemplo.txt`
      Verify: arquivo existe e segue o formato do exemplo do resumo (confirma na revisão; sem comando).

- [ ] 12. Cobrir no `.gitignore` o arquivo REAL de fechamento, no mesmo bloco do resumo. O real em
      produção fica em `config\` (já coberto por `deploy/config/`), mas, por simetria com
      `destinatarios.txt`/`ultimo-resumo.txt` e para proteger cópias acidentais na raiz, adicionar a
      entrada `destinatarios-fechamento.txt`.
      Files: `.gitignore`
      Verify: `git status --porcelain` não lista um `destinatarios-fechamento.txt` criado na raiz como
      arquivo a versionar (teste manual: criar o arquivo temporário e confirmar que fica ignorado).

- [ ] 13. Atualizar o `appsettings.json` do Worker (seção `Email`): acrescentar a chave
      `"CaminhoDestinatarios": ""` e complementar o `_comentario` explicando que o TXT (um e-mail por
      linha, `#` comenta, relido a cada envio) tem **precedência** sobre `Destinatarios`; vazio =
      `<telemetria>\destinatarios-fechamento.txt`; fallback TXT → `Destinatarios` → não-envio com aviso.
      Files: `src/KitWmsIntegrador.Worker/appsettings.json`
      Verify: `dotnet build KitWmsIntegrador.sln` (JSON válido, host carrega); JSON parseável.

- [ ] 14. Atualizar o molde `deploy/config-exemplo/appsettings.Production.json` (seção `Email`):
      acrescentar `"CaminhoDestinatarios": "C:\\Friozem\\importacao_notas\\config\\destinatarios-fechamento.txt"`
      e ajustar o `_comentario`/`_VIRADA` para indicar que a LISTA de fechamento agora vem do TXT em
      `config\` (precedência sobre `Destinatarios`, que vira fallback).
      Files: `deploy/config-exemplo/appsettings.Production.json`
      Verify: JSON válido (parseável); confirmar na revisão que o caminho casa com o de
      `Monitoramento:CaminhoDestinatarios`.

- [ ] 15. Atualizar `docs/OPERACAO.md` (seção 4, E-mail): explicar os **dois** TXTs editáveis a quente
      — o de **fechamento** (novo, `Email:CaminhoDestinatarios`, default
      `<telemetria>\destinatarios-fechamento.txt`) e o de **resumo** (seção 6.5, já existente) — com a
      precedência e o fallback de cada um. Deixar explícito o fallback do fechamento: TXT →
      `Email:Destinatarios` → **não envia** (loga aviso, não quebra o fechamento), que é diferente do
      fallback do resumo (`ti@friozem.com.br`). Mencionar `deploy/config-exemplo/destinatarios-fechamento.exemplo.txt`
      como modelo.
      Files: `docs/OPERACAO.md`
      Verify: revisão de conteúdo (sem comando); confirmar que descreve precedência e fallback dos dois
      TXTs sem contradizer o item 1.

- [ ] 16. Atualizar `docs/INSTALACAO-SERVIDOR.md`: incluir `destinatarios-fechamento.txt` na árvore de
      `config\` (ao lado de `destinatarios-resumo.txt`) e acrescentar, nos passos de configuração/virada,
      o passo de copiar `destinatarios-fechamento.exemplo.txt` para
      `config\destinatarios-fechamento.txt` e apontar `Email:CaminhoDestinatarios`, análogo ao passo já
      existente do resumo.
      Files: `docs/INSTALACAO-SERVIDOR.md`
      Verify: revisão de conteúdo (sem comando); confirmar simetria com as instruções do resumo. NÃO
      tocar em `docs/diario` nem `docs/controle`.

## Verificação final

- `dotnet build KitWmsIntegrador.sln` sem erros/avisos novos.
- `dotnet test tests/KitWmsIntegrador.Tests/KitWmsIntegrador.Tests.csproj` verde: 313 anteriores +
  novos (fonte de fechamento + integração do FechadorPacote), com os testes do resumo intactos.
- Limpar quaisquer arquivos temporários criados na verificação do `.gitignore` (item 12).

## Pontos em aberto / suposições

- Mantida a lista de fechamento **separada por `;`** em `Email:Destinatarios` como fallback (formato
  atual preservado); o TXT usa um e-mail por linha (formato do resumo). Suposição razoável: espelhar o
  resumo no TXT e manter o JSON como está.
- O nome default `destinatarios-fechamento.txt` foi escolhido para coexistir com o
  `destinatarios.txt` do resumo na mesma pasta base; em produção ambos moram em `config\` com caminhos
  absolutos distintos.
