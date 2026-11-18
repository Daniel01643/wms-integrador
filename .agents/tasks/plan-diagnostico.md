# Plano de implementação: Diagnóstico agente integrador (release 2026.11.18-1)

Base: main, integrador em produção 2026.11.17-1. Sem worktree, sem push, sem deploy. Commits locais em pt-BR, `git add` só dos arquivos tocados. Build e testes direto em `c:/wms-integrador`:

- `dotnet build c:/wms-integrador/KitWmsIntegrador.sln -c Release` deve terminar com 0 avisos e 0 erros.
- `dotnet test c:/wms-integrador/KitWmsIntegrador.sln -c Release` parte de 1009 aprovados e 9 ignorados; só pode crescer.

Artefatos FEAT: `c:/wms-integrador/.agents/tasks/task-diagnostico/` (`task.json`, `context.json`, `features/FEAT-001..003.json`). O plano abaixo é a visão linear; os FEATs são a divisão de execução.

## 1. Achados da exploração (código lido)

| Tema | Onde está hoje | Consequência para o plano |
|---|---|---|
| Alerta de queda/recuperação do GetToken | `Core/ClienteWms/AlertaAutenticacaoWmsEmail.cs` usa `FonteDestinatariosFechamento` (Pacote) | Trocar a fonte por uma nova `FonteDestinatariosDiagnostico`; os testes em `AutenticacaoWmsTests.cs` (linhas ~402-412) constroem a classe e precisam ser ajustados |
| GetToken | `Core/ClienteWms/ClienteWmsHttp.cs`: `ObterTokenAsync` (laço de tentativas imediatas), `TentarGetTokenAsync` (usado também por `TestarTokenDetalhadoAsync`), `ObterTokenPorEnvioAsync`/`GarantirTokenAsync` (onde saem `RegistrarFalha`/`RegistrarSucesso`) | Registrar cada tentativa no laço de `ObterTokenAsync` (não dentro de `TentarGetTokenAsync`, para não contar o `--testar-token`); registrar queda/recuperação nos 2 pontos onde hoje se calcula `alertarIndisponivel`/`alertarRecuperacao` |
| Backend do balanceador | `BackendWms.Chave` (nome do cookie de afinidade + hash de 8 hex, ou `Server=`) já sai de `IdentificadorBackendWms.De`; `GuardaAutenticacaoWms.FalhasPorBackend` só guarda contagem em memória desde o início do processo | O diário grava `Backend` por tentativa (sem segredo) e o relatório agrupa por janela de 24 h |
| Pacote interrompido por autenticação | `Core/Pacote/ProcessadorLote.cs` ~linha 483: `catch (AutenticacaoWmsIndisponivelException)` (seta `_abortoAutenticacao`, notas restantes ficam na entrada) | Ponto do evento `auth-pacote-interrompido` com a contagem de notas restantes do lote |
| Recuperação I30/I10 | `Core/Orquestrador/Orquestrador.cs`: `EhEmpresaNaoLocalizada` → I10, `EhProdutoNaoCadastrado` → I30, depois reenvia I50; mensagem do gatilho = `ultimaMensagem` (`ClassificadorRetorno.PrimeiraMensagem`); sucesso da pré-condição = `PreCondicaoOk` (inclui "já existe") | Eventos `cadastro-gatilho` (antes da chamada) e `cadastro-chamada` (depois) |
| I51 | `Core/Separacao/ServicoSeparacaoI51.cs`: `ConsultarAsync` (falha de rede com `HttpRequestException.StatusCode`, "N" fora do padrão, vazio, ok), `CicloAsync` (`ResultadoCicloI51`) | Eventos `i51-consulta` e `i51-ciclo` |
| I60 | `Core/Separacao/ProcessadorI60Separacao.cs`: `ProcessarPendentesAsync` com enum `Desfecho` (Enviada, Associada, Imp, Rejeitada, Comunicacao, Adiada); `AlertaI60.ProcessarAsync` devolve nº de notas alertadas por evento | Eventos `i60-desfecho` e `i60-alerta` |
| Lista de notas não associadas, ajustes, aguardando, atrasadas | `ServicoRelatorioDiarioI60.MontarModelo(agora, fimJanela)` (público) devolve `ModeloRelatorioI60` com `Problemas`, `ListaAtrasadas`, `Ajustes`, `Aguardando`, `Atrasadas`, `LinhaNotaI60` (Numero, Serie, Transporte, Onda, Motivo, Mensagem) | Reutilizar; não recalcular |
| Pacotes e notas | `RegistradorTelemetria.LerPacotes()` + `LerEventosServico()` + `AgregadorTelemetria.Agregar(pacotes, eventos, inicio, fim)` → `ResumoPeriodo` (notas, integradas, revisar produto, pendentes, não integradas, `ErrosPorCategoria`, `TempoMedioPorNotaMs`, `TempoWmsMedioPorNotaMs`, `InicializacoesNoPeriodo`) | Reutilizar. Arquivos ignorados: `GerenciadorPacote.cs` ~linha 291 (`ProcessarNaoXml` devolve a lista); não há contador em telemetria, então vira evento `arquivos-ignorados`. "Lotes divididos" não é detectável hoje: a seção mostra "não coletado" |
| Devolução | `Core/Devolucao/AuditoriaDevolucao.cs`: `<Estado>\devolucao\devolucao_yyyyMMdd.log`, linhas `data;ip;usuario;chave;nota;status;transacao;mensagem` (linhas antigas têm 7 campos); sucesso = `StatusChaveDevolucao.Enviada` | Leitor novo só de leitura; o relatório mostra contagens por usuário (sem IP, sem chave) |
| Limpeza | `Worker.cs` chama `_limpador.Limpar()` a cada 24 h (`ResultadoLimpeza`: arquivos, pastas, erros) | Evento `limpeza` gravado ali |
| Agendador e marca | `AgendadorRelatorioDiario.DeveEnviar(agora, ultimoSlot, hora)` (estático, não olha dia da semana: serve para fim de semana); `MarcaUltimoRelatorioI60` (arquivo `ultimo-relatorio-i60.txt` em `<telemetria>`, escrita atômica); `ServicoRelatorioDiarioI60.VerificarEEnviarAsync` marca o slot só depois de gravar/enviar | Reutilizar o agendador; criar `MarcaUltimoDiagnostico` no mesmo molde (as 3 marcas existentes são cópias do mesmo molde) |
| Destinatários | `LeitorListaEmails.Ler` (txt, `#` comenta, relido a cada chamada); `FonteDestinatariosResumo` (txt → padrão `ti@friozem.com.br` com aviso) | `FonteDestinatariosDiagnostico`: txt → `Diagnostico:Destinatarios` (default `ti@friozem.com.br`) → constante `ti@friozem.com.br` |
| Renderização | `Core/Email/CorpoEmailRelatorioI60.cs` + `IdentidadeVisual` (cabeçalho/logo "Relatório de Integração"), `Core/Relatorio/GeradorRelatorioI60.cs` (`ModoLogoRelatorio.Cid` para e-mail, `DataUri` para disco) | `CorpoEmailDiagnostico` e `GeradorDiagnostico` no mesmo molde. PDF não entra: cada relatório tem renderizador PdfSharp próprio (~20 KB), não reutilizável |
| CLI | `Worker/Program.cs` despacha `--relatorio-i60` etc. antes do host; `RelatorioManualI60.cs` monta um `ServiceCollection` mínimo com `ConfiguracaoManual` e `AdicionarSeparacaoI51()` | `DiagnosticoManual` copia esse molde |
| Agendador no host | `Worker.cs` chama `ExecutarRelatorioDiarioAsync` dentro do laço de 5 s do pacote | O diagnóstico roda em `BackgroundService` próprio (`DiagnosticoWorker`), para a coleta de tamanho de pastas não atrasar o ciclo do pacote |
| Versão | `AssemblyInformationalVersion` (já usada em `EventoServico` no `Worker.cs`) | Mesma leitura no coletor |
| Config de produção | `deploy/config-exemplo/appsettings.Production.json` aponta `*:CaminhoDestinatarios` para `C:\Friozem\importacao_notas\config\...` | Acrescentar `Diagnostico` ali e um `destinatarios-diagnostico.exemplo.txt` em `deploy/config-exemplo/` |

