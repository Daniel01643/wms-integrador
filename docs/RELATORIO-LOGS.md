# Relatório de logs (PDF) — requisito

Além do alerta por e-mail, o time pediu um **arquivo de relatório** estruturado,
preferencialmente **PDF**, com os erros de integração.

## Conteúdo (confirmado pelo usuário)

Cada linha de erro deve ter:

| Coluna | Origem |
|--------|--------|
| **Número da nota** | `nNF` do XML / `documento` do retorno WMS |
| **Número do transporte** | `Transporte:` do infCpl (agrupador da onda) |
| **Horário do erro** | timestamp do processamento (local, com fuso) |
| **Mensagem do erro** | `retornos[].mensagem` do WMS (ou erro do agente) |

## Formato

- **PDF estruturado** (tabela). Um relatório por ciclo/lote, ou acumulado por dia — a
  definir (provável: um PDF por execução + um consolidado diário).
- Nome sugerido: `erros_<yyyy-MM-dd_HHmm>.pdf`, na pasta de logs do serviço
  (`C:\Friozem\importacao_notas\logs\` em produção; configurável).

## Escolha de biblioteca (.NET) — a decidir

Opções para gerar PDF em C#/.NET 8, com baixo footprint (requisito do servidor):

| Lib | Licença | Nota |
|-----|---------|------|
| **QuestPDF** | MIT (Community p/ empresas < US$1M receita) | API fluente, boa p/ tabelas, leve. Verificar faturamento da empresa para licença Community vs. paga. |
| **PdfSharpCore** | MIT | Leve, porém API mais baixo-nível (desenho manual). |
| **iText7** | AGPL / comercial | Potente, mas AGPL exige cuidado ou licença paga. |

Recomendação inicial: **QuestPDF** (produtividade + tabela nativa), **confirmando a
licença** conforme o porte da empresa. Alternativa livre de dúvida de licença:
PdfSharpCore. **A decidir com o time.**

## Relação com o e-mail e o store de estado

- O **store de estado** (Etapa 6) já vai registrar cada nota: nº nota, transporte,
  estado (sucesso/pendente/erro), mensagem, timestamp. O relatório PDF é uma **projeção
  desses dados** filtrando os erros/pendências.
- O **e-mail** (Etapa 7) pode **anexar** esse PDF, ou apenas resumir e apontar o caminho
  do arquivo.
- Portanto o PDF se encaixa naturalmente na **Etapa 6/7**, consumindo o store de estado.

## Além dos erros (sugestão)

Considerar também um relatório/seção de **pendências de revisão** (produtos criados por
I30 de contenção, divergências de transporte planilha×XML) — mesma estrutura, outra
aba/seção. A confirmar se entra no mesmo PDF ou separado.
