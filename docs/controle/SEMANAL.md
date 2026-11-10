# Controle Semanal — Integrador WMS (KitWmsIntegrador)

Consolida horas (humanas e IA) e marcos por semana. Atualizar ao fechar cada semana.
Detalhe diário em `docs/diario/`; registro mestre em `docs/CONTROLE-ESFORCO.md` (esforço só em horas).

---

## Semana 2026-W44 (27/out a 02/nov) — FECHADA

| Data       | Humana | IA  | Total | Marcos |
|------------|--------|-----|-------|--------|
| 2026-10-29 | 2,0    | 0,6 | 2,6   | Desenho: briefing, arquitetura, deck, financeiro |
| 2026-10-30 | 5,0    | 1,8 | 6,8   | Stack .NET, 4 etapas de código, comunicação validada, chamado I60 |
| **Total**  | **7,0** | **2,4** | **9,4** | |

**Resumo da semana:**
- Projeto iniciado: do desenho à validação de comunicação ao vivo com o WMS.
- Entregue: esqueleto .NET 8, parser de XML, Cliente WMS, Construtores I10/I30/I50/I60.
- Cadeia I30→I10→I50 validada 100% no homolog; I60 em chamado com o fornecedor.
- 4 de ~7 etapas de código concluídas e aprovadas. Etapa 5 (Orquestrador) aguarda I60.

**Pendência que atravessa a semana:** resposta do fornecedor sobre conferência de
quantidade da I60 (retomar terça 03/11).

---

## Semana 2026-W45 (03/nov a 09/nov) — FECHADA

| Data | Humana | IA | Total | Marcos |
|------|--------|-----|-------|--------|
| 2026-11-03 | 3,0 | 1,5 | 4,5 | Etapa 5 (I50 enriquecida, flag I60, relatório HTML/PDF); Orquestrador iniciado |
| 2026-11-04 | 4,5 | 2,5 | 7,0 | Etapas 8, 7, 7b, 7c; e-mail visual; validação REAL no homolog (até 1196 notas) |
| 2026-11-05 | 4,75 | 3,5 | 8,25 | Etapa 9 (instalação NSSM no servidor + validação 50 notas); monitoramento; e-mail configurável; Etapa 10 (manuais) |
| 2026-11-06 | 6,0 | 4,5 | 10,5 | Virada e validação em PRODUÇÃO; não-XML; placa na I50; correções TLS 1.2 e GetToken; manuais no servidor; 5 releases |
| 2026-11-09 | 4,9 | 4,0 | 8,9 | Acabamento da apresentação; resumo semanal no servidor; encerramento; I60 Plano A testado no homolog |
| **Total** | **23,15** | **16,0** | **39,15** | |

**Resumo da semana:**
- Do Orquestrador à produção: watcher, pacote, relatório, e-mail, instalação NSSM, manuais e
  virada em produção (06/11), com centenas de notas reais integradas.
- 09/11: apresentação final (só horas, sem valores), retenção de 60 dias e retomada da I60.
- I60 no homolog: a conferência de volume passou com `qtdeVolume = 0`; o bloqueio agora é a
  conferência de caixas por item (WMS lê 0 caixas na nota de venda). Hipótese: `qtdeProduto`
  é lido em unidades e convertido pela embalagem do produto. A confirmar no cadastro.

**Pendência que atravessa a semana:** I60 (conferência de caixa por item e teste com `IMP`).
