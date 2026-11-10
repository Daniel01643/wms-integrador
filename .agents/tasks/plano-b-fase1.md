# Plano B (I40) - Fase 1: verificacao de integracao

Iteracao 1 (review-plano-b.json nao existia). Nada foi enviado ao WMS; SRVAPPALC02 nao foi acessado; sem push.

## Arquivos alterados (desde e7084cc~1, 35 arquivos)

Bloco 1 (docs do Plano A, deck, manuais): docs/CONTROLE-ESFORCO.md, docs/ENCERRAMENTO.md, docs/controle/SEMANAL.md, docs/controle/MENSAL.md, docs/diario/2026-11-09-i60.md, docs/diario/2026-11-10.md, docs/apresentacao/apresentacao.html e .pdf, docs/manuais/MANUAL-TECNICO (.md/.html/.pdf), MANUAL-USUARIO (.html/.pdf), MANUAL-AGENTE-IA.md, docs/wms-codigo-customizado/ (HISTORICO-PLANO-A.md, README.md, notavenda-vincula-itemdoccarga.sql, teste-a/c1/c2 *.sql).

Bloco 2 (codigo I40 fase 1 e docs): ClienteWms/InterfaceWms.cs, Configuracao/OpcoesDevolucao.cs, Construtores/ConstrutorI40.cs, IConstrutorI40.cs, Construtores/Dtos/PayloadI40.cs, Devolucao/LocalizadorXmlDevolucao.cs, Worker/EnvioManualI40.cs, Worker/Program.cs, Worker/appsettings.json, deploy/config-exemplo/appsettings.Production.json, tests (ConstrutorI40Tests, LocalizadorXmlDevolucaoTests, OpcoesDevolucaoTests), docs/ARQUITETURA-PLANO-B-I40.md, docs/ROTEIRO-TESTE-I40.md.

## Verificacoes executadas e resultados

- dotnet build KitWmsIntegrador.sln Debug: 0 avisos, 0 erros. Release: 0 avisos, 0 erros.
- dotnet test (KitWmsIntegrador.Tests): Aprovado, 0 falhas, 400 aprovados, 9 ignorados (cenarios de stress, ja ignorados por padrao), total 409.
- Flags: Worker/appsettings.json tem I60:Habilitada=false (linha 54) e Devolucao:Habilitada=false (linha 59); deploy/config-exemplo/appsettings.Production.json tem I60:Habilitada=false (linha 61) e Devolucao:Habilitada=false (linha 66).
- Valores monetarios: grep nas linhas adicionadas (docs/md/html) por R$, US$, custo, valor/hora, cambio, dolar, reais. Unicos casos: "custo" dentro de "customizado" (falso positivo, 3 linhas de horas/10-11). Nenhum valor monetario.
- Deck: apresentacao.pdf com 12 paginas (/Type /Page, encoding 28591); BOM EF BB BF preservado no apresentacao.html; 0 caracteres U+FFFD; slides data-i=0..11 (12).
- Screenshots: nao havia registro de conferencia previa; regenerei os slides 8 e 12 em arquivos temporarios fora do repo (Edge headless 1280x720) e li os PNG: slide 8 (linha do tempo, linha 10/11 = aprox. 6,0 h, 3,5 h humanas + 2,5 IA; nota com 26,25 h humanas/14,4 h IA e retomada I60 8,4 h/6,5 h) e slide 12 (I60 pausada, devolucao I40 em desenvolvimento, retencao 60 dias) estao legiveis, sem corte. Temporarios removidos.
- Aritmetica de horas: humanas 31,15 + 3,50 = 34,65 (CONTROLE-ESFORCO); IA aprox. 18,40 + 2,50 = 20,90; total aprox. 55,55. Deck: 26,25 + 8,4 = 34,65 humanas e 14,4 + 6,5 = 20,9 IA, consistente. W46 = 3,5/2,5/6,0 em SEMANAL, MENSAL e deck. Novembro parcial MENSAL: W45 (23,15/16,0/39,15) + W46 = 26,65/18,5/45,15, correto.
- git status: limpo antes do relatorio; sem temporarios no repo.

Correcoes necessarias nesta iteracao: nenhuma no codigo/docs. Somente este relatorio foi adicionado.

## Ficou para a fase 2

- Tela web de devolucao (pedir chave e placa; usa Devolucao:Habilitada e Porta).
- Publicacao (release e deploy), mantendo Devolucao:Habilitada=false ate o teste ao vivo.
- Definir o default de InverterPartes apos o teste ao vivo (invertido e nao invertido, conforme respostas da interface).
- Confirmar o nome da interface (WEBAPI-I40-Json esta como "a confirmar") e o formato array/objeto do payload.
- Cadastrar no WMS o motorista fixo e configurar Devolucao:NomeMotorista.

## Horas

- IA: aprox. 2,5 h em 10/11 (estimativa por tempo de sessao; fase 1 do Plano B incluida na estimativa do dia). Esta verificacao de integracao: aprox. 0,3 h de IA, nao somada nos controles.
- Humanas: apenas as 3,5 h informadas pelo usuario para 10/11. Nenhuma outra hora humana registrada.
