# Revisão do design (3ª rodada): I51 como gatilho da I60

Documento revisado: `.agents/tasks/design-i51-i60.md` (lido inteiro), contra `brief-i51-i60.md`,
`docs/ESCOPO-I51-I60.md` (ambos lidos inteiros), as mensagens originais do usuário e o código real.

Veredito: **CHANGES_REQUESTED** (0 HIGH, 3 MEDIUM, 6 NIT).

Os 5 MEDIUM e os 3 NIT da 2ª rodada foram incorporados de forma coerente (CampoUnico, órfãos e desempate,
MaxTentativasOutro, validações, fallback de XML, validação na partida do consumidor, PadroesSemRetorno, itens
cortáveis). Os achados abaixo são novos.

## Achados

### 1. MEDIUM: o registro no DI de D4 cria DUAS instâncias de `ClienteWmsHttp`, o oposto do que D4 quer
Onde: D4 (seção 3) e `Program.cs` (seção 13).
O design manda registrar `AddHttpClient<ClienteWmsHttp>(...)` e expor `IClienteWms` e `IClienteWmsBruto` por
`AddSingleton(sp => sp.GetRequiredService<ClienteWmsHttp>())`. Um typed client de `AddHttpClient<T>` é registrado
como **transient**. Duas lambdas `AddSingleton`, cada uma chamando `GetRequiredService<ClienteWmsHttp>()`, resolvem
duas instâncias transientes diferentes (uma para `IClienteWms`, outra para `IClienteWmsBruto`), cada uma com seu
`_token` e seu `_tokenLock`. Isso reproduz exatamente o problema que D4 quer evitar (sessões concorrentes no
`GetToken`, que o código atual já atribui a 401 transitório no WMS). Verifiquei em `Program.cs`: hoje existe um único
`AddHttpClient<IClienteWms, ClienteWmsHttp>` com `ConfigurePrimaryHttpMessageHandler` (TLS 1.2/1.3), e o
`Orquestrador` e o `ServicoDevolucao` são singletons que capturam instâncias separadas dele.
Correção: usar um client nomeado e UMA única instância singleton compartilhada pelas duas interfaces:
```csharp
builder.Services.AddHttpClient("wms", configure).ConfigurePrimaryHttpMessageHandler(/* igual ao atual */);
builder.Services.AddSingleton<ClienteWmsHttp>(sp => ActivatorUtilities.CreateInstance<ClienteWmsHttp>(
    sp, sp.GetRequiredService<IHttpClientFactory>().CreateClient("wms")));
builder.Services.AddSingleton<IClienteWms>(sp => sp.GetRequiredService<ClienteWmsHttp>());
builder.Services.AddSingleton<IClienteWmsBruto>(sp => sp.GetRequiredService<ClienteWmsHttp>());
```
Adicionar teste de DI (construir o provider do `Program` ou um espelho dele): `ReferenceEquals(GetService<IClienteWms>(),
GetService<IClienteWmsBruto>())` e igual ao que o `Orquestrador` e o `ServicoDevolucao` recebem. Registrar como
risco que o singleton segura o handler (o `SocketsHttpHandler` não rotaciona); definir `PooledConnectionLifetime`
no handler para não prender DNS antigo.

### 2. MEDIUM: `--reenfileirar-i60` roda em outro processo e o índice em memória do serviço não enxerga a mudança
Onde: seções 6.2 (Reenfileirar), 6.4 (índice em memória, "memória = disco") e 13 (CLIs).
As CLIs do projeto são modos manuais que saem antes do host subir (verificado em `Program.cs`: `SmokeTestWms`,
`EnvioManualI60`, `EnvioManualI40` retornam antes de montar o host). O `ReenfileirarI60` seguiria o mesmo padrão:
processo separado que grava em `Estado\i51\notas\<chave>.json`. O serviço em execução carregou o índice na partida e só
relê o disco no restart. Resultado: a nota continua `Rejeitada` para o serviço, o ciclo nunca a reenvia, e a próxima
gravação do serviço sobre aquele arquivo (ex.: limpeza, `Atualizar`) pode sobrescrever o reenfileiramento. A
invariante "memória = disco" (seção 17) fica falsa sempre que a CLI roda com o serviço ligado, que é o caso de uso
descrito (ajustar `I60__Padroes*` e reenfileirar por `--motivo Outro`). O mesmo vale para ler o estado em
`RelatorioManualI60` (leitura é inofensiva, só pode estar defasada).
Correção, escolher uma e escrever no design:
- (recomendado) a CLI NÃO edita notas: grava um arquivo de comando `Estado\i51\comandos\reenfileirar_<carimbo>.json`
  (`{chave|motivo}`, escrita atômica). O `ServicoSeparacaoI51` consome e apaga os comandos no início de cada ciclo,
  chamando `repositorio.Reenfileirar(...)` no mesmo processo (única escritora do índice). Códigos de saída da CLI:
  0 comando gravado, 2 parâmetro inválido; o resultado (quantas notas) aparece no log do ciclo e no relatório.
