# Verificação: token por envio (16/11/2026)

Executado direto em c:\wms-integrador (sem TEMP):

- `dotnet build KitWmsIntegrador.sln -c Release`: 0 Aviso(s), 0 Erro(s).
- `dotnet test KitWmsIntegrador.sln -c Release --no-build`: Aprovado 991, Falha 0, Ignorado 9, Total 1000 (baseline 971/9; +20 testes novos em `TokenPorEnvioTests.cs`).
- Testes existentes de 401 passam; os cenários de cache (`AutenticacaoWmsTests`, `Autenticacao401PacoteTests`, `ClienteWmsHttpTests` cache) agora fixam `TokenPorEnvio=false`.

Decisões: `MaxGetTokenPorMinuto` padrão 300 (não 120) e acima do teto a chamada ESPERA (não falha a nota); `UseCookies=false` no handler com cookie de afinidade repetido só no POST do mesmo envio; nova tentativa do GetToken usa HttpClient/handler novo + `Connection: close`.
Manuais (Usuário §11, Técnico §8.6) com o link `http://SRVAPPALC02:8085/` e `http://200.200.200.213:8085/`; HTML/PDF regerados (gerar-manuais.ps1 + Edge headless). Nada foi alterado no servidor.
