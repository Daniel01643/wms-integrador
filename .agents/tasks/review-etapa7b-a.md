# Foto do pacote, e-mail Sempre e reconciliação na retomada (Etapa 7b, Parte A)

O gerenciador deixa de fechar o pacote por quietude. Agora a janela de coleta é fixa e, ao fim dela, uma foto congela os XMLs prontos, que são processados por transporte em sequência. O pacote fecha no mesmo ciclo, com relatório e um e-mail. Arquivos que chegam durante o processamento ficam para a foto seguinte. Entram também a política de e-mail `Sempre` por padrão (com `Habilitado=false`), a validação de Estado/Relatórios, a consolidação de registros e a reconciliação de moves na retomada. Esta é a segunda passada: os dois achados da primeira (registro antigo escondendo arquivo regravado; backoff atrasando arquivo pronto) foram corrigidos em c298f48 e cobertos por testes.

Watch for: nenhum item bloqueante. Evidência em `verificacao-etapa7b.md` (build 0/0, 242 testes verdes em 3 execuções); não re-executei build nem testes.

**Verdict**: APPROVED

## High-level view

A janela fixa conta da primeira detecção e a foto usa só os arquivos prontos naquele instante. Com isso, chegada contínua não adia o pacote e nada que chegue depois entra no pacote em andamento. A quietude foi removida e o período de coleta (primeira/última gravação) vai para o log, o corpo do e-mail e o cabeçalho do HTML/PDF.

Arquivo que nunca estabiliza não abre pacote sozinho. Foto sem nenhum pronto não gera relatório vazio e aplica backoff exponencial até 120 s, só avaliando o detector, então sem loop e sem CPU. O backoff vale apenas para instáveis; um arquivo pronto novo abre pacote sem esperar.

Na retomada, o diário é a fonte de verdade. Registro cujo arquivo ainda está na entrada e não foi regravado só é movido, sem WMS. Se o arquivo foi regravado depois do registro, o registro antigo é descartado e o arquivo é reprocessado como novo. `ConsolidadorRegistros` (primeiro integrado, senão primeiro) é a rede de segurança na retomada e no fechamento. `RepetirMovesPendentes` roda a cada ciclo Ocioso.

<details>
<summary>Issues (1)</summary>

1. **[possible] Retomada com registro descartado** — se todos os registros do diário forem descartados por regravação e o arquivo regravado falhar de forma que permaneça na entrada, o pacote usa `_inicio = agora`. Comportamento coerente; sem ação, apenas observar em teste de campo.

</details>

<details>
<summary>Details</summary>

## Retomada: regravado vs. já tratado

A correção de c298f48 resolve o achado anterior. Em `Retomar`, registro com `LastWriteTimeUtc > Horario` sai do conjunto mantido e o arquivo segue para a foto como novo. Registro com arquivo ausente (já movido) é mantido, e registro com arquivo presente e não regravado passa por `ReconciliarMove`. O teste `Retomada_ArquivoRegravadoDepoisDoRegistro_ETratadoComoNovo` agora força falha no WMS e afirma que a linha do relatório é não integrada. O caso de `_registros` vazio após descartes foi tratado (`_inicio = agora`), sem exceção de `Min` em coleção vazia.

## Backoff de foto vazia

A condição `candidatos == 0 || (prontosNovos == 0 && agora < _proximaFoto)` garante que instáveis e aguardando sozinhos respeitam o backoff, enquanto pronto novo abre pacote. A foto com pronto zera `_fotosVazias` e `_proximaFoto`. O teste `ArquivoProntoNovo_IgnoraBackoffDeFotoVazia` cobre isso, e o `ArquivoInstavelTests` cobre alerta único, ausência de loop e a sequência 20/40/80/120 s. O comportamento está documentado em OPERACAO.md e ARQUITETURA.md.

## Validação de Estado/Relatórios

`Validar(pastaRelatorios)` recusa Estado e `Relatorio:PastaSaida` iguais ou dentro de Processados/Erro. `Validar()` delega com null e preserva o chamador existente. O `Worker` passa `Relatorio:PastaSaida`. `OpcoesPastaValidacaoTests` cobre os casos.

## Escopo preservado

O diff não toca `docs\diario`, `docs\controle`, o Orquestrador nem os construtores de blocos/I60. A mudança no construtor do `Worker` (injeção de `OpcoesRelatorio`) é a esperada para a validação. O comentário do `Program.cs` foi atualizado. `QuietudeSegundos` foi removida, `BackoffMaximoSegundos` adicionada e `EnviarQuando` passou a `Sempre`. Testes novos cobrem foto, períodos, e-mail por pacote, validação, deduplicação/retomada e arquivo instável (36 novos mais 2 da iteração).

</details>

<details>
<summary>File map</summary>

Core/Pacote: GerenciadorPacote (foto, backoff, retomada), ProcessadorLote (RepetirMovesPendentes, ReconciliarMove), ConsolidadorRegistros e PeriodoColeta (novos), FechadorPacote (período). Core/Configuracao: OpcoesPacote, OpcoesEmail, OpcoesPasta. Core/Email e Core/Relatorio: período no corpo e no cabeçalho. Worker: Worker.cs, Program.cs, appsettings.json, email.settings.example.json. Docs: OPERACAO.md, ARQUITETURA.md. Testes: seis arquivos novos mais ajustes. Diff completo: `git diff 0853335 HEAD`.

</details>
