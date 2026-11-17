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

---

## Semana 2026-W47 (17/nov a 23/nov) — em aberto (monitoramento)

| Data | Humana | IA | Total | Marcos |
|------|--------|-----|-------|--------|
| 2026-11-17 | 2,0 | 3,0 | 5,0 | I51 com identificador `WEBAPI-I51-Json`; I60 cadastrada no WMS; I60 parada e religada (duplicação de DocCarga no WMS); release 2026.11.17-1 (quantidade em embalagens); 610 associadas |
| **Total parcial (até 17/11)** | **2,0** | **3,0** | **5,0** | |

**Estado em 17/11.** Produção na release 2026.11.17-1. Fluxo I50 → separação → I51 → I60 funcionando em produção
(610 notas associadas, 0 rejeitadas). Monitoramento pelas manhãs até 20/11.

## Semana 2026-W46 (10/nov a 16/nov) — fechada

| Data | Humana | IA | Total | Marcos |
|------|--------|-----|-------|--------|
| 2026-11-10 | 4,5 | 7,5 | 12,0 | I60 Plano A pausado (aguarda fornecedor); I40 validada no homolog e tela de devolução implementada |
| 2026-11-11 | 2,0 | 3,5 | 5,5 | Repositório no GitHub (XMLs fora); tela da I40 com busca por número+série, sem placa; escopo da Puxada TSP |
| 2026-11-12 | 4,24 | 7,5 | 11,74 | Puxada TSP implementada e testada no homolog; relatório com tempo de integração, cabeçalho/logo e cartão Transportes; escopo, design e plano da I51/I60 |
| 2026-11-13 | 5,0 | 10,0 | 15,0 | I51 → I60 validada no homolog (7/7 associadas); Puxada e TSP validadas; TSP com XML solto/subpastas e sem placa; login na tela de devolução; alerta da I60, janela de silêncio, retenção 30 dias; release preparada |
| 2026-11-16 | 4,0 | 10,0 | 14,0 | Releases 2026.11.16-1/-2/-3 em produção; 401 resolvido (3 servidores, token por envio, usuário BRF); I51/I60 e tela de devolução ligadas; deck final; **projeto finalizado** |
| **Total da semana** | **19,74** | **38,5** | **58,24** | |

**Estado em 16/11 (fechamento).** Produção na release 2026.11.16-3. Causa do 401 confirmada pela Assist
(3 servidores em balanceamento; orientação: um token por envio). Usuário do WMS trocado para BRF. I51/I60
ligadas em produção (EDI e evento configurados no WMS). Tela de devolução no ar em
`http://SRVAPPALC02:8085/`. Primeiro lote na versão nova (578 notas de um ZIP, 1 pacote) aguardando separação.
**Projeto travado como finalizado**; reabre só para novos ajustes.

**Estado em 13/11.** Fluxo I50 → separação → I51 → I60 validado no homolog: 7 de 7 notas associadas (INE,
`qtdeVolume` e `qtdeTotalNota` = 0, ajuste de quantidade 11 → 9 aceito). Pré-requisitos no WMS: EDI da I51
(WEBSERVI) e evento interno de retorno da separação. Release preparada localmente; publicação no servidor
fica para outro dia. Texto do ENC ainda desconhecido: alerta imediato por e-mail para capturar.

**Estado em 12/11.** Puxada TSP testada no homolog: ondas `TSP-20261112-01-SOROCABA` e `-02-SJC`
criadas e notas enviadas, mas pendentes na interface ("Proprietaria/Chave 01.838.723/0098-50 nao
encontrada"), aguardando o fornecedor. Relatório com tempo de integração (nível B), cabeçalho novo e
título "Relatório de Integração". I51 como gatilho da I60 (INE; ENC → IMP; I40 como última alternativa):
design aprovado e plano pronto; workflow pausado, implementação continua em 13/11.

**Estado em 11/11.** I60 (Plano A) aguarda o fornecedor. I40 (Plano B): formato validado no homolog e
tela de devolução pronta e desligada (fora do manual oficial), aguardando teste da tela. Puxada TSP:
escopo fechado em `docs/ESCOPO-PUXADA-TSP.md`; execução em 12/11.

**Resumo parcial:**
- Plano A (I60 automática) testado no homolog e pausado: ver `docs/wms-codigo-customizado/HISTORICO-PLANO-A.md`.
- Plano B (devolução pela I40) em início: testar variantes invertido e não invertido, ICD, placa obrigatória.
