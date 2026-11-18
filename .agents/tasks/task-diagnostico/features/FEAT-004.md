# FEAT-004: adendo ao diagnóstico de 18/11/2026

Status: concluída. Commits locais na main: `00ca710` (código, testes e configuração) e `36d2d8e` (documentação e manuais). Sem push, sem deploy, sem acesso ao servidor.

Verificação (todos em `c:\wms-integrador`):
- `dotnet build c:\wms-integrador\KitWmsIntegrador.sln -c Release`: 0 avisos, 0 erros.
- `dotnet test c:\wms-integrador\KitWmsIntegrador.sln -c Release`: **1125 aprovados, 9 ignorados, 0 falhas** (baseline 1083 + **42 novos**).
- `dotnet src\KitWmsIntegrador.Worker\bin\Release\net8.0\KitWmsIntegrador.Worker.dll --diagnostico` com `Pastas__Entrada` e `Relatorio__PastaSaida` temporários: saída 0, gerou `diagnostico_*.html` e `diagnostico_*.pdf`.
- `powershell -ExecutionPolicy Bypass -File docs\manuais\gerar-manuais.ps1` e Edge headless `--headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf`: HTML e PDF de MANUAL-USUARIO e MANUAL-TECNICO regenerados; texto novo conferido nos HTML.

## Chaves de configuração novas

| Chave | Padrão | Onde | Efeito |
|---|---|---|---|
| `Logging:LogLevel:System.Net.Http.HttpClient` | `Warning` | `appsettings.json` e `deploy/config-exemplo/appsettings.Production.json` | Remove as 4 linhas Information por chamada ao WMS |
| `Logging:LogLevel:Microsoft.AspNetCore` | `Warning` | idem | Remove o log de cada acesso à tela 8085 |
| `Limpeza:DiasRetencaoLogs` | `30` (mínimo 1) | idem | Retenção dos logs rotacionados do NSSM |
| `Limpeza:PastaLogs` | vazio (= `<app>\..\..\logs`); exemplo de produção: `C:\Friozem\importacao_notas\logs` | idem | Pasta dos logs |
| `Diagnostico:IntervaloAmostraMinutos` | `15` (0 desliga; 1 a 1440) | idem | Intervalo da amostra de memória |
| `Diagnostico:AlertaMemoriaLivreMb` | `500` (0 desliga) | idem | Limite do alerta de memória física livre (2 amostras seguidas, no máximo 1 e-mail por hora) |

Variáveis de ambiente equivalentes: `Logging__LogLevel__System.Net.Http.HttpClient`, `Limpeza__DiasRetencaoLogs`, `Limpeza__PastaLogs`, `Diagnostico__IntervaloAmostraMinutos`, `Diagnostico__AlertaMemoriaLivreMb`.

## O que foi feito, por item

### 1. Log HTTP em Warning
`src/KitWmsIntegrador.Worker/appsettings.json` e `deploy/config-exemplo/appsettings.Production.json` ganharam `Logging:LogLevel` com os dois filtros; `Default` e `Microsoft.Hosting.Lifetime` seguem em Information (logs próprios mantidos). Teste: `ConfiguracaoLogHttpTests` (lê os dois JSON; também confere as chaves novas de Limpeza e Diagnostico).

### 2. Limpeza dos logs rotacionados do NSSM
- `OpcoesLimpeza`: `DiasRetencaoLogs`, `PastaLogs`.
- `LimpadorPastas.LimparLogsRotacionados` (privado) + `ResolverPastaLogs`: padrões `worker.out-*.log` e `worker.err-*.log` (o arquivo atual `worker.out.log`/`worker.err.log` não casa com o padrão, pois exige o hífen). Idade por `LastWriteTimeUtc`; pasta inexistente é ignorada; raiz de drive recusada; reparse points não seguidos; `DiasRetencaoLogs < 1` não apaga e vira erro em `ResultadoLimpeza.Erros`. Roda antes da validação das pastas do fluxo (independente de `Pastas`), conta em `ArquivosRemovidos`, e `Recusar` passou a repassar essa contagem.
- **Brutos processados da I51:** confirmado que já eram limpos: `ServicoSeparacaoI51.Limpar()` (a cada 24 h, dentro do ciclo) chama `ArmazenamentoBrutoI51.Limpar(I51:DiasRetencaoEstado)` (30). Nenhuma mudança de código; coberto por teste de ciclo (`LimpezaLogsRotacionadosTests.BrutosProcessadosDaI51_...`: 29 dias permanece, 32 dias é apagado).
- Testes: `LimpezaLogsRotacionadosTests` (11).

