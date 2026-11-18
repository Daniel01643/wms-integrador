# Investigação: o integrador consome memória e trava o SRVAPPALC02?

Data da análise: 18/11/2026. Modo somente leitura. Nada foi alterado e o servidor de produção não foi acessado (inacessível).
Base: código do repositório na versão 2026.11.17-1 (HEAD `d8da08b`).

## 1. Resumo executivo

1. **Pelo código, não há vazamento de memória que explique um travamento do servidor.** Não existe `FileSystemWatcher` (a entrada é lida por varredura a cada 5 s). Não existem caches estáticos nem timers. Os únicos dicionários de processo são limitados (`GuardaAutenticacaoWms`: 20 backends e janela de 300 chamadas) ou limpam as próprias entradas (`DetectorArquivoPronto._estados`, `ServicoSeparacaoI51._tentativasBruto`). O `HttpClient` do WMS é um singleton com `SocketsHttpHandler` e `PooledConnectionLifetime` de 5 min. O handler "conexão nova" é descartado a cada uso. A memória esperada do processo é de dezenas a poucas centenas de MB (índice das notas I51 em memória, strings transitórias de ~1,4 MB por consulta, renderização de PDF por pacote).
2. **O que o código permite concluir com segurança é que o integrador pode pressionar o disco e o log, não que ele esgote RAM.** Os pontos concretos estão na seção 3:
   - Crescimento de disco sem teto: respostas brutas da I51, logs rotacionados do NSSM que ninguém apaga, `Processados` e `relatorios`.
   - Falhas que se repetem a cada 5 s sem backoff: fechamento de pacote que falha, move pendente, relatório diário.
   - Trabalho O(documentos × notas) por ciclo da I51, com churn de memória.
   - **O `worker.out.log` não tem carimbo de hora** (nenhum `TimestampFormat` configurado). Hoje não dá para correlacionar o log com 09:48 ou com o travamento.
3. **A autenticação das 09:48–09:56 não foi amplificada pelo integrador.** Em 8 min o código faz no máximo ~9 chamadas de GetToken (3 episódios × 3 tentativas). Os 8 min batem com a escada de cooldown 60 s + 120 s + 300 s, que alinha a recuperação ao próximo gatilho (ciclo da I51 ou pacote). A indisponibilidade real pode ter sido menor. O motivo informado no e-mail das 09:48 diz de que lado veio a falha: "HTTP 401" aponta para o WMS; "sem resposta (timeout/rede)" aponta para rede ou servidor já degradado.
4. **Os e-mails de "uma nota" em horários diferentes, sem arquivos novos, têm uma explicação provável no código: o alerta imediato da I60.** O e-mail é "[KitWms] I60 precisa de atenção: N nota(s)". Ele sai uma vez por nota quando a nota fica `Atrasada` 24 h depois de registrada (`HorasParaAtraso=24`), quando é rejeitada, ENC/IMP, pendente ou "não reconhecida". As notas foram registradas em momentos diferentes no dia 17. Elas vencem 24 h depois, em horários diferentes, e cada e-mail leva uma nota. O assunto do e-mail das 09:13 confirma ou descarta a hipótese. A causa alternativa (fechamento de pacote de 1 nota por sobras na entrada ou diário retomado) está na seção 3.2.
5. **O travamento é do sistema todo** (ping responde, WinRM e a tela 8085 não; falha do host WSMan). O padrão é típico de esgotamento de memória/commit, pool não paginado, handles ou disco/IO. Só se prova no servidor, com o Evento 2004 (Resource-Exhaustion-Detector) e os contadores do processo (seção 5). **O teste mais barato e conclusivo: parar o serviço `Friozem-ImportacaoNotas` por algumas horas e ver se o servidor volta a travar.** A decisão é sua; os pacotes ficam na entrada.

Esforço desta investigação: ~1,5 h de IA. Horas humanas: informadas por você (não estimadas aqui).

---

## 2. Evidências por pergunta

### 2.1 Vazamentos e crescimento ilimitado (pergunta 1)