## 2. Decisões de projeto

1. **Diário em JSONL por dia**: `Estado\diagnostico\yyyyMMdd.jsonl` (data local). Um arquivo novo por dia mantém a leitura de uma janela de 24 h em no máximo 2 arquivos e a retenção vira apagar arquivos antigos. Alternativa descartada: um único arquivo (leitura cresce sem limite).
2. **`IRegistroDiagnostico` opcional nos construtores** (parâmetro final com default `null`, tratado como `RegistroDiagnosticoNulo`). Mantém compatível todo `new X(...)` existente nos testes e nas CLIs. Construtores afetados: `ClienteWmsHttp`, `Orquestrador`, `ProcessadorLote`, `GerenciadorPacote`, `ServicoSeparacaoI51`, `ProcessadorI60Separacao`, `AlertaI60`.
3. **Queda de autenticação registrada em `ClienteWmsHttp`**, não na guarda: só ali se conhece o status HTTP. A `GuardaAutenticacaoWms` não muda.
4. **Janela do relatório**: agendado = `[slot-24h, slot]` (igual ao relatório I60, sem buracos entre relatórios); CLI = `[agora-24h, agora]`. Estado atual (notas, disco) é sempre o de agora.
5. **Sem PDF** nesta versão (decisão acima). O HTML vai a `Relatorio:PastaSaida\diagnostico\diagnostico_yyyyMMdd_HHmm.html` e no corpo do e-mail.
6. **Contagem de Warning/Error do log não entra**: o log não tem timestamp e não há telemetria dele. A seção de saúde mostra "não coletado" nesse item. Contar por `ILoggerProvider` fica como possível evolução.
7. **Alerta de autenticação** continua respeitando `Wms:AlertaAutenticacao` e `Email:Habilitado`, e independe de `Diagnostico:Habilitado` (este liga só o relatório diário). Destinatários: `FonteDestinatariosDiagnostico`.
8. **Alertas I60 imediatos** não mudam (`IDestinatariosAlertaI60` continua `FonteDestinatariosRelatorioI60`).

