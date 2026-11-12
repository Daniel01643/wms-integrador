# Relatório: tempo de integração (nível B), cabeçalho/logo e título do relatório

Pedido do usuário de 12/11/2026 (3 itens). Plano: `.agents/tasks/plano-tempo-cabecalho.md`.
Iteração 1 (não havia `review-tempo-cabecalho.json`). Commits locais em `main`, **sem push**.

## O que foi feito

1. **Linha "Notas por transporte: ..." removida** do resumo do HTML e do PDF. A tabela "Notas por
   transporte" e os cabeçalhos de grupo ("Transporte X — N notas · N integradas · N com problema")
   continuam iguais. O e-mail **não tinha** essa linha (nada a remover lá). `ResumoRelatorio.NotasPorTransporte`
   continua existindo no modelo; só deixou de ser renderizado.
2. **Bloco "Tempo de integração" (nível B)** no HTML, no PDF e no e-mail de fechamento, no lugar da
   linha removida (logo após o resumo):
   - Resumo do pacote: início, fim, duração total, notas, tempo médio por nota, notas por segundo.
   - Etapas: espera da janela de coleta, leitura do ZIP/XML, integração no WMS (duração de parede e
     coluna "Por nota" = soma das chamadas I50/I10/I30/I60 ÷ notas, o número comparável aos ~12 s
     manuais).
   - Por onda: só quando há TSP (onda, notas, transportes, tempo da onda). Na PUXADA não aparece.
   - Formato: `850 ms`, `4,2 s`, `45 s`, `1 min 25 s`, `1 h 05 min`; números pt-BR (`1.234`, `0,07`),
     sem depender de ICU (`NumberFormatInfo` próprio).
   - Medido com `IClock` (Orquestrador: cada chamada WMS; ProcessadorLote: leitura e início/fim de cada
     nota, gravados no diário; GerenciadorPacote: janela). Um único `BlocoTempo` alimenta HTML, PDF e
     e-mail, então os números são idênticos (teste de integração confere).
   - Dado ausente (pacote retomado/antigo): linha omitida ou bloco omitido, sem quebrar.
   - Com a tabela por onda, os "chips"/parágrafo "Ondas da Puxada TSP" deixam de aparecer (evita repetir).
     Sem medição, o comportamento antigo permanece.
   - Telemetria retrocompatível: `EsperaJanelaMs`, `LeituraMs`, `IntegracaoWmsMs`, `ChamadasWmsMs`,
     `TempoRelatorioMs`, `Ondas` (opcionais, default 0/null). Diário: `InicioNota`, `DuracaoWmsMs`.
     Resumo semanal (e-mail e PDF): linha "Integração no WMS por nota" só quando > 0 (média só dos
     pacotes com medição).
3. **Logo e cabeçalho**: faixa azul `#17479E` com a logo negativa real, título "Relatório de Integração"
   + subtítulo "KitWms Integrador", barra tricolor e linha de informações com três itens separados
   (`Pacote <id> · PUXADA|TSP|PUXADA + TSP`, `Gerado em ...`, `Coleta: ... a ...`). HTML autocontido
   (logo base64), PDF com a logo sobre a faixa em todas as páginas, `print-color-adjust:exact` para a
   impressão, `alt="Friozem"`.
   - **Causa do "só texto Friozem"**: `Relatorio:LogoPath` vazio em `appsettings.json` e em
     `deploy/config-exemplo/appsettings.Production.json`, e nenhum script de deploy copia logo.
   - **Solução**: a logo negativa (PNG 560×313, 35.060 bytes, decodificada byte a byte do
     `logo-friozem.b64.txt` já versionado no Worker; mesma imagem da apresentação/manuais) virou recurso
     embutido do Core: `src/KitWmsIntegrador.Core/Relatorio/Recursos/logo-friozem-negativo.png`.
     Ordem: `LogoPath` válido → embutida → texto "Friozem". Nada a mudar no servidor; `LogoPath`
     continua opcional (comentários de config e doc atualizados).
   - Efeito colateral tratado: a assinatura do e-mail (fechamento e semanal) e o cabeçalho do PDF
     semanal têm fundo branco, onde a logo branca sumia; ali a logo vai sobre um **selo azul**.

