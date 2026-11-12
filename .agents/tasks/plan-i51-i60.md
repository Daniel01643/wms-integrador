# Plano de implementação: I51 como gatilho da I60

Fontes: `brief-i51-i60.md`, `docs/ESCOPO-I51-I60.md`, `design-i51-i60.md` (seções citadas como "D§n") e
`design-review-i51-i60.md` (3ª revisão, tratada nos itens marcados **[R1..R9]**). Em conflito, vale o brief.

## Regras gerais (valem para todos os itens)

- Repo `c:\wms-integrador`, .NET 8, `KitWmsIntegrador.sln`, branch `main`, sem worktree. Commits locais por item, sem push.
- Nunca versionar `*.xml`, `*.zip` nem dados reais. Testes geram dados fictícios em runtime.
- Não editar `docs/CONTROLE-ESFORCO.md`, `docs/controle/*`, `docs/diario/*`. Esforço só em horas (humanas e IA), sem valores monetários.
- Deck (`docs/apresentacao/apresentacao.html`) e manuais (`docs/manuais/`) NÃO entram aqui: ficam para a etapa final de documentação.
- Todo código novo de I/O nunca lança por falha de disco (padrão `RegistroOndasTsp`: `.tmp` + `File.Move(overwrite)`).
- Logs sem token, senha, XML nem corpo bruto.
- Com `I51:Habilitada=false` nada novo roda nem toca disco (D§7.2, D§17).
- Comandos: `dotnet build c:/wms-integrador/KitWmsIntegrador.sln -c Release` (0 avisos, 0 erros) e
  `dotnet test c:/wms-integrador/KitWmsIntegrador.sln -c Release`. Baseline: **655 aprovados, 9 ignorados**; o total de
  aprovados só pode crescer. Se o Worker rodando travar DLL, testar numa cópia temporária (e apagá-la depois).
- Estrutura real: `src/KitWmsIntegrador.Core/{ClienteWms,Configuracao,Construtores,Devolucao,Email,Monitoramento,Orquestrador,Pacote,Relatorio}`,
  `src/KitWmsIntegrador.Worker/{Program.cs,Worker.cs,EnvioManualI60.cs,...}`, testes em `tests/KitWmsIntegrador.Tests`
  (fakes em `Infra/`: `FakeClienteWms`, `FakeClock`, `FakeHttpMessageHandler`, `AmbientePacote`, `AmbienteDevolucao`).
  O projeto de testes referencia Core **e** Worker, então registro de DI e CLIs do Worker são testáveis.
- Decisão: **não restruturar o workflow**. As peças compartilham tipos (estado, opções, processador) e se entrelaçam
  (registro ↔ serviço ↔ I60 ↔ relatório); dividir em FEATs separadas geraria costuras caras. O loop existente
  (build-loop, veredito em `.agents/tasks/review-i51-i60.json`) implementa na ordem abaixo.

## Itens obrigatórios do orquestrador e da 3ª revisão (rastreio)

| Id | Tema | Item do plano |
|---|---|---|
| R1 (MEDIUM) | Uma única instância de `ClienteWmsHttp` | 2 |
| R2 (MEDIUM) | `--reenfileirar-i60` em outro processo | 8 (comando em arquivo) e 15 (teste) |
| R3 (MEDIUM) | Filtro de proprietário pelo `CnpjProprietario` do retorno | 6 |
| R4 (NIT) | `ParametrosI60.QtdeVolume` é `decimal?` | 9 |
| R5 (NIT) | `UltimoUnicoEm`/`TentativasUnico` gravados antes da chamada | 7 |
| R6 (NIT) | `documentos` vazio/ausente = nada a retornar, qualquer `sucesso` | 5 |
| R7 (NIT) | Gatilho do TODOS ignora notas muito antigas (`I51:DiasMaxTodos`, padrão 14) | 7 |
| R8 (NIT) | Relatório diário em `try/catch` próprio no `Worker` | 12 |
| R9 (NIT) | Órfão guarda Tipo/SubTipo/Transporte e carimbo do bruto no nome; roteiro manda contar órfãos | 6 e 14 |

