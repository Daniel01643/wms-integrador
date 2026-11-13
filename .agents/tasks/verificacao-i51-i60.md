# Verificação: I51 como gatilho da I60

Data: 13/11/2026. Nenhum Worker em execução travou DLLs (testado direto na solução).

## O que rodou

- `dotnet build KitWmsIntegrador.sln -c Release --no-incremental`: **0 avisos, 0 erros**.
- `dotnet test KitWmsIntegrador.sln -c Release`: **839 aprovados, 0 falhas, 9 ignorados** (baseline 655/9; +184 testes, os 9 ignorados são os de stress de antes).
- Relatório diário de exemplo (dados fictícios) gerado por `GERAR_EXEMPLOS=1 ... --filter GerarExemploRelatorioI60` em `docs/exemplos/relatorio-i60-exemplo.{html,pdf}` e conferido por screenshot headless do Edge (`.agents/tasks/relatorio-i60-exemplo.png`): cabeçalho com logo, saúde, cartões, todas as tabelas, visão por pacote (TSP com onda) e candidatas à I40 renderizados sem quebra.

## Cobertura nova (WMS fake, sem rede)

TODOS consumível (não repete), bruto gravado antes de processar (reprocesso sem chamar o WMS), UNICO repetível e recuperação de atrasada, `sucesso=N`/`PadroesSemRetorno`, ajuste de quantidade (3 campos, total, volume, Omitir/Zerar, soma 0), INE (nunca IMB), ENC -> IMP (recusa e ressalva), `TpAcaoReenvioImp` configurável (UPD/I) e reenvio UPD, limite de 4 chamadas, backoff de comunicação, resposta desconhecida + reenfileirar, XML indisponível/fallback, ondas TSP, reinício, atrasada, `DiasMaxTodos`, I51 desligada sem tocar disco, reenfileirar entre processos (comando em arquivo), cliente WMS único (DI), filtro de proprietário pelo `CnpjProprietario` do retorno, relatório (modelo, HTML encoded, PDF, agendador, serviço), appsettings (as três chaves `Habilitada` = false), logs sem segredos/XML, regressão da I40 (busca por chave e número+série com I51 ligada; `WEBAPI-I40-Json`, IMP, sem placa/motorista).

## Achados da 3ª revisão do design

R1 instância única (`RegistroClienteWms`, teste); R2 reenfileirar entre processos (`ComandosI51`, teste); R3 proprietário pelo retorno; R4 `ParametrosI60.QtdeVolume` é `decimal?`; R5 `UltimoUnicoEm` gravado antes da chamada; R6 `documentos` vazio = nada a retornar; R7 `DiasMaxTodos`; R8 relatório em try/catch próprio no `Worker`; R9 órfão com Tipo/SubTipo e carimbo do bruto.

## Não verificado

- Nada contra o WMS real (homolog bloqueado pela proprietária BRF): texto do ENC, TpAcao do IMP, sinal de "associada", campo do UNICO e mensagem do `sucesso=N` seguem como padrões configuráveis (ver `docs/ROTEIRO-TESTE-I51-I60.md`).
- Horas: não registradas aqui (esforço só em horas; horas humanas não foram informadas).
- Cortados (marcados como cortáveis no plano): `I60:Max4xxConsecutivos` e disjuntor por ciclo (4xx conta como falha de comunicação comum, nunca terminal).
- Deck e manuais não foram alterados (etapa posterior).
