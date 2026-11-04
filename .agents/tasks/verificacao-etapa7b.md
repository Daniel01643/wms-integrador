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