## 3. Contratos

### 3.1 `IRegistroDiagnostico` (Core/Diagnostico)

```csharp
public interface IRegistroDiagnostico { void Registrar(EventoDiagnostico evento); } // nunca lança
public sealed class RegistroDiagnosticoNulo : IRegistroDiagnostico { public static readonly RegistroDiagnosticoNulo Instancia = new(); ... }
public sealed record EventoDiagnostico
{
    public string Tipo { get; init; } = "";          // ver tabela
    public DateTimeOffset QuandoLocal { get; init; } // preenchido pelo registrador (IClock) se default
    public string? Subtipo { get; init; }
    public int? Status { get; init; }                // HTTP
    public bool? Ok { get; init; }
    public string? Backend { get; init; }            // BackendWms.Chave
    public string? Mensagem { get; init; }           // sempre sanitizada e truncada
    public string? Nota { get; init; }
    public string? Serie { get; init; }
    public int? Quantidade { get; init; }
    public int? Quantidade2 { get; init; }
    public long? DuracaoMs { get; init; }
    public int? Segundos { get; init; }
}
```

`RegistroDiagnosticoArquivo` (IClock + `IOptions<OpcoesPasta>` + `IOptions<OpcoesLimpeza>` + logger): pasta `<CaminhoEstado>\diagnostico`; `Registrar` serializa com `JsonSerializerDefaults.Web` e `DefaultIgnoreCondition = WhenWritingNull`, usa `lock`, `FileMode.Append`, `FileShare.ReadWrite`, `try/catch (Exception)` que só loga (uma vez por minuto no máximo, para não inundar o log). Também expõe `IReadOnlyList<EventoDiagnostico> Ler(DateTimeOffset inicio, DateTimeOffset fim)` (abre só os arquivos dos dias que tocam a janela, ignora linha inválida) e `int Limpar(int diasRetencao)` (apaga `yyyyMMdd.jsonl` mais antigos que `Limpeza:DiasRetencao`).

`SanitizadorDiagnostico.Texto(string?, int max = 200)`: remove caracteres de controle, mascara `Bearer \S+`, `Basic \S+`, `Authorization...`, sequências tipo JWT (`eyJ[\w-]+\.[\w-]+\.[\w-]+`) e `(senha|password|token)\s*[=:]\s*\S+`, depois trunca com reticências. `RegistroDiagnosticoArquivo` aplica o sanitizador em `Mensagem` e `Backend` antes de gravar (ponto único de defesa da regra D).

### 3.2 Tipos de evento (uma linha JSON cada)