Cortáveis (marcar no código/commit; cortar só se as horas estourarem): `I60:Max4xxConsecutivos`, disjuntor por ciclo,
`I51:IntervaloUnicoMinutos`/`MaxUnicoPorCiclo` como chaves, `I51:ValoresNeutros`, `ConsultaManualI51`.
Não cortáveis: `CampoUnico`, `PadroesSemRetorno`, órfãos, `MaxTentativasOutro` + reenfileirar, `Validar()` das três seções, `RelatorioManualI60`.

---

# Implementation Plan

- [ ] 1. Opções e utilitários de texto (base de tudo).
      Criar `Configuracao/OpcoesI51.cs` (D§4.1, mais `DiasMaxTodos`=14, faixa 1..365, **R7**), `Configuracao/OpcoesRelatorioI60.cs` (D§4.3),
      ampliar `Configuracao/OpcoesI60.cs` (D§4.2; padrões novos preservam o comportamento atual; `Validar()`), todos com `Validar()` que
      devolve a lista de problemas. Criar `Separacao/TextoNormalizado.cs` (FormD sem NonSpacingMark, minúsculas, espaços colapsados;
      padrão vazio nunca casa) e `Separacao/ParserRessalvas.cs` (extrai `Ressalvas : {...}`). Listas `Padroes*` como texto separado por `;`.
      Files: `src/KitWmsIntegrador.Core/Configuracao/{OpcoesI51,OpcoesRelatorioI60,OpcoesI60}.cs`, `src/KitWmsIntegrador.Core/Separacao/{TextoNormalizado,ParserRessalvas}.cs`.
      Testes: `OpcoesI51Tests`, `OpcoesI60ValidarTests`, `OpcoesRelatorioI60Tests`, `TextoNormalizadoTests` ("encerrado"/"ENCERRADO"/"Encerrádo", "não confere"/"nao confere", padrão vazio), `ParserRessalvasTests`.
      Verify: `dotnet build ... -c Release` 0 avisos; `dotnet test ... --filter "FullyQualifiedName~Opcoes|FullyQualifiedName~TextoNormalizado|FullyQualifiedName~ParserRessalvas"` passa; `OpcoesI60Tests` existente continua verde.

- [ ] 2. Cliente WMS bruto e instância única **[R1]**. Depende de 1 (nada), mas deve vir antes de qualquer serviço.
      Criar `ClienteWms/IClienteWmsBruto.cs` (`PostBrutoAsync(string identificadorEdi, string jsonBruto, ct)`). Em `ClienteWmsHttp`: extrair `PostCoreAsync`
      (token, retry, backoff, query de 5 parâmetros inalterados), `EnviarAsync(InterfaceWms)` passa a chamá-lo, `MontarQueryString(string)`.
      Em `Program.cs` trocar `AddHttpClient<IClienteWms, ClienteWmsHttp>` por client nomeado `"wms"` (mesma configuração e mesmo
      `SocketsHttpHandler` TLS 1.2/1.3, mais `PooledConnectionLifetime` de alguns minutos), um `AddSingleton<ClienteWmsHttp>` criado com
      `ActivatorUtilities.CreateInstance` + `IHttpClientFactory.CreateClient("wms")`, e `IClienteWms`/`IClienteWmsBruto` apontando para esse singleton.
      Extrair o registro para um método de extensão público em `src/KitWmsIntegrador.Worker/` (ex.: `RegistroClienteWms.AdicionarClienteWms`) para ser testável.
      Files: `Core/ClienteWms/{IClienteWmsBruto,ClienteWmsHttp}.cs`, `Worker/Program.cs`, `Worker/RegistroClienteWms.cs`.
      Testes: `ClienteWmsBrutoTests` (query com `IDENTIFICADOR` configurável, retry do token reaproveitado, corpo bruto devolvido) e
      `RegistroClienteWmsTests` (provider real: `ReferenceEquals(IClienteWms, IClienteWmsBruto)` e igual ao recebido por `Orquestrador` e `ServicoDevolucao`).
      Verify: `ClienteWmsHttpTests` existente sem edição verde; `dotnet test --filter "FullyQualifiedName~ClienteWms|FullyQualifiedName~RegistroClienteWms"`.