- (alternativa) a CLI exige o serviço parado: detecta um arquivo de trava `Estado\i51\servico.lock` mantido aberto
  pelo serviço, recusa com saída 3 se estiver em uso, e documenta no roteiro.
Teste: dois repositórios sobre a mesma pasta; o comando gravado pelo primeiro é aplicado pelo ciclo do segundo e a
nota é reenviada.

### 3. MEDIUM: o filtro por proprietário usa o CNPJ de consulta, que a Q3 declara configurável, e o destino do documento filtrado não é especificado
Onde: seção 8.1 ("filtra por `CnpjProprietario` (dígitos iguais ao CNPJ de consulta; ausente = aceita)"), 4.1
(`CnpjConsulta`) e Q3 (seção 20).
Q3 admite que o CNPJ da CONSULTA pode ser o armazém ou o destinatário, não o proprietário (`I51__CnpjConsulta`). Se for
trocado, o filtro de 8.1 compara `CnpjProprietario` do documento com esse mesmo valor e descarta TODOS os documentos
(que trazem o CNPJ do proprietário BRF). O TODOS já consumiu os documentos, então eles se perdem, e nenhuma nota
casa até o UNICO (que usa o mesmo CNPJ). Além disso o design não diz o que acontece com um documento reprovado no
filtro (ignorado? órfão? contado?). Os órfãos de 6.3 só cobrem "sem candidato", não "outro proprietário".
Correção: o filtro de proprietário deve comparar SEMPRE com `Operacional:CnpjDepositante` (dígitos), nunca com
`I51:CnpjConsulta`. Documento com `CnpjProprietario` preenchido e diferente: não é gravado como órfão (não é nosso),
é contado em "documentos de outro proprietário", logado em `Information` (número, sem corpo) e o bruto continua
sendo gravado e processado normalmente. Adicionar teste: `CnpjConsulta` ≠ `CnpjDepositante` ainda casa documento
com o proprietário correto; documento de outro proprietário é contado e ignorado.