| `tipo` | Quem grava | Campos usados |
|---|---|---|
| `gettoken` | `ClienteWmsHttp.ObterTokenAsync`, por tentativa | `ok`, `status` (null se sem resposta), `backend`, `duracaoMs`, `quantidade` (nº da tentativa), `mensagem` (`timeout`, `rede`, `sem-token` ou null) |
| `auth-queda` | `ClienteWmsHttp`, ao chamar `_guarda.RegistrarFalha` | `subtipo` (`inicio` = primeira falha do episódio, `escalada`), `status`, `segundos` (cooldown aplicado), `quantidade` (falhas consecutivas) |
| `auth-recuperada` | `ClienteWmsHttp`, quando `RegistrarSucesso` devolve true | (só `tipo` e hora) |
| `auth-pacote-interrompido` | `ProcessadorLote` no `catch` de autenticação | `quantidade` (notas do lote ainda não processadas, contando a atual), `status`, `subtipo` (`cooldown` ou `falha`) |
| `cadastro-gatilho` | `Orquestrador`, antes de chamar I10/I30 | `subtipo` (`I10`/`I30`), `mensagem` (mensagem da I50 que disparou), `nota` |
| `cadastro-chamada` | `Orquestrador`, depois da chamada | `subtipo` (`I10`/`I30`), `ok` (`PreCondicaoOk`), `mensagem` (`ja-existe` quando OK por "já existe"; texto do WMS quando erro; nome do tipo da exceção quando lançou, exceto falha de autenticação, que não é erro do cadastro), `nota`, `duracaoMs` |
| `i51-consulta` | `ServicoSeparacaoI51.ConsultarAsync` | `subtipo` (`TODOS`/`UNICO`), `ok`, `status` (HTTP quando `HttpRequestException.StatusCode`), `mensagem` (`vazio`, `fora-do-padrao` ou tipo da exceção) |
| `i51-ciclo` | `ServicoSeparacaoI51.CicloAsync`, só se houve separadas ou atrasadas | `quantidade` (novas separadas), `quantidade2` (novas atrasadas) |
| `i60-desfecho` | `ProcessadorI60Separacao.ProcessarPendentesAsync` | `subtipo` (`enviada`, `associada`, `imp`, `rejeitada`, `comunicacao`), `nota`, `serie` |
| `i60-alerta` | `AlertaI60.ProcessarAsync`, um por evento (`Rejeitada`, `ENC/IMP`, `Pendente`, `NaoReconhecida`, `Atrasada`) | `subtipo` (evento), `quantidade`, `ok` (e-mail enviado) |
| `limpeza` | `Worker.cs`, após `_limpador.Limpar()` | `quantidade` (arquivos), `quantidade2` (pastas), `ok` (sem erros) |
| `arquivos-ignorados` | `GerenciadorPacote` (~linha 291), se a lista for não vazia | `quantidade` |

Exemplo de linha (dados fictícios):
`{"tipo":"gettoken","quandoLocal":"2026-11-18T09:48:12-03:00","ok":false,"status":401,"backend":"BIGipServerWMS#a1b2c3d4","duracaoMs":412,"quantidade":2}`

Início do serviço e reinícios: reutilizar `servico.jsonl` (`EventoServico`, já gravado em `Worker.cs`); nada novo.

## 4. Configuração

`OpcoesDiagnostico` (seção `Diagnostico`, `Core/Configuracao/OpcoesDiagnostico.cs`): `Habilitado=true`; `HoraDoDia="14:00"` (string `HH:mm`) e `Horario` (TimeOnly, 14:00 se inválida); `AssuntoPrefixo="[KitWms]"`; `CaminhoDestinatarios=""` (vazio = `<telemetria>\destinatarios-diagnostico.txt`); `Destinatarios="ti@friozem.com.br"` (separados por `;`, usado quando o txt está ausente ou vazio); `MaxLinhasPorSecao=100` (10 a 1000); `MaxGruposMensagem=10`; `Validar()` no molde de `OpcoesRelatorioI60.Validar`.

Arquivos de configuração: `src/KitWmsIntegrador.Worker/appsettings.json` (seção `Diagnostico` com `_comentario`) e `deploy/config-exemplo/appsettings.Production.json` (`CaminhoDestinatarios = C:\Friozem\importacao_notas\config\destinatarios-diagnostico.txt`), mais `deploy/config-exemplo/destinatarios-diagnostico.exemplo.txt` (um `ti@friozem.com.br` e comentário `#`).

## 5. Agendamento

