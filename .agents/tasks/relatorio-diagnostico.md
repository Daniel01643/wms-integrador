# Relatório: Diagnóstico agente integrador (release 2026.11.18-1)

Status: implementado, testado e empacotado localmente. **Deploy não executado**, nada copiado ao servidor, sem push. Sem credenciais nem dados reais neste arquivo.

## Pedido

Hoje o alerta de falha de token ia para a lista do relatório. O usuário pediu avaliação técnica separada: alerta de token só para `ti@friozem.com.br` e um e-mail diário às 14:00, "Diagnóstico agente integrador", só para a TI, com falhas de token, erros que geraram as chamadas I10 e I30 (e os erros dessas chamadas), notas não associadas e resumo. Depois, o adendo de 18/11 (travamentos do servidor por memória virtual baixa, que causaram os timeouts de token das 09:48 às 09:56).

## O que mudou

1. **Diário de diagnóstico (JSONL)** em `Estado\diagnostico\AAAAMMDD.jsonl`, com instrumentação do GetToken (sucesso, falha por classe, queda e recuperação), I10/I30 (gatilho e chamada), I51 (consultas e ciclos), I60 (desfechos) e reinícios do serviço. Nunca lança e não grava token, senha, cabeçalho nem corpo de nota.
2. **Alerta de autenticação só para a TI**: usa apenas os destinatários de diagnóstico (TXT `Diagnostico:CaminhoDestinatarios` > `Diagnostico:Destinatarios` > `ti@friozem.com.br`).
3. **E-mail diário às 14:00** "[KitWms] Diagnóstico agente integrador" (HTML gravado em `Relatorio:PastaSaida\diagnostico`), com seções: login do WMS (por classe de falha, por servidor, janelas de indisponibilidade), pacotes e notas, I10 e I30 (gatilhos por mensagem e erros das chamadas), I51 e I60 (inclui notas não associadas), devolução I40 e saúde do serviço. CLI `KitWmsIntegrador.Worker.exe --diagnostico [--enviar]`.
4. **Documentação e manuais** (OPERACAO, INSTALACAO-SERVIDOR, ARQUITETURA, Manual Técnico, Manual do Usuário; HTML e PDF regenerados).

### Itens do adendo (FEAT-004)

1. **Log HTTP menor**: `Logging:LogLevel` com `System.Net.Http.HttpClient` e `Microsoft.AspNetCore` em `Warning` (`appsettings.json` e exemplo de produção). Logs próprios mantidos.
2. **Limpeza dos logs rotacionados do NSSM**: `Limpeza:DiasRetencaoLogs` (padrão 30, mínimo 1) e `Limpeza:PastaLogs` (padrão `<app>\..\..\logs`). Apaga `worker.out-*.log` e `worker.err-*.log`, nunca o arquivo atual. Os brutos processados da I51 já eram limpos por `I51:DiasRetencaoEstado` (confirmado e coberto por teste).
3. **Amostra de memória e alerta**: a cada 15 min (`Diagnostico:IntervaloAmostraMinutos`) grava working set e memória privada do processo, handles, threads, memória física livre/total do servidor, memória virtual comprometida e os 5 maiores consumidores (só nome e MB). O diagnóstico mostra pico, média, mínimo de memória livre e consumidores. Alerta só à TI quando a memória livre fica abaixo de `Diagnostico:AlertaMemoriaLivreMb` (500) por 2 amostras seguidas, no máximo 1 e-mail por hora. Sem dependências novas.
4. **E-mails enxutos com PDF anexo**: relatório I51/I60 das 12:00 e diagnóstico passam a ter no corpo só resumo e problemas, com a linha "Detalhes completos no PDF anexo". O diagnóstico ganhou PDF próprio (`diagnostico.pdf`); o PDF da I60 já era anexado e segue completo.

## Arquivos

Commits locais na `main` desde `d8da08b`: `f6c16fb`, `89e6e73`, `9268aac`, `00ca710`, `36d2d8e`, `8ce56fd` (69 arquivos, 6.815 inserções, 71 remoções).

- Código novo (Core/Diagnostico e Worker): diário, instrumentação, montador, coletor, gerador HTML e PDF, agendador, `ColetorAmostraSistema`, `ServicoAmostraSaude`, `SaudeProcessoWorker`, CLI.
- Alterados: `OpcoesLimpeza`, `OpcoesDiagnostico`, `LimpadorPastas`, `CorpoEmailRelatorioI60`, `GeradorRelatorioI60`, `EnviadorRelatorioDiarioI60`, `Program.cs`, `appsettings.json`, `deploy/config-exemplo/appsettings.Production.json`, novo `deploy/config-exemplo/destinatarios-diagnostico.exemplo.txt`.
- Docs: `docs/OPERACAO.md`, `docs/INSTALACAO-SERVIDOR.md`, `docs/ARQUITETURA.md`, `docs/manuais/MANUAL-TECNICO` e `MANUAL-USUARIO` (md, html, pdf).
- Não editados: `docs/CONTROLE-ESFORCO.md`, `docs/controle/*`, `docs/diario/*`.

