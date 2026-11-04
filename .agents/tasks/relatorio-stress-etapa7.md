# Relatório de teste de esforço - Etapa 7 (pacote por "foto")

## 1. Objetivo e escopo

Medir vazão, tempo, memória, geração de relatório, e-mails e integridade do fluxo
`GerenciadorPacote` (foto da pasta) -> `ProcessadorLote` -> `Orquestrador` REAL -> relatório HTML/PDF -> e-mail,
sob carga de 500 a 2000 XMLs e relatórios de até 10.000 linhas.

100% local: diretório temporário, `FakeClienteWmsCarga` (fake thread-safe de `IClienteWms`, com latência simulada e
plano de falhas) e `GravadorEmail`. Nenhum `HttpClient`, nenhum WMS real, nenhum dado real. O Orquestrador, os
construtores I10/I30/I50 e o classificador são os de produção (I60 desligada).

Harness: `tests\KitWmsIntegrador.Tests\Stress\` (`StressFact` só roda com `STRESS=1`; sem a variável os testes aparecem
como ignorados e o `dotnet test` padrão não fica lento). Cada cenário foi executado em uma invocação separada do
`dotnet test` (o pico `PeakWorkingSet64` é do processo inteiro).

## 2. Ambiente e parâmetros

| Item | Valor |
|---|---|
| CPU | Intel Core i7-1355U, 12 threads lógicos |
| RAM | 15,7 GB |
| SO | Windows NT 10.0.26300 |
| .NET SDK | 8.0.427, configuração Release |
| Latência configurada do fake | 80 ms por chamada (`Task.Delay(80)`) |
| Latência efetiva medida (Stopwatch) | 87 a 91 ms por chamada (granularidade do timer do Windows) |
| Janela de coleta | 1 s (padrão de produção: 10 s) |
| Estabilização | 0 s (o detector ainda exige 2 observações) |
| Ciclo do gerenciador | 200 ms (padrão de produção: 5 s) |
| Transportes | 25 (nota `i` no transporte `70000 + i % 25`) |
| Arquivos de saída | `.agents\tasks\stress-resultados-base.jsonl` (uma linha JSON por execução) |

Observação: com latência configurada de 80 ms a latência efetiva foi ~90 ms. Os modelos de capacidade abaixo mostram as duas.

## 3. Resultados por cenário (números reais)

Paralelismo = `MaxTransportesConcorrentes`. "Pico WS" = `Process.PeakWorkingSet64`; "Pico heap" = maior `GC.GetTotalMemory(false)` amostrado a cada 200 ms.

| Cenário | Notas | Transp. | Paral. | Chamadas WMS | Pacotes | E-mails | Tempo total (s) | Notas/s | Pico WS (MB) | Pico heap (MB) | Alocado (MB) | GC 0/1/2 | HTML (ms) | PDF (ms) | PDF (KB) | Integridade |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1. 500 XMLs, 25 transp., 1 pacote | 500 | 25 | 1 | 500 | 1 | 1 | 48,32 | 10,35 | 88,8 | 11,3 | 61,5 | 11/4/3 | 5,8 | 111,9 | 107,4 | OK |
| 2. 2000 XMLs | 2000 | 25 | 1 | 2000 | 1 | 1 | 189,17 | 10,57 | 99,6 | 26,4 | 211,6 | 36/6/4 | 10,5 | 223,2 | 256,3 | OK |
| 3. Chegada contínua (20 arq/s por 30 s) | 600 | 25 | 1 | 600 | 5 | 5 | 63,22 | 9,49 | 97,3 | 15,7 | 87,3 | 19/9/2 | 11,0 | 262,6 (soma dos 5) | 92,1 (maior) | OK |
| 4. 1000 XMLs com 10% de erros | 1000 | 25 | 1 | 1345 | 1 | 1 | 127,70 | 7,83 | 92,9 | 18,0 | 116,7 | 20/4/3 | 8,3 | 170,5 | 181,7 | OK |
| Sem latência (custo interno) | 2000 | 25 | 1 | 2000 | 1 | 1 | 4,07 | 491,03 | 100,4 | 23,0 | 210,2 | 38/9/6 | 8,7 | 493,1 | 254,5 | OK |

Integridade verificada em todos os cenários com WMS fake (não é só contagem):
- cada arquivo gerado terminou em `Processados` ou `Erro` exatamente uma vez; nenhum perdido, nenhum duplicado, nenhum inesperado;
- nada ficou na pasta de entrada;
- soma das linhas dos relatórios de todos os pacotes == total de arquivos, sem nota repetida;
- e-mails enviados == pacotes (relatórios gerados) em todos os cenários (1 e-mail por pacote, política `Sempre`);
- concorrência máxima observada no WMS fake <= paralelismo configurado.

Detalhes do cenário 3: 600 arquivos produzidos a 20/s durante 30 s (10% gravados em duas etapas, abertos sem
compartilhamento por ~700 ms; o restante escrito em staging e movido atomicamente). Formaram 5 pacotes
(tamanhos mín/média/máx = 2/120/338). Arquivos que chegaram durante o processamento não entraram no pacote em
andamento e nenhum se perdeu ou duplicou. A produção (20/s) superou a vazão (9,5/s), por isso os pacotes seguintes
acumularam; o sistema drenou tudo ao final.

Detalhes do cenário 4 (1000 XMLs): 100 erros (25 `ProdutoNaoCadastrado` persistente, 25 `EmpresaNaoLocalizada`
persistente, 25 `Outro` por rejeição genérica, 25 `XmlInvalido`), 120 notas que terminam em Processados com ressalva
(30 pendência, 30 "já existe", 30 produto resolvido via I30, 30 empresa resolvida via I10) e 780 sucessos diretos.
Resultado: `Erro\<Categoria>` com 25 arquivos em cada uma das 4 categorias, 900 em Processados, 1345 chamadas
(I50/I10/I30 = 1135/105/105, média de 1,345 chamada por nota), 1 relatório e 1 e-mail. O relatório classificou 60
pendentes (30 pendência + 30 produto resolvido, "integrada - revisar produto" conta como integrada com ressalva) e
130 linhas "não integradas" (100 erros + 30 "já existe", estas últimas em Processados por idempotência).

## 4. Relatório com milhares de linhas (HTML/PDF)

Linhas sintéticas: 30% com erro e mensagem longa, histórico de 5 chamadas, 176 transportes distintos (25 a 200).

| Linhas | Tempo HTML (ms) | Tempo PDF (ms) | Tamanho HTML (KB) | Tamanho PDF (KB) | Páginas PDF | Pico WS (MB) | Pico heap (MB) | Alocado (MB) | GC 0/1/2 | Rodapé "Página X de Y" |
|---|---|---|---|---|---|---|---|---|---|---|
| 1.000 | 6,2 | 140,5 | 1.075 | 232,7 | 94 | 148,2 | 10,8 | 94,0 | 17/4/2 | presente |
| 5.000 | 45,0 | 679,8 | 5.145 | 845,7 | 432 | 106,4 | 37,8 | 439,1 | 75/16/5 | presente |
| 10.000 | 96,3 | 1.266,8 | 10.264 | 1.610,0 | 857 | 133,0 | 58,1 | 864,4 | 146/28/8 | presente |

Crescimento linear (PDF: ~0,13 ms por linha; ~0,16 KB por linha). Não há comportamento quadrático visível até 10.000
linhas. O pico de working set é do processo inteiro (runtime, JIT, framework de testes) e não é monotônico entre as
execuções (o de 1.000 linhas saiu maior que o de 5.000); use o "Pico heap" como medida do relatório. A paginação foi conferida com `PdfReader`
(contagem de páginas) e por texto (`PdfTextoHelper`: rodapé "Página X de Y").

## 5. Comparativo de paralelismo entre transportes (1 vs 2 vs 4)

Mesma carga, 25 transportes, latência 80 ms (efetiva ~90 ms). Cada transporte continua processando suas notas em
sequência; o paralelismo é entre transportes.

| Variante | Paral. | Tempo (s) | Ganho vs 1 | Notas/s | Concorrência máx. observada | Integridade |
|---|---|---|---|---|---|---|
| 500 notas limpas | 1 | 48,26 | 1,00x | 10,36 | 1 | OK |
| 500 notas limpas | 2 | 25,81 | 1,87x | 19,37 | 2 | OK |
| 500 notas limpas | 4 | 14,41 | 3,35x | 34,70 | 4 | OK |
| 300 notas, 10% de erros | 1 | 38,47 | 1,00x | 7,80 | 1 | OK |
| 300 notas, 10% de erros | 2 | 20,20 | 1,90x | 14,85 | 2 | OK |
| 300 notas, 10% de erros | 4 | 11,49 | 3,35x | 26,11 | 4 | OK |

Memória não muda de forma relevante com o paralelismo (pico WS 88 a 98 MB; todas essas execuções ocorreram no mesmo processo, então o pico WS não é comparável entre linhas).

Recomendação de `MaxTransportesConcorrentes`:
- O ganho é quase linear (1,87x com 2; 3,35x com 4), limitado pelo número de transportes distintos do pacote. Pacote com um único transporte não ganha nada.
- O Orquestrador é sem estado entre notas e o harness confirmou integridade (nenhuma duplicata/perda) com 2 e 4.
- O fake NÃO modela rate limit, token compartilhado sob carga nem erros 429/5xx do WMS real, que é o principal risco de subir o paralelismo.
- Manter o padrão 1 (decisão já tomada). Só subir para 2 (depois 4) se o responsável pelo WMS aprovar, após o teste real controlado (seção 10) e observando erros HTTP, tempo de resposta e fila do WMS. Se aprovado, 2 já dobra a capacidade.

## 6. Gargalos, correções e recomendações

Medição do custo interno (cenário "sem latência", 2000 notas): 4,07 s no total, ou seja ~2 ms por nota, incluindo
a janela de coleta de 1 s, ~0,5 s de PDF e as ~5 varreduras do detector. Descontando a janela e o relatório, restam
~1,2 ms por nota para diário (fsync por registro), leitura/parse do XML, movimentação de arquivo e classificação.
Isso é ~1,5% do tempo por nota com 80 ms de latência.

Conclusão: o único gargalo real é a latência serial das chamadas ao WMS (uma nota por vez dentro do transporte, uma
chamada por vez). Descontando as chamadas (nº de notas x latência efetiva), sobram ~3,0 s sobre 48,3 s no cenário 1 (6%) e ~9,4 s sobre
189 s no cenário 2 (5%): janela de 1 s, PDF, varreduras e o atraso de agendamento entre chamadas.

Correções aplicadas: nenhuma. Nenhum candidato simples (fsync por registro do diário, abertura exclusiva por ciclo no
detector, listagem dupla de pastas, ordenação por data de criação, renderização do PDF) apareceu como custo relevante nas
medições; mexer nesses pontos traria risco (o fsync é a garantia de retomada após queda) sem ganho mensurável.

Recomendações (complexas, não implementadas):
1. Paralelismo entre transportes (ganho de 1,9x com 2 e 3,35x com 4) condicionado à aprovação do WMS.
2. Paralelismo de notas dentro de um mesmo transporte exigiria validar ordem e idempotência no WMS; não recomendado antes de medir no homolog.
3. PDF muito grande: com milhares de linhas o anexo cresce linearmente (~0,16 KB/linha; 2000 notas = 256 KB; 10.000 = 1,6 MB). Para pacotes bem maiores, considerar anexar só as linhas com problema (o resumo no corpo já traz os totais).
4. Em produção, ajustar `IntervaloVarreduraSegundos`/`JanelaColetaSegundos` (padrão 5 s/10 s) conforme o tempo aceitável entre a chegada do arquivo e o início do pacote; com milhares de arquivos o custo de cada varredura (uma abertura exclusiva por arquivo) cresce linearmente e foi irrelevante nas medições.
5. Medir a latência real do homolog (o valor de 80 ms é o medido anteriormente) e o impacto de renovação de token antes de definir metas.

## 7. Estimativa de capacidade

Modelo: `notas/hora = 3600 / (L x N) x P`, onde L = latência por chamada, N = chamadas por nota e P = paralelismo efetivo
(<= número de transportes no pacote). Caminhos: N=1 (feliz), N=2 (referência intermediária), N=3 (produto novo ou empresa nova: I50, I30/I10, I50), N=6 (esgotamento: 3x I50 + 3x pré-condição),
N=9 (limite conservador).

| Chamadas por nota | Notas/hora com L=80 ms, P=1 | com L=90 ms (medida), P=1 | P=2 (x1,9) | P=4 (x3,35) |
|---|---|---|---|---|
| 1 (caminho feliz) | 45.000 | 40.000 | 76.000 | 134.000 |
| 2 | 22.500 | 20.000 | 38.000 | 67.000 |
| 3 (produto/empresa novos) | 15.000 | 13.333 | 25.333 | 44.667 |
| 6 (esgotamento) | 7.500 | 6.667 | 12.667 | 22.333 |
| 9 | 5.000 | 4.444 | 8.444 | 14.889 |

Validação do modelo com os números medidos:
- Cenário 2: N=1, L efetiva 89,9 ms: modelo 40.044 notas/h; medido 10,57 notas/s = 38.052 notas/h (95%; a diferença é janela, relatório e varreduras).
- Cenário 4: N=1,345, L efetiva 91 ms: modelo 29.413 notas/h; medido 7,83 notas/s = 28.188 notas/h (96%).
- Paralelismo: 1,87x (P=2) e 3,35x (P=4) medidos vs 2x e 4x ideais (efeito do tempo fixo e da distribuição desigual entre transportes).

Tempo para drenar (P=1, caminho feliz, medido/modelo): 500 notas = 48 s (medido); 2000 notas = 189 s (medido); 10.000 notas = ~16 min (modelo 10.000 x 0,09 s = 900 s + overhead). Com mistura de 10% de erros (N médio 1,345): 1000 notas = 128 s (medido); 10.000 notas = ~21 min.

Fora do caminho do WMS, o integrador sozinho processa ~490 notas/s (cenário sem latência): a capacidade é dominada pela latência do WMS, não pelo integrador.

## 8. E-mail

- Um e-mail por pacote em todos os cenários (cenário 3: 5 pacotes = 5 e-mails). Política padrão `Sempre`.
- Anexo PDF: 107 KB (500 notas), 256 KB (2000 notas), 1,6 MB (10.000 linhas de teste com histórico completo e mensagens longas). O limite típico de SMTP corporativo é 10 a 25 MB; o risco só aparece com dezenas de milhares de linhas por pacote (crescimento linear ~0,16 KB/linha).
- Corpo com resumo e anexo gerados em milissegundos; o tempo de envio SMTP real não foi medido (usado `GravadorEmail`).
- Cenário com muitos pacotes pequenos (chegada contínua) gera muitos e-mails (um por foto). Isso é o comportamento decidido com o usuário; se incomodar, aumentar `JanelaColetaSegundos`.

## 9. Conclusão: pronto para teste real?

Do lado do integrador, sim: sob 500 a 2000 XMLs, chegada contínua e mistura de erros, não houve perda, duplicidade,
e-mail extra ou crescimento anormal de memória (pico de working set ~100 MB, heap < 30 MB), e o relatório escala
linearmente até 10.000 linhas. O que NÃO está validado por este teste (porque o WMS é fake): latência e estabilidade do
WMS real sob carga, expiração/renovação do token durante pacotes longos, rate limit, erros 5xx/timeouts, envio SMTP real e
comportamento do antivírus/rede sobre a pasta de entrada real. Portanto: pronto para um teste real PEQUENO e controlado
(seção 10), não para carga plena. Nada foi executado contra o WMS homolog.

## 10. Roteiro seguro de teste real controlado (NÃO EXECUTADO)

Este roteiro NÃO foi executado. Só executar com aprovação explícita e prévia do usuário.

Pré-requisitos:
1. Aprovação escrita do usuário para usar o WMS homolog, com data/hora e quem acompanha.
2. Ambiente HOMOLOG (nunca produção). Credenciais somente por variáveis de ambiente `WMS__Usuario` e `WMS__Senha` (nunca em arquivo ou commit).
3. 3 a 5 notas conhecidas, de UM transporte, com resultado esperado conhecido (pelo menos uma já existente no WMS para testar o "já existe", se aceitável).
4. Configuração: `MaxTransportesConcorrentes=1`; `Email.Habilitado=false` (ou destinatário de teste); I60 desligada; `Pacote.JanelaColetaSegundos` padrão.
5. Pastas dedicadas e vazias: entrada, `Processados` e `Erro` próprias para o teste (não usar as de produção), fora de `Estado`/`Relatorios` dentro delas.
6. Backup das notas de teste fora da pasta de entrada.

Passo a passo:
1. Subir o Worker apontando para as pastas de teste e confirmar no log a configuração (homolog, paralelismo 1, e-mail desligado).
2. Copiar UMA nota para a entrada. Observar log: foto (1 arquivo), chamadas I50, resultado, move para `Processados` ou `Erro\<Categoria>`, relatório gerado.
3. Conferir no WMS homolog que o documento existe e os dados batem com o XML. Conferir o relatório HTML/PDF.
4. Se tudo conforme, copiar as demais notas (mesmo transporte) e repetir a conferência.
5. Registrar: horário de cada chamada, latência observada por chamada, resposta do WMS, destino do arquivo, tamanho do PDF.

Critério de parada (parar o Worker imediatamente): qualquer resposta inesperada do WMS, erro 401/403/429/5xx, nota duplicada, nota em pasta errada, relatório divergente do WMS, ou qualquer dúvida do operador.

Reversão e limpeza: parar o Worker; desfazer/cancelar no WMS homolog os documentos criados (conforme procedimento do WMS e com o responsável); mover os arquivos de teste de volta; apagar o diário `Estado\pacote-atual.jsonl` do teste; manter os relatórios como evidência.

Só depois de um teste bem-sucedido avaliar: aumentar o volume gradualmente (por exemplo 20, 100, 500 notas) e, separadamente e com aprovação do WMS, `MaxTransportesConcorrentes=2`.

## DECISÕES EM ABERTO

1. Dados SMTP reais (servidor, porta, SSL, usuário/senha, remetente e destinatários) em `email.settings.json` e quando ligar `Email.Habilitado`.
2. Se haverá teste no WMS homolog real (e quando, com quem acompanhando) e a aprovação do roteiro da seção 10.
3. Valores de tempo para produção: `IntervaloVarreduraSegundos` (padrão 5), `JanelaColetaSegundos` (padrão 10), `EstabilizacaoSegundos` (padrão 3), `TimeoutArquivoSegundos` (padrão 300) e `BackoffMaximoSegundos` (padrão 120). Os valores de 1 s/0 s/200 ms usados neste stress servem só para medir.
4. Valor de `MaxTransportesConcorrentes` em produção (padrão 1; 2 ou 4 só após aprovação do WMS).
5. Se o anexo PDF deve conter todas as linhas ou só as com problema quando o pacote for muito grande (hoje: todas).
