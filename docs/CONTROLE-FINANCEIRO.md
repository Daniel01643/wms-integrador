# Controle Financeiro — Integrador WMS

Registro de esforço do projeto, separando **horas humanas** (trabalho do time:
decisões, validação, operação) e **horas de IA** (trabalho do agente Kiro, estimado
pelo tempo de sessão). Documento de **fechamento** (Etapa 11).

## Parâmetros de custo

| Parâmetro                         | Valor        | Observação                                                                 |
|-----------------------------------|--------------|-----------------------------------------------------------------------------|
| Custo hora humana                 | R$ 28,70/h   | Derivado do salário mensal ÷ 220 h/mês (o salário não é exposto).           |
| Custo hora IA (proporcional)      | ~R$ 0,95/h   | Custo **da ferramenta**, ilustrativo — ver nota metodológica.               |
| Jornada de referência             | 220 h/mês    | Base para converter custo mensal em custo/hora.                             |
| Câmbio de referência              | R$ 5,20 / US$| **Cotação de referência — atualizar no fechamento final.**                  |

**Custo da ferramenta de IA**: assinatura Kiro Pro+ de US$ 40/mês. A US$ 40 × R$ 5,20
= ~R$ 208/mês; ~R$ 208 ÷ 220 h = **~R$ 0,95/h**. Esse valor representa o **custo
proporcional da ferramenta** no período — a IA **não** é contratada por hora; o número
é apenas ilustrativo do custo da ferramenta durante o projeto.

## Registro de horas (reconciliado)

Convenção: **horas humanas** são informadas pelo time; **horas de IA** são estimadas
pelo tempo de execução/sessão do agente.

| Data       | Tipo    | Horas | Descrição                                                                                     |
|------------|---------|-------|-----------------------------------------------------------------------------------------------|
| 2026-10-29 | Humana  | 2,00  | Briefing, levantamento do fluxo, regras de negócio, decisões, arquitetura                     |
| 2026-10-29 | IA      | 0,60  | Arquitetura + diagramas, deck inicial, controle financeiro                                    |
| 2026-10-30 | Humana  | 5,00  | Decisões de negócio, validações no WMS, comunicação validada ao vivo, chamado I60             |
| 2026-10-30 | IA      | 1,80  | Stack .NET + 4 etapas de código, testes E2E ao vivo, correções, chamado                       |
| 2026-11-03 | Humana  | 3,00  | Decisões (I50 enriquecida, flag I60, relatório, orquestrador reativo), validações             |
| 2026-11-03 | IA      | 1,50  | Etapa 5 (I50 enriquecida, flag I60, relatório HTML/PDF), início do Orquestrador               |
| 2026-11-04 | Humana  | 4,50  | Etapas 6/7/7b/7c/8, watcher, subpastas, limpeza, e-mail visual, validação REAL até 1196 notas |
| 2026-11-04 | IA      | 2,50  | Orquestrador reativo, relatório refinado, watcher/pacote/e-mail, teste de esforço             |
| 2026-11-05 | Humana  | 4,75  | Etapa 9 (pacote + NSSM + 50 notas), monitoramento/resumo semanal, e-mail por TXT, Etapa 10    |
| 2026-11-05 | IA      | 3,50  | Pacote de instalação, serviço NSSM, e-mail configurável, manuais (Etapa 10)                   |
| 2026-11-06 | Humana  | 6,00  | Virada em PRODUÇÃO, não-XML, placa na I50, TLS 1.2, GetToken resiliente, 5 releases            |
| 2026-11-06 | IA      | 4,50  | Correções TLS/GetToken, tratamento não-XML, placa no XML→WMS, manuais embarcados, releases     |
| Planejada  | Humana  | 1,00  | Apresentação e treinamento                                                                    |

Subtotal humano (29/10 a 06/11): **25,25 h** + 1,00 h de apresentação/treinamento = **26,25 h**.

## Totais e fechamento financeiro

| Tipo        | Horas     | Valor/hora   | Custo (R$)   |
|-------------|-----------|--------------|--------------|
| Humana      | 26,25     | R$ 28,70     | **R$ 753,38** |
| IA (ferramenta, ilustrativo) | ~14,40 | ~R$ 0,95 | **~R$ 13,68** |
| **Total do desenvolvimento** | —   | —            | **~R$ 767**  |

- Custo humano: 26,25 h × R$ 28,70 = **R$ 753,38**.
- Custo IA (proporcional, ilustrativo): ~14,40 h × ~R$ 0,95 = **~R$ 13,68**.
- Custo total do desenvolvimento (humano + ferramenta): **~R$ 767,06 ≈ R$ 767**.

O grosso do custo é a **hora humana**; a IA é o **multiplicador de produtividade**, não
uma linha de custo relevante por hora.

## Comparativo de custo (ilustrativo)

Ordem de grandeza da economia frente a referências de mercado. Comparação ilustrativa
do escopo deste integrador — **não** uma equivalência exata.

| Abordagem                       | Esforço  | Custo aprox.  | Observação                                              |
|---------------------------------|----------|---------------|---------------------------------------------------------|
| Desenvolvimento tradicional     | ~180 h   | a partir de ~R$ 80 mil | Projeto sob medida no Brasil, prazo de meses (estimativa de mercado, 2026) |
| Assistido por IA (mercado)      | ~110 h   | —             | Ganho típico documentado de ~30 a 55% em tarefas de código |
| **Este projeto (Kiro + 1 pessoa)** | **~26 h** humanas | **~R$ 767** | Do desenho à produção, com validação real              |

> **Metodologia do comparativo**: as horas de referência das abordagens tradicional
> (~180 h) e assistida por IA (~110 h) são **estimativas de mercado**; apenas as
> **~26 h humanas deste projeto são medidas** (ver "Registro de horas"). O valor de
> R$ 80 mil do desenvolvimento tradicional é estimativa de referência de mercado (2026).
> A comparação mostra a **ordem de grandeza da economia**, não uma equivalência exata.

## Notas metodológicas

- **Salário não é exposto**: o valor/hora humano (R$ 28,70) deriva do salário mensal
  dividido por 220 h/mês; apenas o valor/hora é apresentado.
- **IA como custo proporcional de ferramenta**: a assinatura Kiro Pro+ (US$ 40/mês) é
  rateada por 220 h/mês para chegar a ~R$ 0,95/h. A IA não é contratada por hora; o
  número é ilustrativo do custo da ferramenta no período.
- **Câmbio atualizável**: R$ 5,20/US$ é cotação de referência — **atualizar no
  fechamento final** se necessário.
- **Horas de IA**: estimadas pelo tempo de execução/sessão do agente.
- **Horas humanas**: decisões, validação de credenciais, testes no WMS, operação e
  acompanhamento.