- [ ] 3. Modelo de estado e repositório. Depende de 1.
      Criar em `Core/Separacao/`: `SituacaoNota`, `MotivoI60`, `NotaSeparacao` (record, D§6.1), `ItemSeparacao`, `IRepositorioSeparacao`,
      `RepositorioSeparacaoArquivo` (um JSON por nota em `Estado/i51/notas`, carga preguiçosa, `lock`, `Atualizar` com tabela de transições
      e troca do índice só após o `Move`, `Registrar` idempotente, `Reenfileirar` como única aresta `Rejeitada→Separada`, `Limpar` só de terminais vencidos,
      arquivo corrompido preservado como `.corrompido-<carimbo>`, nunca lança por I/O), `RepositorioOrfaosI51` (D§6.3 + **R9**: nome com carimbo do bruto, grava Tipo, SubTipo e transporte).
      Files: `src/KitWmsIntegrador.Core/Separacao/*`.
      Testes: `RepositorioSeparacaoTests` (`.tmp` não sobra, corrompido, transição inválida, falha de disco mantém índice, idempotência,
      construir não cria pasta, limpeza, `Atualizar` recusa `Rejeitada→Separada`), `RepositorioOrfaosTests`.
      Verify: `dotnet test --filter "FullyQualifiedName~Repositorio"` passa.

- [ ] 4. Registro na I50 e desligamento da I60 antiga. Depende de 3.
      `Separacao/IRegistradorSeparacao.cs` + `RegistradorSeparacao.cs` (primeira instrução testa `I51:Habilitada` e sai; copia XML para `Estado/i51/xml`;
      itens com `Sequencia = índice+1`; chave sintética `nf-<numero>-<serie>-<cnpjDest>`; TSP grava `Tipo=TSP` e `Onda`). `ProcessadorLote`: parâmetro opcional no
      fim `IRegistradorSeparacao? separacao = null`, chamado em `ProcessarOndaAsync` antes de `Concluir`, em `try/catch` que só loga, para `Integrada`, `Pendente` e "já existe" com destino Processados (D§7.2).
      `Orquestrador`: parâmetro opcional `IOptions<OpcoesI51>? = null`; I60 só junto da I50 se `I60.Habilitada && !I51.Habilitada`; com ambos, linha de histórico "I60 adiada...".
      Files: `Core/Separacao/{IRegistradorSeparacao,RegistradorSeparacao}.cs`, `Core/Pacote/ProcessadorLote.cs`, `Core/Orquestrador/Orquestrador.cs`.
      Testes: `RegistradorSeparacaoTests` (Integrada, Pendente, reprocesso já-existe, idempotente, rejeitada não registra, TSP, cópia de XML falha segue), `ProcessadorLoteComI51Tests`
      (registrador **real** com I51 desligada não cria `Estado/i51`), `OrquestradorI51Tests` (I51 desligada + I60 ligada = I60 junto da I50 como hoje; I51 ligada = sem I60 e com "I60 adiada").
      Verify: `OrquestradorTests`, `OrquestradorCapturaTests`, `ProcessadorLoteTests`, `ProcessadorLoteTspTests`, `PacoteEndToEndTests`, `PacoteTspEndToEndTests` sem edição verdes + novos.

- [ ] 5. Cliente e parser da I51. Depende de 1 e 2.
      `Separacao/Dtos/{RequisicaoI51,RespostaI51}.cs`, `ParserI51.cs` (quantidades "24.0000" InvariantCulture, datas, só `volumes.Count`, limite de 20 MB, quantidade inválida isola o documento),
      `ClienteI51.cs` (`ConsultarTodosAsync`, `ConsultarUnicoAsync(cnpj, numero)`; `CampoUnico` Numero/NroDocumento com `ValoresNeutros`; CNPJ com máscara; número 9 dígitos).
      Espiada do corpo: `NadaARetornar` quando **`documentos` ausente ou vazio, com qualquer `sucesso`** (**R6**), sem arquivo bruto; `PadroesSemRetorno` preenchida e `N` fora da lista = `FalhaConsulta`.
      Files: `Core/Separacao/{Dtos/*,ParserI51,ClienteI51}.cs`.
      Testes: `ParserI51Tests`, `ClienteI51Tests` com `FakeHttpMessageHandler` (corpo da CONSULTA das duas variantes de `CampoUnico`, TODOS/UNICO), `sucesso=N`, `sucesso=S` sem documentos (R6).
      Verify: `dotnet test --filter "FullyQualifiedName~I51"` passa.