### 3. Amostra de memória, seção no diagnóstico e alerta
Classes novas (namespace `KitWmsIntegrador.Core.Diagnostico`, salvo indicação):
- `ColetorAmostraSistema` / `IColetorAmostraSistema` / `AmostraSistema` / `ConsumidorMemoria`: `System.Diagnostics.Process` (processo atual e os 5 maiores por working set, somados por nome, só o nome) + `GlobalMemoryStatusEx` (kernel32; memória física livre/total e commit). Nunca lança.
- `ServicoAmostraSaude`: grava o evento `saude-amostra`; avalia o alerta (contador de amostras seguidas abaixo do limite; no máximo 1 e-mail por hora; ao reiniciar, lê o último `saude-alerta` da última hora no diário); grava `saude-alerta` (ok = e-mail enviado). E-mail só para `FonteDestinatariosDiagnostico`, respeitando `Email:Habilitado` e SMTP; texto sem segredos.
- `EventoDiagnostico` ganhou campos opcionais: `ProcessoWsMb`, `ProcessoPrivadoMb`, `Handles`, `Threads`, `LivreMb`, `TotalMb`, `PaginacaoUsadaMb`, `PaginacaoTotalMb`, `Consumidores` (lista de `ConsumidorMemoria`).
- `SecaoMemoria` (novo record em `ModeloDiagnostico.cs`) e `ModeloDiagnostico.Memoria` (último parâmetro, opcional). `MontadorDiagnostico.Memoria` agrega: amostras, pico/média do working set, pico/média da privada, pico de handles e threads, mínimo de memória livre (com hora e total), pico de commit, consumidores no pico do integrador e no mínimo de memória livre, alertas (total e enviados). `CorpoEmailDiagnostico` e `RenderizadorPdfDiagnostico` mostram a seção 7.
- `Worker/SaudeProcessoWorker` (BackgroundService; atraso inicial 15 s; intervalo `IntervaloAmostraMinutos`); registrado em `Program.cs`; `RegistroDiagnostico.AdicionarDiagnostico()` registra `IColetorAmostraSistema` e `ServicoAmostraSaude`.
- `OpcoesDiagnostico`: `IntervaloAmostraMinutos`, `AlertaMemoriaLivreMb` e validação.
- Testes: `AmostraSaudeTests` (16).

### 4. E-mails enxutos e PDF anexo
- `CorpoEmailRelatorioI60.Montar(..., bool enxuto = false)`: com `enxuto` sai só Atenção, Saúde da consulta, cartões (contagens por situação), I60 com problema, Ajustes de quantidade e Atrasadas, mais a linha `Detalhes completos no PDF anexo` (constante `LinhaDetalhesNoPdf`). `IGeradorRelatorioI60.GerarHtml(..., enxuto = false)`; `EnviadorRelatorioDiarioI60` usa `enxuto: true` só para o corpo do e-mail. O PDF (já era anexado, `relatorio-i60.pdf`) e o HTML/PDF em disco continuam completos.
- Diagnóstico: antes **não tinha PDF** (decisão do plano). Agora: `RenderizadorPdfDiagnostico` (novo, A4 paisagem, mesma identidade do PDF da I60, todas as seções e tabelas), `IGeradorDiagnostico.GerarPdf`, `EnviadorDiagnostico` grava `diagnostico_*.pdf` ao lado do HTML e anexa `diagnostico.pdf` (`EnviadorDiagnostico.NomeAnexoPdf`). `CorpoEmailDiagnostico.Montar(..., enxuto)`: corpo com cartões de resumo, problemas (falhas por classe, quedas, erros da I10/I30, falhas da I51, alertas da I60, notas não associadas, atrasadas), memória e a linha do PDF; tabelas de detalhe (por servidor, gatilhos I10/I30 por mensagem, I60 por desfecho, devolução por usuário, saúde) só no PDF e no HTML em disco.
- Teste de PDF: novo helper `tests/KitWmsIntegrador.Tests/Infra/PdfTexto.cs` extrai o texto do PDF (inflate + `/ToUnicode` por fonte), então os testes provam que o PDF **contém** as listas (ex.: 80 associadas `ASSOC001..080`) e o corpo do e-mail **não**. `CorpoEmailEnxutoTests` (11).

## Nomes reais de classes tocadas

Novas: `ColetorAmostraSistema`, `IColetorAmostraSistema`, `AmostraSistema`, `ConsumidorMemoria`, `ServicoAmostraSaude`, `SecaoMemoria`, `RenderizadorPdfDiagnostico` (internal), `SaudeProcessoWorker`; testes `LimpezaLogsRotacionadosTests`, `ConfiguracaoLogHttpTests`, `AmostraSaudeTests` (inclui `FakeColetorAmostra`), `CorpoEmailEnxutoTests`, `Infra/PdfTexto`.
Alteradas: `OpcoesLimpeza`, `OpcoesDiagnostico`, `LimpadorPastas`, `EventoDiagnostico`, `ModeloDiagnostico`, `MontadorDiagnostico`, `CorpoEmailDiagnostico`, `CorpoEmailRelatorioI60`, `GeradorDiagnostico`/`IGeradorDiagnostico`, `GeradorRelatorioI60`/`IGeradorRelatorioI60`, `EnviadorDiagnostico`, `EnviadorRelatorioDiarioI60`, `RegistroDiagnostico`, `Program.cs`, `DiagnosticoManual` (só comentário); teste existente `DiagnosticoAgendadorTests.Email_AssuntoComPrefixoEData_...` (agora espera 1 anexo PDF em vez de nenhum).