## Mudança de título (reversível)

"Relatório de Erros de Integração" → **"Relatório de Integração"** (HTML `<title>`/`<h1>`, PDF
cabeçalho/rodapé/`Info.Title`). Para reverter: voltar a constante `IdentidadeVisual.Titulo` em
`src/KitWmsIntegrador.Core/Relatorio/IdentidadeVisual.cs` e os testes que citam o texto
(`LogoEmbutidoTests.Titulo_EORelatorioDeIntegracao`, `RelatorioRefinadoTests` 2 pontos,
`RelatorioCabecalhoTempoTests`). Registrado em `docs/RELATORIO-LOGS.md` e no Manual do Usuário.
Nomes de arquivo (`pacote_<id>.html/.pdf`, exemplos `relatorio-erros-exemplo.*`) não mudaram.

## Arquivos alterados

- Novos (Core): `Relatorio/FormatadorDuracao.cs`, `Relatorio/MedicaoTempo.cs` (`MedicaoTempo`,
  `MedicaoOnda`, `DadosPacote`), `Relatorio/BlocoTempo.cs`, `Pacote/EtapasPacote.cs`,
  `Relatorio/Recursos/logo-friozem-negativo.png`.
- Alterados (Core): `Orquestrador/Orquestrador.cs`, `Orquestrador/ResultadoIntegracaoNota.cs`,
  `Pacote/ProcessadorLote.cs`, `Pacote/GerenciadorPacote.cs`, `Pacote/FechadorPacote.cs`,
  `Pacote/DiarioPacote.cs`, `Relatorio/IGeradorRelatorio.cs`, `Relatorio/GeradorRelatorio.cs`,
  `Relatorio/GeradorResumoSemanal.cs`, `Relatorio/IdentidadeVisual.cs`, `Relatorio/RenderizadorHtml.cs`,
  `Relatorio/RenderizadorPdf.cs`, `Relatorio/RenderizadorPdfResumo.cs`, `Email/CorpoEmailResumo.cs`,
  `Email/CorpoEmailResumoSemanal.cs`, `Monitoramento/RegistroTelemetria.cs`, `MapeadorTelemetria.cs`,
  `ResumoPeriodo.cs`, `AgregadorTelemetria.cs`, `Configuracao/OpcoesRelatorio.cs`,
  `KitWmsIntegrador.Core.csproj` (EmbeddedResource; nenhum pacote novo).
- Config: `src/KitWmsIntegrador.Worker/appsettings.json`, `deploy/config-exemplo/appsettings.Production.json`
  (só o `_comentario`; `LogoPath` continua `""`).
- Testes novos: `FormatadorDuracaoTests`, `BlocoTempoTests`, `MedicaoTempoPacoteTests`,
  `TelemetriaTempoTests`, `LogoEmbutidoTests`, `RelatorioCabecalhoTempoTests`, `CorpoEmailTempoTests`,
  `TempoSintetico` (dados sintéticos). Ajustados: `RelatorioRefinadoTests`, `PeriodoColetaTests`,
  `PacoteTspEndToEndTests`, `GerenciadorPacoteTests`, `RetomadaDeduplicacaoTests`, `Stress/GeradorMedido`,
  `ExemplosRelatorio` (gera também o e-mail de exemplo e a conferência visual).
- Docs: `docs/RELATORIO-LOGS.md`, `docs/OPERACAO.md` (§4, §6.1, §8.4), `docs/ARQUITETURA.md` (§2.2),
  `docs/INSTALACAO-SERVIDOR.md` (§5), `docs/manuais/MANUAL-USUARIO.md` (cabeçalho, status, bloco de
  tempo, e-mail), `MANUAL-TECNICO.md` (§10.1 campos/medição/limitação, §10.2 logo), `MANUAL-AGENTE-IA.md`;
  HTML e PDF dos manuais regenerados; `docs/exemplos/*` regenerados (sintéticos). Sem devolução/I40 nos
  manuais. `CONTROLE-ESFORCO.md`, `docs/controle/*`, `docs/diario/*` e `email.settings*.json` não tocados.