- [ ] 6. Armazenamento bruto e casamento. Depende de 3 e 5.
      `ArmazenamentoBrutoI51.cs` (`pendentes/`, `processados/<data>/`, `falhos/`, `consulta.json`; gravar antes de processar) e `CasadorDocumentoNota.cs`:
      número com `TrimStart('0')` (vazio = inválido), **filtro de proprietário comparando `CnpjProprietario` do documento com `Operacional:CnpjDepositante`, nunca com `I51:CnpjConsulta`; documento de outro proprietário é só contado e logado (Information, sem corpo), não vira órfão, e o bruto segue processado (R3)**;
      desempate por CNPJ destinatário e depois a mais antiga + `Observacao`; sem candidato vira órfão (R9); `TentarOrfaos(nota)`; soma de documentos repetidos por item; `CalculadorAjuste` (D§9.2: `numeroSequencialItem`, fallback por `codProduto`, `Zerar`/`Omitir`, soma 0 = `SemQuantidadeSeparada`).
      Files: `Core/Separacao/{ArmazenamentoBrutoI51,CasadorDocumentoNota,CalculadorAjuste}.cs`, ajuste em `RegistradorSeparacao` para chamar `TentarOrfaos`.
      Testes: `CasadorDocumentoNotaTests` (zeros à esquerda, `"000000000"` inválido, ambiguidade, **`CnpjConsulta` ≠ `CnpjDepositante` ainda casa; outro proprietário contado e ignorado**, órfão casa depois, órfão de nota já separada é descartado), `CalculadorAjusteTests`, `ArmazenamentoBrutoI51Tests`.
      Verify: `dotnet test --filter "FullyQualifiedName~Casador|FullyQualifiedName~CalculadorAjuste|FullyQualifiedName~ArmazenamentoBruto"`.

- [ ] 7. Serviço de ciclo da I51. Depende de 3, 5 e 6.
      `ServicoSeparacaoI51.CicloAsync` (D§8): atrasos (`Atrasada` por `HorasParaAtraso`); brutos pendentes; **TODOS só se existir nota `AguardandoSeparacao`/`Atrasada` com `RegistradaEm` dentro de `DiasMaxTodos` (R7)**;
      UNICO de recuperação para `Atrasada` com **`UltimoUnicoEm`/`TentativasUnico` gravados antes da chamada (R5)**, `IntervaloUnicoMinutos`, `MaxUnicoPorCiclo`; `MaxTentativasProcessarBruto`;
      processamento de comandos (item 8) no início do ciclo; passo da I60 (item 9) em seguida; limpeza a cada 24 h; log de resumo sem segredos.
      Files: `Core/Separacao/ServicoSeparacaoI51.cs`.
      Testes (com `FakeWmsI51` — criar em `tests/.../Infra/FakeWmsI51.cs`, implementa `IClienteWms` e `IClienteWmsBruto`: TODOS consome, UNICO repete, `sucesso=N`, falhas e corpos programáveis, respostas da I60 por predicado, registro de cada envio):
      `ServicoSeparacaoI51Tests` — TODOS consumível (2ª consulta vazia, sem duplicar), bruto gravado antes de processar (falha proposital, ciclo seguinte processa **sem chamar o WMS**), UNICO repetível e recuperação de atrasada, `sucesso=N` (sem bruto, sem erro), atrasada com `FakeClock` +25 h, nota TSP, UNICO com falha de rede não é repetido a cada ciclo (R5), nota com mais de `DiasMaxTodos` não libera o TODOS (R7), I51 desligada não toca disco.
      Verify: `dotnet test --filter "FullyQualifiedName~ServicoSeparacaoI51"` passa.

