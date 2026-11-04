# Verificacao Etapa 7b

## Parte A (ajustes: foto do pacote, e-mail Sempre, correcoes da revisao)

Comandos executados em `c:\wms-integrador` (Windows, .NET 8):

| Comando | Resultado |
|---|---|
| `dotnet build KitWmsIntegrador.sln -nologo --no-incremental` | Compilacao com exito, **0 Aviso(s), 0 Erro(s)** |
| `dotnet test KitWmsIntegrador.sln -nologo --no-build` (3 execucoes seguidas) | **Aprovado: 241, Falha: 0, Ignorado: 0, Total: 241** em todas |

Contagem: 206 testes anteriores - 1 (`Debounce_...`, substituido por teste de foto) + 36 novos = 241.

### O que mudou

- `GerenciadorPacote` reescrito: janela de coleta FIXA, foto = arquivos prontos no fim da janela, processamento por transporte em sequencia, fechamento no mesmo ciclo (relatorio + 1 e-mail), proximo pacote so em novo ciclo. Quietude removida (`OpcoesPacote.QuietudeSegundos`); novo `BackoffMaximoSegundos` (120). Foto sem arquivo pronto = volta a Ocioso com backoff exponencial (`ProximaFotoUtc`); instaveis nunca abrem pacote sozinhos.
- `PeriodoColeta` (primeira/ultima gravacao) no log, no corpo do e-mail e no cabecalho de HTML/PDF (parametro opcional `periodo` em `IGeradorRelatorio`).
- `OpcoesEmail.EnviarQuando` padrao `Sempre`; `Habilitado=false` mantido; appsettings/example/docs atualizados.
- `OpcoesPasta.Validar(pastaRelatorios)`: recusa Estado e Relatorio:PastaSaida iguais/dentro de Processados/Erro; `Worker` passa `Relatorio:PastaSaida`.
- `ConsolidadorRegistros` (um registro por arquivo: primeiro integrado, senao primeiro) usado na retomada e no fechamento; retomada reconcilia moves (`ProcessadorLote.ReconciliarMove`) sem reenviar ao WMS; `RepetirMovesPendentes` a cada ciclo Ocioso.
- Comentario de `Worker\Program.cs` atualizado. Docs: `docs\OPERACAO.md` e `docs\ARQUITETURA.md`. `docs\diario` e `docs\controle` nao tocados; Orquestrador, construtores e I60 inalterados.

### Testes novos (arquivos em `tests\KitWmsIntegrador.Tests`)

- `FotoPacoteTests`: arquivo que chega durante o processamento fica para o proximo pacote; 2 pacotes = 2 PDFs/2 e-mails; ordem por transporte (A,A,B,B); janela fixa; chegada continua sem perda/duplicidade.
- `PeriodoColetaTests`: primeira/ultima gravacao e presenca no e-mail e no HTML.
- `EmailPorPacoteTests`: Sempre/Nunca/ComProblema; SMTP nao configurado nao falha.
- `OpcoesPastaValidacaoTests`: Estado/Relatorios dentro de Processados/Erro.
- `RetomadaDeduplicacaoTests`: consolidacao, retomada sem WMS, arquivo regravado = novo, moves pendentes.
- `ArquivoInstavelTests`: arquivo travado alem do timeout nao abre pacote nem gera loop de foto; alerta unico em log; backoff 20/40/80/120/120 s; arquivo em gravacao nao impede outros.

### Iteracao de revisao (review-etapa7b-a)
- Achado 1 (confirmed): `GerenciadorPacote.Retomar` descarta o registro antigo do diario quando o arquivo foi regravado depois do registro (`LastWriteTimeUtc > Horario`); o arquivo e reprocessado como novo e o relatorio mostra o resultado novo. Teste `Retomada_ArquivoRegravadoDepoisDoRegistro_ETratadoComoNovo` agora forca falha no WMS e afirma a linha do relatorio (nao integrada).
- Achado 2 (possible): backoff de foto vazia passa a valer so para instaveis; arquivo PRONTO novo abre pacote sem esperar (`prontosNovos`). Novo teste `ArquivoProntoNovo_IgnoraBackoffDeFotoVazia`; documentado em `docs\OPERACAO.md` e `ARQUITETURA.md`.
| Comando | Resultado |
|---|---|
| `dotnet build KitWmsIntegrador.sln -nologo --no-incremental` | **0 Aviso(s), 0 Erro(s)** |
| `dotnet test KitWmsIntegrador.sln -nologo --no-build` (3 execucoes) | **Aprovado: 242, Falha: 0, Total: 242** em todas |

