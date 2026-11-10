# Plano A (I60) pausado e Plano B (I40 fase 1): registro e envio manual de devolução

Dois blocos: documentação do Plano A pausado (diário 10/11, histórico, controles de esforço, deck, manuais) e a fase 1 da devolução pela I40 (opções, construtor, localizador de XML, modo manual `--enviar-i40`, testes, docs). As decisões do usuário foram implementadas como pedido, as flags versionadas estão desligadas e o registro do Plano A bate com os fatos. Revisão por leitura do código e docs mais o relatório do integrador (`plano-b-fase1.md`); build, testes, PDF e BOM não foram reexecutados, e a evidência deles está no relatório (build 0/0, 400 aprovados, 9 ignorados; PDF com 12 páginas; 0 U+FFFD).

Watch for: o default de `InverterPartes` (false) e o formato array de `ITEM_NOTA_FISCAL` são hipóteses a validar no teste ao vivo (confirmed, documentados como tal); o nome da interface `WEBAPI-I40-Json` está "a confirmar" (confirmed); o modo manual grava o JSON enviado em disco (confirmed, sem credenciais).

**Verdict**: APPROVED

## High-level view

O construtor da I40 reaproveita o mapeamento da I60 (CNPJs operacionais, transportadora com fallback no armazém, volume como soma das caixas) e só diverge no que a devolução exige: `tipoNota` DE, `tipoOperacaoNota` ICD, placa obrigatória e motorista fixo configurável. A inversão de partes é um único ponto, que troca emitente/remetente por destinatário/local de entrega, e `cnpjNota` acompanha o emitente do envio. Nenhuma lógica depende da I60.

O modo manual é o único caminho que envia algo ao WMS, e só com `--enviar-i40` e credenciais por ambiente. `Devolucao:Habilitada` e `I60:Habilitada` ficam `false` nos appsettings versionados, e a tela web ficou para a fase 2.

A documentação do Plano A registra as 15 tentativas com as mensagens exatas, descarta a hipótese do fator de conversão (embalagem 1 para 1), registra a pausa e as perguntas 1 a 4 ao fornecedor. O esforço está só em horas, com 3,5 h humanas em 10/11, e a aritmética fecha.

<details>
<summary>Issues (3)</summary>

1. **Default de InverterPartes** — fica `false` até o teste ao vivo (invertido e não invertido); registrar o resultado e fixar o default na fase 2. Não bloqueante.
2. **Formato de ITEM_NOTA_FISCAL e nome da interface** — array e `WEBAPI-I40-Json` são hipóteses; confirmar na resposta da interface. Não bloqueante.
3. **Arquivo de envio manual** — `envio-manual-i40/*.txt` grava o JSON com CNPJs e nota ao lado do binário; garantir que a pasta fique fora do git e do pacote de deploy. Não bloqueante (possible).

</details>

<details>
<summary>Details</summary>

### Decisões do Plano B no construtor e nas opções

Conferido em `ConstrutorI40.cs` e `OpcoesDevolucao.cs`. O CFOP vem de `Itens[0].Cfop` sem conversão. `TipoOperacaoNota` (default ICD) e `TipoNota` (default DE) são configuráveis. A placa vazia lança `ArgumentException`, e `NomeMotorista` vazio (default) lança `InvalidOperationException` com a mensagem "Devolucao:NomeMotorista não configurado...". Com `InverterPartes=true`, `cnpjEmitente`/`cnpjRemetente`/`cnpjNota` recebem o destinatário original, e `cnpjDestinatario`/`cnpjLocalEntrega` recebem o emitente original. `ITEM_NOTA_FISCAL` é serializado como array e o comentário do DTO documenta a divergência com o exemplo do manual (confirmed). O construtor cria `OpcoesDevolucao` padrão quando as opções não são injetadas, o que é inofensivo porque o motorista vazio falha.

### Segurança do modo manual

`EnvioManualI40` lê `WMS__Usuario`/`WMS__Senha` do ambiente, aborta sem elas e nunca as registra. O log e o arquivo gravam JSON, URL, resultado e retornos, sem credencial (confirmed). Nada é enviado por build ou testes: o envio só ocorre dentro de `ExecutarAsync`, acionado por `--enviar-i40`. O `appsettings.json` traz `NomeMotorista` vazio e comentário avisando que credencial é só por env. O localizador é somente leitura, valida 44 dígitos e DV módulo 11, escolhe o `pacote_*` mais recente e não procura em Erro.

### Registro do Plano A e esforço

O histórico lista 15 tentativas com as mensagens exatas (por exemplo "Total de volume(s) do pedido(doccarga): 0, não confere com a qtdvolume da nota venda: N" e "Quantidade caixa do(s) N pedido(s) não confere com a quantidade da nota venda 0"). Registra o teste A mínimo passando sem ORA-20901 e as versões seguintes com ORA-20901. A hipótese do fator de conversão consta como descartada pelo usuário, a pausa aguarda o fornecedor, e há quatro perguntas numeradas. Nas linhas adicionadas, a busca por R$, US$, valor/hora, câmbio, dólar e "reais" não retornou nada, e o único "~" que a busca poderia pegar no deck está tratado pelo relatório ("Aprox."). A aritmética (34,65 / 20,90 / 55,55; W46 3,5/2,5/6,0) está coerente no relatório e coincide com os trechos que li; as 3,5 h humanas são as informadas pelo usuário.

### Testes

Há 14 testes no `ConstrutorI40Tests`, 9 no `LocalizadorXmlDevolucaoTests` e 2 no `OpcoesDevolucaoTests`, cobrindo as áreas pedidas (construtor, localizador com DV módulo 11, defaults). Não cobertos: o `EnvioManualI40` ponta a ponta (depende do WMS) e o envio real, o que é coerente com a regra de nada enviar por testes.

### Observações de revisão

O mojibake visto na saída do console do PowerShell vem da codificação do terminal. O relatório confirma 0 U+FFFD e BOM preservado, e não reexecutei essa verificação. Nenhuma alteração fora do escopo: os 35 arquivos do relatório são todos dos dois blocos.

</details>

<details>
<summary>File map</summary>

- `Core/Configuracao/OpcoesDevolucao.cs`: opções e defaults da devolução
- `Core/ClienteWms/InterfaceWms.cs`: interface I40
- `Core/Construtores/ConstrutorI40.cs`, `IConstrutorI40.cs`, `Dtos/PayloadI40.cs`: montagem do payload
- `Core/Devolucao/LocalizadorXmlDevolucao.cs`: busca do XML por chave
- `Worker/EnvioManualI40.cs`, `Program.cs`, `appsettings.json`: modo manual e DI
- `deploy/config-exemplo/appsettings.Production.json`: flags desligadas
- `tests/*`: três classes de teste novas
- `docs/*`: Plano A, esforço, deck, manuais, arquitetura e roteiro I40

Diff completo: `git diff e7084cc~1 HEAD`.

</details>
