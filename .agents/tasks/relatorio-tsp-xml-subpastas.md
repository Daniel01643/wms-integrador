# Relatório: TSP com XML solto e ZIP com subpastas

## O que foi feito
- `ClassificadorEntrada`: PuxadaTSP passa a aceitar `.xml` e `.zip` (primeiro nível apenas).
- `ProcessadorLote`: XMLs soltos da PuxadaTSP do pacote formam UMA onda `TSP-AAAAMMDD-NN` (sem título), via `RegistroOndasTsp.ObterOuCriar("", "")` (sem hash, sem reaproveitamento; sequencial compartilhado com ZIPs, alocado na ordem de chegada). ZIPs seguem com uma onda por ZIP.
- `LeitorZipSeguro`: já extraía XMLs de subpastas usando só o nome do arquivo em pasta numerada (sem colisão, sem traversal). Nenhuma mudança de código; comportamento agora coberto por testes.
- Mensagem de ExtensaoInvalida na TSP: "aceita apenas ZIP ou XML".
- Docs: ESCOPO, ARQUITETURA, OPERACAO, ROTEIRO (novos casos), manuais (MD, HTML, PDF).

## Decisões
- Rótulo de Origem do XML solto: `PuxadaTSP` com Zip vazio (distingue de onda de ZIP, que tem Zip preenchido).
- Só `.zip` (sem `.rar`/`.7z`).

## Verificação
Ver `verificacao-tsp-xml-subpastas.md`: build 0 avisos; 851 aprovados, 9 ignorados.

## Esforço
Horas de IA estimadas: aprox. 0,7 h. Horas humanas: a informar pelo usuário.