## Parte B (teste de esforço, 100% local com fakes)

Comandos executados em `c:\wms-integrador` (Windows, .NET 8.0.427, i7-1355U, 15,7 GB). Nenhuma chamada HTTP real ao WMS.

### Build e testes (sem STRESS)
- `dotnet build KitWmsIntegrador.sln` -> **0 Aviso(s), 0 Erro(s)**.
- `dotnet build KitWmsIntegrador.sln -c Release` -> 0 Aviso(s), 0 Erro(s).
- `dotnet test KitWmsIntegrador.sln --no-build` -> Com falha: 0, Aprovado: 242, Ignorado: 9, Total: 251, duração ~2 s.
  Os 9 ignorados são os `[StressFact]` ("defina STRESS=1"); o `dotnet test` padrão continua rápido.

### Stress (STRESS=1, Release, uma invocação por cenário)
Comando-modelo: `$env:STRESS='1'; $env:STRESS_SAIDA='c:\wms-integrador\.agents\tasks\stress-resultados-base.jsonl'; dotnet test tests\KitWmsIntegrador.Tests -c Release --no-build --filter "FullyQualifiedName~CenariosStress.<Cenario>"`

| Cenário (filtro) | Resultado | Integridade |
|---|---|---|
| Cenario1_500Xmls_25Transportes_1Pacote | Aprovado | OK, 1 pacote, 1 e-mail |
| Cenario2_2000Xmls | Aprovado | OK, 1 pacote, 1 e-mail |
| Cenario3_ChegadaContinua | Aprovado | OK, 5 pacotes, 5 e-mails, 600/600 arquivos |
| Cenario4_Mistura10PorCentoErros | Aprovado | OK, Erro\<Categoria> 4x25, 900 Processados |
| Cenario5_Relatorio_1000/5000/10000Linhas | Aprovado | PDF 94/432/857 páginas, rodapé Página X de Y |
| Comparativo_Paralelismo (1, 2, 4) | Aprovado | OK nas 6 execuções, concorrência máx. observada <= paralelismo |
| Cenario_SemLatencia_2000Notas | Aprovado | OK, 491 notas/s |

Números completos (tempo, notas/s, pico WS, heap, GC, HTML/PDF, tamanho do PDF): `.agents\tasks\relatorio-stress-etapa7.md`
e `.agents\tasks\stress-resultados-base.jsonl`. Nenhuma correção de código de produção foi necessária (nenhum gargalo simples
medido; o custo dominante é a latência serial do WMS). Logo, a execução do stress foi feita no código final.

### Confirmações
- `git diff 0853335 HEAD --stat -- src/KitWmsIntegrador.Core/Orquestrador src/KitWmsIntegrador.Core/Construtores docs/diario docs/controle src/KitWmsIntegrador.Core/Configuracao/OpcoesI60.cs` -> vazio (Orquestrador, construtores, OpcoesI60, docs\diario e docs\controle sem alteração). I60 permanece desligada (`OpcoesI60.Habilitada=false`; o harness usa `Habilitada=false`).
- `Select-String "ClienteWmsHttp|HttpClient" tests\KitWmsIntegrador.Tests\Stress\*.cs` -> vazio (nenhum HttpClient real no harness).
- `git check-ignore -v src\KitWmsIntegrador.Worker\email.settings.json` -> ignorado por `.gitignore:30`.
- Observação: `docs/apresentacao/deck.html` aparece modificado na árvore de trabalho; não foi tocado por esta etapa e não é incluído nos commits.