- [ ] 8. Reenfileirar entre processos **[R2]**. Depende de 3 e 7.
      O `ReenfileirarI60` (CLI do Worker, `--reenfileirar-i60 --chave <c>|--motivo <MotivoI60>`) **não edita notas**: grava de forma atômica
      `Estado/i51/comandos/reenfileirar_<carimbo>.json` (`{chave|motivo}`; valida `MotivoI60` exceto `Nenhum`; saída 0 gravado, 2 parâmetro inválido).
      O `ServicoSeparacaoI51` consome e apaga os comandos no início de cada ciclo e chama `repositorio.Reenfileirar` no próprio processo (única escritora do índice), zerando
      `TentativasI60`, `TentativasOutro`, `FalhasComunicacao`, `ProximaTentativaI60Em`, `FinalizadaEm`, `Motivo`, `Mensagem` e anotando `Observacao`; o resultado (quantas notas) vai ao log do ciclo.
      Comando corrompido vai a `comandos/falhos`. Registrar no `Program.cs` (modo manual que sai antes do host, como `EnvioManualI60`).
      Files: `Core/Separacao/ComandosI51.cs` (gravar/consumir), `Worker/ReenfileirarI60.cs`, `Worker/Program.cs`, `Core/Separacao/ServicoSeparacaoI51.cs`.
      Testes: `ReenfileirarI60Tests` — **dois repositórios sobre a mesma pasta**: a CLI (processo simulado, repositório A ou só o arquivo) grava o comando e o ciclo do serviço (repositório B, já carregado) aplica, a nota volta a `Separada` e é reenviada; por `--motivo Outro`; parâmetro inválido = 2; comando corrompido; o serviço nunca é sobrescrito por leitura defasada.
      Verify: `dotnet test --filter "FullyQualifiedName~Reenfileirar"` passa.

- [ ] 9. I60 depois da separação (ajuste de quantidade, INE, ENC→IMP). Depende de 1, 3, 4 e 6.
      `Construtores/ParametrosI60.cs` e overload `Construir(NotaFiscal, ParametrosI60)` em `IConstrutorI60`/`ConstrutorI60` (os métodos atuais ficam intactos).
      **`ParametrosI60.QtdeVolume` é `decimal?` (R4)**: null = `Round(soma ajustada)`; não nulo = usa; `I60:QtdeVolumeZero=true` tem precedência; teste por combinação (SomaCaixas, VolumesI51 com contagem >0, VolumesI51 com 0 cai em SomaCaixas, QtdeVolumeZero).
      `Separacao/LeituraI60.cs`, `ClassificadorMotivoI60.cs`, `ProcessadorI60Separacao.cs` implementando o algoritmo de D§9.3 exatamente: XML carregado antes do write-ahead (fallback `LocalizadorXmlDevolucao` só com chave de 44 dígitos e `try/catch ArgumentException`);
      INE com `tpacao=I`; ENC tem prioridade → `FluxoImp` (`IMP` em `tipoOperacaoNota` e `Tpsit`, `TpAcaoReenvioImp`=UPD padrão, reenvio UPD se pedir); `PadroesExigeUpd` antes de qualquer sucesso/pendência; `Associada` só com INE e sucesso (dois `Atualizar`);
      `Outro` até `MaxTentativasOutro`; comunicação nunca terminal, backoff exponencial até `BackoffMaxMinutos`, `FalhasComunicacao` separado de `TentativasI60`; no máximo 4 chamadas por nota por ciclo; `I60.Habilitada=false` pula o passo e a nota fica `Separada`.
      Cortáveis aqui (marcar com comentário `// CORTAVEL`): `Max4xxConsecutivos` e disjuntor. Sem eles, 4xx é falha de comunicação comum.
      Files: `Core/Construtores/{ParametrosI60,IConstrutorI60,ConstrutorI60}.cs`, `Core/Separacao/{LeituraI60,ClassificadorMotivoI60,ProcessadorI60Separacao}.cs`.
      Testes: `ConstrutorI60ParametrosTests`, `ProcessadorI60SeparacaoTests` — ajuste de quantidade (três campos = `qtdeProdutoSep`, total e volume recalculados, original × separada no estado, `Omitir`/`Zerar`, soma 0 não envia), operação `INE` nunca `IMB`,
      **ENC→IMP** (recusa e ressalva; `Tpsit`/`tipoOperacaoNota`=`IMP`; erro "doccarga encerrado" no estado; nunca `Associada`), **TpAcao no reenvio** (padrão UPD; `I` + "precisa ser UPD" gera 1 reenvio UPD; IMP que ainda pede UPD = `Rejeitada/JaExiste`; resposta real "pendente + UPD" para INE e IMP; no máximo 4 chamadas),
      pendente sem/com ressalva, sucesso real e `PadroesAssociadaSucesso`, `QuantidadeDiferente`, `VolumeDiferente`, falha de comunicação (5 falhas não rejeitam; backoff 5/10/20/40/60 com `FakeClock`; 401/5xx nunca terminais), `Outro` + reenfileirar, `XmlIndisponivel` sem consumir tentativa, I60 desligada, **reinício no meio do fluxo** (nova instância de repositório/serviço sobre a mesma pasta após: aguardando, bruto pendente, separada, I60 gravada).
      Verify: `ConstrutorI60Tests` existente sem edição verde; `dotnet test --filter "FullyQualifiedName~I60"` passa.