### 4. NIT: dono do cálculo de `QtdeVolume` ambíguo e tipo incompatível
Onde: seção 9.1.
O texto diz que `QtdeVolume` depende de `I60:QtdeVolumeOrigem` (calculado pelo construtor, "soma ajustada
arredondada") e ao mesmo tempo que `ParametrosI60.QtdeVolume` (`int?`) "carrega o valor escolhido". Não fica claro se
o construtor ou o `ProcessadorI60Separacao` calcula, nem se `I60:QtdeVolumeZero` vale com `VolumesI51`.
`PayloadI60.QtdeVolume` é `decimal` e `CalcularQtdeVolume` devolve `decimal`.
Correção: `ParametrosI60.QtdeVolume` é `decimal?`; null = o construtor calcula `Round(soma das quantidades ajustadas)`
(SomaCaixas); não nulo = usa o valor (o processador passa `QtdeVolumesI51` quando `VolumesI51` e a contagem > 0).
`QtdeVolumeZero=true` tem precedência sobre os dois (diagnóstico). Um teste por combinação.

### 5. NIT: quando `UltimoUnicoEm` é gravado não está definido
Onde: seção 8, passo 5 e 6.1.
O UNICO de recuperação seleciona notas com `UltimoUnicoEm` mais antigo que `IntervaloUnicoMinutos`. Se o campo só é
gravado após resposta com sucesso, uma falha de rede (WMS fora) deixa a nota elegível em todo ciclo e o ciclo faz até
`MaxUnicoPorCiclo` chamadas falhas a cada 5 min.
Correção: gravar `UltimoUnicoEm` e `TentativasUnico++` antes da chamada (write-ahead, como o `TentativasI60`).

### 6. NIT: resposta `sucesso=S` sem documentos grava um arquivo bruto a cada ciclo
Onde: seção 5.4 item 2.
Só `sucesso=N` sem documentos é "nada a retornar". Um `S` sem documentos (ou com `documentos` vazio) cai em "qualquer
outro corpo" e gera um arquivo em `brutos\pendentes` e depois em `processados` a cada 5 minutos.
Correção: tratar `documentos` ausente ou vazio, com qualquer `sucesso`, como `NadaARetornar` (sem arquivo); `N` com
documentos continua sendo processado.

### 7. NIT: o TODOS fica sempre liberado por causa de notas atrasadas permanentes
Onde: seção 8 passo 4 e seção 22 ("atrasadas nunca expiram").
O gatilho do TODOS é "existe nota `AguardandoSeparacao`/`Atrasada`". Uma única nota que nunca separa (doccarga
cancelado no WMS) mantém o TODOS ligado para sempre e anula a proteção contra consumir documentos que não temos como
casar.
Correção: gatilho do TODOS = existe nota `AguardandoSeparacao`/`Atrasada` com `RegistradaEm` mais recente que
`HorasParaAtraso × 7` (ou uma chave `I51:DiasMaxTodos`, padrão 14), ou então aceitar e registrar a limitação em 22. Se
não corrigir, adicionar uma linha explícita no roteiro e no relatório ("TODOS ativo por nota atrasada").

### 8. NIT: chamada do relatório no loop do `Worker` fica no mesmo `try` do ciclo do pacote
Onde: seção 11.3 ("mesmo isolamento por try/catch").
No `Worker.cs` real, `_gerenciador.CicloAsync` e `_servicoResumo.VerificarEEnviarAsync` estão no mesmo `try`. Uma
exceção do ciclo do pacote impede a chamada do resumo, e o relatório novo herdaria isso. A frase "mesmo isolamento"
descreve um isolamento que não existe.
Correção: chamar `ServicoRelatorioDiarioI60.VerificarEEnviarAsync` dentro de um `try/catch` próprio (que só loga e
propaga cancelamento), imediatamente depois do resumo, e fora do `try` do ciclo do pacote ou com o seu.

### 9. NIT: premissa A1 (documento `Numero` = nNF) sem alternativa configurável e nome de órfão pode colidir
Onde: seções 6.3 e 21 (A1), Q7.
O brief diz "Numero (pedido/doccarga)" e o escopo cita "número do doccarga/transporte". O código confirma que a I50
envia `NumeroPedido = nota.Numero` (nNF), então A1 é coerente, mas é a chave de todo o casamento e só será
confirmada no homolog. Se for outro número, tudo vira órfão e atrasada, e o conserto exige release. Também, o órfão se
chama `<numero>_<cnpjDest>.json`: dois documentos com o mesmo número e destinatário (Tipo/SubTipo diferentes, ou o
mesmo documento em duas respostas) se sobrescrevem em vez de somar.
Correção: manter A1, mas (a) gravar `Tipo`, `SubTipo` e `NumeroTransporte` do documento (se existir) no órfão, para
permitir remapear sem perder dados, (b) acrescentar ao nome o carimbo do bruto de origem, e (c) listar no roteiro "contar
órfãos após o 1º TODOS: se > 0 e nenhuma nota casou, A1 está errada".

## Verified Assumptions (conferidas no código)

- A I60 só é disparada em `Orquestrador.IntegrarAsync` no ramo de sucesso da I50, protegida por `_opcoesI60.Habilitada`.
  O construtor real é posicional e termina em `ILogger<Orquestrador> logger`; um parâmetro opcional no final compila
  para os construtores de teste existentes.
- `ProcessadorLote.ProcessarOndaAsync` tem `resultado`, `arquivo`, `ctx` (com `Onda`) e `transporte` antes de
  `Concluir`; `Concluir`/`Finalizar` é que move o arquivo. `ClassificadorCategoriaErro.DestinoDe` devolve Processados para
  `Integrada`, `Pendente` e "já existe". O ZIP é extraído em pasta temporária limpa só no `finally` de `ProcessarAsync`.
- `ClassificadorRetorno` tem `EhSucesso` (Sucesso=="S" ou "Registrado com sucesso"), `EhPendencia` e `EhJaExiste`, todos
  por `Contains` ordinal ignorando caixa, sem normalização de acento (o `TextoNormalizado` novo é de fato necessário).
- `ConstrutorI60` usa a mesma quantidade em `QtdeProduto`, `QtdeProdutoConfirmada` e `QtdeEmbalagem`, numera os itens
  `i + 1` como a I50, usa `Pedidos = [nota.Numero]` e `CalcularQtdeVolume` é `internal static`. `NotaVendaI60.Tpacao` e
  `Tpsit` são propriedades `init` com padrão `"I"` e `"INE"`; `PayloadI60.QtdeVolume` é `decimal`.
- Os 4 fakes de `IClienteWms` existem (`FakeClienteWms`, `ClienteComLatencia`, `ClienteQueFalha`, `FakeClienteWmsCarga`) e
  só `ConstrutorI60` implementa `IConstrutorI60`; a interface `IClienteWmsBruto` e o overload novo não quebram testes.
  Nenhum teste faz `new Worker(`.
- `docs/chamado/I60-associacao-manual-IMP.json` e `...-IMB.json` diferem apenas em `tipoOperacaoNota` e `Tpsit`;
  `respostas-homolog.txt` traz `sucesso="N"` com "Foi importado e esta pendente na interface. Ressalvas : {Nota [...] já
  existe, TpAcao precisa ser UPD!}" para IMB e IMP.