`ServicoDiagnostico` (Core/Monitoramento) no molde de `ServicoRelatorioDiarioI60`:

- `VerificarEEnviarAsync(ct)`: sai se `!Diagnostico:Habilitado`; `agora = clock.UtcNow.ToLocalTime()`; `AgendadorRelatorioDiario.DeveEnviar(agora, marca.Ler(), opcoes.Horario)`; se devido, monta a janela `[slot-24h, slot]`, gera, grava HTML, envia e-mail (se `Email:Habilitado` e SMTP configurado) e só então `marca.Marcar(slot)`. Falha de gravação em disco não marca (tenta no próximo ciclo); falha de e-mail só loga (igual ao relatório I60). Todo o corpo em `try/catch` que não derruba o host.
- `GerarAgoraAsync(enviar, ct)` para a CLI (janela `[agora-24h, agora]`, não toca a marca).
- `DiagnosticoWorker : BackgroundService`: atraso inicial de 30 s, depois verifica a cada 60 s. Não faz nada se `Diagnostico:Habilitado=false`; valida a hora na partida e só loga aviso se inválida.
- Fim de semana: nenhuma regra de dia da semana (o agendador não tem). Teste explícito com sábado e domingo.
- Restart às 15:00 com marca do slot de hoje: não reenvia. Servidor desligado às 14:00 e ligado às 16:00: um único envio do slot de 14:00. Vários dias parado: um único envio (comportamento do agendador).

## 6. Conteúdo do relatório

Modelo `ModeloDiagnostico` (records imutáveis) e montador puro `MontadorDiagnostico.Montar(EntradaDiagnostico)`; coleta de I/O em `ColetorDiagnostico` (injeta `RegistroDiagnosticoArquivo`, `RegistradorTelemetria`, `ServicoRelatorioDiarioI60`, `LeitorAuditoriaDevolucao`, `IProvedorSaudeSistema`, `IClock`, opções). `IProvedorSaudeSistema` (espaço livre de um drive, tamanho de uma pasta, com limite de tempo e tolerância a erro) permite fake nos testes.

1. **Login do WMS**: total de `gettoken`, sucessos, falhas por classe (401, 403, 5xx, timeout, rede/outros), tabela por backend (tentativas, falhas, % de falha, ordenada por falhas), janelas de indisponibilidade (de `auth-queda subtipo=inicio` até o próximo `auth-recuperada`, com duração; sem recuperação até o fim da janela = "em andamento"; recuperação sem início na janela = "início antes da janela"), nº de ativações de cooldown (`auth-queda`), pacotes interrompidos e notas deixadas aguardando (soma de `quantidade`).
2. **Pacotes e notas**: `ResumoPeriodo` (pacotes, notas, integradas, revisar produto, pendentes, não integradas, XML inválido, `ErrosPorCategoria`, tempo médio por nota, tempo WMS por nota) + arquivos ignorados (soma de `arquivos-ignorados`) + "lotes divididos: não coletado".
3. **I10 e I30**: (a) gatilhos por interface, agrupados por `mensagem` (top `MaxGruposMensagem` + "outros"), com total de notas distintas; (b) chamadas por interface: total, sucesso (separando "já existia"), erros e principais mensagens de erro.
4. **I51 e I60**: consultas I51 (total, ok, vazias, falhas por status HTTP/tipo), novas separadas e atrasadas (soma de `i51-ciclo`), I60 por desfecho (`i60-desfecho`), ajustes de quantidade, aguardando separação e atrasadas (do `ModeloRelatorioI60`), alertas I60 imediatos (só contagem por evento) e a lista de notas não associadas (`Problemas` + `ListaAtrasadas`: nota, série, transporte, onda, motivo, mensagem truncada em 200), com teto `MaxLinhasPorSecao` e "N não exibidas". Com `I51:Habilitada=false`, a seção mostra "I51 desligada".
5. **Devolução (I40)**: envios no período, sucesso e falha, por usuário (arquivos `devolucao_yyyyMMdd.log` dos dias da janela, filtrando pela coluna de data). `Devolucao:Habilitada=false` e nenhum arquivo: "sem envios".
6. **Saúde do serviço**: versão, hora do último início e reinícios no período (`servico.jsonl`), espaço livre da entrada e do drive da aplicação, resultado da limpeza (último evento `limpeza` da janela), tamanhos de Processados, Erro e Estado, e a linha "Warning/Error do log: não coletado".