## Testes e build

| | Aprovados | Ignorados | Falhas |
|---|---|---|---|
| Antes (baseline 2026.11.17-1) | 1009 | 9 | 0 |
| Depois | 1125 | 9 | 0 |

Aumento de 116 testes (74 nas FEAT-001 a 003 e 42 no adendo). `dotnet build KitWmsIntegrador.sln -c Release`: 0 avisos, 0 erros. Revisão (`review-diagnostico.json`): `APPROVED`. A primeira execução da revisão falhou em `ConfiguracaoAplicacaoTests` por variáveis de ambiente `Pastas__Entrada` e `Relatorio__PastaSaida` herdadas da verificação da CLI; sem elas, passou.

## Evidências

- Screenshot do diagnóstico de exemplo: `c:\wms-integrador\.agents\tasks\diagnostico-exemplo.png` (HTML de exemplo: `exemplo-diagnostico.html`).
- CLI `--diagnostico` com pastas temporárias: saída 0, gerou `diagnostico_*.html` e `diagnostico_*.pdf`.
- Testes de PDF provam que o PDF contém as listas (ex.: 80 associadas) e o corpo do e-mail não.
- Pacote: `c:\wms-integrador\artifacts\2026.11.18-1\KitWmsIntegrador-2026.11.18-1-win-x64.zip`, 46.813.604 bytes, SHA-256 `8D27C857FC5B284DECCE7C7E6A942A7B83B000CC211901CA8A85F7FC1FEB4E00`. Manuais: `manuais.zip`, SHA-256 `EE6BCC30256A854FC15BDA64D0BFF949A286E8653458ED95CB372B8ECA438969`. Confirmado no `app\appsettings.json` do pacote: chaves novas e `HttpClient` em Warning.
- Checklist de deploy: `c:\wms-integrador\.agents\tasks\checklist-release-2026-11-18-1.md`.

## Riscos e limites conhecidos

- **Contagem de Warning/Error do log: não coletada.** O diagnóstico mostra "não coletado" nessa linha, pois o `worker.out.log` não é lido.
- **Lotes divididos: não coletado** (sem fonte de dados no diário).
- **"Paginação" = memória virtual comprometida** (RAM + arquivo de paginação, via `GlobalMemoryStatusEx`), não o percentual de `pagefile.sys` (exigiria dependência nova).
- **Alerta de memória** só detecta pressão de memória livre física. Ele avisa a TI, mas não resolve a causa: no servidor, os maiores consumidores eram `node.exe` de outras aplicações e o PowerBI Gateway; o integrador usa cerca de 155 MB.
- **Lista de destinatários**: `Diagnostico:Habilitado` é `true` por padrão. Criar o TXT e definir o caminho antes de publicar (checklist §4); sem eles vale o fallback `ti@friozem.com.br`.
- **Mudança para os destinatários do 12:00**: corpo menor; candidatas à devolução pela I40 e "I60 enviadas aguardando confirmação" saíram do corpo (ficam no PDF).
- **Rollback** para a 2026.11.17-1 remove diagnóstico, amostra e limpeza, e o alerta de token volta à lista anterior.
- **Volume**: diário com cerca de 96 amostras/dia mais um `gettoken` por envio; retenção de 30 dias.
- **Não verificado em produção**: SMTP real, `GlobalMemoryStatusEx` no Windows Server 2012 R2 e entrega do e-mail só serão confirmados após o deploy (checklist §7 e §8).
- Avisos de LF/CRLF do git são conhecidos (working copy em LF, autocrlf).

## Esforço (somente horas)

Horas de IA estimadas no plano (seção 10): FEAT-001 3,0 + FEAT-002 4,5 + FEAT-003 1,5 + revisão e correção 1,5 + pacote, checklist e relatório 0,8 = 11,3 h. A seção 10 do plano não traz linha para o adendo (FEAT-004); estimativa da IA para ele: 3,0 h (código e testes 2,2 h; docs e manuais 0,8 h), a confirmar no fechamento. **Total estimado de IA: 14,3 h.** As horas humanas são informadas pelo usuário; não foram estimadas aqui. Registro mestre em `docs/CONTROLE-ESFORCO.md`, não editado.
