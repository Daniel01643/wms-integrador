# Controle Mensal — Integrador WMS (KitWmsIntegrador)

Consolida horas (humanas e IA) e marcos por mês. Atualizar ao fechar cada mês.
Detalhe por semana em `docs/controle/SEMANAL.md`; mestre em `docs/CONTROLE-FINANCEIRO.md`.

---

## Outubro/2026 — FECHADO (projeto iniciado em 29/10)

| Semana | Período | Humana | IA | Total |
|--------|---------|--------|-----|-------|
| W44 | 27/10–02/11 (parte em out: 29–31) | 7,0 | 2,4 | 9,4 |
| **Total do mês** | | **7,0** | **2,4** | **9,4** |

> Nota: a semana W44 cruza outubro/novembro. As horas acima (29 e 30/10) são de
> outubro. O que for lançado em 31/10 entra aqui; 01–02/11 entram em novembro.

**Marcos de outubro:**
- Concepção e arquitetura do integrador WMS (BRF/Friozem, SolWMS Assist).
- Decisão de stack: C#/.NET 8 self-contained sob NSSM (Windows Server 2012 R2).
- Descoberta-chave: número do transporte vem do XML (infCpl), planilha dispensável.
- Comunicação REST validada ao vivo: token + I10/I30/I50 OK (~80ms/chamada vs ~12s da tela).
- 4 etapas de código concluídas e aprovadas (esqueleto, parser, cliente, construtores).
- Cadeia de pré-condições mapeada: empresa (I10) + produtos (I30) antes de I50/I60.
- Chamado aberto no fornecedor para a conferência de quantidade da I60.

**Custo do mês:** _a definir_ (depende do valor/hora IA e humana — ver CONTROLE-FINANCEIRO.md).

---

## Novembro/2026 — em aberto

| Semana | Período | Humana | IA | Total |
|--------|---------|--------|-----|-------|
| W45 | 03/11–09/11 | - | - | - |
| **Total do mês** | | **0,0** | **0,0** | **0,0** |

**Metas de novembro (previstas):**
- Fechar a I60 com a resposta do fornecedor.
- Etapa 5: Orquestrador + máquina de estados.
- Etapa 6: Store de estado + relatórios (erros/pendências).
- Etapa 7: Movimentação de arquivos (Processados/Erro, limpeza 10 dias) + alertas e-mail.
