# Tela de devolução da I40 (fase 2): placa opcional, IMP por padrão, Kestrel isolado no Worker

A entrega torna a placa opcional na I40 (desligada: IMP, placa e motorista em branco; ligada: ICD, placa obrigatória e motorista fixo configurado) e adiciona no Worker uma tela web de chaves (Kestrel, página única autocontida) que valida, localiza o XML, monta um lote de até 30 notas e envia pelo `IClienteWms` existente. A tela só sobe com `Devolucao:Habilitada=true`, que continua `false` nos dois appsettings, assim como `I60:Habilitada`. Falhas da tela são capturadas e não derrubam a integração. Base revisada: `d361cc0..292d186` (commits dd8c6d5, 5b727be, df9a83f e docs).

Watch for: (1) o mapeamento da resposta por documento tem um último recurso que consolida todos os retornos quando nenhum casa com a nota, o que pode marcar sucesso para uma nota que o WMS não confirmou (possible, não bloqueante); (2) `ex.Message` de falha do cliente WMS é devolvido ao navegador sem filtro (possible, não bloqueante); (3) a evidência de build/testes é o relatório do coder (451 aprovados, 0 falhas, 9 ignorados, 0 avisos/0 erros em Debug e Release), não reexecutada nesta revisão, conforme instrução.

**Verdict**: APPROVED

## High-level view

A regra da placa vive só no `ConstrutorI40`: `enviarPlaca=false` usa `TipoOperacaoNota` (IMP) e deixa placa e motorista vazios sem validar nada; `enviarPlaca=true` exige placa e `NomeMotorista`, e usa `TipoOperacaoNotaComPlaca` (ICD). `InverterPartes` segue `false` por padrão. Os defaults de `OpcoesDevolucao`, do appsettings do Worker e do exemplo de produção coincidem (IMP, ICD, `EnviarPlacaPadrao=false`, porta 8085, `Habilitada=false`).

`ServicoDevolucao` concentra a lógica sem HTTP: normaliza e deduplica, aplica o limite de 30 depois da deduplicação, valida 44 dígitos e DV no servidor, e trata cada chave de forma independente. Chave inválida, XML ausente ou erro de leitura viram resultado local e não bloqueiam as demais. As válidas vão em uma única requisição I40 e a resposta é mapeada por número de documento.

A tela roda em um host Kestrel separado, dentro de um `BackgroundService` com try/catch total, sem login e com cabeçalhos de segurança. Não há pacote NuGet novo: usa `FrameworkReference Microsoft.AspNetCore.App`. Os testes sobem o mesmo `WebApplication` em porta efêmera com `FakeClienteWms`, então nada vai ao WMS.

<details>
<summary>Issues (3)</summary>

1. **Fallback de mapeamento por documento** — em `MapearResposta`, quando nenhum retorno casa com a nota e a contagem não bate, todos os retornos são consolidados; uma nota ausente da resposta pode herdar o sucesso das outras (possible). Considerar marcar como erro "WMS não retornou esta nota" quando houver retornos com documento preenchido que não incluam a nota.
2. **Mensagem de exceção exposta ao navegador** — no envio, `ex.Message` do cliente WMS vira a mensagem exibida na tela (possible). Hoje a tela é interna e sem login; se a mensagem puder conter URL ou detalhes de infraestrutura, trocar por texto genérico e deixar o detalhe só no log.
3. **Evidência só do relatório do coder** — build e testes não foram reexecutados aqui; o relatório traz as contagens e os commits trazem os testes novos. Sem ação, apenas registro.

</details>

<details>
<summary>Details</summary>

### Mapeamento da resposta por documento e último recurso

`MapearResposta` casa o `documento` do retorno com o número da nota (ignorando zeros à esquerda). Se nada casa e há um retorno por nota sem documento, usa a posição. Caso contrário consolida todos os retornos e exige que todos sejam "S". Esse último ramo é conservador para falhas (qualquer erro marca todas as notas como erro), mas pode produzir falso sucesso para uma nota que o WMS simplesmente não mencionou quando os demais retornos são "S" (possible; depende de o WMS omitir notas, o que não foi observado).

### Exposição de mensagens na tela

Em falha de comunicação, `falha = ex.Message` é copiado para o resultado de cada chave e aparece na tabela e na auditoria. Erros inesperados do serviço, por outro lado, retornam um 500 genérico, e há teste para isso. A assimetria é pequena, mas as duas rotas poderiam seguir a mesma política (possible).

