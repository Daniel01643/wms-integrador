# Verificação de integração do diagnóstico (FEAT-001 a 004 + adendo 18/11)

Iteração 1.

- `dotnet build KitWmsIntegrador.sln -c Release`: 0 avisos, 0 erros.
- `dotnet test KitWmsIntegrador.sln -c Release`: 1125 aprovados, 0 falhas, 9 ignorados (total 1134). Baseline anterior: 1009 aprovados, 9 ignorados; sem regressão.
- CLI `dotnet run --project src/KitWmsIntegrador.Worker -c Release --no-build -- --diagnostico` (entrada e saída em pasta temporária): exit 0, gerou HTML e PDF em `<saida>\diagnostico`, e-mail não enviado (sem `--enviar`).
- `AlertaAutenticacaoWmsEmail` depende apenas de `FonteDestinatariosDiagnostico` (nenhuma fonte de destinatários de fechamento ou resumo). O alerta de memória (`ServicoAmostraSaude`) usa a mesma fonte.
- Adendo: `appsettings.json` e `appsettings.Production.json` de exemplo têm `System.Net.Http.HttpClient` e `Microsoft.AspNetCore` em Warning, `Limpeza:DiasRetencaoLogs`/`PastaLogs`, `Diagnostico:AlertaMemoriaLivreMb` (500). `ServicoAmostraSaude` está registrado no DI do Worker (`RegistroDiagnostico.cs`, `SaudeProcessoWorker`).
- `git status`: nenhum arquivo proibido (*.xml, *.zip, dados reais, credenciais, docs/CONTROLE-ESFORCO.md, docs/controle/*, docs/diario/*).
- Nenhuma costura quebrada encontrada; sem alterações de código nesta iteração.
