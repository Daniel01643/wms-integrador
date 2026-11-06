# Implementation Plan — Placa do veículo na I50 + Manuais nas releases + Capítulo técnico

Projeto: KitWmsIntegrador (C#/.NET 8), solução `KitWmsIntegrador.sln`, branch `main`, SEM worktree.
Comandos reais do projeto (descobertos na exploração):

- Build: `dotnet build KitWmsIntegrador.sln -c Release`
- Testes: `dotnet test KitWmsIntegrador.sln -c Release`
- Projeto de testes: `tests\KitWmsIntegrador.Tests\KitWmsIntegrador.Tests.csproj` (xUnit). Os 2 XMLs
  reais (`Recursos\NFE...4491612.XML` com placa `AYC8G72` e `Recursos\NFE...3034056.XML` com placa
  `QPJ1H28`) já são copiados para o output do teste via `<None Include="Recursos\**\*.XML">`.
- Gerar HTML dos manuais: `powershell -ExecutionPolicy Bypass -File docs\manuais\gerar-manuais.ps1`
- Gerar PDF: Microsoft Edge headless (ver `docs\manuais\README.md`).

Restrições (do pedido): não tocar `docs/diario` nem `docs/controle`; I60 continua desligada; NÃO
fazer deploy no servidor; commits pequenos por arquivo nomeado (nunca `git add .`).

## Decisões de design (registradas aqui, com justificativa breve)

1. **Parser lê `transp/veicTransp/placa` por `LocalName`** (igual a todos os outros campos do
   `LeitorNotaFiscalXml`, que comparam por `reader.LocalName` ignorando o namespace
   `http://www.portalfiscal.inf.br/nfe`). Isso mantém o parser consistente e robusto ao prefixo/ns.
   Apenas `veicTransp/placa`; `reboque/placa` é ignorado (não é lido). Ausência → string vazia.
2. **Placa enviada "como está"**: sem `ToUpper`, sem hífen, sem máscara. O `LeitorNotaFiscalXml`
   usa `ReadElementContentAsString()`, que já não aplica trim; manter esse valor cru. (O XML real
   traz `AYC8G72`/`QPJ1H28` sem formatação.) Não criar helper em `FormatoWms` — não há formatação.
3. **Flag `I50:EnviarPlacaVeiculo` (bool, default `true`)** segue o padrão das demais opções da
   `OpcoesI50` (seção `I50`, injetada via `IOptions<OpcoesI50>` já ligada em `Program.cs`). O
   `ConstrutorI50` já recebe `OpcoesI50`; basta ler a nova flag. Ligado → preenche
   `placaVeiculo` com `nota.PlacaVeiculo`; desligado → mantém `""` (comportamento atual).
4. **Campo novo no modelo**: `NotaFiscal.PlacaVeiculo` (string, default `""`, não-nulo — coerente
   com os demais campos string obrigatórios do record; os opcionais de transporte são `string?`,
   mas a placa, por ir sempre a um campo string do payload, fica `string` com default `""`).
5. **Manuais no pacote (decisão de empacotamento)**: `publicar.ps1` copia o conteúdo final de
   `docs\manuais` (os `.md/.html/.pdf`, `README.md` e `assets\`; **exclui** `gerar-manuais.ps1`)
   para `artifacts\<versao>\manuais\` — uma pasta **irmã** de `app\`, NUNCA dentro do zip de app, de
   modo que os manuais jamais entrem em `app\current`. Também gera um `manuais.zip` em
   `artifacts\<versao>\` para transporte ao servidor. `instalar.ps1` copia os manuais para
   `C:\Friozem\importacao_notas\manuais` (idempotente, sobrescreve), com parâmetro `-CopiarManuais`
   (default `$true`) e origem resolvida a partir de `-ManuaisDir`/`-ManuaisZip` ou, por default, de
   uma pasta/zip `manuais` irmã da origem da release. Mantém a separação app vs manuais já existente
   na estrutura do servidor.
6. **Capítulo da placa só no MANUAL-TECNICO**. O MANUAL-USUARIO não fala de placa.

## Itens (ordenados por dependência)

- [ ] 1. Adicionar o campo `PlacaVeiculo` (string, default `""`) ao modelo `NotaFiscal`, com
      XML-doc apontando a origem `transp/veicTransp/placa`.
      Files: `src/KitWmsIntegrador.Core/Modelos/NotaFiscal.cs`
      Verify: `dotnet build KitWmsIntegrador.sln -c Release` — 0 erros.

- [ ] 2. No `LeitorNotaFiscalXml`, ler `veicTransp/placa` dentro de `LerTransp` (novo caso
      `veicTransp` que abre `ReadSubtree()` e lê `placa` via `LocalName`), propagar o valor até o
      `return new NotaFiscal { ... PlacaVeiculo = placa ?? "" }`. NÃO ler `reboque`. Ausência →
      `""`. Seguir o mesmo estilo dos métodos `LerTransporta`/`LerVolume`.
      Files: `src/KitWmsIntegrador.Core/Leitura/LeitorNotaFiscalXml.cs`
      Verify: `dotnet build KitWmsIntegrador.sln -c Release` — 0 erros.

- [ ] 3. Adicionar testes de parser da placa em `LeitorNotaFiscalXmlTests`: (i) Xml1 →
      `PlacaVeiculo == "AYC8G72"`; (ii) Xml2 → `PlacaVeiculo == "QPJ1H28"`. Reaproveitar os XMLs já
      em `Recursos\`.
      Files: `tests/KitWmsIntegrador.Tests/LeitorNotaFiscalXmlTests.cs`
      Verify: `dotnet test KitWmsIntegrador.sln -c Release` — testes novos passam e nenhum existente
      quebra.

- [ ] 4. Adicionar a opção `EnviarPlacaVeiculo` (bool, default `true`) à `OpcoesI50`, com XML-doc
      explicando liga/desliga (true = envia a placa lida de `veicTransp/placa`; false = envia em
      branco). Override por env `I50__EnviarPlacaVeiculo`.
      Files: `src/KitWmsIntegrador.Core/Configuracao/OpcoesI50.cs`
      Verify: `dotnet build KitWmsIntegrador.sln -c Release` — 0 erros.

- [ ] 5. No `ConstrutorI50.Construir`, preencher `PedidoI50.PlacaVeiculo` com `nota.PlacaVeiculo`
      quando `_i50.EnviarPlacaVeiculo` for `true`; caso contrário `""` (string vazia explícita, como
      hoje). Enviar a placa como está (sem transformação). `Program.cs` já injeta `OpcoesI50`; sem
      mudança de DI.
      Files: `src/KitWmsIntegrador.Core/Construtores/ConstrutorI50.cs`
      Verify: `dotnet build KitWmsIntegrador.sln -c Release` — 0 erros.

- [ ] 6. Adicionar testes ao `ConstrutorI50Tests`: (i) flag ON + Xml1 → `pedido.PlacaVeiculo ==
      "AYC8G72"`; (ii) flag OFF (`EnviarPlacaVeiculo = false`) + Xml1 → `pedido.PlacaVeiculo == ""`;
      (iii) flag ON + nota sem placa (`LerXml1() with { PlacaVeiculo = "" }`) → `""` sem erro;
      (iv) end-to-end com o XML real (`AYC8G72`). Atualizar `I50Padrao()` para `EnviarPlacaVeiculo =
      true` de modo a cobrir o default. NÃO quebrar os testes existentes do contrato JSON.
      Files: `tests/KitWmsIntegrador.Tests/ConstrutorI50Tests.cs`
      Verify: `dotnet test KitWmsIntegrador.sln -c Release` — todos passam.

- [ ] 7. Adicionar a chave `I50:EnviarPlacaVeiculo = true` ao `appsettings.json` versionado, dentro
      da seção `I50` existente, com comentário em `_comentario` (ou comentário próprio) explicando
      liga/desliga.
      Files: `src/KitWmsIntegrador.Worker/appsettings.json`
      Verify: `dotnet build KitWmsIntegrador.sln -c Release` — 0 erros; conferir que o JSON é válido
      (o build do Worker copia e valida o appsettings).

- [ ] 8. No molde de produção `deploy/config-exemplo/appsettings.Production.json` (que hoje NÃO tem
      seção `I50`), adicionar uma seção `I50` mínima com `_comentario` e `EnviarPlacaVeiculo: true`
      documentando a chave (o molde só traz o que muda/é relevante em produção).
      Files: `deploy/config-exemplo/appsettings.Production.json`
      Verify: JSON válido; `dotnet build KitWmsIntegrador.sln -c Release` continua 0 erros (arquivo
      não entra no build, mas confirmar que não há regressão).

- [ ] 9. Atualizar o exemplo `docs/exemplos/I50-enriquecida-exemplo.json` para refletir a placa
      preenchida: `"placaVeiculo": "AYC8G72"` (o exemplo é derivado do Xml1). É edição manual
      (não há rotina que o gere); ajustar só esse campo e documentar no corpo do commit.
      Files: `docs/exemplos/I50-enriquecida-exemplo.json`
      Verify: JSON válido (`Get-Content ... | ConvertFrom-Json` não lança).

- [ ] 10. No `publicar.ps1`, após o publish/zip de `app\`, copiar o conteúdo de `docs\manuais`
      (todos os `.md/.html/.pdf`, `README.md` e `assets\`, EXCLUINDO `gerar-manuais.ps1`) para
      `artifacts\<versao>\manuais\` e gerar `artifacts\<versao>\manuais.zip` (via
      `Compress-ZipPortavel`). NÃO incluir manuais no zip de app. Logar o caminho e o tamanho.
      Files: `deploy/publicar.ps1`
      Verify: `powershell -File deploy\publicar.ps1 -Versao 0.0.0-test -WhatIf` roda sem erro; sem
      `-WhatIf` (ambiente local) gera `artifacts\0.0.0-test\manuais\` com HTML/PDF e `manuais.zip` >
      0 bytes. Remover o artefato de teste depois.

- [ ] 11. No `instalar.ps1`, adicionar `-CopiarManuais` (bool, default `$true`), `-ManuaisDir` e
      `-ManuaisZip` (origem opcional). Após apontar `app\current`, copiar os manuais para
      `C:\Friozem\importacao_notas\manuais` (criar via `Confirm-Directory`, sobrescrever,
      idempotente). Resolver a origem: `-ManuaisZip`/`-ManuaisDir` se informados; senão procurar uma
      pasta/zip `manuais` irmã de `-ReleaseDir`/`-ReleaseZip`; se não achar, logar alerta e seguir
      (não falhar a instalação). Logar o caminho de destino. Garantir que os manuais fiquem em
      `...\manuais`, FORA de `app\current`.
      Files: `deploy/instalar.ps1`
      Verify: `powershell -File deploy\instalar.ps1 -ReleaseDir <dir> -ManuaisDir <dir-manuais>
      -RaizInstalacao <temp> -WhatIf` mostra os passos de cópia dos manuais sem erro de parse.
      (Execução real de instalação é conduzida pelo usuário no servidor — não rodar aqui.)

- [ ] 12. (Opcional/coerência) Refletir o mesmo passo de manuais em `publicar-release.ps1` se for
      desejável que a promoção de release no servidor também atualize os manuais. Decisão: manter o
      copy de manuais no `instalar.ps1` como ponto único; `publicar-release.ps1` NÃO precisa copiar
      manuais (a promoção troca só `app\current`). Documentar essa escolha no commit. (Sem mudança
      de arquivo salvo se mantida a decisão.)

- [ ] 13. Documentar em `docs/INSTALACAO-SERVIDOR.md` que os manuais ficam em
      `C:\Friozem\importacao_notas\manuais` no servidor (acrescentar na estrutura de diretórios da
      §1 e uma nota no passo 3.4 de instalação).
      Files: `docs/INSTALACAO-SERVIDOR.md`
      Verify: revisão textual; links/caminhos consistentes com os scripts.

- [ ] 14. Adicionar ao `MANUAL-TECNICO.md` um CAPÍTULO novo sobre ligar/desligar o envio da placa:
      explica a chave `I50:EnviarPlacaVeiculo` em `config\appsettings.Production.json` (true = envia
      a placa lida de `transp/veicTransp/placa`; false = em branco); que após editar é preciso
      REINICIAR o serviço (`servico.ps1 -Acao Restart` em
      `C:\Friozem\importacao_notas\tools\deploy`); que a placa só vai se existir no XML; origem
      `transp/veicTransp/placa`. Adicionar a linha correspondente à tabela "Quero fazer X" (§12) e
      inserir o item no Índice. Mencionar também que os manuais ficam em
      `C:\Friozem\importacao_notas\manuais` no servidor.
      Files: `docs/manuais/MANUAL-TECNICO.md`
      Verify: revisão textual; o Índice e a tabela refletem o novo capítulo. MANUAL-USUARIO NÃO é
      tocado.

- [ ] 15. Regenerar `MANUAL-TECNICO.html` e `MANUAL-TECNICO.pdf` (e USUARIO só se tocado — não é):
      rodar `gerar-manuais.ps1` e depois o Edge headless (comandos do README). Manter capa e
      diagramas. Confirmar PDF > 0 bytes.
      Files: `docs/manuais/MANUAL-TECNICO.html`, `docs/manuais/MANUAL-TECNICO.pdf`
      Verify: `powershell -File docs\manuais\gerar-manuais.ps1` gera o HTML; Edge headless gera o
      PDF; `(Get-Item docs\manuais\MANUAL-TECNICO.pdf).Length -gt 0` é `True`.

## Notas / lacunas

- Se o Edge/Chrome headless não estiver disponível no ambiente, o PDF pode ser gerado manualmente
  (Plano B do README) — registrar no output se não for possível gerar automaticamente.
- O exemplo `I50-enriquecida-exemplo.md` (companheiro do `.json`) não precisa de mudança de dados;
  revisar só se mencionar explicitamente `placaVeiculo` vazio.