- [ ] 10. Combinações das chaves e regressão do comportamento antigo. Depende de 4 e 9.
      Teste de integração `FluxoI51I60Tests` com `AmbientePacote`: I51=false/I60=true = I60 junto da I50 exatamente como hoje (payload idêntico ao de `OrquestradorTests`); I51=true/I60=false = nota chega a `Separada` e nenhuma I60 sai; I51=true/I60=true = fluxo completo (I50 → registro → TODOS → I60 INE → `Associada`); onda TSP (ZIP `PuxadaTSP`) registra `Tipo=TSP`+`Onda` e percorre até a I60; segredos: logger coletor sem token/senha/trecho de XML/bruto.
      Files: `tests/KitWmsIntegrador.Tests/FluxoI51I60Tests.cs`, `SegredosI51Tests.cs`.
      Verify: `dotnet test --filter "FullyQualifiedName~FluxoI51I60|FullyQualifiedName~SegredosI51"` passa.

- [ ] 11. Relatório diário: modelo, HTML, PDF e e-mail. Depende de 3 e 9.
      `Relatorio/{ModeloRelatorioI60,MontadorRelatorioI60,IGeradorRelatorioI60,GeradorRelatorioI60,RenderizadorPdfRelatorioI60}.cs` e `Email/CorpoEmailRelatorioI60.cs` (D§11.1/11.2: seções cabeçalho+saúde, cartões, aguardando com idade, separadas, associadas, problemas com `MotivoI60`, `I60Enviada` aguardando confirmação, ajustes item a item, atrasadas, **visão por pacote (decisão D§11.4: seção no diário)**, candidatas à I40 com aviso se `Devolucao:Habilitada=false`, teto `MaxLinhasPorSecao`),
      identidade visual `IdentidadeVisual` ("Relatório de Integração"), replicando a paginação do `RenderizadorPdfResumo` sem alterar o resumo semanal. HTML em disco com logo data URI; e-mail com CID e tudo HTML-encoded.
      Helper de teste `ExemplosRelatorioI60` (dados fictícios, padrão `ExemplosResumoSemanal`) para gerar HTML/PDF de exemplo.
      Files: `Core/Relatorio/*I60*.cs`, `Core/Email/CorpoEmailRelatorioI60.cs`, `tests/.../ExemplosRelatorioI60.cs`.
      Testes: `MontadorRelatorioI60Tests` (contagens, idades, ajustes, por pacote, candidatas I40, TSP/onda), `RelatorioI60RenderTests` (HTML sem `<script>`/CSS externo, texto encoded, PDF legível com `PdfTextoHelper`, teto de linhas).
      Verify: `dotnet test --filter "FullyQualifiedName~RelatorioI60"` passa; `ResumoSemanalRenderTests` e `GeradorRelatorioTests` existentes verdes.

