# Encerramento (por ora) — KitWms Integrador

Documento curto para quem voltar ao tema. Data do fechamento: 09/11/2026.

## 1. Estado final

- Integra NF-e (XML) da pasta de entrada para o WMS Friozem via webservice (I50), **em produção**
  no servidor SRVAPPALC02 (Windows Server 2012 R2), serviço NSSM `Friozem-ImportacaoNotas`.
- Versão em produção: a última documentada é a série **2026.11.06-1 a -5** (diário de 06/11).
  **Conferir no servidor** qual está em `app\current` (não há registro de release posterior).
- A **I60 (associação de nota) está desativada por flag**; o código foi mantido.
- Resumo semanal de telemetria funcionando (verificado em 09/11).
- Último build: sem avisos e sem erros. Testes: ver `.agents/tasks/fechamento-projeto.md`.

## 2. Mapa rápido

| O quê | Repo (`c:\wms-integrador`) | Servidor |
|---|---|---|
| Código | `src\KitWmsIntegrador.Core`, `src\KitWmsIntegrador.Worker`, `tests\` | `C:\Friozem\importacao_notas\app\current` |
| Config padrão | `src\KitWmsIntegrador.Worker\appsettings.json` | variáveis de ambiente (`WMS__*`, `I60__Habilitada`...) |
| Scripts de operação | `deploy\` | `C:\Friozem\importacao_notas\tools\deploy` |
| Entrada de XML | — | `C:\Friozem\kitbrf` |
| Destinatários / telemetria | modelos em `deploy\config-exemplo` e `docs\exemplos` | `config\destinatarios-resumo.txt`, `data\telemetria\` |
| Manuais | `docs\manuais` | `C:\Friozem\importacao_notas\manuais` |
| Apresentação | `docs\apresentacao\apresentacao.html` e `.pdf` | — |
| Histórico e horas | `docs\diario`, `docs\controle`, `docs\CONTROLE-ESFORCO.md` (só horas) | — |

## 3. Dia a dia

Não duplicado aqui. Usar `docs\manuais\MANUAL-USUARIO` (operador), `docs\manuais\MANUAL-TECNICO`
(técnico nível 1), `docs\OPERACAO.md` e `docs\INSTALACAO-SERVIDOR.md` (instalar, publicar release,
rollback, segredos).

## 4. Decisões fechadas

- **I60 desativada por flag** (`I60:Habilitada=false`); código mantido. Reativar com
  `I60__Habilitada=true`. Pode-se travar com um hash, se desejado.
- **Subpastas por pacote** em `Processados` e `Erro`.
- **Não-XML** vai para `Erro\<pacote>\ExtensaoInvalida` com `.motivo.txt`; temporários ignorados.
- **Placa do veículo** na I50 via `I50:EnviarPlacaVeiculo` (padrão ligado).
- **TLS 1.2/1.3 forçado** no cliente HTTP e **GetToken com retry** (causa dos 401 em massa no 2012 R2).
- **Resumo semanal**: segunda 12:00 (hora local do servidor).

## 5. Pendências e riscos

| Item | Dono sugerido |
|---|---|
| Trocar senhas que apareceram em chat (e-mail, admin do servidor, SMTP) | Usuário / TI |
| Conta WMS de produção em provisionamento (401 intermitente deve estabilizar) | Usuário / equipe WMS |
| Apresentação e treinamento de 1h | Usuário |
| Atualizar o SO do servidor (2012 R2 fora de suporte) | TI |
| Arquivo local `~/.srv-cred.xml` (credencial WinRM cifrada por DPAPI): apagar ou manter ciente | Usuário |
| Horas de 09/11 não contabilizadas (decisão do usuário; nada a lançar em `docs\controle`) | — |
| Repositório sem remote: o histórico existe só nesta máquina (fazer backup/push) | Usuário |
| Testes não puderam ser executados na revisão de 09/11 (bloqueio de Controle de Aplicativo do Windows); na etapa I60 (mesmo dia) rodaram: 367 aprovados, 0 falhas, 9 ignorados | Usuário / TI |
| Teste I50 + I60 em homolog após gravar o PL/SQL de QTD_VOLUMES (roteiro em `docs\wms-codigo-customizado\ROTEIRO-TESTE-HOMOLOG.md`); depois promover por `publicar-release.ps1` | Usuário |
| Plano B (devolução pela I40) arquitetado, não implementado: ver `docs\ARQUITETURA-PLANO-B-I40.md` (perguntas em aberto P1 a P9) | Usuário |
| Confirmar o tipo de operação da I60 (`I60:TipoOperacaoNota`; o manual do repo cita IMB/INE/IMP, não INV) | Usuário / fornecedor WMS |

## 6. Como retomar

1. Obter o repositório (clonar ou copiar `c:\wms-integrador`) e abrir em .NET 8 SDK.
2. `dotnet build KitWmsIntegrador.sln` e `dotnet test tests\KitWmsIntegrador.Tests\KitWmsIntegrador.Tests.csproj`.
3. Ler o diário mais recente em `docs\diario` (hoje `2026-11-09.md`) e este documento.
4. No servidor: conferir o serviço `Friozem-ImportacaoNotas` (rodando) e a release em `app\current`.
5. Conferir a telemetria: `data\telemetria\ultimo-resumo.txt` e o e-mail de segunda 12:00.
6. Manuais em `docs\manuais`; regenerar com `powershell -ExecutionPolicy Bypass -File docs\manuais\gerar-manuais.ps1`
   e depois gerar os PDF (ver `docs\manuais\README.md`).
7. PDF da apresentação via Edge headless (salve o HTML em **UTF-8 com BOM** para não quebrar acentos):

   ```powershell
   & 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe' --headless=new --disable-gpu --no-pdf-header-footer "--print-to-pdf=<saida.pdf>" ("file:///" + "<caminho/com/barras/normais>/apresentacao.html")
   ```

8. O `gerar-manuais.ps1` lê a logo base64 da linha 125 de `apresentacao.html` (com fallback para a
   primeira ocorrência); evite reordenar essa parte do arquivo.
