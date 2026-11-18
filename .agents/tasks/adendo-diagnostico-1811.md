# Adendo ao diagnóstico (18/11/2026) — itens extras pedidos pelo usuário

Entram na mesma release 2026.11.18-1 do diagnóstico. Regras do repositório iguais às do brief do workflow
(main direto, sem push, sem deploy, pt-BR, só horas, sem dados reais/segredos).

## Contexto (fatos do servidor em 18/11)
- O SRVAPPALC02 travou duas vezes (tela azul às 10:09 e "System Unresponsive" às 11:41) por memória virtual baixa
  (evento 2004 às 09:56/10:01/10:05/10:10). Maiores consumidores: `node.exe` de outras aplicações (1,07 GB; Next.js
  em `npm run dev` em C:\SERVIDOR\my-app), PowerBI Enterprise Gateway (845 MB). O integrador usa ~155 MB e não
  aparece nos alertas. A "falha de token" 09:48–09:56 foram timeouts (38–84 s) e "thread pool starvation" causados
  pelo servidor sem memória, não 401.
- Investigação de código: `c:\wms-integrador\.agents\tasks\investigacao-memoria.md` (ler seções 2.4, 3.1, 3.5, 6).

## Itens
1. **Log HTTP menor:** `appsettings.json` (Logging:LogLevel) com `System.Net.Http.HttpClient` e `Microsoft.AspNetCore`
   em `Warning` (hoje cada chamada gera 4 linhas Information no worker.out.log). Manter os logs próprios do
   integrador (envio concluído, falhas, ciclo I51). Atualizar o exemplo de produção.
2. **Limpeza dos logs rotacionados do NSSM** (`C:\Friozem\importacao_notas\logs\worker.out-*.log`, 10 MB cada, hoje sem
   limpeza): o `LimpadorPastas` (ou um limpador novo) remove os rotacionados mais antigos que
   `Limpeza:DiasRetencaoLogs` (padrão 30) — nunca o arquivo atual. Caminho configurável (`Limpeza:PastaLogs`, padrão
   `<pasta do app>\..\..\logs` resolvido de forma segura; se não existir, ignora). Também limitar a retenção de
   `Estado\i51\brutos\processados` à mesma retenção já existente (confirmar que já é aplicada; se não, aplicar).
3. **Saúde do processo e do servidor no diagnóstico das 14:00:** amostra periódica (ex.: a cada 15 min, configurável)
   gravada no diário JSONL do diagnóstico: memória do processo (working set e privada), handles, threads, memória
   física livre e total do servidor, uso do arquivo de paginação e **os 5 processos que mais usam memória** (nome e MB,
   sem linha de comando). No diagnóstico: pico e média do integrador, mínimo de memória livre do servidor, e a lista dos
   maiores consumidores no momento do pico. Alerta para a TI (destinatarios-diagnostico) quando a memória livre do
   servidor ficar abaixo de `Diagnostico:AlertaMemoriaLivreMb` (padrão 500) por 2 amostras seguidas, no máximo 1 e-mail
   por hora. Em Windows Server 2012 R2 (sem dependências novas; usar System.Diagnostics.Process e
   GlobalMemoryStatusEx/PerformanceCounter disponíveis no .NET 8 Windows).
4. **E-mail do relatório diário da I51/I60 (12:00) enxuto:** o corpo do e-mail passa a ter SÓ o resumo (contagens por
   situação, saúde da consulta) e os PROBLEMAS (rejeitadas, ENC/IMP, não reconhecidas, atrasadas, ajustes de quantidade).
   As listas do que deu certo (associadas, separadas, visão por pacote completa) saem do corpo e ficam no **PDF anexado**
   (o PDF continua completo). Incluir no corpo uma linha "Detalhes completos no PDF anexo". Aplicar o mesmo critério ao
   corpo do e-mail do diagnóstico (resumo + problemas no corpo, detalhes no PDF anexo). Verificar se o PDF já é anexado;
   se não, anexar. Testes: corpo não contém a lista de associadas; PDF contém.