- [ ] 12. Agendamento e envio diário às 12:00. Depende de 11.
      `Monitoramento/{AgendadorRelatorioDiario,MarcaUltimoRelatorioI60,FonteDestinatariosRelatorioI60,EnviadorRelatorioDiarioI60,ServicoRelatorioDiarioI60}.cs` (D§11.3; grava HTML+PDF em `<Relatorio:PastaSaida>/i60`, e-mail se SMTP configurado, marca o slot só após gravar, falha de e-mail só loga, vazio sem `EnviarQuandoVazio` marca sem gerar).
      **No `Worker.cs`, chamar `ServicoRelatorioDiarioI60.VerificarEEnviarAsync` em `try/catch` próprio (loga, propaga cancelamento), logo após o resumo semanal e fora do `try` do ciclo do pacote (R8).**
      Files: `Core/Monitoramento/*I60*.cs`, `Core/Monitoramento/AgendadorRelatorioDiario.cs`, `Worker/Worker.cs`.
      Testes: `AgendadorRelatorioDiarioTests` (antes/depois das 12:00, idempotência, slot perdido no mesmo dia, parada de vários dias = um envio, dia seguinte reabre), `ServicoRelatorioDiarioI60Tests` (falha gera sem marcar; e-mail falha mantém arquivo; exceção no ciclo do pacote não impede o relatório — verificar via estrutura do `Worker` isolando o método de passo, extraindo-o se necessário).
      Verify: `dotnet test --filter "FullyQualifiedName~RelatorioDiario|FullyQualifiedName~AgendadorRelatorioDiario"`; `AgendadorResumoSemanalTests` existente verde.

- [ ] 13. Worker, DI e CLIs. Depende de 2, 7, 8, 9 e 12.
      `Worker/ConsultaI51Worker.cs` (`BackgroundService`; desligado = loga e termina sem timer; valida `OpcoesI51.Validar()`/`OpcoesI60.Validar()` antes do 1º ciclo e encerra se inválido; `AtrasoInicialSegundos` mín. 5; erro de ciclo só loga).
      `Worker.cs`: validar I51/I60/RelatorioI60 junto de Pastas/Zip **só com I51 ligada** (`StopApplication()`). `Program.cs`: `Bind` de `OpcoesI51` e `OpcoesRelatorioI60`, singletons de todos os serviços novos, `AddHostedService<ConsultaI51Worker>()`, modos manuais `--consultar-i51` (`ConsultaManualI51`, só UNICO, TODOS recusado com saída 2; cortável) e `--relatorio-i60 [--enviar]` (`RelatorioManualI60`, não cortável).
      `appsettings.json`: seções `I51` e `RelatorioI60` e chaves novas de `I60` com `_comentario` no estilo atual; `I51:Habilitada=false`, `I60:Habilitada=false`, `Devolucao:Habilitada=false`. `.gitignore`: `destinatarios-i60.txt`, `ultimo-relatorio-i60.txt`. Conferir `deploy/config-exemplo` (lacuna declarada no design) e atualizar se existir appsettings de exemplo.
      Files: `Worker/{ConsultaI51Worker,ConsultaManualI51,RelatorioManualI60,Worker,Program}.cs`, `Worker/appsettings.json`, `.gitignore`, `deploy/config-exemplo/*` (se existir).
      Testes: `ConsultaI51WorkerTests` (opções inválidas não chamam o WMS; desligado termina sem timer; erro de ciclo não derruba), `AppsettingsPadroesTests` (arquivo real: as três chaves `Habilitada` = false; todas as seções novas bindam sem erro), `RelatorioManualI60Tests`.
      Verify: `dotnet build ... -c Release` 0 avisos; `dotnet test --filter "FullyQualifiedName~ConsultaI51|FullyQualifiedName~Appsettings|FullyQualifiedName~RelatorioManual"`.