- `LocalizadorXmlDevolucao.Localizar` lança `ArgumentException` em chave inválida (`ValidarChave`) e o construtor exige o
  caminho de Processados; correto o tratamento descrito em 9.3.
- `OpcoesPasta.Estado` resolve contra a Entrada, mas `VarredorPasta` enumera só `TopDirectoryOnly`, então XMLs copiados
  para `Estado\i51\xml\` não são reingeridos; `OpcoesPasta.Validar` protege o Estado da limpeza.
- `ClienteWmsHttp` usa `EnsureSuccessStatusCode` (a `HttpRequestException.StatusCode` existe), trata 401 com uma
  renovação e 5xx com retry; `OpcoesMonitoramento.HoraDoDia` cai para 12:00; `FormatoWms.ZerosEsquerda`/`Documento` e
  `IdentidadeVisual` (internal, mesmo assembly) existem.
- O design não contém valores monetários; o esforço está em horas e as horas humanas ficam "a informar pelo usuário".
- Cobertura dos pedidos do usuário: TODOS/UNICO só com CNPJ e número, gravação do bruto antes de processar, `INE` (nunca
  `IMB`), ENC → `IMP` com registro do erro, ajuste para a quantidade separada, TSP no fluxo, intervalo configurável,
  relatório 12:00 com avaliação por pacote, I40 pronta e protegida por regressão, chaves desligadas por padrão e
  comportamento antigo preservado com `I51:Habilitada=false`.

## Unverified / Wrong Assumptions

- Errada (D4, achado 1): "expor `IClienteWms` e `IClienteWmsBruto` por `AddSingleton(sp => sp.GetRequiredService<ClienteWmsHttp>())`
  garante instância única". Com typed client transient isso cria uma instância por registro.
- Errada (seções 6.4/13/17, achado 2): "memória = disco" continua verdadeira com uma CLI que edita arquivos em outro processo.
- Errada em caso de configuração (8.1, achado 3): comparar `CnpjProprietario` do documento com `I51:CnpjConsulta`.
- Não verificável antes do homolog (adequadamente tratadas como configuráveis): texto real de ENC (Q1), exigência de UPD
  no IMP (Q2), CNPJ da consulta (Q3), campo do UNICO (Q13), sinal de I60 associada (Q11), mensagem do `sucesso=N` (Q14),
  `Numero` do documento = nNF (A1/Q7), unidade de `qtdeProdutoSep` igual à de `qCom` (premissa implícita ao remover a Q5).
- Não verificado por mim: `deploy\config-exemplo` e manuais (o design já declara isso); o formato real do corpo
  `documentos`/`volumes` do WMS (o parser foi desenhado a partir do resumo do brief, sem amostra real).