### Auditoria e segredos

A auditoria grava data, IP de origem, placa, chave, nota, status, transação e mensagem, uma linha por chave, sem JSON e sem credenciais; `;` e quebras de linha são neutralizados e a mensagem é truncada em 300 caracteres. Falha de gravação só gera log. O log do Worker repete chave, nota, status, transação e placa.

### Isolamento e ciclo de vida

O `ServidorTelaDevolucao` resolve opções e serviços dentro do try/catch, retorna sem abrir porta quando desligado ou com porta inválida, e registra falhas (porta ocupada incluída) sem propagar. O host da tela não trata Ctrl+C (`SemCicloDeVida`); a parada vem do `stoppingToken` do host principal. Os testes cobrem desabilitada, habilitada, porta inválida, serviço ausente e porta ocupada.

### Página e acessibilidade

A página tem `lang="pt-BR"`, labels associados, `aria-live`/`role="alert"` para mensagens, foco visível, botão de remover com `aria-label`, Enter adiciona a chave (compatível com leitor de código de barras) e colar várias chaves as separa. O único recurso de imagem é o logo `data:` base64; não há CDN, `link`, `@import` ou URL externa (verificado por busca no HTML). A CSP restringe a `'self'` com `unsafe-inline` apenas para o script e o estilo embutidos, o que é coerente com a página única. O checkbox de placa parte de `EnviarPlacaPadrao` via `/api/config`; após envio, só as chaves sem sucesso permanecem para reenvio.

### Validação, limite e duplicadas

O cliente valida 44 dígitos e DV na tela; o servidor revalida com `LocalizadorXmlDevolucao.ValidarChave`. O limite (30, configurável) é aplicado após a deduplicação, e o corpo da requisição é limitado a 64 KB. A placa é normalizada e exigida (5 a 8 alfanuméricos) só quando `EnviarPlaca=true`.

### Cobertura de testes

Há testes unitários do construtor (regra da placa nos dois estados, `InverterPartes`), do validador, do serviço (lote, mapeamento por documento, chaves ausentes) e do endpoint (IMP sem placa, 400 para placa ausente, 31 chaves, lista vazia, JSON malformado, falha do WMS, 500 genérico, isolamento do servidor). `FakeClienteWms` garante que nada é enviado ao WMS. Não testado: o fallback de mapeamento descrito acima com um retorno omitido, e o comportamento real em Windows Server 2012 R2 sob NSSM (o relatório declara que não foi validado por falta de acesso ao servidor).

### Documentação e manuais

Porta 8085, `EnviarPlacaPadrao=false`, auditoria em `<Estado>\devolucao`, firewall restrito e "como ligar sem ligar" aparecem de forma coerente no código, no appsettings, no manual técnico, em OPERACAO, INSTALACAO e ROTEIRO. O manual do usuário explica o checkbox desmarcado por padrão e o campo de placa. Os HTML/PDF foram regenerados nos commits. O resultado do ICD ("Erro na criacao do controlador de veiculo") e a razão da placa começar desligada estão registrados. O diário traz 5,0 h de IA e deixa as horas humanas para o usuário, e nenhum valor monetário foi adicionado nas linhas incluídas (busca por R$, US$ e valor/hora sem resultados).

</details>

<details>
<summary>File map</summary>

- `Core/Configuracao/OpcoesDevolucao.cs`: IMP padrão, ICD com placa, `EnviarPlacaPadrao`, porta, limite, pasta de auditoria
- `Core/Construtores/ConstrutorI40.cs`, `IConstrutorI40.cs`: flag `enviarPlaca`, validações só com placa
- `Core/Devolucao/*`: serviço, validador, auditoria, modelos
- `Worker/TelaDevolucao/*`: host Kestrel isolado, endpoints, página HTML e logo embutidos
- `Worker/Program.cs`, `.csproj`, `appsettings.json`, `EnvioManualI40.cs`: DI, FrameworkReference, defaults, `--placa` opcional
- `deploy/config-exemplo/appsettings.Production.json`: mesma regra, `Habilitada=false`
- `tests/*`: construtor, serviço, validador, endpoint, ambiente fake
- `docs/*`, `docs/manuais/*`: arquitetura, roteiro, operação, instalação, diário, manuais MD/HTML/PDF

Diff completo: `git diff d361cc0 HEAD`.

</details>
