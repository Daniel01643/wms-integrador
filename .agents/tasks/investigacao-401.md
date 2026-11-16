# Investigação do 401 no GetToken (produção, SRVAPPALC02)

Data da análise: 16/11/2026. Fonte: `worker.out.log` (82.644 linhas, release 2026.11.06-x), configuração e
variáveis de ambiente do servidor (somente leitura) e código em `ClienteWmsHttp.cs`.
Sem dados de notas e sem segredos neste documento.

## Resumo

- O 401 ocorre **somente** no `GET Integracao/GetToken` (Basic). Nenhum POST recebeu 401.
- Ele **não é intermitente nota a nota**: vem em **janelas contínuas** em que todo GetToken falha, e a janela
  termina sozinha, sem nenhuma mudança do nosso lado. Dentro de cada janela não houve nenhum 200 intercalado.
- A credencial do servidor está correta: a sonda de hoje (1 GET) retornou **200**.
- Causa mais provável: **estado de autenticação do lado do WMS/Assist** (conta/usuário, bloqueio temporário,
  propagação ou manutenção) por alguns minutos. Do nosso lado há um **agravante** (sem circuit breaker, cada
  nota refaz 3 GetToken), que não causa o 401 mas o multiplica.

## Evidências

### Totais
| Item | Valor |
|---|---|
| GetToken enviados | 1.371 |
| GetToken 401 | 1.352 (767 tentativas com retry + 585 definitivos) |
| GetToken 200 | 20 (contagem do orquestrador; cerca de 1 por pacote bem-sucedido, incluindo o do início do log em homologação `:8888`) |
| POST 401 | 0 |
| Latência do GetToken em 401 | 6 a 9 ms em média (máx. 109 ms), ou seja, rejeição imediata na borda de autenticação |
| Latência do GetToken em 200 | cerca de 118 ms |

Os 200 aparecem **uma vez por pacote** (o token fica em cache e serve o pacote inteiro, até 188 notas e 554 POSTs
com um único GetToken). Isso mostra que o cache funciona e que, com a conta saudável, não há 401.

### Janelas de 401 (padrão por pacote: horário pelo id do pacote)
| Dia | Pacote (hora de abertura) | GetToken 401 | GetToken 200 | Observação |
|---|---|---|---|---|
| 06/11 | 13:56:17, 13:56:32, 13:56:50, 13:57:06 | 22, 66, 49, 51 | 0 | durante provisionamento da conta |
| 06/11 | 14:01:32 | 0 | 1 | volta ao normal sem alteração nossa |
| 06/11 | 17:01:59, 17:07:00, 17:08:37 | 3 cada | 0 | 3 notas, 1 GetToken x3 por nota |
| 06/11 | 17:13:22 | 0 | 1 | volta ao normal |
| 06/11 | 17:57:41 | 5 | 0 | |
| 06/11 | 18:21:02 | 0 | 1 | volta ao normal |
| 15/11 | 17:27:49 | **606** | 0 | 202 notas x 3 tentativas, 100% falha |
| 15/11 | 17:33:15 | **541**, depois **1 x 200** | 1 | 401 contínuo (aprox. 180 notas), vira 200 e segue com 270 POST 200 |
| 15/11 | 18:34:54 | 3 | 0 | 1 nota |
| 15/11 | 20:22:17 | 0 | 1 | normal |

Sequência no pacote de 17:33:15: `401 x541, 200 x1, POST 200 x270`. A virada de 401 para 200 é única e definitiva
dentro do pacote, o que descarta intermitência aleatória e concorrência.

Duração estimada da janela de 15/11: cada nota gasta cerca de 1,5 s (backoff 0,5 s + 1 s). Isso dá cerca de
5 min no pacote das 17:27 e cerca de 4,5 min no das 17:33, ou seja, **aprox. 10 minutos contínuos (17:27 a 17:38)**,
e novamente uma janela curta às 18:34. Dias sem janela (09, 10, 11, 13/11) tiveram 200 de primeira.

Em 15/11 (domingo) o primeiro pacote após 13/11 19:17 já falhou. Não houve falha prévia nossa que pudesse ter
disparado bloqueio por tentativas erradas.

### Concorrência e cache de token (nosso código)
- `ClienteWmsHttp` é singleton e protege o GetToken com `SemaphoreSlim` e dupla verificação. Com isso, **não há
  GetToken concorrente**: o log não mostra pares de GetToken sobrepostos, e o paralelismo de transportes
  (`ProcessadorLote`, `MaxTransportesConcorrentes`) passa pelo mesmo cache.
- O cache só guarda token **bem-sucedido**. Se o GetToken falha 3 vezes, a nota é para `Erro` e a **nota seguinte
  recomeça do zero** (mais 3 GetToken). Não existe circuit breaker nem espera maior após falhas repetidas.
  Resultado em 15/11: 1.147 GetToken 401 em cerca de 10 minutos (cerca de 2 por segundo) contra uma conta que já
  estava rejeitando. Isso não explica o início do 401, mas pode **prolongar** um bloqueio por tentativas, se o WMS
  tiver esse mecanismo (não confirmado).

### Credencial e codificação
- Variáveis de máquina `WMS__Usuario` (8 caracteres, `BLUEFOOD`) e `WMS__Senha` (16 caracteres): sem espaço, sem
  quebra de linha, sem `:`, sem caractere fora de ASCII imprimível, sem espaço nas pontas. Há caracteres especiais
  (ASCII), que UTF-8 e Latin-1 codificam igual, então **encoding não é a causa**.
