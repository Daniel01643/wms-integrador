# Plano de Implementação — Tratamento de Arquivos NÃO-XML

Contexto: hoje `GerenciadorPacote.ListarXml()` lista só `*.xml`; o resto fica parado na entrada. O
usuário quer que arquivos não-XML sejam MOVIDOS para `Erro\<pacote>\ExtensaoInvalida`, REGISTRADOS
no relatório/e-mail como "Arquivo ignorado (não é XML)" e LIMPOS pela rotina de 10 dias, com exceção
de arquivos temporários de cópia (ignorados, deixados na entrada).

Fluxo análogo existente (a imitar): em `ProcessadorLote.ProcessarAsync`, um XML que não parseia vira
`RegistroDiario` (XmlInvalido=true, Status=NaoIntegrada, Categoria=`CategoriaErro.XmlInvalido`,
Destino=`DestinoArquivo.Erro`) via `Registro(...)` + `Finalizar(...)`, que grava no diário e chama
`OrganizadorArquivos.Mover(...)`; em Erro com `OrganizacaoErro.PorCategoria`, cai em
`Erro\<id>\XmlInvalido\` e grava `<arquivo>.motivo.txt`. O não-XML será ANÁLOGO, com categoria e
status próprios, e SEM passar pelo Orquestrador (não é nota).

Decisões de design (registradas aqui, uma frase cada):
- Extensões temporárias default (configuráveis em `Pastas:ExtensoesTemporarias`): `.tmp .temp
  .crdownload .part .partial .filepart .!ut .opdownload .bak .lock` + ocultos/sistema — são arquivos
  em trânsito que podem virar `.xml`, então ficam na entrada em paz.
- Flag `Pastas:TratarNaoXmlComoErro` (default true) permite desligar o tratamento (volta ao comportamento atual).
- A separação XML / temporário / não-XML fica na DETECÇÃO (não no Orquestrador), preservando
  Orquestrador e construtores intactos (restrição).
- Novo `StatusLinhaRelatorio.ArquivoIgnorado` com cor neutra/cinza (não é falha de WMS, é aviso).
- Não-XML não conta como nota real: entra num contador próprio "Arquivos ignorados" e não infla
  Total/NaoIntegradas.
- E-mail: pacote com ≥1 nota real segue a política normal (ignorados aparecem no relatório/corpo);
  pacote 100% não-xml gera relatório e NÃO envia e-mail (loga aviso).

Baseline confirmado localmente: `dotnet build` 0/0; `dotnet test` 323 aprovados, 9 ignorados (stress), 0 falhas.

Comandos reais: build `dotnet build C:\wms-integrador\KitWmsIntegrador.sln -c Debug --nologo`;
testes `dotnet test C:\wms-integrador\KitWmsIntegrador.sln -c Debug --nologo`.

Restrições: branch main, SEM worktree; commits pequenos por feature, só arquivos nomeados (nunca
`git add .`); I60 permanece desligada; não tocar `docs/diario` nem `docs/controle`; validação LOCAL
(sem deploy no servidor).

---

- [ ] 1. Adicionar `CategoriaErro.ExtensaoInvalida` ao enum.
      Novo valor com XML-doc, junto de `XmlInvalido`.
      Arquivos: `C:\wms-integrador\src\KitWmsIntegrador.Core\Pacote\CategoriaErro.cs`
      Verificar: `dotnet build C:\wms-integrador\KitWmsIntegrador.sln -c Debug --nologo` — 0/0.

- [ ] 2. Adicionar `StatusLinhaRelatorio.ArquivoIgnorado` e tratá-lo em `LinhaRelatorioErro`.
      Novo valor no enum; `StatusDescricao` retorna "Arquivo ignorado (não é XML)"; `EstaIntegrada`
      permanece false; helper `ArquivoIgnorado`.
      Arquivos: `C:\wms-integrador\src\KitWmsIntegrador.Core\Relatorio\StatusLinhaRelatorio.cs`,
      `C:\wms-integrador\src\KitWmsIntegrador.Core\Relatorio\LinhaRelatorioErro.cs`
      Verificar: build 0/0.

- [ ] 3. Contador "Arquivos ignorados" no `ResumoRelatorio` e ajuste das contagens em `ModeloRelatorio.Montar`.
      Novo campo `int ArquivosIgnorados`; `Total` passa a contar só notas reais (exclui ignorados);
      ignorados não entram em Integradas/Pendentes/NaoIntegradas nem em `GrupoTransporte.ComProblema`.
      Arquivos: `C:\wms-integrador\src\KitWmsIntegrador.Core\Relatorio\ModeloRelatorio.cs`
      Verificar: build 0/0 (os testes de resumo rodam no item 13).

- [ ] 4. Renderizar o status ignorado no HTML (cor neutra, cartão, filtro).
      `ChaveStatus` → "ignorado"; CSS `.st-ignorado`/`.card.ign` cinza; cartão "Arquivos ignorados";
      `<option value="ignorado">` no filtro; coluna Nota = nome do arquivo, Transporte vazio.
      Arquivos: `C:\wms-integrador\src\KitWmsIntegrador.Core\Relatorio\RenderizadorHtml.cs`
      Verificar: build 0/0.

- [ ] 5. Renderizar o status ignorado no PDF (cor neutra no rótulo e cartão).
      Cor cinza na etiqueta quando `ArquivoIgnorado`; cartão "Arquivos ignorados".
      Arquivos: `C:\wms-integrador\src\KitWmsIntegrador.Core\Relatorio\RenderizadorPdf.cs`
      Verificar: build 0/0.

- [ ] 6. Corpo do e-mail: cartão "Arquivos ignorados" e chip cinza.
      `AppendCartoes` ganha o cartão; `AppendChipStatus` mapeia "ignorado" para cinza; linhas
      ignoradas aparecem na tabela com o chip próprio.
      Arquivos: `C:\wms-integrador\src\KitWmsIntegrador.Core\Email\CorpoEmailResumo.cs`
      Verificar: build 0/0.

- [ ] 7. Regra de e-mail do pacote 100% não-xml em `PoliticaEnvioEmail` + `FechadorPacote`.
      Política recebe a contagem de notas reais; se 0, retorna false sempre; `FechadorPacote` calcula
      `notasReais`, loga "Pacote sem notas, apenas arquivos ignorados; relatório salvo, e-mail não
      enviado." e retorna true.
      Arquivos: `C:\wms-integrador\src\KitWmsIntegrador.Core\Email\PoliticaEnvioEmail.cs`,
      `C:\wms-integrador\src\KitWmsIntegrador.Core\Pacote\FechadorPacote.cs`
      Verificar: build 0/0.

- [ ] 8. Novas opções em `OpcoesPasta` (`ExtensoesTemporarias`, `TratarNaoXmlComoErro`) + helper `EhExtensaoTemporaria`.
      Lista default das extensões; flag default true; helper normaliza extensão e trata Hidden/System
      como temporário. Não alterar `Validar()`.
      Arquivos: `C:\wms-integrador\src\KitWmsIntegrador.Core\Configuracao\OpcoesPasta.cs`
      Verificar: build 0/0.

- [ ] 9. Criar `ClassificadorEntrada` (separa XML / temporário-ignorado / não-xml definitivo).
      Classe (estática ou injetável) que, dada a lista top-level e `OpcoesPasta`, devolve os dois
      grupos de ação; respeita `TratarNaoXmlComoErro`.
      Arquivos: `C:\wms-integrador\src\KitWmsIntegrador.Core\Pacote\ClassificadorEntrada.cs`
      Verificar: `dotnet test C:\wms-integrador\KitWmsIntegrador.sln -c Debug --nologo` — os testes do item 12 passam.

- [ ] 10. `GerenciadorPacote.ListarXml`: enumerar `*` top-level e usar `ClassificadorEntrada`; expor os
      não-xml definitivos à foto do pacote.
      XML seguem o caminho atual (detector/foto/processador). Não-xml definitivos estáveis são
      entregues ao `ProcessadorLote` para registrar+mover na foto, sem Orquestrador. Um pacote só de
      ignorados pode abrir (item 4 pede o tratamento).
      Arquivos: `C:\wms-integrador\src\KitWmsIntegrador.Core\Pacote\GerenciadorPacote.cs`
      Verificar: build 0/0 (comportamento validado nos itens 12–14).

- [ ] 11. `ProcessadorLote`: tratar não-xml definitivos (registrar `ExtensaoInvalida` + mover) e ajustar `RegistroDiario.ParaLinha`.
      Para cada não-xml: motivo "Arquivo ignorado: extensão <ext> não é XML. Coloque apenas arquivos
      .xml na pasta de entrada.", `RegistroDiario` (NumeroNota=nome do arquivo, Transporte vazio,
      Status=ArquivoIgnorado, Categoria=ExtensaoInvalida, Destino=Erro) via `Finalizar(...)`. Em
      `ParaLinha`, quando Status==ArquivoIgnorado, não usar o rótulo "(sem transporte)".
      Arquivos: `C:\wms-integrador\src\KitWmsIntegrador.Core\Pacote\ProcessadorLote.cs`,
      `C:\wms-integrador\src\KitWmsIntegrador.Core\Pacote\DiarioPacote.cs`
      Verificar: `dotnet test` — itens 13 em diante passam.

- [ ] 12. Config: `appsettings.json` e molde de produção.
      Adicionar `ExtensoesTemporarias` e `TratarNaoXmlComoErro` à seção `Pastas` (com comentário) nos
      dois arquivos; `Program.cs` já bina a seção (confirmar, sem mudar construtor).
      Arquivos: `C:\wms-integrador\src\KitWmsIntegrador.Worker\appsettings.json`,
      `C:\wms-integrador\deploy\config-exemplo\appsettings.Production.json`,
      `C:\wms-integrador\src\KitWmsIntegrador.Worker\Program.cs` (verificação)
      Verificar: build 0/0.

- [ ] 13. Testes de unidade do `ClassificadorEntrada`.
      `.xml`→XML; `.tmp/.part/.crdownload`→ignorado; `.pdf/.txt/.xlsx`→não-xml; flag false→ignorado;
      extensões custom; arquivo oculto→ignorado.
      Arquivos: `C:\wms-integrador\tests\KitWmsIntegrador.Tests\ClassificadorEntradaTests.cs`
      Verificar: `dotnet test` — novos testes verdes.

- [ ] 14. Testes do `ProcessadorLote` para não-xml + helper em `AmbientePacote`.
      (i) `.pdf`/`.txt` viram `ExtensaoInvalida`, movem para `Erro\<id>\ExtensaoInvalida`, geram
      `.motivo.txt`, Status=ArquivoIgnorado, sem chamar o Orquestrador; (ii) `.tmp/.part` permanece
      na entrada. Adicionar `ArquivosErroCategoriaQualquer(categoria)` em `AmbientePacote` (usa `*`,
      não `*.xml`) para enxergar não-xml.
      Arquivos: `C:\wms-integrador\tests\KitWmsIntegrador.Tests\ProcessadorLoteTests.cs`,
      `C:\wms-integrador\tests\KitWmsIntegrador.Tests\Infra\AmbientePacote.cs`
      Verificar: `dotnet test` — verdes.

- [ ] 15. Testes de relatório/resumo com linha `ArquivoIgnorado`.
      `StatusDescricao`/`ChaveStatus`/contador `ArquivosIgnorados`; HTML contém `st-ignorado` e o
      cartão; PDF gera (`%PDF`) sem exceção.
      Arquivos: `C:\wms-integrador\tests\KitWmsIntegrador.Tests\RelatorioRefinadoTests.cs`
      Verificar: `dotnet test` — verdes.

- [ ] 16. Testes do e-mail por pacote (100% não-xml e misto).
      (iii) 100% não-xml com `Sempre` → gera relatório e NÃO envia; (iv) misto (1 xml + 1 pdf) →
      envia 1 e-mail listando ambos.
      Arquivos: `C:\wms-integrador\tests\KitWmsIntegrador.Tests\EmailPorPacoteTests.cs`
      Verificar: `dotnet test` — verdes.

- [ ] 17. Teste da limpeza de 10 dias cobrindo `Erro\<id>\ExtensaoInvalida`.
      Subpasta de pacote com `ExtensaoInvalida` vencida (>10 dias) é removida por inteiro (análogo ao
      caso de `XmlInvalido`/`ProdutoNaoCadastrado`).
      Arquivos: `C:\wms-integrador\tests\KitWmsIntegrador.Tests\LimpezaSubpastaTests.cs`
      Verificar: `dotnet test` — verde; corrigir qualquer regressão de contagem em testes existentes.

- [ ] 18. Script de limpeza forçada `deploy\limpar-extensao-invalida.ps1`.
      `[CmdletBinding(SupportsShouldProcess)]`, `$ErrorActionPreference='Stop'`, compat PS 4.0/5.1,
      UTF-8 BOM; `-Caminho` (default `C:\Friozem\kitbrf`); guarda: só apaga pastas folha
      `ExtensaoInvalida` DENTRO de `<Entrada>\Erro`, nunca fora, sem seguir reparse points; confirma
      antes (ou `-Force`); idempotente.
      Arquivos: `C:\wms-integrador\deploy\limpar-extensao-invalida.ps1`
      Verificar: parse-check `[ScriptBlock]::Create((Get-Content -Raw ...))` → OK; `-WhatIf` em árvore
      temporária não apaga nada e lista só `ExtensaoInvalida`; execução real `-Force` remove
      `ExtensaoInvalida` e preserva `XmlInvalido`; primeiros 3 bytes = `EF BB BF`.

- [ ] 19. Documentar o script em `INSTALACAO-SERVIDOR.md`.
      Subseção: o que faz, quando usar, e que o `instalar.ps1` o replica para `tools\deploy`
      (Copy-ScriptsDeployParaServidor copia todos os `.ps1`). Exemplo com `-WhatIf`.
      Arquivos: `C:\wms-integrador\docs\INSTALACAO-SERVIDOR.md`
      Verificar: revisão manual (texto presente e correto).

- [ ] 20. Atualizar `MANUAL-USUARIO.md` (FAQ do arquivo não-XML).
      "E se eu colocar um arquivo que não é XML?" → não integra, move para `Erro\...\ExtensaoInvalida`,
      registra como "Arquivo ignorado", limpa em 10 dias; coloque só `.xml`. Nota sobre temporários.
      Arquivos: `C:\wms-integrador\docs\manuais\MANUAL-USUARIO.md`
      Verificar: revisão manual.

- [ ] 21. Atualizar `MANUAL-TECNICO.md`.
      `ExtensaoInvalida`, `ArquivoIgnorado` (cinza, contador), detecção em 3 grupos +
      `ExtensoesTemporarias`/`TratarNaoXmlComoErro`, regra de e-mail do pacote 100% não-xml, o script
      `limpar-extensao-invalida.ps1` e a cobertura da limpeza de 10 dias.
      Arquivos: `C:\wms-integrador\docs\manuais\MANUAL-TECNICO.md`
      Verificar: revisão manual.

- [ ] 22. Regenerar HTML e PDF dos manuais.
      Rodar `gerar-manuais.ps1` (HTML) e gerar os PDFs via Edge headless (ver `docs\manuais\README.md`).
      Manter capa e diagramas. Se o diagrama do fluxo do usuário puder ganhar a nota "arquivo não-XML
      vai para Erro", adicionar (opcional).
      Arquivos: `C:\wms-integrador\docs\manuais\MANUAL-USUARIO.html`,
      `C:\wms-integrador\docs\manuais\MANUAL-TECNICO.html`, e os respectivos `.pdf`
      Verificar: `powershell -ExecutionPolicy Bypass -File C:\wms-integrador\docs\manuais\gerar-manuais.ps1`
      roda sem erro; HTML atualizados contêm "Arquivo ignorado" e "ExtensaoInvalida"; PDFs com
      timestamp recente (ou anotar indisponibilidade do Edge).

- [ ] 23. Verificação final da suíte.
      Rodar build + testes completos.
      Verificar: `dotnet build ... -c Debug --nologo` → 0/0; `dotnet test ... -c Debug --nologo` →
      0 falhas, aprovados ≥ 323 + novos.

## Mapa requisito → arquivos

- Req 1 (detecção 3 grupos): itens 8, 9, 10 — `OpcoesPasta.cs`, `ClassificadorEntrada.cs`, `GerenciadorPacote.cs`.
- Req 2 (tratamento não-xml + .motivo.txt): item 11 — `ProcessadorLote.cs`, `DiarioPacote.cs`, `CategoriaErro.cs` (item 1), reusa `OrganizadorArquivos.cs`.
- Req 3 (relatório/e-mail + status + contador): itens 2–6 — `StatusLinhaRelatorio.cs`, `LinhaRelatorioErro.cs`, `ModeloRelatorio.cs`, `MapeadorRelatorio.cs`, `RenderizadorHtml.cs`, `RenderizadorPdf.cs`, `CorpoEmailResumo.cs`.
- Req 4 (e-mail pacote 100% não-xml): item 7 — `PoliticaEnvioEmail.cs`, `FechadorPacote.cs`.
- Req 5 (limpeza 10 dias cobre a pasta): item 17 — teste em `LimpezaSubpastaTests.cs` (sem mudar `LimpadorPastas.cs`).
- Req 6 (config): itens 8, 12 — `OpcoesPasta.cs`, `appsettings.json`, `appsettings.Production.json`, `Program.cs`.
- Req 7 (script deploy): itens 18, 19 — `limpar-extensao-invalida.ps1`, `INSTALACAO-SERVIDOR.md`.
- Req 8 (manuais): itens 20–22 — `MANUAL-USUARIO.md`, `MANUAL-TECNICO.md`, HTML+PDF.

## Testes novos (resumo)
- `ClassificadorEntradaTests.cs` — separação XML/temporário/não-xml, flag, extensões custom, ocultos.
- `ProcessadorLoteTests.cs` — não-xml vira `ExtensaoInvalida` e move; temporário permanece.
- `RelatorioRefinadoTests.cs` — status/ chave/ contador/ HTML/ PDF com `ArquivoIgnorado`.
- `EmailPorPacoteTests.cs` — pacote 100% não-xml não envia; misto envia e lista ambos.
- `LimpezaSubpastaTests.cs` — subpasta `ExtensaoInvalida` vencida removida.