| Item | Onde | Conclusão |
|---|---|---|
| HttpClient por envio/retry | `RegistroClienteWms.AdicionarClienteWms` registra um `ClienteWmsHttp` singleton sobre um `HttpClient` nomeado. `CriarFabricaConexaoNova` só é usada em `ClienteWmsHttp.TentarGetTokenAsync` com `conexaoNova` (tentativas 2 e 3 do GetToken), que faz `temporario?.Dispose()` no `finally`, com `disposeHandler: true`. | **Sem vazamento.** Handler novo só em falha de GetToken (no máximo 2 por episódio). Cada um é descartado. Em falha em massa vira handshake TLS extra e TIME_WAIT do lado cliente (dezenas, não milhares). |
| CookieContainer | `UseCookies=false` no handler; o cookie é lido de `Set-Cookie` à mão (`IdentificadorBackendWms.De`) e reenviado só no POST do mesmo envio. | Sem container acumulando. |
| Pool de conexões | `SocketsHttpHandler` com `PooledConnectionLifetime=5 min`. Com token por envio, GetToken e POST reaproveitam a mesma conexão keep-alive. | Pool pequeno e estável. |
| `IHttpClientFactory` | O singleton captura o `HttpClient` uma vez. A fábrica enfileira o handler como "expirado" após 2 min mas não o libera, porque o cliente continua referenciado. | Custo desprezível (timer de limpeza da fábrica a cada 10 s). Não cresce. |
| Contadores de backend | `GuardaAutenticacaoWms`: `_porBackend` limitado a 20 chaves ("(outros)"); `_chamadasGetToken` guarda no máximo `MaxGetTokenPorMinuto` (300) instantes. | Limitado. |
| Cache de token | Com `TokenPorEnvio=true` não há token em cache (`SessaoToken` local ao envio). | Nada acumula. |
| Índice da I51 | `RepositorioSeparacaoArquivo._indice`: carregado uma vez (lazy). Fica todo o estado de notas (600+ hoje; terminais saem após 30 dias em `Limpar`). | Cresce com o volume de 30 dias. Estimativa de dezenas de MB em milhares de notas. Não é vazamento. |
| Órfãos e brutos | `RepositorioOrfaosI51`: um JSON por documento, relido do disco todo ciclo. `ArmazenamentoBrutoI51`: arquivo por consulta. Retenção 30 dias. | Crescimento em **disco**, não em RAM (ver 2.2 e 3.1). |
| Dedup de alerta | Carimbo em cada nota (`AlertaI60Em`/`AlertaAtrasoEm`) no JSON. | Sem estrutura em memória. |
| Handlers/eventos re-inscritos | Busca por `+=`, `Timer`, `Task.Run`, `FileSystemWatcher`, `PeriodicTimer` em `src` não encontrou ocorrência. | Não se aplica. |
| Leitura inteira em memória | `ArmazenamentoBrutoI51.Ler` faz `File.ReadAllText` do bruto (~700 KB vira ~1,4 MB em string UTF-16, **LOH**). `ClienteWmsHttp` faz `ReadAsStringAsync` do mesmo corpo. `ParserI51` usa `JsonDocument.Parse`. | Por consulta com dados: 3 a 4 cópias de ~1,4 MB (transitório, 5 a 10 MB de pico). Gera pressão de LOH/gen2 a cada 5 min, não crescimento. |
| PDF/HTML | `PdfSharpCore` (com `SixLabors.ImageSharp` para o logo). `FechadorPacote` gera HTML e PDF do pacote inteiro, depois monta `MailMessage` com o PDF em `MemoryStream`. | Pico proporcional ao pacote (centenas de notas = dezenas a poucas centenas de MB transitórios). Ver 3.3 para o caso de repetição. |
| Kestrel (8085) | `TelaDevolucaoApp`: `CreateSlimBuilder`, `MaxRequestBodySize=64 KB`, sem autenticação, `0.0.0.0`, sem `MaxConcurrentConnections` nem timeouts customizados. `LocalizadorXmlDevolucao` varre `Processados` e todos os `pacote_*` por busca (só nomes, não lê XML). | Sem crescimento em condições normais. Uma varredura de rede (scanner) pode manter conexões abertas. Risco baixo, mas não há teto. |
| GC | `Worker.csproj` não define `ServerGarbageCollector`. O padrão é workstation concorrente, sem teto de heap. | Sem limite de memória: um bug futuro cresce até o sistema sofrer. |

### 2.2 CPU e IO (pergunta 2)

**Sem laço apertado.** `Worker.ExecuteAsync` sempre passa por `Task.Delay(IntervaloVarreduraSegundos=5 s)`. `ConsultaI51Worker` espera `IntervaloMinutos` (5) depois de cada ciclo, mesmo com exceção. Em cooldown de auth, `GerenciadorPacote` só retorna e loga uma vez (`_aguardandoLogado`).

**Trabalho recorrente a cada ciclo da I51 (5 min):**
- `HaNotaAguardandoRecente` é quase sempre verdadeiro (há notas `AguardandoSeparacao` ou `Atrasada` com menos de 14 dias). Logo o **TODOS roda a cada 5 min**: GetToken + POST + resposta de até ~700 KB, gravada em `Estado\i51\brutos\pendentes`, relida, processada e movida para `processados\<data>`.
- `CasadorDocumentoNota.Destino` chama `_repositorio.Listar()` (copia e **ordena todas as notas**) **por documento**. Com D documentos e N notas: custo D × N log N. `ReprocessarOrfaos` roda `Destino` 2 vezes por órfão e **relê do disco todos os arquivos de órfão** a cada ciclo.
- `LogarResumo` faz `Listar()` mais 6 contagens. `AlertaI60.ProcessarAsync` faz outro `Listar()` ordenado por ciclo.
- Para 600 notas e alguns milhares de documentos/órfãos: ordem de 10⁴ ordenações de 600 itens por ciclo, algumas centenas de MB de alocação transitória. É CPU/GC, não vazamento. Cresce com o número de órfãos (sem teto até 30 dias).

