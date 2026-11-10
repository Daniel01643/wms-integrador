# Controle de Esforço — Integrador WMS

Registro de esforço do projeto **em horas**, separando **horas humanas** (trabalho do
time: decisões, validação, operação) e **horas de IA** (trabalho do agente Kiro,
estimado pelo tempo de sessão).

> **Regra do projeto:** esforço é estimado e registrado **somente em horas**. Não
> registrar valores monetários (custo, valor/hora, câmbio) neste documento nem na
> apresentação.

## Registro de horas (reconciliado)

Convenção: **horas humanas** são informadas pelo time; **horas de IA** são estimadas
pelo tempo de execução/sessão do agente.

| Data       | Tipo    | Horas | Descrição                                                                                     |
|------------|---------|-------|-----------------------------------------------------------------------------------------------|
| 2026-10-29 | Humana  | 2,00  | Briefing, levantamento do fluxo, regras de negócio, decisões, arquitetura                     |
| 2026-10-29 | IA      | 0,60  | Arquitetura + diagramas, deck inicial, controle de esforço                                    |
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
| 2026-11-09 | Humana  | 4,90  | Apresentação (nome, "Aprox.", só horas), resumo semanal no servidor, encerramento, I60 Plano A no homolog |
| 2026-11-09 | IA      | 4,00  | Ajustes do deck, diagnóstico WinRM, encerramento, I60 (código customizado, qtdeVolume, testes, envio manual) — estimado |

Subtotal humano (29/10 a 06/11): **25,25 h** + 1,00 h de apresentação/treinamento = **26,25 h**
(entrega inicial, até a produção). Com 09/11: **31,15 h**.

## Totais

| Tipo                 | Entrega inicial (até 06/11) | Com 09/11        |
|----------------------|-----------------------------|------------------|
| Humana               | **26,25**                   | **31,15**        |
| IA (ferramenta)      | **aprox. 14,40**            | **aprox. 18,40** |
| **Total de esforço** | **aprox. 40,65**            | **aprox. 49,55** |

O comparativo abaixo e a apresentação usam a **entrega inicial** (do desenho à produção).
09/11 é acabamento e a retomada da I60.

O grosso do esforço humano está em decisões, validação e operação; a IA atua como
multiplicador de produtividade na escrita de código, testes e documentação.

## Comparativo de esforço (ilustrativo)

Ordem de grandeza do ganho em horas frente a referências de mercado. Comparação
ilustrativa do escopo deste integrador — **não** uma equivalência exata.

| Abordagem                          | Esforço              | Observação                                                 |
|------------------------------------|----------------------|------------------------------------------------------------|
| Desenvolvimento tradicional        | aprox. 180 h         | Projeto sob medida, prazo de meses (estimativa de mercado) |
| Assistido por IA (mercado)         | aprox. 110 h         | Ganho típico documentado de aprox. 30 a 55% em código      |
| **Este projeto (Kiro + 1 pessoa)** | **aprox. 26 h** humanas + aprox. 14,4 h de IA | Do desenho à produção, com validação real |

> As horas das abordagens tradicional e assistida por IA são **estimativas de mercado**;
> apenas as horas deste projeto são **medidas** (ver "Registro de horas").

## Notas metodológicas

- **Horas humanas**: informadas pelo time (decisões, validação de credenciais, testes no
  WMS, operação e acompanhamento).
- **Horas de IA**: estimadas pelo tempo de execução/sessão do agente.
- **Estimativas de novas etapas**: sempre em horas humanas e horas de IA, antes de iniciar.