## Verificação (rodada nesta sessão)

- `dotnet build KitWmsIntegrador.sln -c Debug`: 0 avisos, 0 erros. `-c Release`: 0 avisos, 0 erros.
- `dotnet test KitWmsIntegrador.sln`: **654 aprovados, 9 ignorados, 0 falhas** (antes 584 + 9; +70 testes).
- `git ls-files | Select-String '\.(xml|zip)$'`: 0. `PackageReference` adicionados: 0. Valores monetários
  no diff: 0. Nenhum envio ao WMS/e-mail real (testes com fakes e `GravadorEmail`); servidor não acessado.
- Observação de ambiente: uma vez o Windows (Controle de Aplicativo) bloqueou a DLL de testes recém-compilada
  ("Uma política de Controle de Aplicativo bloqueou este arquivo"); uma recompilação com conteúdo diferente
  resolveu. Não é problema do código.

## Conferência visual (PNG em `.agents/tasks/`)

- `tempo-cabecalho-relatorio-tsp.png` (HTML, Edge headless 1280 px): faixa azul com logo nítida, título,
  linha "Pacote ... · TSP | Gerado em ... | Coleta: ...", sem a linha "Notas por transporte:", bloco de
  tempo com Resumo/Etapas e tabela por onda (SJC 2 notas/2 transp./13 s; SOROCABA 5/4/35 s). Conferido.
- `tempo-cabecalho-relatorio-puxada.png`: mesmo cabeçalho com "· PUXADA", bloco sem tabela por onda,
  grupos com contadores. Conferido.
- `tempo-cabecalho-email-tsp.png` (corpo do e-mail, logo trocada de CID por data URI só para a prévia):
  cartão "Tempo de integração" com a cara dos demais, mesmos números do relatório, assinatura com logo
  sobre selo azul. Conferido.
- `tempo-cabecalho-pdf-tsp-p1.png`: página 1 do PDF (renderizada via API `Windows.Data.Pdf`, pois o Edge
  headless não rasteriza PDF): logo sobre a faixa (alfa correto), linha de informações, bloco e tabela por
  onda; PDF com 2 páginas, A4 paisagem, texto extraível (testes `Pdf_*`). Conferido. O PDF semanal também
  foi conferido (logo no selo azul).
- Manuais: Usuário 7 → 7 páginas; Técnico 15 → 16 páginas (novas §10.1/10.2).

## Limitações

- **Etapa de relatório**: o tempo de gerar o próprio relatório não aparece nele nem no e-mail (que repete
  o relatório). É medido com `Stopwatch` e fica **só na telemetria** (`TempoRelatorioMs`); nota curta sob
  o bloco avisa o leitor.
- Pacote retomado após queda: espera da janela e leitura ficam sem valor (linhas omitidas; 0 na
  telemetria). Integração no WMS e ondas saem do diário e continuam corretas.
- "Fim do pacote"/"Duração total" vão até o início do fechamento (não incluem relatório/e-mail).
- O tempo de onda vai do início da 1ª nota ao registro da última; a abertura do ZIP está em "Leitura".
- Cartão "Transportes" mostra 0 em pacote só TSP (conta transportes da PUXADA; comportamento anterior,
  não alterado).

## Pendências para o teste do usuário (homolog)

- Conferir num pacote real TSP e num PUXADA: cabeçalho/logo no HTML, no PDF impresso e no Outlook
  (logo por CID no cabeçalho e na assinatura).
- Comparar a coluna "Por nota" de "Integração no WMS" com os ~12 s do processo manual.
- Avaliar o novo título "Relatório de Integração" (reversível, ver acima).

## Esforço (somente horas)

- Horas de IA: cerca de **3 h** (estimativa pelo tempo desta sessão: implementação, testes, conferência
  visual, documentação e manuais).
- Horas humanas: a informar pelo usuário (não estimadas aqui; registro consolidado pelo orquestrador).
