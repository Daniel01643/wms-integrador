# Verificacao hotfix 16/11/2026 (--testar-token / CLIs / configurar-segredos)

## Causa raiz (o que foi confirmado e o que NAO foi)
- NAO reproduzi "CLI usa a BaseUrl de homolog". O codigo antigo de `--testar-token` ja lia appsettings.json + appsettings.Production.json
  ao lado do exe. Prova (sem enviar credencial real): rodei o exe com WMS__Usuario/Senha falsos e HTTPS_PROXY apontando para um
  listener local que so registra o CONNECT:
  - build local antigo + appsettings.Production.json copiado do servidor (sem segredos): `CONNECT friozem.assistsoftware.com.br:443`.
  - exe REAL em C:\Friozem\importacao_notas\app\current no servidor (so leitura, processo efemero, env so no processo, WorkingDirectory=%TEMP%,
    sem Wms__BaseUrl): `CONNECT friozem.assistsoftware.com.br:443`. Ou seja, a URL efetiva ja era a de producao.
  - Conferencia no servidor: env de maquina, env de processo e AppEnvironmentExtra (NSSM) com mesmo usuario BRF e senha de 16 caracteres (iguais).
  Hipotese mais provavel para o 401 consistente anterior: credenciais obsoletas na sessao WinRM/shell usada no teste (sessao herda o ambiente ao abrir),
  nao a URL; nao ha como provar retroativamente. O host agora impresso pela CLI tira a duvida na proxima vez.
- Divergencias REAIS corrigidas (causa do incidente de 13/11 com --reenfileirar-i60 gravando em pasta padrao):
  `ConfiguracaoManual.Criar()` (usada por --reenfileirar-i60, --consultar-i51, --relatorio-i60) lia so appsettings.json + email.settings.json + env:
  sem appsettings.Production.json (Pastas:Entrada de producao ignorada -> default) e sem argumentos de linha de comando.
  `EnvioManualI40/I60` e `SmokeTestWms` tambem ignoravam appsettings.Production.json (SmokeTest ainda dependia do cwd).
  `--testar-token` lia ainda `config\appsettings.Production.json`, que o servico NAO le (divergia do servico).

## Correcao
- Novo `ConfiguracaoAplicacao` (Worker): `Host.CreateApplicationBuilder` com ContentRootPath = pasta do exe, ambiente Production por padrao,
  + email.settings.json por ultimo. Program.cs e TODAS as CLIs (testar-token, smoke-wms, enviar-i60, enviar-i40, consultar-i51,
  relatorio-i60, reenfileirar-i60) usam o mesmo carregador.
- `--testar-token` imprime `(host, porta N)` no fim da linha (nunca usuario/senha/token/caminho).
- `deploy\configurar-segredos.ps1`: grava tambem AppEnvironmentExtra (REG_MULTI_SZ) com WMS__Usuario/WMS__Senha, preservando outras entradas,
  ShouldProcess/-WhatIf, sem eco, `-SemNssm`; chave inexistente => aviso e segue.

## Verificacao
- `dotnet build KitWmsIntegrador.sln -c Release`: 0 Aviso(s), 0 Erro(s).
- `dotnet test KitWmsIntegrador.sln -c Release`: Aprovado 971, Ignorado 9, Falha 0 (baseline 966 + 5 novos em ConfiguracaoAplicacaoTests;
  TestarTokenWmsTests ajustado para a nova saida com host/porta).
- Exe novo (build Release local) com cwd = %USERPROFILE%: saida `GetToken: sem resposta (timeout/rede) FALHA (friozem.assistsoftware.com.br, porta 443)`.
- configurar-segredos.ps1: parse sem erros; funcao Merge-EntradasWms testada (preserva FOO/BAR=a=b, troca entradas WMS em qualquer caixa,
  lista vazia). Nao executei o script contra o HKLM local nem do servidor (nada alterado em producao).
- Docs: INSTALACAO-SERVIDOR.md, OPERACAO.md (§12.1 rotacao de senha), MANUAL-TECNICO.md; gerar-manuais.ps1 + Edge headless
  --no-pdf-header-footer: MANUAL-TECNICO.html/.pdf regenerados (PDF 1083331 bytes). MANUAL-USUARIO.html revertido (so mudava a data da capa).
- Nenhum *.xml/*.zip/segredo/copia da config do servidor no repo; copia temporaria do Production.json ficou so em .agents\tasks\repro-bin e foi apagada.