**Reprocessamento sem arquivo novo (e-mails de 1 nota):**
- **Alerta imediato da I60** (`AlertaI60`): alerta uma vez por nota/evento, disparado no ciclo da I51. `Atrasada` = 24 h após `RegistradaEm`. Em horários diferentes, sem arquivo novo, com 1 nota por e-mail. **É a hipótese mais compatível com o relato.**
- **Pacote de 1 nota** por sobras na entrada: se o GetToken falha no meio do pacote, as notas já tratadas vão no e-mail do pacote e as demais ficam na entrada (`UltimoAbortoAutenticacao`). Depois do cooldown (60/120/300 s), outro pacote abre sobre as sobras e gera outro e-mail. O mesmo vale para ZIP mantido na entrada (`MoverZipTratado` não move). Exige arquivos colocados antes na pasta.
- **Retomada do diário**: `GerenciadorPacote.CicloAsync` chama `_diario.Apagar()` e **ignora o retorno**. Se o arquivo `Estado\pacote-atual.jsonl` estiver bloqueado (antivírus, backup) e o apagamento falhar, qualquer reinício do serviço executa `Retomar`, fecha o pacote e **reenvia o e-mail do pacote antigo**. Combina com reinícios (OOM, NSSM `AppExit Default Restart`, deploy).
- **Reenvio a cada 5 s por falha repetida**:
  - `FechadorPacote.FecharAsync` devolve `false` se gerar/gravar relatório falhar, e o estado fica `Fechando`. O ciclo seguinte (5 s depois) renderiza o PDF de novo, sem backoff nem teto.
  - `ServicoRelatorioDiarioI60` e `ServicoResumoSemanal`: se o envio funciona e `Marcar(slot)` falha (só loga), reenviam a cada 5 s.
  - `OrganizadorArquivos.Mover` com falha permanente em `RepetirMovesPendentes` gera log de erro com stack a cada 5 s.
  Estes casos dão e-mails repetidos em segundos, não em horários espaçados, e dependem de um erro específico de disco/permissão.

### 2.3 Caminho do token por envio e a janela 09:48–09:56 (pergunta 3)

- `PostCoreAsync` → `ObterTokenPorEnvioAsync` → `ObterTokenAsync` (até `TokenTentativasImediatas=3`, backoff 0,5 s e 1 s, tentativas 2 e 3 em conexão nova com `Connection: close`, cada uma com timeout de 30 s).
- Esgotadas as tentativas, `RegistrarFalha` abre o cooldown global: 60 s, 120 s, 300 s, 600 s, 900 s. Durante o cooldown **nenhuma** chamada ao WMS (`_guarda.Bloqueada`). O pacote não abre e a I51 falha rápido.
- Pior caso em 8 min: 3 episódios × 3 GetToken = **9 chamadas**. `MaxGetTokenPorMinuto=300` nunca atua nesse cenário. Em operação normal o teto também raramente atua: o processamento é sequencial (`MaxTransportesConcorrentes=1`), com GetToken ~120 ms (`docs/OPERACAO.md`), na faixa de 100 a 150 chamadas/min por fluxo.
- O alerta de 09:48 é a primeira falha de episódio (`RegistrarFalha` devolve `primeira`). A recuperação às 09:56 é o primeiro sucesso depois da escada 1+2+5 min. O próximo gatilho depois do cooldown é o ciclo da I51 (a cada 5 min) ou a abertura de um pacote, então o "fim" do incidente depende do polling.
- Pontos de atenção (não amplificam o outage):
  - Um 401 do **POST** após a renovação única propaga `HttpRequestException` e a nota vai para `Erro` (categoria "Outro") em vez de ser tratado como falha de autenticação. Há `deploy\reprocessar-erro-401.ps1` por causa disso.
  - Na renovação por 401 do POST, o primeiro GetToken novo usa conexão do pool, não conexão nova (só a 2ª/3ª tentativa mudam).
  - I51 e I60 compartilham o mesmo `ClienteWmsHttp` e a mesma guarda, então um cooldown parado trava os dois (comportamento correto).

### 2.4 Volume de log (pergunta 4)

- Provedor de log: padrão do `Host.CreateApplicationBuilder`: console (stdout → NSSM), Debug, EventSource e **EventLog (Windows, nível Warning ou acima)**. `appsettings.json` define `Default=Information` e nenhum filtro para `System.Net.Http` nem `Microsoft.AspNetCore`.
- Consequências:
  - `IHttpClientFactory` loga a cada requisição **4 linhas Information** (`System.Net.Http.HttpClient.wms.LogicalHandler`/`ClientHandler`: start, send, headers, end). Com token por envio são 2 requisições por nota (GetToken + POST), logo ~8 linhas por nota. Um pacote de 188 notas ≈ 1.500 linhas extras. Os clientes de "conexão nova" não passam pela fábrica e não geram essas linhas.
  - Kestrel (via `ILoggerFactory` compartilhado) loga "Request starting/finished" em Information para cada acesso à tela 8085 (e a qualquer varredura ou monitor que a chame).
  - Cada Warning/Error também vai para o **Event Log "Application"**.