Visual: `CorpoEmailDiagnostico.Montar(modelo, logo, ModoLogoRelatorio)` copia o esqueleto de `CorpoEmailRelatorioI60` (cabeçalho azul com logo e `IdentidadeVisual.Titulo`, subtítulo "Diagnóstico agente integrador", bloco de período, cartões e tabelas). Assunto: `{AssuntoPrefixo} Diagnóstico agente integrador — dd/MM/yyyy` (data do fim da janela). Destinatários: só `FonteDestinatariosDiagnostico.Ler()`.

## 7. Regra D (segredos) na prática

- Todo texto livre passa por `SanitizadorDiagnostico` antes de ir ao JSONL e de novo no montador antes de ir ao HTML.
- Nenhum evento carrega corpo de nota, JSON de resposta, cabeçalho, usuário ou senha. `Backend` é a chave já existente (hash curto). `Mensagem` do WMS é truncada em 200.
- O relatório de devolução não mostra IP nem chave de acesso, só nota e contagens.
- Teste dedicado semeia mensagens com `Bearer abc.def.ghi`, `Basic dXNlcjpwYXNz`, `senha=123` e um JWT, e confirma que nenhuma aparece no JSONL, no HTML nem no e-mail.

## 8. Itens de execução (ordem de dependência)

- [ ] 1. **Núcleo do diário** (FEAT-001). Criar `Core/Diagnostico/{IRegistroDiagnostico,EventoDiagnostico,RegistroDiagnosticoNulo,SanitizadorDiagnostico,RegistroDiagnosticoArquivo}.cs`, `Core/Configuracao/OpcoesDiagnostico.cs`, `Core/Monitoramento/FonteDestinatariosDiagnostico.cs`. Registrar em `Program.cs` (`OpcoesDiagnostico`, `RegistroDiagnosticoArquivo` e `IRegistroDiagnostico`, `FonteDestinatariosDiagnostico`).
  Verificação: `dotnet build c:/wms-integrador/KitWmsIntegrador.sln -c Release` sem avisos; testes novos (item 3) passam.
- [ ] 2. **Instrumentação e roteamento do alerta** (FEAT-001). Aplicar a tabela 3.2 (parâmetro opcional final `IRegistroDiagnostico? diagnostico = null` nos 7 construtores; `ResultadoGetToken` ganha campo opcional `Motivo` preenchido nos dois `catch` de `TentarGetTokenAsync`). Trocar `FonteDestinatariosFechamento` por `FonteDestinatariosDiagnostico` em `AlertaAutenticacaoWmsEmail` e atualizar `AutenticacaoWmsTests.cs`. Em `Worker.cs`: gravar `limpeza` e chamar `RegistroDiagnosticoArquivo.Limpar(Limpeza:DiasRetencao)` junto da limpeza de 24 h (parâmetro opcional no construtor do `Worker`, no molde de `relatorioDiario`).
  Verificação: `dotnet test c:/wms-integrador/KitWmsIntegrador.sln -c Release` com os 1009 testes antigos verdes mais os novos.
- [ ] 3. **Testes do núcleo** (FEAT-001), arquivos `tests/KitWmsIntegrador.Tests/Diagnostico*Tests.cs`, `FakeClock` de `Infra/`:
  - `DiagnosticoRegistroTests`: grava e lê por janela (2 dias), linha truncada ignorada, pasta somente leitura/caminho inválido não lança, retenção apaga só arquivos antigos, sanitização.
  - `FonteDestinatariosDiagnosticoTests` (molde de `FonteDestinatariosResumoTests`): txt vence `Diagnostico:Destinatarios`; txt ausente/vazio usa a opção; opção vazia cai em `ti@friozem.com.br`; edição do txt a quente.
  - `AlertaAutenticacaoDestinatariosTests`: queda e recuperação vão só aos destinatários de diagnóstico (e-mail falso, `DestinatariosOverride` igual à lista de diagnóstico, nunca a de fechamento); `Wms:AlertaAutenticacao=false` e `Email:Habilitado=false` seguem só logando.
  - `DiagnosticoInstrumentacaoTests`: `ClienteWmsHttp` com `FakeHttpMessageHandler` (401 com cookie de afinidade, depois 200) grava `gettoken` com backend, `auth-queda inicio`, `auth-recuperada`; `Orquestrador` com `FakeClienteWms` grava `cadastro-gatilho` e `cadastro-chamada` para I10 e I30 (sucesso, já existe, erro); `ProcessadorLote` grava `auth-pacote-interrompido`; `ServicoSeparacaoI51` (usar `Infra/AmbienteI51.cs` e `FakeWmsI51`) grava `i51-consulta` e `i60-desfecho`.
  Verificação: `dotnet test c:/wms-integrador/KitWmsIntegrador.sln -c Release --filter "FullyQualifiedName~Diagnostico|FullyQualifiedName~AutenticacaoWms"`.
