# Revisão hotfix 16/11/2026 (commits 1d787ae, 6ee8e83 sobre 9811a5b)

Veredito: APPROVED. Nenhum bloqueio.

Evidência lida: .agents\tasks\verificacao-hotfix-1611-2.md (build Release 0 avisos/0 erros; testes 971 aprovados, 9 ignorados, 0 falhas = baseline 966 + 5 novos). Build e testes não foram reexecutados.

1. Causa raiz: o coder não reproduziu "BaseUrl errada" e diz isso. Provou com proxy de registro de CONNECT que o exe antigo e o exe real do servidor já iam para friozem.assistsoftware.com.br:443. Corrigiu as divergências reais comprovadas no diff: ConfiguracaoManual.Criar() (reenfileirar-i60, consultar-i51, relatorio-i60), EnvioManualI40/I60 e SmokeTestWms ignoravam appsettings.Production.json; --testar-token lia config\appsettings.Production.json, que o serviço não lê. Host e porta agora impressos eliminam a dúvida. Aceito.
2. Carregador único: ConfiguracaoAplicacao usa Host.CreateApplicationBuilder com ContentRootPath = AppContext.BaseDirectory, ambiente Production por padrão e email.settings.json por último. Program.cs e as 7 CLIs o usam (confirmado no diff). Documentado em INSTALACAO-SERVIDOR, OPERACAO, MANUAL-TECNICO.
3. --testar-token imprime só `(host, porta N)`; sem caminho, usuário, senha ou token. Testes existentes ajustados e mantêm SemSegredos.
4. Testes: ConfiguracaoAplicacaoTests, pasta temporária com appsettings.json (:8888) + Production; produção vence, env vence o arquivo, CLI vence env, email por último, sem Production usa base.
5. configurar-segredos.ps1: AppEnvironmentExtra REG_MULTI_SZ, Merge-EntradasWms preserva demais entradas e troca WMS__* sem diferenciar caixa, ShouldProcess cobre o -WhatIf (mensagem sem senha), sem eco, -SemNssm, chave ausente gera alerta e segue. Lógica de merge verificada por leitura (null de valor inexistente é filtrado).
6. Docs: INSTALACAO-SERVIDOR, OPERACAO §12.1 (rotação: duas fontes, restart, --testar-token), MANUAL-TECNICO .md/.html/.pdf atualizados.
7. Restrições: sem *.xml/*.zip, sem credenciais (grep por senha citada na conversa e por R$/US$ vazio), CONTROLE-ESFORCO, docs/controle e docs/diario ausentes do diff, sem monetários. Sem push (origin/main em 9811a5b).