Docs: `docs/OPERACAO.md` (§13 e novo §13.3, §10, §3 Limpeza), `docs/INSTALACAO-SERVIDOR.md` (§3 chaves da release e §7 tabela), `docs/ARQUITETURA.md` (§2.4), `docs/manuais/MANUAL-TECNICO.md` (§3, §8, §8.5, §8.8, tabela 12), `docs/manuais/MANUAL-USUARIO.md` (e-mail das 12:00), mais HTML/PDF dos dois manuais regenerados.

## Desvios e pontos para o revisor

1. **"Uso do arquivo de paginação" = memória virtual comprometida (commit)**, obtida de `GlobalMemoryStatusEx` (`ullTotalPageFile - ullAvailPageFile`, limite = RAM + paginação). O percentual real de `pagefile.sys` exigiria `PerformanceCounter` (pacote `System.Diagnostics.PerformanceCounter`, fora do framework) ou WMI (`System.Management`), ambos dependências novas. O commit é o número que o evento 2004 acompanha. Documentado em OPERACAO §13.3.
2. **O diagnóstico agora gera e anexa PDF** (o plano original dizia "sem PDF"). Pedido do item 4 ("detalhes no PDF anexo"). Novo renderizador próprio, no mesmo molde do da I60 (código de paginação duplicado, como já ocorre entre os renderizadores existentes).
3. **Também são apagados `worker.err-*.log`** (além de `worker.out-*.log`): o NSSM rotaciona os dois arquivos e o `instalar.ps1` configura ambos. Retenção e pasta são as mesmas.
4. **Candidatas à devolução pela I40** e "I60 enviadas aguardando confirmação" saíram do corpo do e-mail das 12:00 (ficam no PDF e no HTML em disco). Não estavam na lista de PROBLEMAS do adendo; as notas problemáticas continuam no corpo via "I60 com problema".
5. **Consumidores por nome:** processos com o mesmo nome (vários `node.exe`) são somados (working set), para refletir o consumo da aplicação. Só nome e MB; nome passa pelo `SanitizadorDiagnostico`.
6. **Pico do integrador = maior working set**; mostra-se também a lista no instante do mínimo de memória livre quando é outra amostra (o adendo fala "no momento do pico"; a segunda lista cobre o caso de a pressão do servidor não coincidir com o pico do integrador).
7. **Alerta "no máximo 1 por hora":** o limite sobrevive a reinício (lê `saude-alerta` do diário). Com e-mail desligado/sem destinatário, o alerta só é registrado no diário e no log (também conta para a janela de 1 hora).
8. `DiasRetencaoLogs < 1` é recusado (não apaga nada, erro em `ResultadoLimpeza.Erros`), enquanto `DiasRetencao` aceita 0. Decisão de segurança para não apagar todos os rotacionados por engano.
9. `appsettings.json` e o exemplo de produção receberam `_comentario_logs`/`_comentario_memoria`; edição preservou os finais de linha existentes. Avisos de LF/CRLF do git são os mesmos de FEAT-003 (working copy em LF, repositório com autocrlf).
10. Regra D: nenhum evento novo carrega token, senha, cabeçalho ou corpo de nota; o e-mail de alerta traz só números e nomes de processo; sanitização aplicada aos nomes.
11. Esforço: não registrado aqui (somente horas, a cargo do fechamento; horas humanas informadas pelo usuário). Nenhum arquivo de `docs/CONTROLE-ESFORCO.md`, `docs/controle/*` ou `docs/diario/*` foi tocado.

## Para o passo de pacote (release 2026.11.18-1)

- Ao subir, o serviço passa a gravar `saude-amostra` a cada 15 min em `Estado\diagnostico\AAAAMMDD.jsonl` (~96 linhas/dia, ~1 KB cada) e a apagar `worker.out-*.log`/`worker.err-*.log` com mais de 30 dias de `C:\Friozem\importacao_notas\logs` (o exemplo de produção já define `Limpeza:PastaLogs`). Conferir que o `config\appsettings.Production.json` do servidor não sobrescreve `Logging:LogLevel` com valores mais detalhados.
- O primeiro Diagnóstico (14:00) chega com PDF anexo e corpo enxuto; a seção 7 só terá dados depois das primeiras amostras.
- Os e-mails das 12:00 (I60) ficam mais curtos; avisar os destinatários de que a lista completa está no PDF anexo.