- [ ] 4. **Modelo, coleta e montagem** (FEAT-002). Criar `Core/Diagnostico/{ModeloDiagnostico,EntradaDiagnostico,MontadorDiagnostico,ColetorDiagnostico,LeitorAuditoriaDevolucao,IProvedorSaudeSistema}.cs` (+ `ProvedorSaudeSistema` com `DriveInfo` e enumeração de pasta com teto de tempo de 20 s por pasta).
  Verificação: `DiagnosticoMontadorTests` com diário semeado: falhas de token por backend, janela de queda fechada e em andamento, I10/I30 disparados por mensagem e erros próprios, lista de I60 não associadas, devolução por usuário (arquivo semeado com linhas de 7 e 8 campos), saúde com `IProvedorSaudeSistema` falso. `dotnet test ... --filter "FullyQualifiedName~DiagnosticoMontador"`.
- [ ] 5. **Renderização** (FEAT-002). Criar `Core/Email/CorpoEmailDiagnostico.cs`, `Core/Relatorio/GeradorDiagnostico.cs` (`IGeradorDiagnostico`), `Core/Monitoramento/EnviadorDiagnostico.cs` (grava HTML em `Relatorio:PastaSaida\diagnostico`, envia por `IEnviadorEmail` com `DestinatariosOverride`, `ImagemEmbutida` por CID).
  Verificação: `DiagnosticoRenderTests` (cabeçalho "Relatório de Integração", todas as 6 seções, seção vazia "sem ocorrências", HTML-encode de mensagens, sem segredos semeados) e `dotnet build` sem avisos.
- [ ] 6. **Agendador, host e CLI** (FEAT-002). Criar `Core/Monitoramento/{MarcaUltimoDiagnostico,ServicoDiagnostico}.cs`, `Worker/{DiagnosticoWorker,DiagnosticoManual,RegistroDiagnostico}.cs`; em `Program.cs` despachar `--diagnostico [--enviar]` junto das outras CLIs (antes do host), registrar `AdicionarDiagnostico()` e `AddHostedService<DiagnosticoWorker>()`; acrescentar `Diagnostico` ao `appsettings.json`.
  Verificação: `DiagnosticoAgendadorTests` (relógio e e-mail falsos): slot das 14:00 enviado uma vez; segundo ciclo não reenvia; reinício (novo `ServicoDiagnostico` sobre a mesma pasta) não reenvia; slot perdido (relógio às 16:00 sem marca) envia uma vez; sábado e domingo enviam; `Diagnostico:Habilitado=false` não envia; `Email:Habilitado=false` grava HTML e marca sem enviar; falha de e-mail não impede a marca; assunto com prefixo e data; destinatários só os de diagnóstico. Rodar também `dotnet run --project c:/wms-integrador/src/KitWmsIntegrador.Worker -c Release -- --diagnostico` apontando `Pastas__Entrada` para uma pasta de teste e confirmar saída 0 e HTML gerado.
