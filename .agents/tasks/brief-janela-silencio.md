# Brief do workflow — Janela de silêncio na coleta do pacote (NÃO disparado; aguardando ordem do usuário)

Status: **preparado em 13/11/2026; só executar depois da avaliação da I51/I60** (pedido do usuário).

## Problema (medido no servidor de produção, log `C:\Friozem\importacao_notas\logs\worker.out.log`)

A janela de coleta é FIXA (`Pacote:JanelaColetaSegundos = 10`, contada a partir do primeiro arquivo e
nunca adiada; `GerenciadorPacote.cs` ~linha 151) e cada arquivo ainda precisa ficar estável por
`Pacote:EstabilizacaoSegundos = 3`. Na prática só entra no pacote o que terminou de chegar ~7 s depois do
primeiro arquivo. Lotes grandes viram 2 (ou mais) pacotes e 2 relatórios/e-mails para o mesmo envio.

Lotes divididos de 06/11 a 13/11 (linhas "Pacote aberto" + "Foto da pasta" do log):

| Data | Pacotes | Notas |
|---|---|---|
| 06/11 13:56 | 4 pacotes seguidos | 22 + 66 + 49 + 51 |
| 06/11 18:27 | 2 | 14 + 1 |
| 06/11 18:46 | 2 | 121 + 18 |
| 06/11 19:59 | 2 | 47 + 53 |
| 09/11 10:59 | 2 | 310 + 104 |
| 11/11 17:15 | 2 | 191 + 30 |
| 13/11 07:40 | 2 | 24 + 51 (chegada de 07:40:13 a 07:40:35, 22 s) |

Observação: a data de CRIAÇÃO dos XMLs no servidor não serve para medir a chegada (a cópia via SMB
preserva datas de origem); usar o horário do log/da detecção.

## Solução pedida pelo usuário: janela de silêncio

O pacote fecha quando passar `Pacote:SilencioSegundos` sem chegar arquivo novo (nem mudar tamanho/data
de um existente), com teto `Pacote:JanelaMaximaSegundos` contado a partir da abertura. Lote pequeno fecha
rápido; lote grande não é cortado. Objetivo declarado: **diminuir o número de relatórios**.

Sugestão de padrões (planner confirma): `SilencioSegundos = 15`, `JanelaMaximaSegundos = 180`,
`EstabilizacaoSegundos = 3` (inalterado). Arquivo ainda instável no fim do teto continua como hoje
(fica para o próximo pacote, com alerta).

Compatibilidade: manter `JanelaColetaSegundos` lido como fallback (ou modo `Pacote:ModoJanela = Fixa|Silencio`,
padrão `Silencio`) para poder voltar sem release. Documentar a decisão.

## Pontos de partida

- `src\KitWmsIntegrador.Core\Pacote\GerenciadorPacote.cs` (estado Ocioso/Coletando, foto, backoff de fotos
  vazias, `_inicio`, `JanelaColetaSegundos`), `DetectorArquivoPronto.cs` (estabilização),
  `src\KitWmsIntegrador.Core\Configuracao\OpcoesPacote.cs`.
- Telemetria/relatório: `EsperaJanelaMs` (RegistroTelemetria/MapeadorTelemetria) e a etapa "Espera da janela
  de coleta" no relatório (renomear para "Coleta (janela de silêncio)" se fizer sentido).
- Log "Pacote aberto (...): N arquivo(s) na entrada; janela de coleta de Xs." → atualizar o texto.
- Valem para raiz, `Puxada` e `PuxadaTSP` (ZIP e XML solto; XMLs soltos da TSP continuam uma onda por pacote,
  então a janela de silêncio também evita dividir uma onda TSP de XML solto).

## Regras do repositório

Main direto, sem worktree, sem push. Commits locais em pt-BR. Nunca versionar *.xml/*.zip/dados reais. Não
editar docs/CONTROLE-ESFORCO.md, docs/controle/*, docs/diario/*. Esforço só em horas.

## Verificação

- `dotnet build c:\wms-integrador\KitWmsIntegrador.sln -c Release` com 0 avisos.
- `dotnet test c:\wms-integrador\KitWmsIntegrador.sln -c Release` (linha de base: 863 aprovados, 9 ignorados;
  se um Worker travar DLL, testar em cópia temporária).
- Testes com relógio falso: chegada contínua por 25 s → 1 pacote; pausa maior que o silêncio → 2 pacotes;
  chegada contínua além do teto → fecha no teto; arquivo crescendo (cópia lenta) adia o fechamento;
  lote único pequeno fecha em ~silêncio; modo `Fixa` reproduz o comportamento antigo.
- Atualizar `docs\OPERACAO.md`, `docs\ARQUITETURA.md`, Manual Técnico (seção 8 da janela de coleta) e
  regenerar HTML/PDF (`docs\manuais\gerar-manuais.ps1` + Edge headless). Exemplo de produção
  `deploy\config-exemplo\appsettings.Production.json` com as chaves novas.

## Forma

Um passo de implementação + revisão em loop (máx. 2, revisor por último), veredito em
`c:\wms-integrador\.agents\tasks\review-janela-silencio.json`. Relatório curto em
`c:\wms-integrador\.agents\tasks\relatorio-janela-silencio.md` com horas de IA.

Estimativa: aprox. 1,0 h de IA; aprox. 0,3 h humana (teste copiando um lote grande no homolog).