- Não há variável de usuário/processo concorrente, nem `AppEnvironmentExtra`/`AppParameters` no NSSM sobrepondo.
- `appsettings.Production.json` não contém usuário/senha; `BaseUrl` = produção sem `:8888`, `TimeoutSegundos` = 30,
  `MaxTentativas` = 3. O único GetToken com `:8888` do log é do início (homologação), não de produção.

### Sonda de hoje
1 GET em `<BaseUrl>/Integracao/GetToken` com as credenciais de ambiente da máquina (Basic UTF-8): **HTTP 200**
(10:36 no servidor; corpo de 247 bytes, não impresso; cookie de sessão presente e omitido). Confirma credencial,
codificação e TLS corretos hoje. Nenhum POST foi enviado.

### Outros usos de BLUEFOOD
Os diários registram que o **mesmo usuário e senha** são usados em homologação (`:8888`) e produção
(06/11: "conta de produção sendo criada com os mesmos dados"). Em 12/11 houve rodada de testes com 401 por senha
digitada errada no terminal. Não há como saber, pelos nossos registros, se algum teste em homologação com esse
usuário ocorreu em 15/11 por volta de 17:27 ou 18:34. Isso precisa ser conferido com a equipe e com o fornecedor.

## Causa(s) mais provável(is)

1. **Lado do fornecedor (Assist), mais provável.** O usuário passa a ser rejeitado por janelas de minutos e volta
   sozinho, com a mesma credencial. Padrões compatíveis: usuário compartilhado entre homologação e produção que
   é bloqueado/sincronizado entre ambientes, bloqueio temporário por tentativas, replicação ou renovação da conta,
   manutenção do serviço de autenticação, ou limite de sessões por usuário.
2. **Nosso lado, fator agravante (não é a origem):** ausência de circuit breaker e nova rodada de 3 GetToken por
   nota; ao falhar, o pacote inteiro vai para `Erro` em minutos (383 notas de 15/11).
3. **Descartado do nosso lado:** encoding/formatação da credencial, divergência de config vs. ambiente, URL de
   homologação em produção, concorrência de GetToken, cache de token (funciona), TLS (corrigido em 06/11).

## O que é nosso e o que é do fornecedor

| Lado | Item |
|---|---|
| Fornecedor (Assist) | Por que o usuário BLUEFOOD é rejeitado em janelas de cerca de 5 a 10 minutos (15/11 17:27 a 17:38 e 18:34; 06/11 em vários horários). Política de bloqueio, sessões e ambientes do usuário |
| Nosso | Não martelar o WMS quando o GetToken falha; não mandar o pacote todo para `Erro` por uma janela curta; usar credencial dedicada de produção; alerta/monitor de GetToken 401 |

## Correções sugeridas no nosso lado (a decidir, fora deste passo)

1. **Circuit breaker no GetToken**: após o GetToken falhar com 401 por X tentativas, pausar (ex.: 60 s, depois
   crescente) antes de nova tentativa e **manter as notas pendentes** em vez de mover cada uma para `Erro`. Muda
   o comportamento visível (notas ficam na entrada por alguns minutos), por isso exige OK do usuário.
2. **Falha de autenticação não é erro de nota**: tratar 401 de GetToken como falha do pacote, com reprocesso
   automático quando o token voltar, e e-mail de alerta.
3. **Credencial exclusiva de produção** (diferente da homologação), se o fornecedor confirmar interferência.
4. **Monitor**: contar GetToken 401 por pacote no relatório/e-mail.
5. **Reprocessar as 383 notas de 15/11** (em `Erro\pacote_20261115_*`) somente após a correção e com OK do
   usuário. Nenhum arquivo foi movido nesta investigação.

## Perguntas para o fornecedor (Assist)

1. O usuário BLUEFOOD tem bloqueio temporário por tentativas, limite de sessões ou token único por usuário? Qual
   a janela e como é liberado?
2. Houve manutenção, deploy, replicação de contas ou reinício do serviço de autenticação em 15/11 entre 17:27 e
   17:38 e por volta de 18:34 (horário local do servidor)? E em 06/11 entre 13:56 e 14:01, 17:01 e 17:08, 17:57?
3. O usuário é compartilhado entre homologação (`:8888`) e produção no lado de vocês? Uma operação em um
   ambiente invalida o outro?
4. Qual o tempo de vida do token e há revogação do token anterior quando um novo GetToken é emitido?
5. Existe limite de requisições ao GetToken por IP/usuário (rate limit)? Qual código retornam quando atingido
   (hoje vemos 401 em 6 a 9 ms)?
6. Podem informar, nos logs de vocês, o motivo da rejeição dos GetToken do IP 200.200.200.213 nos horários acima?
7. É possível criar um usuário exclusivo para produção?

## Próximos passos

1. Enviar as perguntas acima ao fornecedor, com os horários da tabela.
2. Confirmar internamente se houve uso de BLUEFOOD (homologação ou testes) em 15/11 por volta de 17:27 e 18:34.
3. Decidir com o usuário o circuit breaker (item 1) antes de implementar.
4. Só então reprocessar as 383 notas de `Erro\pacote_20261115_*`.