- [ ] 14. Roteiro de teste no homolog e documentação técnica. Depende de 13.
      Criar `docs/ROTEIRO-TESTE-I51-I60.md` (pt-BR): pré-requisitos (EDI WEBSERVI, proprietária BRF pendente no fornecedor), UNICO manual, ciclo completo, ENC, quantidade diferente, TSP, reinício, atrasada,
      UNICO nas duas variantes `Numero`/`NroDocumento` (Q13), mensagem real do `sucesso=N` (Q14), `--reenfileirar-i60` (explicar que o Worker em execução aplica no ciclo seguinte), relatório forçado `--relatorio-i60`,
      **aviso de que a 1ª ativação do TODOS consome o backlog da proprietária** e **contar órfãos após o 1º TODOS: se >0 e nenhuma nota casou, a premissa A1 (Numero = nNF) está errada (R9)**, registro das respostas reais da I60 (Q1, Q2, Q11, Q12), regressão da I40 via `ROTEIRO-TESTE-I40.md` seção 9.
      Atualizar `docs/ESCOPO-I51-I60.md` (seção "implementado", status e decisões efetivas), `docs/ARQUITETURA.md` (seção 2.3 e diagrama) e `docs/OPERACAO.md` (chaves, pastas `Estado/i51`, comandos, relatório diário). Esforço só em horas; **não** tocar nos arquivos de controle de esforço.
      Files: `docs/ROTEIRO-TESTE-I51-I60.md`, `docs/ESCOPO-I51-I60.md`, `docs/ARQUITETURA.md`, `docs/OPERACAO.md`.
      Verify: build ainda com 0 avisos; conferir que cada chave citada no roteiro existe em `appsettings.json` (revisão manual) e que os links relativos existem.

- [ ] 15. Regressão da I40 e verificação final. Depende de tudo.
      Criar `RegressaoI40ComI51Tests` (D§16.5): (a) com `I51:Habilitada=true` o XML continua em `Processados/<pacote>/` e a busca por chave e por número+série o acha; (b) I40 pelo `ServicoDevolucao` com `ClienteWmsHttp` real sobre `FakeHttpMessageHandler` usa `IDENTIFICADOR=WEBAPI-I40-Json`, `IMP`, `InverterPartes=false`, sem placa e sem motorista; (c) a tela sobe e responde com I51 ligada e `Devolucao:Habilitada=true`; (d) com `Devolucao:Habilitada=false` o `ServidorTelaDevolucao` não abre porta.
      Rodar a suíte inteira da I40 sem edição (`ConstrutorI40Tests`, `ServicoDevolucaoTests`, `BuscaNumeroSerieDevolucaoTests`, `LocalizadorXmlDevolucaoTests`, `ValidadorChavesDevolucaoTests`, `OpcoesDevolucaoTests`, `TelaDevolucaoEndpointTests`).
      Gerar o relatório diário de exemplo (dados fictícios) via `ExemplosRelatorioI60` em pasta temporária e conferir com screenshot do Edge headless (`msedge --headless=new --screenshot`), apagando os temporários depois.
      Revisar cada item de R1..R9 e anotar no commit/relatório onde foi tratado.
      Files: `tests/.../RegressaoI40ComI51Tests.cs`.
      Verify: `dotnet build c:/wms-integrador/KitWmsIntegrador.sln -c Release` com **0 avisos e 0 erros**; `dotnet test c:/wms-integrador/KitWmsIntegrador.sln -c Release` verde, **mais de 655 aprovados e 9 ignorados** (os ignorados não aumentam sem justificativa); `git status` sem `*.xml`/`*.zip`/segredos.

## Estimativa (somente horas; humanas a informar pelo usuário)

Horas de IA estimadas: itens 1-3 aprox. 3,0; 4-10 aprox. 5,5; 11-13 aprox. 3,5; 14-15 aprox. 1,5. Total aprox. 13,5 h de IA para a implementação (deck e manuais, aprox. 2,0 h, ficam na etapa final). Horas humanas: não estimadas aqui.

## Contrato do loop

O revisor (último passo do build-loop) confere o diff contra este plano, R1..R9 e os requisitos do brief, e escreve
`c:/wms-integrador/.agents/tasks/review-i51-i60.json` com `{"verdict":"APPROVED"|"CHANGES_REQUESTED","findings":[...],"reviewDoc":"..."}`.
Depois do loop seguem as etapas de documentação (deck e manuais, gate `docs-review-i51-i60.json`) e o relatório final `relatorio-i51-i60.md`.
