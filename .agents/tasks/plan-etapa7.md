# Plano — Etapa 7: watcher de pasta + pacote + movimentação + limpeza + e-mail

Projeto KitWmsIntegrador (C#/.NET 8). Branch `main`, sem worktree. Commits pequenos por feature, só com arquivos
específicos (nunca `git add .`). Não tocar em `docs/diario` nem `docs/controle`.
Comandos reais: `dotnet build c:/wms-integrador/KitWmsIntegrador.sln` e `dotnet test c:/wms-integrador/KitWmsIntegrador.sln`
(xUnit 2.5.3; baseline: 129 testes passando). Tests só referenciam o Core, então **toda lógica testável fica no Core**;
o Worker fica fino (BackgroundService + DI).

## 0. O que foi lido (base das decisões)

- `Worker.cs` hoje é de execução única (varre, agrupa, loga, `StopApplication`). Será substituído por loop contínuo.
- `Program.cs`: DI com singletons; `Orquestrador` (ctor com `IClienteWms`, construtores, `ClassificadorRetorno`, `IOptions<OpcoesI60>`, `IClock`, logger) é stateless → seguro para uso concorrente; `ClienteWmsHttp` tem `_tokenLock` (SemaphoreSlim) no token → concorrência entre transportes é segura.
- `VarredorPasta.Varrer` usa `TopDirectoryOnly` + `*.xml`, então subpastas `Processados`/`Erro` dentro da entrada não são re-varridas. Erros de parse são coletados em `Erros` (`ErroArquivo(Arquivo, Mensagem)`). `LeitorNotaFiscalXml.ParseArquivo` abre com `FileShare.Read`; `NotaFiscal.ArquivoOrigem` guarda o caminho do XML (permite mapear nota → arquivo).
- `IOrquestrador.IntegrarAsync(IEnumerable<NotaFiscal>, transporte)` é sequencial por nota e devolve 1 `ResultadoIntegracaoNota` por nota. Nota sem transporte vira `NaoIntegrada` com motivo "Nota sem número de transporte…" (caso B, `Tentativas=0`).
- Estados: `Integrada | Pendente | NaoIntegrada`; híbrido conhecido: `Integrada=true` + `Estado=Pendente` (produto provisório via I30) — **usar `Integrada` para decidir "integrada"**.
- `ClassificadorRetorno` classifica por texto (âncoras privadas: "Registrado com sucesso", "Empresa não localizada", "não ativo ou faltando ou não cadastrado", "pendente na interface", "já existe").
- Relatório: `IGeradorRelatorio.GerarHtml/GerarPdf(IReadOnlyList<LinhaRelatorioErro>, DateTimeOffset)`, `GerarArquivos` (nome fixo `erros_<yyyy-MM-dd_HHmm>`, colide se 2 pacotes fecharem no mesmo minuto), `MapeadorRelatorio.De(resultados, horario)`, `ModeloRelatorio.Montar(linhas).Resumo` (`ResumoRelatorio`: Total, Integradas, IntegradasRevisarProduto, Pendentes, NaoIntegradas, Transportes, NotasPorTransporte). `OpcoesRelatorio.PastaSaida` (default `relatorios`).
- `IClock` só tem `UtcNow`; `FakeClock(inicio)` + `Avancar(delta)` em `tests/.../Infra`; `FakeClienteWms` com filas por `InterfaceWms` (repete a última resposta). Helpers `Sucesso()/ProdutoNaoCadastrado()/…` são `private static` em `OrquestradorTests.cs` (copiar o padrão, não referenciar).
- Fixtures: `tests/.../Recursos/*.XML` (2 XMLs BRF reais, copiados para a saída). Para o e2e de 2 transportes, gerar XMLs sintéticos válidos (padrão de `PendenciaSemTransporteTests`: XML mínimo nfeProc 4.00, com `infCpl` "Transporte: 123").
- `appsettings.json` já tem seções Entrada/Operacional/I50/I60/Relatorio/Wms. `.gitignore` não cobre arquivo de config local de e-mail.
- `ARQUITETURA.md` §5 (idempotência: WMS bloqueia duplicado, "já existe" não é erro) e §7 (decisões abertas: movimentação, concorrência, e-mail).

## 1. Decisões (cada uma fácil de mudar por configuração ou por 1 ponto de código)

**D1. Subpasta de erro: por CATEGORIA (recomendado), com opção por arquivo.** `Erro\<Categoria>\arquivo.xml` +
sidecar `arquivo.xml.motivo.txt` (mensagem, tentativas, transporte, data). Justificativa: a pasta por nota geraria uma
pasta com 1 arquivo por erro (centenas de pastas num lote de 400 notas), enquanto por categoria o operador vê de relance
"o que corrigir" (cadastrar produto, cadastrar empresa…) e o sidecar preserva o detalhe individual. "Uma pasta para cada
erro" é atendida como "um tipo de erro = uma pasta". Mudança fácil: `Pastas:OrganizacaoErro` = `PorCategoria` (padrão) |
`PorArquivo` (subpasta `Erro\<nome-do-arquivo-sem-extensão>\`) | `Plana`. Categorias (`CategoriaErro`): `XmlInvalido`,
`SemTransporte`, `ProdutoNaoCadastrado`, `EmpresaNaoLocalizada`, `TentativasEsgotadas`, `Outro`.
Prioridade da classificação (um único `switch` em `ClassificadorCategoriaErro`, fácil de reordenar):
XML não parseou → `XmlInvalido`; motivo "sem número de transporte" → `SemTransporte`; mensagem final contém âncora de
produto → `ProdutoNaoCadastrado`; âncora de empresa → `EmpresaNaoLocalizada`; `Tentativas >= Orquestrador.MaxTentativas`
→ `TentativasEsgotadas`; senão `Outro`. Mensagem antes de tentativas porque é mais acionável. Reaproveita o classificador
do Orquestrador adicionando **overloads `string?` aditivos** em `ClassificadorRetorno` (`EhEmpresaNaoLocalizada(string?)`,
`EhProdutoNaoCadastrado(string?)`, `EhJaExiste(string?)`) reutilizando as mesmas constantes; nenhuma decisão do
Orquestrador muda.

**D2. Destino por nota.** `Integrada=true` (inclui híbrido) ou `Estado=Pendente` → `Processados`. `NaoIntegrada` e XML
inválido → `Erro\<Categoria>`. Exceção de idempotência (D7): `NaoIntegrada` cuja mensagem contém "já existe" → `Processados`,
com mensagem prefixada "Já existia no WMS (reprocesso)" na linha do relatório. Mover logo após o resultado de CADA nota
(não no fim do pacote) — reduz a janela de reenvio após crash a 1 nota.

**D3. Interpretação de "velocidade + enviar por transporte" (CONFIRMAR com o usuário; ver §7).** Tratamos como:
(a) notas do mesmo transporte são enviadas **juntas e em sequência** como um grupo (`AgrupadorTransporte` → `Onda`);
(b) transportes distintos podem rodar **em paralelo com limite** `Pacote:MaxTransportesConcorrentes` (padrão **1**, conservador;
seguro subir para 2 porque Orquestrador é stateless e o token do cliente tem lock); (c) ordem de chegada: arquivos
ordenados por data de criação e depois nome; transportes ordenados pelo primeiro arquivo; (d) só processa arquivo
**completamente gravado** (D5). Janela de coleta (`JanelaColetaSegundos`, padrão 10) antes do 1º lote do pacote para que
uma cópia em massa de um mesmo transporte chegue inteira antes de enviar. Chegada tardia de nota de transporte já enviado no
mesmo pacote vai em lote posterior com o mesmo `nroagrupseparacao` (risco: WMS bloqueia onda que saiu de "editando" —
ARQUITETURA §5; isso cairia como NaoIntegrada com a mensagem do WMS). Em aberto, não descartado.

**D4. Watcher = polling, não `FileSystemWatcher`.** Pasta pode ser de rede/compartilhada e o FSW perde eventos (overflow de
buffer em cópia em massa). Loop `CicloAsync` a cada `Pacote:IntervaloVarreduraSegundos` (padrão 5). Ciclo único e
determinístico (sem `Task.Delay` interno) para teste com `FakeClock`.

**D5. Arquivo completo.** Pronto = (i) aparece com mesmo tamanho+LastWriteTime em 2 ciclos consecutivos E visto há
≥ `EstabilizacaoSegundos` (padrão 3, medido pelo `IClock`, não pelo mtime) E (ii) abre com `FileShare.None` (acesso
exclusivo). Arquivo que não fica pronto em `TimeoutArquivoSegundos` (padrão 300): loga warning, **não bloqueia o
fechamento** do pacote (continua na entrada; entra no próximo pacote quando liberar). XML truncado porém estável cai em
`XmlInvalido`. Só `*.xml` (case-insensitive), topo da pasta.

**D6. Máquina de estados do pacote** (`EstadoPacote`):
`Ocioso → Coletando → Processando → Fechando → Ocioso`.
- Ocioso: nenhum arquivo `.xml` na entrada. Ao detectar ≥1: abre pacote (`InicioUtc`), vai a Coletando.
- Coletando: espera `JanelaColetaSegundos` desde o último arquivo novo visto; depois Processando.
- Processando: a cada ciclo pega os prontos (snapshot), parseia, agrupa por transporte, integra, move, grava diário.
  Arquivos novos entram no mesmo pacote no ciclo seguinte.
- Fechamento: entrada sem arquivos pendentes (descontando os "instáveis acima do timeout") E nada em processamento E
  `QuietudeSegundos` (padrão 30) sem arquivo novo/processado. Chegada de arquivo durante a quietude volta a Processando
  (debounce reinicia). Chegada durante Fechando fica para o próximo pacote.
- Fechando: monta linhas (resultados + XMLs inválidos), gera HTML+PDF em `Relatorio:PastaSaida` (sempre), decide e envia
  e-mail, apaga o diário, `FimUtc`, volta a Ocioso. Falha no relatório/e-mail NUNCA relança: loga erro; diário só é apagado
  se o relatório foi gravado (senão tenta de novo no ciclo seguinte; relatório em disco é a garantia mínima).
- Exceção em um arquivo/transporte: capturada por arquivo/transporte (arquivo vai para `Erro\Outro` com motivo; os demais
  seguem); exceção no ciclo inteiro: logada, estado preservado, próximo ciclo tenta de novo. Cancelamento (`stoppingToken`)
  interrompe de forma limpa sem fechar o pacote.

**D7. Retomada e idempotência.** Fonte de verdade = WMS ("já existe", ARQUITETURA §5). Medidas: (1) mover cada arquivo
logo após o resultado; (2) diário do pacote `Pastas:Estado\pacote-atual.jsonl` (1 linha JSON por resultado/XML inválido,
append + flush) — no restart o pacote é reaberto com as linhas do diário, assim o relatório final contém também o que
foi enviado antes da queda; (3) se a nota em voo na queda for reenviada, o WMS responde "já existe" → D2 trata como
processada. **Suposição NÃO verificada:** o texto exato da resposta da I50 para duplicata contém "já existe" (o
Orquestrador hoje a trataria como `NaoIntegrada`/"outro erro"). Registrar em §7; se o texto for diferente, ajustar só
`EhJaExiste`. (4) Falha ao mover (arquivo travado/destino existe): nome único com sufixo `_n`; se continuar falhando, o
caminho entra num `HashSet` em memória "já enviado, mover pendente" que NUNCA é reenviado ao WMS; só repete o move.

**D8. Limpeza.** `LimpadorPastas.Limpar()` na inicialização e depois a cada 24 h (checado no loop, executado entre ciclos,
sem concorrência com o processamento). Idade medida por `LastWriteTimeUtc`; **a movimentação carimba
`LastWriteTimeUtc = agora`** (File.Move preserva o mtime original do XML, que poderia ser antigo e apagar o arquivo no
mesmo dia). `Limpeza:DiasRetencao` padrão 10, `Limpeza:Habilitada` padrão true. Segurança: caminhos resolvidos com
`Path.GetFullPath`; só apaga arquivos cujo caminho completo está DENTRO de `Processados` ou `Erro` (checagem de prefixo com
separador); recusa limpar (loga erro) se `Processados`/`Erro` forem iguais, contiverem ou forem a entrada, ou a raiz de
um drive; não segue reparse points/symlinks; remove subpastas vazias de categoria mas nunca as raízes; nunca toca na entrada
nem em `relatorios`. Loga cada remoção (caminho + idade) e um total.

**D9. E-mail.** `System.Net.Mail.SmtpClient` (sem novo pacote; já suficiente para STARTTLS/587 e SMTP interno), atrás de
`IEnviadorEmail` para trocar por MailKit depois sem tocar no resto. Limitação documentada: `SmtpClient` não suporta TLS
implícito (porta 465) nem OAuth2 (ex.: Microsoft 365/Gmail modernos); se o provedor exigir, avaliar MailKit (decisão em §7).
Config (`Email`, bind em `OpcoesEmail`): `Habilitado=false`, `Host`, `Porta=587`, `UsarTls=true`, `Usuario`, `Senha`
(placeholder vazio; override `Email__Senha`), `Remetente`, `RemetenteNome`, `Destinatarios` (string separada por `;`, para
override simples por env), `AssuntoPrefixo="[KitWms]"`, `EnviarQuando` = `ComProblema` (padrão) | `Sempre` | `Nunca`,
`TimeoutSegundos=60`. "Arquivo de configuração dedicado": o Worker também carrega `email.settings.json` (opcional,
`reloadOnChange`), listado no `.gitignore`; versionar apenas `email.settings.example.json`. Se `Habilitado=false` ou host/
remetente/destinatários vazios → `ResultadoEnvioEmail.NaoConfigurado`, só loga "relatório salvo em …" (nunca falha).
"ComProblema" = existe `NaoIntegradas>0`, `Pendentes>0`, `IntegradasRevisarProduto>0` ou XML inválido. Corpo HTML simples
(contagens por status + tabela de erros/pendências, tudo HTML-encoded, mensagem resumida pelo `ResumoMensagem`), anexo PDF
(em memória/arquivo gravado). Senha nunca logada.

**D10. Concorrência/escala.** Um `SemaphoreSlim(MaxTransportesConcorrentes)` em volta de cada onda; dentro da onda
sequencial por nota (chamando `IntegrarAsync(new[]{nota}, transporte)` por nota para poder mover logo após cada uma, mantendo a
semântica do Orquestrador). Diário e lista de resultados protegidos por lock.

**D11. Hospedagem.** `AddHostedService<Worker>` mantém; adicionar `builder.Services.AddWindowsService()` NÃO é necessário
(NSSM já hospeda o exe; evita pacote `Microsoft.Extensions.Hosting.WindowsServices`). Shutdown limpo via `stoppingToken`.

**D12. Opções de configuração** (Core, `Core/Configuracao`): `OpcoesPasta` (seção `Pastas`: `Entrada` — se vazia usa o legado
`Entrada:PastaEntrada` via `PostConfigure` no Program —, `Processados="Processados"`, `Erro="Erro"`, `Estado="Estado"`,
`OrganizacaoErro=PorCategoria`; caminhos relativos resolvem contra a Entrada; propriedades `CaminhoEntrada/Processados/Erro/Estado`),
`OpcoesPacote` (seção `Pacote`: `IntervaloVarreduraSegundos=5`, `EstabilizacaoSegundos=3`, `JanelaColetaSegundos=10`,
`QuietudeSegundos=30`, `TimeoutArquivoSegundos=300`, `MaxTransportesConcorrentes=1`), `OpcoesLimpeza` (seção `Limpeza`),
`OpcoesEmail` (seção `Email`). `OpcoesEntrada` (Worker) permanece para compatibilidade.

## 2. Divisão em features (sequenciais; commits separados)

- FEAT-001 — pastas, categorias de erro, movimentação, limpeza (Core + testes).
- FEAT-002 — e-mail (Core + testes) e configuração/appsettings/.gitignore de e-mail. Independente da FEAT-001.
- FEAT-003 — motor do pacote (detector, diário, processador, gerenciador, fechador), Worker/Program, e2e, docs. Depende de 001 e 002.

## 3. Plano ordenado (checklist)

### FEAT-001 — Pastas, classificação de erro, movimentação e limpeza

- [ ] 1. Criar `Core/Configuracao/OpcoesPasta.cs` (+ enum `OrganizacaoErro`) e `OpcoesLimpeza.cs` (D8, D12), com propriedades de caminho resolvido e método `Validar()` que devolve lista de problemas (Processados/Erro iguais/contendo a entrada, raiz de drive).
      Verify: `dotnet build c:/wms-integrador/KitWmsIntegrador.sln` sem avisos novos.
- [ ] 2. Adicionar overloads `string?` aditivos em `Core/Orquestrador/ClassificadorRetorno.cs` (`EhEmpresaNaoLocalizada`, `EhProdutoNaoCadastrado`, `EhJaExiste`) reutilizando as constantes; os métodos existentes (`RespostaWms`) delegam para o mesmo código sem mudar comportamento. Criar `Core/Pacote/CategoriaErro.cs` e `Core/Pacote/ClassificadorCategoriaErro.cs` (`Classificar(ResultadoIntegracaoNota)`, `ClassificarXmlInvalido()`).
      Verify: `dotnet test c:/wms-integrador/KitWmsIntegrador.sln --filter "ClassificadorRetorno|ClassificadorCategoriaErro"` — testes antigos do classificador continuam verdes + novos cobrem cada categoria e a prioridade.
- [ ] 3. Criar `Core/Pacote/OrganizadorArquivos.cs`: `Mover(arquivo, destino: Processados|Erro, categoria?, motivo)` — cria pastas, resolve nome único (`_1`, `_2`), grava sidecar `.motivo.txt` em Erro, carimba `LastWriteTimeUtc` com `IClock`, respeita `OrganizacaoErro`; nunca lança por 1 arquivo (retorna `ResultadoMovimento` com `Sucesso/Caminho/Erro`).
      Verify: testes em diretório temporário (`Path.GetTempPath()` + GUID, limpeza no `Dispose`): move para Processados, Erro/<Categoria>, `PorArquivo`, `Plana`, colisão de nome, arquivo travado (aberto com FileShare.None) devolve falha sem exceção, mtime carimbado.
- [ ] 4. Criar `Core/Pacote/LimpadorPastas.cs` (`Limpar()` → `ResultadoLimpeza` com removidos/erros): D8 completo, logando cada remoção via `ILogger`.
      Verify: testes com `FakeClock` e arquivos com `File.SetLastWriteTimeUtc`: apaga >10 dias em Processados e Erro (inclusive subpasta de categoria e remove subpasta vazia), preserva ≤10 dias, **não apaga** nada na entrada, nada fora das pastas (sibling com prefixo igual, ex. `Erro2`), recusa configuração perigosa (Processados == Entrada), `DiasRetencao` configurável, pastas inexistentes não falham.
- [ ] 5. Commit: `git add` apenas dos arquivos novos/alterados da FEAT-001 e `git commit -m "feat(pacote): pastas Processados/Erro por categoria, movimentacao e limpeza de 10 dias"`.
      Verify: `dotnet test c:/wms-integrador/KitWmsIntegrador.sln` — todos verdes (129 + novos).

### FEAT-002 — E-mail

- [ ] 6. Criar `Core/Configuracao/OpcoesEmail.cs` (D9) com `EstaConfigurado`, `ListaDestinatarios`, enum `EnviarQuando`; criar `Core/Email/IEnviadorEmail.cs`, `MensagemEmail`, `ResultadoEnvioEmail` (`Enviado | NaoConfigurado | Falhou(+Motivo)`), `Core/Email/PoliticaEnvioEmail.cs` (decide por `ResumoRelatorio` + XMLs inválidos + `EnviarQuando`).
      Verify: build ok; testes unitários da política (ComProblema/Sempre/Nunca; só integradas → não envia no padrão).
- [ ] 7. Criar `Core/Email/CorpoEmailResumo.cs` (HTML simples, HTML-encoded, contagens + tabela de erros/pendências limitada a N linhas com "…e mais X, ver PDF") e `Core/Email/EnviadorEmailSmtp.cs` (System.Net.Mail; TLS via `EnableSsl`, timeout, anexo PDF; captura exceção → `Falhou`; nunca loga senha) e `EnviadorEmailNaoConfigurado`/lógica `NaoConfigurado` quando `EstaConfigurado=false`.
      Verify: testes do corpo (contagens, encode de `<script>` na mensagem, truncamento), do enviador com config desabilitada/incompleta (não tenta rede, devolve `NaoConfigurado`), e construção da `MailMessage` via método interno testável (assunto com prefixo, destinatários múltiplos, anexo). Sem SMTP real.
- [ ] 8. Config: seção `Email` com placeholders (sem segredo) em `Worker/appsettings.json`; criar `Worker/email.settings.example.json`; adicionar `email.settings.json` ao `.gitignore`.
      Verify: `dotnet build` ok; `git check-ignore src/KitWmsIntegrador.Worker/email.settings.json` confirma ignorado (verificação auxiliar; a verificação principal é o build+testes).
- [ ] 9. Commit: `feat(email): envio de resumo e PDF por SMTP atras de IEnviadorEmail`.
      Verify: `dotnet test c:/wms-integrador/KitWmsIntegrador.sln` — todos verdes.

### FEAT-003 — Motor do pacote, Worker, e2e e documentação

- [ ] 10. Criar `Core/Configuracao/OpcoesPacote.cs`; `Core/Pacote/DetectorArquivoPronto.cs` (D5, usa `IClock`, guarda "visto em / tamanho / mtime" por caminho); `Core/Pacote/DiarioPacote.cs` (JSONL append, leitura no restart, tolera última linha truncada, `Apagar()`).
      Verify: testes: arquivo crescendo não fica pronto; estável + tempo → pronto; travado não fica pronto; timeout; diário round-trip e linha corrompida ignorada.
- [ ] 11. Criar `Core/Pacote/ProcessadorLote.cs`: dado arquivos prontos → parse (`LeitorNotaFiscalXml.ParseArquivo`, erro → Erro/XmlInvalido + linha) → `AgrupadorTransporte.Agrupar` → por onda (semáforo D10) `IOrquestrador.IntegrarAsync` por nota → classificar destino (D1/D2/D7) → `OrganizadorArquivos.Mover` → diário. Notas `SemTransporte` passam pelo Orquestrador com transporte vazio (reaproveita caso B). Captura exceção por arquivo/onda (vai para Erro\Outro). `HashSet` de "enviado, mover pendente" (D7.4).
      Verify: testes com `FakeClienteWms` + Orquestrador real + XMLs sintéticos em temp: sucesso→Processados; Pendente→Processados; produto sempre não cadastrado→Erro\ProdutoNaoCadastrado; XML inválido→Erro\XmlInvalido; "já existe"→Processados; exceção do cliente numa onda não derruba as demais; ordem de chegada respeitada; concorrência 1 vs 2 (contagem máxima simultânea com fake que atrasa).
- [ ] 12. Criar `Core/Pacote/GerenciadorPacote.cs` (máquina D6, `CicloAsync(ct)`, expõe `Estado` e `PacoteAtual`) e `Core/Pacote/FechadorPacote.cs` (monta `LinhaRelatorioErro` com `MapeadorRelatorio`, inclui XMLs inválidos como linhas `NaoIntegrada` — verificar em `RenderizadorHtml/Pdf` como linhas com transporte vazio são rotuladas e, se necessário, usar rótulo "(sem transporte)" apenas na linha; gera HTML+PDF com nome `pacote_<yyyyMMdd_HHmmss>` em `Relatorio:PastaSaida` usando `GerarHtml/GerarPdf` (não `GerarArquivos`, por colisão de nome); chama `PoliticaEnvioEmail` + `IEnviadorEmail`; falha de e-mail só loga).
      Verify: testes com `FakeClock` avançando ciclos: Ocioso→Coletando→Processando→Fechando→Ocioso; debounce (arquivo novo durante quietude reinicia); retomada (diário pré-existente + arquivos restantes → relatório com ambos, sem reenvio dos já movidos); falha de e-mail e e-mail não configurado não impedem fechamento; PDF sempre gerado, inclusive pacote 100% integrado (sem e-mail no padrão).
- [ ] 13. Teste ponta a ponta `PacoteEndToEndTests.cs`: N XMLs (ex.: 6) de **2 transportes** em pasta temporária, `FakeClienteWms` (algumas notas: produto não cadastrado persistente, uma pendência, uma "já existe"), 1 XML inválido, e-mail fake (`IEnviadorEmail` gravador); roda ciclos até Ocioso e confere: pastas (Processados/Erro por categoria), entrada vazia, envios agrupados por transporte (ordem das chamadas I50 contíguas por transporte), 1 relatório HTML+PDF, 1 e-mail com anexo PDF e contagens corretas no corpo, limpeza posterior com relógio +11 dias remove tudo de Processados/Erro.
      Verify: `dotnet test c:/wms-integrador/KitWmsIntegrador.sln --filter PacoteEndToEnd`.
- [ ] 14. Worker: reescrever `Worker/Worker.cs` (loop: limpeza na partida e a cada 24 h; `while(!stoppingToken.IsCancellationRequested){ try{ await gerenciador.CicloAsync } catch(log); await Task.Delay(intervalo) }`; validação de configuração na partida com log claro); `Worker/Program.cs`: bind/registro de `OpcoesPasta` (+PostConfigure do legado `Entrada:PastaEntrada`), `OpcoesPacote`, `OpcoesLimpeza`, `OpcoesEmail`, singletons dos novos serviços, `IEnviadorEmail`, `AddJsonFile("email.settings.json", optional: true, reloadOnChange: true)`; seções `Pastas`, `Pacote`, `Limpeza` em `appsettings.json` (sem segredos). Não alterar construtores existentes nem o registro do Orquestrador.
      Verify: `dotnet build c:/wms-integrador/KitWmsIntegrador.sln` sem avisos; `dotnet test c:/wms-integrador/KitWmsIntegrador.sln` todos verdes; smoke local opcional: rodar o Worker contra uma pasta temporária com `WMS__Usuario` ausente NÃO é necessário — não executar contra WMS real.
- [ ] 15. Documentação: atualizar `docs/ARQUITETURA.md` (componentes, máquina de estados do pacote, idempotência/retomada, §6 fase 3 e §7 decisões fechadas/abertas) e criar `docs/OPERACAO.md` (pastas e organização de Erro, configuração SMTP + `email.settings.json` + env `Email__Senha`, ciclo do pacote e debounce, limpeza de 10 dias, como mudar `OrganizacaoErro`/concorrência, exemplo de appsettings sem segredos, limitações do SmtpClient, suposições). Não tocar em `docs/diario` e `docs/controle`.
      Verify: `dotnet build` ok; revisar que nenhum segredo/credencial real foi gravado (checagem manual do diff).
- [ ] 16. Commits: (a) `feat(pacote): watcher continuo, maquina de estados do pacote e fechamento com relatorio`, (b) `docs: arquitetura e operacao da etapa 7`.
      Verify: `dotnet test c:/wms-integrador/KitWmsIntegrador.sln` — todos verdes; `git status --short` limpo (exceto `.agents/`).

## 4. Plano de testes (resumo)

Sempre em diretório temporário único por teste (limpo no `Dispose`), `FakeClock`, `FakeClienteWms`, `IEnviadorEmail` fake;
sem HTTP, sem SMTP, sem `Task.Delay` real (o ciclo é chamado manualmente). Cobertura mínima: classificação de categoria;
mover (todas as organizações, colisão, travado); limpeza (idade, limites de segurança, config perigosa); detector de
arquivo pronto; diário/retomada; processador (destinos, concorrência, exceção isolada); máquina de estados e debounce;
fechamento (relatório sempre, e-mail por política, falha tolerada); e2e com 2 transportes.

## 5. Opcionais (NÃO obrigatórios; decisão registrada)

- **Checagem de `PreCondicaoOk` de I10/I30 no Orquestrador** (hoje ignora o resultado e reenvia a I50): fora do escopo —
  o requisito pede não alterar decisões do Orquestrador. Recomendação: fazer em etapa própria, com testes dedicados.
- **Estado híbrido `Integrada=true` + `Estado=Pendente`:** mantido; a Etapa 7 usa `Integrada` para decidir destino
  (híbrido → Processados) e o relatório já o apresenta como "Integrada — revisar produto".
- Conferir o PDF em Adobe/Chrome (pendência da Etapa 8) — fora do escopo.

## 6. Riscos

- Texto da resposta do WMS para I50 duplicada não confirmado (D7).
- Chegada tardia de nota num transporte cuja onda saiu de "editando" (D3).
- `SmtpClient` sem TLS implícito/OAuth (D9).
- Nome do relatório por pacote não colidir (`pacote_<yyyyMMdd_HHmmss>`).
- Windows Server 2012 R2: .NET 8 self-contained suportado; TLS 1.2 do SMTP depende do SChannel do SO (validar no servidor).

## 7. Decisões em aberto (precisam do usuário; hoje valem as suposições acima)

1. Confirmar a interpretação de "enviar por transporte" (D3): grupo sequencial por transporte + paralelismo limitado entre
   transportes (padrão 1). Se a intenção for outra (ex.: enviar um transporte só quando todas as suas notas chegarem,
   ou limite por tempo), ajustar `JanelaColetaSegundos`/agrupamento.
2. Confirmar "pasta por categoria de erro" (D1) versus "uma pasta por nota" (basta `Pastas:OrganizacaoErro=PorArquivo`).
3. Quando enviar e-mail: padrão `ComProblema`; confirmar se prefere `Sempre`.
4. Provedor/porta SMTP real (587 STARTTLS? 465? OAuth?) → define se `SmtpClient` basta ou se entra MailKit.
5. Texto real da resposta de duplicata da I50 ("já existe"?) para fechar a idempotência.
6. Valores padrão de quietude (30 s), janela de coleta (10 s) e timeout de arquivo (300 s).
7. Pacote com notas que nunca estabilizam: hoje ficam na entrada e não bloqueiam o fechamento (alerta no log).