- [ ] 7. **Exemplo fictício e screenshot** (FEAT-002). Criar `tests/KitWmsIntegrador.Tests/ExemplosDiagnostico.cs` (molde de `ExemplosRelatorioI60.cs`) com dados inventados (notas e transportes fictícios, backends fictícios) que grava `c:/wms-integrador/.agents/tasks/exemplo-diagnostico.html`; capturar com Edge headless: `& "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" --headless --disable-gpu --hide-scrollbars --window-size=900,2600 --screenshot=c:\wms-integrador\.agents\tasks\diagnostico-exemplo.png file:///c:/wms-integrador/.agents/tasks/exemplo-diagnostico.html`. Abrir o PNG e conferir as 6 seções, a identidade visual e a ausência de texto cortado. Não commitar HTML/PNG de `.agents/tasks`.
- [ ] 8. **Documentação** (FEAT-003). `docs/OPERACAO.md`: nova seção "Diagnóstico agente integrador" (e-mail diário, janela, destinatários, precedência, `Diagnostico:*`, CLI `--diagnostico [--enviar]`, onde fica o diário e a retenção, tabela de roteamento de alertas: auth → TI; I60 imediato, relatório I60, fechamento, resumo semanal → seus destinatários atuais; TI recebe só o resumo dos alertas I60). `docs/INSTALACAO-SERVIDOR.md`: criar `config\destinatarios-diagnostico.txt` com `ti@friozem.com.br` e apontar `Diagnostico:CaminhoDestinatarios` (árvore da linha ~63 e passos das linhas ~248 e ~294). `docs/ARQUITETURA.md`: componentes novos e fluxo do diário. `docs/manuais/MANUAL-TECNICO.md`: seção do diagnóstico. `docs/manuais/MANUAL-USUARIO.md`: nota curta de que alertas técnicos vão para a TI. Regenerar com `powershell -ExecutionPolicy Bypass -File c:\wms-integrador\docs\manuais\gerar-manuais.ps1` e gerar os PDFs com Edge headless (`--headless --disable-gpu --no-pdf-header-footer --print-to-pdf=<pdf> file:///<html>`), seguindo `docs/manuais/README.md`.
  Verificação: HTML e PDF dos manuais regenerados e abrindo; o texto novo aparece no HTML; não editar `docs/CONTROLE-ESFORCO.md`, `docs/controle/*`, `docs/diario/*`.
- [ ] 9. **Convergência** (loop de revisão). Passada completa de build/testes/aviso zero e correção dos achados do revisor; gate `c:/wms-integrador/.agents/tasks/review-diagnostico.json` com `verdict == APPROVED`.
- [ ] 10. **Pacote 2026.11.18-1** (passo final). Seguir o molde de `.agents/tasks/checklist-release-2026-11-17-1.md` e `deploy/publicar-release.ps1`: gerar o pacote local (sem copiar para o servidor), SHA-256, `checklist-release-2026-11-18-1.md` (com passo de criar `config\destinatarios-diagnostico.txt` no servidor antes de ligar, e a observação de que o alerta de token passa a ir só para a TI) e `relatorio-diagnostico.md` (o que mudou, testes, evidências, riscos).

## 9. Riscos e cuidados

- **Volume do diário**: com `TokenPorEnvio=true` há um `gettoken` por envio (milhares de linhas/dia, ~150 bytes cada). Aceitável com retenção de 30 dias; o relatório lê só 2 arquivos.
- **Caminho quente do GetToken**: o `Registrar` faz append síncrono sob `lock`. O registrador nunca lança e não faz I/O além do append.
- **Ciclo de interrupção sem registro**: se o disco de Estado estiver cheio, o diário perde eventos e o relatório mostra números menores; a seção de saúde mostra espaço livre, que explica o caso.
- **Janela entre dias**: evento gravado às 23:59:59 com `QuandoLocal` do dia anterior cai no arquivo correto porque o nome do arquivo vem da data local do evento.
- **DST/offset**: `QuandoLocal` usa o offset do servidor, como a telemetria.
- **Compatibilidade de testes**: todos os parâmetros novos de construtor têm default; qualquer teste que quebre por isso indica parâmetro mal posicionado.
- **Smart App Control**: build e testes sempre em `c:/wms-integrador`, nunca em `%TEMP%`.
- **PDF** fica fora; se o usuário quiser, é um renderizador PdfSharp novo (estimativa adicional de 1,5 a 2 h de IA).

## 10. Estimativa de esforço (somente horas)

| Etapa | Horas de IA (estimadas) |
|---|---|
| FEAT-001 núcleo do diário, instrumentação, roteamento do alerta e testes | 3,0 |
| FEAT-002 modelo, coleta, HTML, agendador, host, CLI, exemplo e screenshot | 4,5 |
| FEAT-003 documentação e manuais (HTML/PDF) | 1,5 |
| Loop de revisão e correção (até 3 iterações) | 1,5 |
| Pacote 2026.11.18-1, checklist e relatório | 0,8 |
| **Total** | **11,3 h de IA** |

Horas humanas: a informar pelo usuário (não estimadas aqui). Registro mestre em `docs/CONTROLE-ESFORCO.md`, que este trabalho não edita.