- Volume estimado em operação normal: ordem de 1 a 5 MB/dia. Em falha repetida a cada 5 s com stack (17.280 vezes/dia): dezenas de MB/dia.
- **NSSM (`deploy\instalar.ps1`)**: `AppRotateFiles=1`, `AppRotateOnline=1`, `AppRotateBytes=10 MB`, `AppRotateSeconds=86400`. Segundo a documentação do NSSM ([nssm.cc/usage](https://nssm.cc/usage)), com rotação online o limite de tamanho vale com o serviço rodando e `AppRotateSeconds` é ignorado nesse modo. O comentário "diariamente **ou** a cada 10 MB" do script não descreve isso. O NSSM rotaciona e **não apaga** os arquivos antigos: cada 10 MB rotacionado fica para sempre em `C:\Friozem\importacao_notas\logs`. Em operação normal é lento; em falha repetida enche o disco.
- Com rotação online o NSSM lê stdout/stderr por pipe e grava o arquivo. Se o disco encher ou o NSSM travar, o console do Worker pode bloquear na escrita (fila do logger com limite de 2.500 mensagens e modo "esperar").
- **Sem carimbo de hora**: nenhum `TimestampFormat` em lugar algum. `worker.out.log` mostra apenas o texto. A correlação com 09:48 só é possível por `LastWriteTime` e pelos artefatos com hora (seção 5.5).

---

## 3. Pontos de risco priorizados (o que pode causar pressão no servidor)

### 3.1 Disco (maior chance de impacto sistêmico)
- `Estado\i51\brutos\processados\<data>`: um arquivo por TODOS/UNICO com dados (até ~700 KB cada), 30 dias de retenção. Com consultas de 700 KB a cada 5 min seriam até ~200 MB/dia e ~6 GB em 30 dias (limite superior; depende de quantas respostas trazem dados).
- Logs rotacionados do NSSM (10 MB cada, sem limpeza).
- `Processados` e `Erro` (30 dias, limpeza diária), `relatorios` (HTML e PDF por pacote, sem limpeza própria), `Estado\i51\orfaos` (um arquivo por documento sem nota), `Estado\i51\xml`, `Estado\i51\alertas`.
- Disco do sistema (C:) cheio ou lento afeta pagefile, WinRM e SMB, mesmo com o Worker pequeno.

### 3.2 Reenvio e e-mails de nota única
Ver 2.2. Hipótese principal: alerta de nota `Atrasada` (24 h). Secundárias: sobras na entrada após interrupção de auth, retomada do diário após reinício com `Apagar()` falho.

### 3.3 Repetição sem backoff a cada 5 s
`FecharAsync` falho (renderização de PDF a cada 5 s), `RepetirMovesPendentes` com erro permanente, `ServicoRelatorioDiarioI60`/`ServicoResumoSemanal` com marca não gravada. Cada repetição aloca dezenas a centenas de MB transitórios (PDF) e pode produzir log e e-mail em excesso. Só ocorre com erro persistente de disco, permissão ou SMTP.

### 3.4 Churn de memória no ciclo da I51
`Listar()` + ordenação por documento e releitura de todos os órfãos. Sem vazamento; mais GC/CPU conforme os órfãos crescem. Medir a contagem de órfãos (seção 5.6).

### 3.5 Ausência de teto de memória e de diagnóstico do processo
Sem `GCHeapHardLimit`, sem registro periódico de memória/handles/threads, sem timestamp no log. Se houver um vazamento ainda não identificado, hoje só o travamento denunciaria.

### 3.6 Menores
- Kestrel sem limites de conexão ou tempo, sem autenticação, escutando em todas as interfaces.
- `LimpadorPastas.Limpar` roda na **partida** (varre todos os pacotes de 30 dias com `AllDirectories`). Reinícios repetidos geram rajadas de IO.
- Fresh handler por retry de GetToken: custo de handshake em falha em massa.

---

## 4. Conclusões

1. Não foi encontrado, no código lido, mecanismo de vazamento ou crescimento de memória sem limite. Também não se pode provar que o processo **não** é o culpado sem os números do servidor (seção 5). Um travamento global com ping vivo é compatível com disco/IO ou esgotamento do sistema por qualquer processo (SMB `kitbrf`, outros serviços NSSM do servidor, antivírus, backup).
2. A janela 09:48–09:56 não foi agravada pelo integrador: o código fez poucas chamadas e respeitou o cooldown. O dado que falta é o motivo do alerta das 09:48 (HTTP 401 versus timeout/rede).
3. Os e-mails de nota única têm candidato forte no alerta de `Atrasada` (24 h) da I60. Confirmar pelo assunto e pelo arquivo `Estado\i51\alertas\2026-11-18.log`.
4. O que mais precisa de correção é diagnosticabilidade (log sem hora, sem métricas do processo) e falta de teto/limpeza (brutos, logs rotacionados, órfãos).

---

## 5. O que verificar no servidor quando voltar

Todos os comandos são somente leitura, compatíveis com PowerShell 4 (Server 2012 R2) e leves. Rodar um bloco por vez no WinRM. Evitar abrir os logs inteiros. Caminhos assumidos (confirmar no deploy): serviço `Friozem-ImportacaoNotas`, logs `C:\Friozem\importacao_notas\logs`, entrada `C:\Friozem\kitbrf`, estado `C:\Friozem\kitbrf\Estado`.

### 5.1 Estado do sistema e reinício
```powershell
Get-WmiObject Win32_OperatingSystem |
  Select LastBootUpTime,
         @{n='RAM_Total_MB';e={[int]($_.TotalVisibleMemorySize/1KB)}},
         @{n='RAM_Livre_MB';e={[int]($_.FreePhysicalMemory/1KB)}},
         @{n='Virtual_Total_MB';e={[int]($_.TotalVirtualMemorySize/1KB)}},
         @{n='Virtual_Livre_MB';e={[int]($_.FreeVirtualMemory/1KB)}}
Get-WmiObject Win32_PageFileUsage | Select Name,AllocatedBaseSize,CurrentUsage,PeakUsage
Get-WmiObject Win32_LogicalDisk -Filter "DriveType=3" |
  Select DeviceID,@{n='Livre_GB';e={[math]::Round($_.FreeSpace/1GB,1)}},@{n='Total_GB';e={[math]::Round($_.Size/1GB,1)}}
Get-Counter '\Memory\Available MBytes','\Memory\Committed Bytes','\Memory\Commit Limit','\Memory\Pool Nonpaged Bytes','\Memory\Pool Paged Bytes','\Process(_Total)\Handle Count' |
  Select -ExpandProperty CounterSamples | Select Path,CookedValue
```

### 5.2 Processo do integrador e maiores consumidores
```powershell
Get-Process KitWmsIntegrador.Worker -ErrorAction SilentlyContinue |
  Select Id,StartTime,Handles,CPU,
         @{n='Threads';e={$_.Threads.Count}},
         @{n='WS_MB';e={[int]($_.WorkingSet64/1MB)}},
         @{n='Private_MB';e={[int]($_.PrivateMemorySize64/1MB)}},
         @{n='Paged_MB';e={[int]($_.PagedMemorySize64/1MB)}},
         @{n='Virtual_MB';e={[int]($_.VirtualMemorySize64/1MB)}}
Get-Process | Sort-Object WorkingSet64 -Descending | Select -First 12 Name,Id,Handles,@{n='WS_MB';e={[int]($_.WorkingSet64/1MB)}},@{n='Priv_MB';e={[int]($_.PrivateMemorySize64/1MB)}}
Get-Process | Sort-Object Handles -Descending | Select -First 8 Name,Id,Handles
Get-WmiObject Win32_Service -Filter "Name='Friozem-ImportacaoNotas'" | Select State,ProcessId,StartMode,StartName
```
Interpretação: `Private_MB` do Worker acima de ~500 MB, ou crescendo entre duas amostras (repetir após 10 min), aponta para vazamento; `Handles` acima de ~5.000 ou `Threads` acima de ~100 também. Valores de ~80 a 250 MB são o esperado.

### 5.3 Rede com o WMS
```powershell
$ips = [System.Net.Dns]::GetHostAddresses('friozem.assistsoftware.com.br') | % { $_.IPAddressToString }
$ips
$n = netstat -ano -p tcp
($n | ? { $_ -match 'TIME_WAIT' }).Count
($n | ? { $_ -match 'ESTABLISHED' }).Count
foreach ($ip in $ips) { "$ip ESTABLISHED/TIME_WAIT: " + ($n | ? { $_ -match [regex]::Escape($ip) -and $_ -match 'ESTABLISHED|TIME_WAIT' }).Count }
($n | ? { $_ -match ':8085\s' }).Count     # conexões na tela de devolução
```
Interpretação: dezenas de conexões ao WMS é normal. Milhares de TIME_WAIT ou muitas conexões em `:8085` merecem atenção.

### 5.4 Event Log entre 08:30 e 11:00 de 18/11 (usar `-ErrorAction SilentlyContinue`: o cmdlet gera erro quando não há eventos)
```powershell
$ini = Get-Date '2026-11-18 08:30'; $fim = Get-Date '2026-11-18 11:00'
function Ev($log,$ids){ Get-WinEvent -FilterHashtable @{LogName=$log;Id=$ids;StartTime=$ini;EndTime=$fim} -ErrorAction SilentlyContinue |
  Select TimeCreated,Id,ProviderName,@{n='Msg';e={($_.Message -replace '\s+',' ').Substring(0,[Math]::Min(400,$_.Message.Length))}} }
Ev 'System' 2004,2019,2020,2017,6005,6006,6008,6009,41,1074,7031,7034,7036,7040,7011,7009
Ev 'Application' 1000,1001,1002,1026,2004
Get-WinEvent -FilterHashtable @{LogName='System';Level=1,2;StartTime=$ini;EndTime=$fim} -MaxEvents 80 -ErrorAction SilentlyContinue | Select TimeCreated,Id,ProviderName,@{n='Msg';e={($_.Message -replace '\s+',' ').Substring(0,[Math]::Min(250,$_.Message.Length))}}
Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-WinRM/Operational';StartTime=$ini;EndTime=$fim} -MaxEvents 60 -ErrorAction SilentlyContinue | Select TimeCreated,Id,LevelDisplayName,@{n='Msg';e={($_.Message -replace '\s+',' ').Substring(0,[Math]::Min(250,$_.Message.Length))}}
Get-WinEvent -FilterHashtable @{LogName='Application';ProviderName='Application';StartTime=$ini;EndTime=$fim} -MaxEvents 60 -ErrorAction SilentlyContinue | Select TimeCreated,Id,LevelDisplayName,@{n='Msg';e={($_.Message -replace '\s+',' ').Substring(0,[Math]::Min(250,$_.Message.Length))}}
```
- **ID 2004** (System, Resource-Exhaustion-Detector): lista os processos que mais consumiram memória virtual e é o dado decisivo. Se o Worker aparece, o integrador é a causa.
- IDs 2019/2020/2017 (origem `Srv`): falta de pool paginado/não paginado no servidor SMB.
- ID 6008/41: desligamento inesperado. 1000/1026: queda de aplicação .NET. 7031/7034: serviço terminou sem querer (inclui o NSSM). 7036: início/parada de serviço.
- Disco: filtrar também `Id=7,11,51,153,129` (fontes `disk`, `Ntfs`, `storahci`).
- Eventos de nível Warning/Error do próprio Worker aparecem no log Application com o provedor "Application" (o último comando).

### 5.5 Logs do serviço (sem ler arquivos grandes inteiros)
```powershell
$L = 'C:\Friozem\importacao_notas\logs'
Get-ChildItem $L | Sort LastWriteTime -Descending | Select -First 25 Name,@{n='MB';e={[math]::Round($_.Length/1MB,1)}},LastWriteTime
(Get-ChildItem $L | Measure-Object Length -Sum).Sum/1MB
Get-Content "$L\worker.out.log" -Tail 400
Get-Content "$L\worker.err.log" -Tail 100
Get-Content "$L\worker.out.log" -Tail 8000 | Select-String 'GetToken|Aguardando autent|Pacote (aberto|fechado|retomado|interrompido)|I60 precisa|Falha ao mover|Falha no ciclo|Falha inesperada|Exce|OutOfMemory|Out of memory' | Select -Last 80
```
O log não tem hora. Correlacionar com os artefatos que têm:
```powershell
$E = 'C:\Friozem\kitbrf'
Get-Content "$E\Estado\telemetria\servico.jsonl" -Tail 20          # servico-iniciado = reinícios, com hora
Get-ChildItem "$E\Estado\i51\alertas" | Sort LastWriteTime -Descending | Select -First 3 Name,LastWriteTime
Get-Content "$E\Estado\i51\alertas\2026-11-18.log" -Tail 60           # hora local, evento, nota
Get-ChildItem "$E\Processados" -Directory | Sort Name -Descending | Select -First 12 Name,LastWriteTime
Get-ChildItem "$E\relatorios" -ErrorAction SilentlyContinue | Sort LastWriteTime -Descending | Select -First 12 Name,Length,LastWriteTime
Get-Item "$E\Estado\pacote-atual.jsonl" -ErrorAction SilentlyContinue | Select Length,LastWriteTime   # diário que sobrou?
Get-Content "$E\Estado\i51\consulta.json"
```
Para o e-mail das 09:13: confira o **assunto**. "Fechamento do pacote ... 1 nota(s)" implica pacote em `Processados\pacote_20261118_09xxxx`. "I60 precisa de atenção: 1 nota(s)" implica uma linha em `alertas\2026-11-18.log` com evento `Atrasada`, `Pendente`, `ENC/IMP` ou `NaoReconhecida`. Para o alerta das 09:48, o corpo do e-mail traz "HTTP 401" ou "sem resposta (timeout/rede)".

### 5.6 Tamanho e contagem do Estado (leve; pastas grandes demoram na rede, rodar um comando por vez)
```powershell
$E = 'C:\Friozem\kitbrf\Estado\i51'
foreach ($d in 'notas','orfaos','xml','alertas','brutos\pendentes','brutos\falhos','comandos') {
  $p = Join-Path $E $d
  if (Test-Path $p) { $f = Get-ChildItem $p -File -ErrorAction SilentlyContinue; "{0,-18} arquivos={1,6}  MB={2,8:N1}" -f $d,$f.Count,(($f | Measure-Object Length -Sum).Sum/1MB) }
}
$bp = Join-Path $E 'brutos\processados'
Get-ChildItem $bp -Directory -ErrorAction SilentlyContinue | Sort Name -Descending | Select -First 5 | % { $f = Get-ChildItem $_.FullName -File; "{0} arquivos={1} MB={2:N1}" -f $_.Name,$f.Count,(($f | Measure-Object Length -Sum).Sum/1MB) }
Get-ChildItem $bp -Directory -ErrorAction SilentlyContinue | Measure-Object | Select Count
(Get-ChildItem 'C:\Friozem\kitbrf' -File | Measure-Object).Count                  # sobras na raiz
(Get-ChildItem 'C:\Friozem\kitbrf\Puxada' -File -ErrorAction SilentlyContinue | Measure-Object).Count
(Get-ChildItem 'C:\Friozem\kitbrf\PuxadaTSP' -File -ErrorAction SilentlyContinue | Measure-Object).Count
```
Sinais de alerta: `orfaos` com milhares de arquivos; `brutos\processados` na casa de GB; `brutos\pendentes` ou `falhos` com arquivos parados; arquivos esquecidos em `Puxada`, `PuxadaTSP` ou na raiz (candidatos a reenvio e e-mail repetido).

### 5.7 Configuração efetiva do NSSM e do ambiente
```powershell
$n = (Get-Command nssm.exe -ErrorAction SilentlyContinue).Path   # ou o caminho em C:\Friozem\importacao_notas\tools
foreach ($p in 'AppRotateFiles','AppRotateOnline','AppRotateBytes','AppRotateSeconds','AppExit','AppPriority','AppRestartDelay','AppEnvironmentExtra') { "$p = " + (& $n get Friozem-ImportacaoNotas $p) }
[Environment]::GetEnvironmentVariables('Machine').GetEnumerator() | ? { $_.Key -match '^(Wms|I51|I60|Devolucao|Logging|DOTNET)' } | % { $_.Key }   # só os nomes, sem valores (podem ter segredo)
```

---

## 6. Correções recomendadas e mitigações

Estimativas só em horas. "IA" = tempo de execução do agente; "Humano" = revisão, deploy e validação. Os valores humanos são estimativas de planejamento; as horas humanas efetivas são as informadas por você.

### 6.1 Mitigações por configuração, sem release (aplicam com reinício do serviço, salvo indicação)
| # | Mudança | Efeito | IA | Humano |
|---|---|---|---|---|
| M1 | Em `appsettings.Production.json`: `Logging:Console:FormatterName=simple` com `FormatterOptions:TimestampFormat="yyyy-MM-dd HH:mm:ss "` | O log passa a ter hora. Mudança de formato do texto do log, sem efeito funcional. | 0,1 | 0,2 |
| M2 | `Logging:LogLevel:System.Net.Http.HttpClient=Warning` e `Logging:LogLevel:Microsoft.AspNetCore=Warning` (variáveis de ambiente equivalentes: `Logging__LogLevel__System.Net.Http.HttpClient`) | Remove ~8 linhas por nota e o log de cada acesso à tela 8085. O GetToken com falha continua em Warning do próprio `ClienteWmsHttp`. | 0,1 | 0,2 |
| M3 | `nssm set Friozem-ImportacaoNotas AppPriority BELOW_NORMAL_PRIORITY_CLASS` | O integrador não disputa CPU com SMB/WinRM. | 0,1 | 0,2 |
| M4 | Variável de máquina `DOTNET_GCHeapHardLimit=0x30000000` (768 MB; os valores de GC são hexadecimais) | Contenção: se houver vazamento, o processo cai com OutOfMemory e o NSSM o reinicia, em vez de afundar o servidor. Muda o modo de falha, portanto aplicar só com o monitoramento de M6 ligado. | 0,1 | 0,3 |
| M5 | `I51__IntervaloMinutos=10` (ou 15) e, se preciso, `I51__MaxUnicoPorCiclo=3` | Menos TODOS (e menos brutos/IO). A I60 das separadas sai com até 10 a 15 min de atraso. Mudança de comportamento: validar com você. | 0,1 | 0,2 |
| M6 | Limpeza manual de logs rotacionados antigos e de `brutos\processados` com mais de 7 dias, depois de olhar a seção 5.5/5.6 | Libera disco. Apagar dados exige sua confirmação. | 0,2 | 0,3 |
| M7 | `Wms__TokenPorEnvio=false` | **Não recomendado como mitigação de memória**: reduz GetToken, mas contraria a orientação da Assist (16/11) e pode reintroduzir 401. Só se o motivo das 09:48 for carga no GetToken. | 0,1 | 0,2 |
| M8 | Parar o serviço por algumas horas (teste de isolamento) | Prova se o integrador é a causa. Os arquivos ficam na entrada. Não desligar `I51:Habilitada`: com ele desligado a I60 volta a sair junto da I50 (mudança de comportamento). | 0,0 | 0,3 |

### 6.2 Correções de código (release futura), por prioridade
| Prioridade | Correção | Arquivos | IA | Humano |
|---|---|---|---|---|
| P1 | Backoff e teto de repetição em `FecharAsync` falho (ex.: 5, 15, 60 s e abandonar o relatório mantendo o diário), no `RepetirMovesPendentes` (log só na 1ª e a cada N min) e nas marcas dos relatórios diário/semanal (não reenviar se `Marcar` falhar) | `GerenciadorPacote.cs`, `ProcessadorLote.cs`, `ServicoRelatorioDiarioI60.cs`, `ServicoResumoSemanal.cs`, `MarcaUltimo*.cs` | 2,5 | 1,0 |
| P1 | Tratar o retorno de `_diario.Apagar()` (se falhar, renomear/esvaziar o diário ou registrar o pacote como fechado) para a retomada não reenviar e-mail antigo | `GerenciadorPacote.cs`, `DiarioPacote.cs` | 1,0 | 0,5 |
| P1 | Métricas do processo (WS, Private, handles, threads, GC) a cada 5 min em `servico.jsonl`, com alerta no log acima de limite; reaproveitar no "Diagnóstico agente integrador" das 14:00 que você pediu | `Worker.cs`, `RegistradorTelemetria.cs`, novo serviço de diagnóstico | 2,0 | 0,8 |
| P1 | Timestamp de log por padrão no `appsettings.json` e filtros `System.Net.Http.HttpClient` e `Microsoft.AspNetCore` em Warning | `appsettings.json`, `appsettings.Production.json` (molde) | 0,3 | 0,3 |
| P2 | Limpeza e teto de disco: apagar logs rotacionados do NSSM (script ou tarefa em `deploy\`), retenção própria para `brutos\processados` (7 dias), não gravar brutos processados com sucesso depois de aplicar, retenção de `relatorios` | `ArmazenamentoBrutoI51.cs`, `LimpadorPastas.cs`, `deploy\instalar.ps1`, novo script | 3,0 | 1,0 |
| P2 | Casamento da I51: indexar notas por número uma vez por ciclo (dicionário) em vez de `Listar()` por documento; cache em memória dos órfãos em vez de reler todos os arquivos por ciclo; `Listar()` único por ciclo para `LogarResumo`/`AlertaI60` | `CasadorDocumentoNota.cs`, `RepositorioOrfaosI51.cs`, `ServicoSeparacaoI51.cs`, `AlertaI60.cs` | 3,0 | 1,0 |
| P2 | Teto no TODOS: não reler o bruto do disco logo após gravá-lo (processar o string em memória e gravar só para auditoria) | `ServicoSeparacaoI51.cs`, `ArmazenamentoBrutoI51.cs` | 1,0 | 0,5 |
| P3 | Kestrel: `MaxConcurrentConnections` (ex.: 50), `RequestHeadersTimeout`, `KeepAliveTimeout` curtos; escutar só na interface interna | `TelaDevolucaoApp.cs`, `ServidorTelaDevolucao.cs` | 0,8 | 0,4 |
| P3 | Tratar 401 do POST após a renovação como falha de autenticação (cooldown), não como erro da nota | `ClienteWmsHttp.cs`, `ProcessadorLote.cs` | 1,5 | 0,8 |
| P3 | Reaproveitar um único handler "sem pool" para as tentativas de GetToken (em vez de criar e descartar) | `RegistroClienteWms.cs` | 1,0 | 0,3 |
| P3 | Rotação do NSSM: corrigir o comentário do script e documentar que a rotação online usa só o tamanho | `deploy\instalar.ps1`, `docs\INSTALACAO-SERVIDOR.md` | 0,3 | 0,2 |

Total das correções P1: ~5,8 h de IA e ~2,6 h de revisão e validação humana. P1 a P3 completas: ~16,4 h de IA e ~7,4 h humanas.

### 6.3 Ordem sugerida
1. Quando o servidor voltar: seções 5.1 a 5.6 (≈ 0,5 h humana, 0,5 h de IA para interpretar os resultados).
2. Aplicar M1 a M3 (e M6 se o disco estiver cheio). Sem release.
3. Decidir sobre o teste de isolamento M8 se o Evento 2004 não for conclusivo.
4. Planejar a release com os itens P1.

## 7. Limitações
- Análise estática do código. Não há acesso ao servidor, aos logs, às contagens de órfãos/brutos, à RAM do servidor nem à versão exata do Windows Update. Os números de volume (700 KB por resposta, 600+ notas) vieram do pedido de investigação.
- A causa dos e-mails de nota única e do motivo do alerta das 09:48 depende do assunto/corpo dos e-mails e dos arquivos citados na seção 5.5.
- Não foi verificado o comportamento do .NET 8 em Windows Server 2012 R2 em relação a consumo de SChannel/LSASS durante falhas em massa de TLS; os handshakes extras existem apenas nas tentativas 2 e 3 do GetToken, poucos por episódio.

## 8. Fontes
- Documentação do NSSM sobre rotação de arquivos: https://nssm.cc/usage (conteúdo parafraseado).
- Código-fonte do repositório (`src\KitWmsIntegrador.Core\...`, `src\KitWmsIntegrador.Worker\...`, `deploy\instalar.ps1`, `docs\OPERACAO.md`).
