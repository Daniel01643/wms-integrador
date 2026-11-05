# Plano de Implementação — Monitoramento / Telemetria + Resumo Semanal

Projeto: KitWmsIntegrador (C#/.NET 8). Solução: `c:\wms-integrador\KitWmsIntegrador.sln`.
Branch `main`, **sem worktree**. Commits pequenos por feature, apenas arquivos nomeados (nunca `git add .`).

Comandos reais do projeto (verificados nesta etapa, base verde: build OK, 264 testes OK / 9 skip):
- Build: `dotnet build c:\wms-integrador\KitWmsIntegrador.sln`
- Testes: `dotnet test c:\wms-integrador\KitWmsIntegrador.sln`

> Esta tarefa foi **decomposta em FEATs** (ver `c:\wms-integrador\.agents\tasks\task-monitoramento\`).
> Este arquivo é o plano legível e o fallback do loop de implementação; os FEATs são a fonte
> para o loop coder→reviewer. A ordem e o conteúdo abaixo espelham os FEATs.

---

## Decisões de design (grounded no código lido)

### (a) Campos de telemetria — coletáveis vs. fora, e por quê

Fonte no fechamento: `GerenciadorPacote.FecharAsync` já tem `registros` (lista de `RegistroDiario`
consolidada), `inicio` (`_inicio`, horário de abertura), `fim` (`_clock.UtcNow`), `_idPacote`
(`pacote_<yyyyMMdd_HHmmss>`) e `_periodo` (`PeriodoColeta`). `RegistroDiario` carrega por nota:
`Status` (`StatusLinhaRelatorio`), `Integrada`, `Tentativas`, `XmlInvalido`, `Categoria`
(`CategoriaErro?`), `NumeroTransporte`, `ProdutosProvisorios`, `Historico`.

**Coletáveis sem tocar no Orquestrador/construtores:**
- `timestamp_abertura` e `timestamp_fechamento` — de `inicio`/`fim` (gravados em horário LOCAL via ISO 8601 com offset).
- `id_pacote` — de `_idPacote`.
- `total_notas` — `registros.Count`.
- Contagens por status — reusar `ModeloRelatorio.Montar(linhas).Resumo`: `Integradas`,
  `IntegradasRevisarProduto`, `Pendentes`, `NaoIntegradas`; `xml_invalido` = `registros.Count(r => r.XmlInvalido)`.
- `transportes` — `Resumo.Transportes` (transportes distintos).
- `tempo_total_ms` — `(fim - inicio)`; `tempo_medio_por_nota_ms` = total / max(1,total_notas).
- `distribuicao_tentativas` — mapa {1,2,3+} a partir de `registros.Select(r => r.Tentativas)`.
- `erros_por_categoria` — `registros.Where(r=>r.Categoria!=null).GroupBy(r=>r.Categoria)` → mapa `CategoriaErro`→contagem.

**Fora (documentado), por serem inobserváveis sem alterar o Orquestrador (proibido):**
- **Chamadas ao WMS por interface (I50/I10/I30)** — `ResultadoIntegracaoNota` NÃO expõe contagem de
  chamadas por interface; o Orquestrador só registra isso como texto livre em `Historico`
  (ex.: `"I10 (empresa): ..."`, `"I30: ..."`). Decisão: derivar **best-effort** por heurística de
  prefixo do `Historico` (contar linhas que começam com `I50`/`I10`/`I30`), gravando em
  `chamadas_wms` com um flag `chamadas_wms_aproximado=true`. Nunca falha se o formato mudar:
  prefixos desconhecidos são ignorados. Isso é observacional e aditivo; não altera o Orquestrador.
  I60 permanece desligada e não é contada.

### (b) Formato JSONL e caminho padrão

Uma linha JSON por pacote (append), análogo ao `DiarioPacote` (FileStream Append + flush, lock, tolerante
a linha truncada na leitura). Arquivo: `telemetria.jsonl`.

Caminho padrão configurável: `OpcoesMonitoramento.CaminhoTelemetria`, default **relativo à pasta de
Estado** — `Path.Combine(OpcoesPasta.CaminhoEstado, "telemetria")` — nunca dentro de Processados/Erro
(a limpeza de 10 dias não alcança o Estado; `OpcoesPasta.Validar` já protege o Estado). Estrutura:
```
<Estado>\telemetria\
  telemetria.jsonl        # 1 linha por pacote
  servico.jsonl           # eventos "servico iniciado" (saúde)
  ultimo-resumo.txt       # marca do último slot de resumo enviado (idempotência)
  destinatarios.txt       # lista de e-mails do resumo (1 por linha; fora do git; recarregada a cada envio)
  resumos\                # fallback em disco (HTML + PDF) quando SMTP desabilitado
```
Schema de uma linha (`RegistroTelemetria`, serializado com `JsonStringEnumConverter`):
```json
{
  "idPacote": "pacote_20260110_090000",
  "aberturaLocal": "2026-01-10T09:00:00-03:00",
  "fechamentoLocal": "2026-01-10T09:03:20-03:00",
  "totalNotas": 12,
  "integradas": 10,
  "integradasRevisarProduto": 1,
  "pendentes": 1,
  "naoIntegradas": 0,
  "xmlInvalido": 0,
  "transportes": 3,
  "tempoTotalMs": 200000,
  "tempoMedioPorNotaMs": 16666,
  "tentativas": { "1": 9, "2": 2, "3": 1 },
  "chamadasWms": { "I50": 12, "I10": 2, "I30": 1 },
  "chamadasWmsAproximado": true,
  "errosPorCategoria": { "ProdutoNaoCadastrado": 0 }
}
```

### (c) Modelo de agregação do período

`AgregadorTelemetria.Agregar(IEnumerable<RegistroTelemetria> registros, DateTimeOffset inicioPeriodo,
DateTimeOffset fimPeriodo, IReadOnlyList<EventoServico> eventos)` → `ResumoPeriodo` (record imutável):
- Filtra pacotes cujo `fechamentoLocal ∈ [inicio, fim)`.
- `totalPacotes`, `totalNotas`, `mediaNotasPorPacote`.
- Somatório por status + **percentuais** (sobre total de notas).
- `diasComAtividade` (dias distintos), `distribuicaoPorDiaSemana` (seg..dom), `distribuicaoPorFaixaHora`
  — **FREQUÊNCIA de uso por faixa Manhã / Tarde / Noite** (três faixas, enum `FaixaHorario`):
  **Manhã 06:00–11:59, Tarde 12:00–17:59, Noite 18:00–05:59** (horário LOCAL do `fechamentoLocal`).
  Faixas **fixas e documentadas** (não configuráveis nesta entrega — mantém escopo enxuto). O resumo
  mostra contagem de **pacotes e notas** por faixa manhã/tarde/noite **e** por dia da semana.
- `tempoMedioPorNotaMs`, `tempoMedioPorPacoteMs` (PERFORMANCE).
- `totalChamadasWms` e por interface (com flag de aproximação propagado).
- `errosPorCategoria` agregados.
- Saúde: `servicoDesde` (menor timestamp em `servico.jsonl`, ou "desconhecido"),
  `inicializacoesNoPeriodo` (eventos no período) — proxy de reinícios/quedas.
- Flag `semAtividade` quando `totalPacotes == 0`.

### (d) Agendador interno ao BackgroundService

`AgendadorResumoSemanal` (classe pura, testável com `IClock`): método
`DeveEnviar(DateTimeOffset agora, DateTimeOffset? ultimoEnviado) → (bool deve, DateTimeOffset slot)`.
- Slot-alvo: o instante do `DiaDaSemana`/`Hora` configurados **na semana corrente** (horário LOCAL).
- Idempotência: só envia se `agora >= slot` **e** o slot ainda não consta como enviado
  (comparar com `ultimoEnviado`, a marca persistida em `ultimo-resumo.txt`).
- **Slot perdido** (serviço caiu atravessando o horário): ao voltar, se `agora >= slot` e o slot não foi
  enviado, **envia no próximo ciclo** (comportamento recuperador). Decisão e comportamento DOCUMENTADOS
  em OPERACAO.md.
- Persistência: `MarcaUltimoResumo` grava/lê `ultimo-resumo.txt` (ISO 8601 do slot enviado), sobrevive a
  reinício; leitura tolerante (arquivo ausente/corrompido = "nunca enviado").
- Integração: o `Worker` chama o serviço de resumo **no mesmo laço** do ciclo de pacotes (a cada
  `IntervaloVarreduraSegundos`), sem bloquear nem atrapalhar o ciclo de pacotes; exceção do resumo é
  apenas logada (igual à limpeza). Não usa Windows Task Scheduler.

### (e) Nova seção de configuração e Opções

`OpcoesMonitoramento` (seção `Monitoramento`), em `src\KitWmsIntegrador.Core\Configuracao\`:
- `Habilitado` (bool, default `true`).
- `DiaDaSemana` (`DayOfWeek`, default `Monday`).
- `Hora` (string `"HH:mm"` default `"12:00"`; usar `TimeOnly` parse tolerante).
- `PeriodoDias` (int, default `7`).
- `AssuntoPrefixo` (string, default `"[KitWms]"`).
- `CaminhoTelemetria` (string, default vazio → resolve ao lado do Estado).
- `CaminhoDestinatarios` (string, default vazio → resolve para `<telemetria>\destinatarios.txt`) — **o
  caminho do arquivo**, NÃO a lista em si.

> **AJUSTE DE REQUISITO (destinatários fora de config estática):** a lista de destinatários do resumo
> **NÃO** fica no `appsettings` nem em bloco de configuração estático — e-mails mudam com frequência e
> precisam de inclusão/exclusão fácil, sem rebuild e sem reiniciar o serviço. No `Monitoramento` do
> appsettings fica **apenas** `Habilitado`, `DiaDaSemana`, `Hora`, `PeriodoDias`, `AssuntoPrefixo`,
> `CaminhoTelemetria` e `CaminhoDestinatarios` (o **caminho** do arquivo de lista). Não há propriedade
> `Destinatarios` nas opções.

**Fonte dos destinatários = arquivo de lista simples dedicado** (`FonteDestinatariosResumo`):
- Um e-mail por linha; linhas em branco e iniciadas por `#` são ignoradas (trim por linha).
- **Fora do git**: padrão `<telemetria>\destinatarios.txt` (pasta já fora do versionamento, pois fica ao
  lado do Estado/dados). Adicionar `destinatarios-resumo.txt` e/ou o caminho ao `.gitignore` como reforço.
- **Recarregado a cada envio** (lê o arquivo no momento de montar/enviar o resumo; sem cache longo) →
  efeito no **próximo envio**, sem reiniciar o serviço.
- Se o arquivo **não existir ou estiver vazio**, cair para o padrão mínimo `ti@friozem.com.br` e **logar aviso**.
- Leitura tolerante a I/O (arquivo travado/ilegível = usa o padrão mínimo + aviso, nunca lança).

As **credenciais SMTP (Host/Porta/Usuario/Senha/TLS/Remetente)** são **reusadas do bloco `Email`** — não
duplicar. Override por env (`Monitoramento__...`). Registrar `AddOptions<OpcoesMonitoramento>().Bind(...)`
e `FonteDestinatariosResumo` no `Program.cs`.

**Subcomando de gerência da lista: DESCARTADO** (confirmação do usuário). O **arquivo txt é o único
mecanismo** — editar o `destinatarios.txt` (incluir/excluir linhas) já é suficiente, com efeito no próximo
envio. Não implementar `Worker.exe resumo-destinatarios ...`. Apenas documentar a edição do txt em OPERACAO.md.

### (f) Corpo HTML + PDF reaproveitando Relatorio/Email

- **HTML**: novo `CorpoEmailResumoSemanal.Montar(ResumoPeriodo, periodoLabel, byte[]? logoPng)` em
  `src\...\Email\`, no **mesmo estilo** de `CorpoEmailResumo` (tabelas + CSS inline p/ Outlook, cabeçalho
  Friozem azul, cartões por status, logo por CID `logo-friozem`, assinatura do sistema com
  `sac@friozem.com.br`). Reusar `IdentidadeVisual` (cores) e `CorpoEmailResumo.CidLogo`.
- **PDF**: novo `RenderizadorPdfResumo` em `src\...\Relatorio\` reusando PdfSharpCore, A4 **paisagem**,
  `IdentidadeVisual` (barra azul/azul-claro/verde, logo, rodapé "Página X de Y"), seguindo a estrutura de
  `RenderizadorPdf` (passada 1 mede, passada 2 desenha). Exposto por um `GeradorResumoSemanal` (interface
  `IGeradorResumoSemanal`) com `GerarHtml`/`GerarPdf`, análogo a `GeradorRelatorio`. Caso **sem atividade**:
  HTML e PDF dizem "nenhuma atividade no período".

### (g) Envio reaproveitando IEnviadorEmail + fallback disco

Problema: `EnviadorEmailSmtp` só envia para `OpcoesEmail.ListaDestinatarios`. Decisão (aditiva, não quebra
o pacote): acrescentar a `MensagemEmail` um campo **opcional** `DestinatariosOverride`
(`IReadOnlyList<string>?`, default `null`); `EnviadorEmailSmtp.MontarMensagem` usa o override quando
presente, senão mantém `OpcoesEmail.ListaDestinatarios`. Teste existente continua válido (default null).
Os destinatários do override vêm de `FonteDestinatariosResumo.Ler()` (arquivo txt, recarregado a cada
envio; padrão mínimo `ti@friozem.com.br` + aviso quando ausente/vazio — ver (e)).
`EstaConfigurado` para o resumo: o `EnviadorResumoSemanal` valida SMTP (Host/Remetente do bloco Email) +
ao menos um destinatário lido do arquivo; se SMTP desabilitado/incompleto, **não falha**: grava HTML+PDF
em `<telemetria>\resumos\resumo_<yyyyMMdd_HHmm>.{html,pdf}` e loga (espelha o fallback do fechamento).

### (h) Lista completa de testes a escrever (xUnit, em `tests\KitWmsIntegrador.Tests\`)

1. **Telemetria por pacote** (`TelemetriaRegistradorTests`): mapeia um conjunto de `RegistroDiario`/`fim`/`inicio`
   para `RegistroTelemetria` com contagens/status/tentativas/categorias corretas; chamadasWms heurística a
   partir de `Historico` com `chamadasWmsAproximado=true`; append em JSONL cria o arquivo e tolera linha final truncada.
2. **Agregação** (`AgregadorTelemetriaTests`): soma por status + % corretos; média notas/pacote;
   dias com atividade, distribuição por dia da semana e faixa de horário; tempos médios; chamadas WMS agregadas;
   filtro por janela `[inicio,fim)`.
3. **Caso sem atividade** (`AgregadorTelemetriaTests`): período vazio → `semAtividade=true` e um resumo não vazio.
4. **Agendador idempotente / slot perdido** (`AgendadorResumoSemanalTests`, com `FakeClock`): antes do slot não
   envia; no/após o slot envia uma vez; após marcar enviado, não reenvia no mesmo slot; slot perdido (relógio já
   passou e marca ausente) → envia no próximo ciclo; próxima semana reabre novo slot.
5. **Marca persistida** (`MarcaUltimoResumoTests`): grava e relê; arquivo ausente/corrompido = nunca enviado.
6. **Envio via fake** (`EnviadorResumoSemanalTests` com `IEnviadorEmail` fake): quando SMTP "configurado", chama
   `EnviarAsync` com HTML não vazio, PDF anexo, `ImagemInline` CID e **DestinatariosOverride = lista lida do arquivo**.
7. **Override de destinatários** (`EnviadorEmailSmtpTests` novo caso): `MontarMensagem` usa o override quando presente
   e mantém `OpcoesEmail` quando ausente (preserva o teste atual).
8. **Fallback disco** (`EnviadorResumoSemanalTests`): SMTP desabilitado → não lança, grava `resumo_*.html` e `.pdf`
   em `<telemetria>\resumos\` e loga.
9. **Eventos de serviço** (`SaudeServicoTests`): registra "servico iniciado" com timestamp + versão do assembly;
   agregação conta inicializações no período e reporta `servicoDesde`.
10. **Fonte de destinatários por arquivo** (`FonteDestinatariosResumoTests`): lê um e-mail por linha, ignora linhas
    em branco e começadas por `#`, faz trim; arquivo ausente/vazio → padrão mínimo `ti@friozem.com.br` + aviso;
    recarrega a cada chamada (altera o arquivo entre duas leituras e vê a mudança).
11. **Distribuição Manhã/Tarde/Noite** (`AgregadorTelemetriaTests`): pacotes em horários de borda (05:59, 06:00,
    11:59, 12:00, 17:59, 18:00) caem na faixa correta; contagem de pacotes e notas por faixa e por dia da semana.

### (i) Evidências / docs a produzir

- `docs\exemplos\resumo-semanal-exemplo.html` — exemplo gerado do corpo (com dados fictícios).
- `docs\exemplos\resumo-semanal-exemplo.pdf` — exemplo do PDF anexo (gerável via teste/helper, se viável no ambiente).
- Seção **"Monitoramento / Resumo semanal"** em `docs\OPERACAO.md`: o que é coletado, onde fica o JSONL,
  o agendamento fixo (segunda 12:00), idempotência e **slot perdido**, a seção `Monitoramento` do appsettings,
  reuso do SMTP, o fallback em disco, as **faixas Manhã/Tarde/Noite** (06–12 / 12–18 / 18–06), e um passo-a-passo de
  **como incluir/excluir um destinatário** (editar o `destinatarios.txt`: um e-mail por linha, `#` comenta; efeito no
  próximo envio, sem reiniciar). Documentar o subcomando `Worker.exe resumo-destinatarios ...` se implementado.
- Criar `docs\exemplos\destinatarios-resumo.exemplo.txt` como modelo do arquivo de lista (fora do git o real).
- Atualizar `appsettings.json` com a seção `Monitoramento` (com `_comentario`, incluindo `CaminhoDestinatarios`,
  **sem** lista de e-mails). Reforçar o `.gitignore` para o arquivo real de destinatários.

---

## Itens ordenados (fallback caso o loop não use os FEATs)

- [ ] 1. **Opções + MensagemEmail override + DI.** Criar `OpcoesMonitoramento` (seção `Monitoramento`, com
      `CaminhoDestinatarios` — o caminho do txt, **sem** lista de e-mails), adicionar `DestinatariosOverride`
      opcional a `MensagemEmail` e usá-lo em `EnviadorEmailSmtp.MontarMensagem`; registrar as opções no `Program.cs`;
      acrescentar a seção `Monitoramento` ao `appsettings.json`.
      Arquivos: `src\KitWmsIntegrador.Core\Configuracao\OpcoesMonitoramento.cs`,
      `src\KitWmsIntegrador.Core\Email\IEnviadorEmail.cs`, `src\KitWmsIntegrador.Core\Email\EnviadorEmailSmtp.cs`,
      `src\KitWmsIntegrador.Worker\Program.cs`, `src\KitWmsIntegrador.Worker\appsettings.json`.
      Verify: `dotnet build c:\wms-integrador\KitWmsIntegrador.sln` compila; `dotnet test` mantém verde incluindo o novo caso de override.

- [ ] 2. **Coleta de telemetria (JSONL) + saúde.** Criar `RegistroTelemetria`, `EventoServico`,
      `RegistradorTelemetria` (append JSONL atômico, leitura tolerante, caminho resolvido ao lado do Estado) e o
      mapeamento a partir de `RegistroDiario`+`inicio`+`fim` (reusando `ModeloRelatorio.Montar`). Enganchar no
      fechamento **sem alterar construtores do Orquestrador**: injetar o registrador no `FechadorPacote` (ou no
      `GerenciadorPacote`) e registrar após o relatório; gravar `servico.jsonl` na partida do `Worker` (timestamp + versão).
      Arquivos: `src\KitWmsIntegrador.Core\Monitoramento\RegistroTelemetria.cs`, `...\EventoServico.cs`,
      `...\RegistradorTelemetria.cs`, `src\KitWmsIntegrador.Core\Pacote\FechadorPacote.cs` (aditivo),
      `src\KitWmsIntegrador.Worker\Program.cs`, `src\KitWmsIntegrador.Worker\Worker.cs`.
      Verify: `dotnet test` — novos testes de telemetria por pacote/JSONL/saúde passam; suíte existente verde.

- [ ] 3. **Agregação do período.** Criar `ResumoPeriodo` e `AgregadorTelemetria` (status+%, frequência por dia/faixa,
      performance, chamadas WMS, erros por categoria, saúde, caso sem atividade).
      Arquivos: `src\KitWmsIntegrador.Core\Monitoramento\ResumoPeriodo.cs`, `...\AgregadorTelemetria.cs`.
      Verify: `dotnet test` — `AgregadorTelemetriaTests` (com e sem atividade) passam.

- [ ] 4. **Render HTML + PDF do resumo.** Criar `CorpoEmailResumoSemanal` (HTML estilo Friozem/Outlook, CID) e
      `RenderizadorPdfResumo` + `GeradorResumoSemanal`/`IGeradorResumoSemanal` (PdfSharpCore, paisagem, identidade).
      Arquivos: `src\KitWmsIntegrador.Core\Email\CorpoEmailResumoSemanal.cs`,
      `src\KitWmsIntegrador.Core\Relatorio\RenderizadorPdfResumo.cs`, `...\GeradorResumoSemanal.cs`, `...\IGeradorResumoSemanal.cs`.
      Verify: `dotnet test` — testes de render (HTML não vazio, PDF com bytes/assinatura `%PDF`) passam.

- [ ] 5. **Agendador + marca + destinatários + envio/fallback.** Criar `AgendadorResumoSemanal` (idempotência/slot
      perdido com `IClock`), `MarcaUltimoResumo` (persistência `ultimo-resumo.txt`), `FonteDestinatariosResumo`
      (lê o txt a cada envio; ignora `#`/vazias; padrão mínimo `ti@friozem.com.br` + aviso), `EnviadorResumoSemanal`
      (usa `IEnviadorEmail` com `DestinatariosOverride` vindos da fonte, fallback para `<telemetria>\resumos\`), e
      `ServicoResumoSemanal` que orquestra ler→agregar→renderizar→enviar; integrar no `Worker` no mesmo laço (sem
      atrapalhar o pacote). Registrar no `Program.cs`. (Sem subcomando — só o txt.)
      Reusar `FakeClock.Avancar` (já existe em `tests\...\Infra\FakeClock.cs`).
      Arquivos: `src\KitWmsIntegrador.Core\Monitoramento\AgendadorResumoSemanal.cs`, `...\MarcaUltimoResumo.cs`,
      `...\FonteDestinatariosResumo.cs`, `...\EnviadorResumoSemanal.cs`, `...\ServicoResumoSemanal.cs`,
      `src\KitWmsIntegrador.Worker\Worker.cs`, `src\KitWmsIntegrador.Worker\Program.cs`.
      (Subcomando `resumo-destinatarios` DESCARTADO por decisão do usuário — só o txt.)
      Verify: `dotnet test` — agendador idempotente/slot perdido, fonte de destinatários por arquivo, envio via fake
      (HTML+PDF+CID+override) e fallback disco passam.

- [ ] 6. **Docs + exemplos.** Gerar `docs\exemplos\resumo-semanal-exemplo.html` (+ `.pdf` se viável),
      `docs\exemplos\destinatarios-resumo.exemplo.txt`, a seção "Monitoramento / Resumo semanal" em
      `docs\OPERACAO.md` (incluindo faixas Manhã/Tarde/Noite e o passo-a-passo de incluir/excluir destinatário no txt);
      completar `appsettings.json` (seção `Monitoramento` com `CaminhoDestinatarios`, sem lista) e reforçar `.gitignore`.
      Arquivos: `docs\exemplos\resumo-semanal-exemplo.html`, `docs\exemplos\resumo-semanal-exemplo.pdf`,
      `docs\exemplos\destinatarios-resumo.exemplo.txt`, `docs\OPERACAO.md`,
      `src\KitWmsIntegrador.Worker\appsettings.json`, `.gitignore`.
      Verify: `dotnet build` compila e `dotnet test` verde; o HTML de exemplo abre e reflete o layout.

## NÃO ALTERAR
Decisões/construtores do Orquestrador (Etapa 6), cliente WMS, flag I60 (permanece DESLIGADA). Não tocar em
`docs/diario` nem `docs/controle`. Telemetria e resumo são **aditivos**. Nenhum segredo em arquivo versionado.
Sem dependências NuGet novas (reusar PdfSharpCore e System.Net.Mail).
