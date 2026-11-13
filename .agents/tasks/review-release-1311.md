# Release 1311: janela de silêncio, defaults da I60 e alerta imediato (FEAT-001..003)

Três commits locais sobre 5f621dd (c5bbd41, ae33103, e2f94fb) mais o commit de verificação e3a398f. A coleta do pacote passa a fechar por silêncio (15 s, teto 180 s, `Fixa` como retorno). Os defaults validados no homolog entram na config (I60 QtdeVolumeZero/QtdeTotalNotaZero, placa na Puxada normal, retenção 30 dias). A I60 ganha alerta imediato por varredura de estado (ENC/IMP, rejeitada, resposta não reconhecida, atrasada) e bloco Atenção no relatório diário.

Watch for: nada bloqueante. Texto real do ENC ainda é desconhecido (calibração via arquivo de alertas, declarado pelo coder). **Verdict**: APPROVED

## High-level view

A janela de silêncio mantém o modo antigo atrás de `ModoJanela=Fixa`. Os testes antigos fixam `Fixa`, então a regressão fica coberta. O fechamento é limitado pelo teto de 180 s.

A config versionada segue a decisão do plano: Puxada normal `EnviarPlacaVeiculo=true`, TSP `EnviarPlacaVeiculoTsp=false`. `I51/I60/Devolucao Habilitada` continuam `false`. Retenção 30/30. Um teste novo (`ConfiguracaoReleaseTests`) lê o appsettings real.

O alerta carimba a nota antes de avisar. No máximo um alerta por nota/evento, mesmo com falha de e-mail ou reinício. Associada nunca alerta. Os carimbos persistem no JSON da nota e `Reenfileirar` zera `AlertaI60Em`.

<details>
<summary>Issues (2)</summary>

1. **Texto do ENC desconhecido (possible, não bloqueante)** — a classificação ENC/IMP depende de `Motivo==DocCargaEncerrado` ou `OperacaoI60==IMP`. Calibrar com o arquivo de alertas e `I60:PadroesDocCargaEncerrado`, como já registrado.
2. **Sobras de "60 dias" em docs/ENCERRAMENTO.md (confirmed, não bloqueante)** — linhas 16, 70 e 87 são histórico, escopo da FEAT-005, declarado em verificacao-release-1311.md.

</details>

<details>
<summary>Details</summary>

## Conformidade com o plano e critérios

Os critérios das três FEATs aparecem nos diffs e na evidência registrada. Build Release com 0 avisos e testes com 926 aprovados, 9 ignorados e 0 falhas estão nos findings das FEATs e em verificacao-release-1311.md (+14, +5, +16 testes). Não reexecutei build nem testes, conforme pedido. A evidência está presente e coerente entre os arquivos: 891 → 905 → 910 → 926.

## Evidências visuais

Os dois PNGs foram abertos. O e-mail de alerta mostra o assunto de atenção com 4 notas e as colunas evento, nota, série, transporte, onda, situação, motivo e texto do WMS. Também mostra a dica de `PadroesDocCargaEncerrado`. O relatório diário mostra o bloco Atenção antes de "Saúde da consulta". Os dados são fictícios (notas 5001..5012, transportes 1000000xxx).

## Higiene do repositório

Nenhum `*.xml`, `*.zip`, `.env`, chave ou credencial entre os arquivos alterados. Nenhuma edição em `docs/CONTROLE-ESFORCO.md`, `docs/controle/*` ou `docs/diario/*`. Nenhum valor monetário nos adicionados. Sem push: o ramo está à frente do remoto só com commits locais. Nenhum acesso a servidor registrado.

## Nota sobre a árvore de trabalho

Há arquivos não rastreados em `.agents/tasks` (FEAT-001/003..005.json, plano, context). Isso não afeta o diff revisado. O FEAT-002.json e os PNGs foram versionados.

</details>

## File map

<details>
<summary>Arquivos</summary>

Core: OpcoesPacote, DetectorArquivoPronto, GerenciadorPacote (janela); AlertaI60, CorpoEmailAlertaI60, IDestinatariosAlertaI60, ModeloSeparacao, CaminhosI51, ServicoSeparacaoI51, RepositorioSeparacaoArquivo (alerta); ModeloRelatorioI60, MontadorRelatorioI60, CorpoEmailRelatorioI60, RenderizadorPdfRelatorioI60, BlocoTempo (relatório). OpcoesLimpeza, OpcoesI51 e textos de retenção (30 dias). Worker: appsettings.json, Worker.cs, RegistroSeparacaoI51. Deploy/docs: appsettings.Production.json, scripts, OPERACAO, ARQUITETURA e demais docs técnicas. Testes: AlertaI60Tests, JanelaSilencioTests, ConfiguracaoReleaseTests e ajustes. Diff completo: `git diff 5f621dd..HEAD`.

</details>
