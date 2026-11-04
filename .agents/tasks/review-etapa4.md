# Construtores de payload I50/I60/I10 + config operacional e I50 (Etapa 4)

A etapa adiciona três construtores (`ConstrutorI50/I60/I10`) no Core que montam os DTOs tipados dos payloads WMS a partir de uma `NotaFiscal` já parseada, sem nenhuma chamada HTTP. A regra central — 1 nota = 1 DocCarga = 1 I50, com `nroagrupseparacao` recebendo o número do transporte — está implementada e coberta por teste com o XML real. Os valores operacionais (CNPJs, armazém, depósito) e os parâmetros da I50 (tipo de carga, separação 1/2, acondicionamento, qtdMandante) saem de duas seções de config novas (`Operacional`, `I50`), com defaults BRF mas sobrescrevíveis por env — nada hardcoded nos construtores. A serialização foi centralizada num `JsonWms.Opcoes` compartilhado com o cliente, garantindo que o JSON inspecionado nos testes é o mesmo do POST futuro.

Watch for: campos "a confirmar" foram marcados com comentários `// A CONFIRMAR` em vez do literal `TODO` que a especificação pediu (likely); o helper `FormatoWms.Decimal(decimal)` é um no-op e `FormatoWms.Decimal(decimal,int)` nunca é usado (confirmed) — a formatação decimal correta vem do `System.Text.Json` por padrão, não do helper. Nenhum dos dois bloqueia.

**Verdict**: APPROVED

## High-level view

A granularidade pedida está correta e testada: `ConstrutorI50.Construir` produz um único `PedidoI50` por nota, com `Nroagrupseparacao = numeroTransporte ?? nota.NumeroTransporte` preservando os zeros à esquerda (string). Transporte vazio lança `ArgumentException`, deixando o caso B (nota sem transporte) para o Orquestrador da Etapa 5. A amarração I50↔I60 usa `numeroPedido = nota.Numero` nos dois construtores, e há um teste que compara os dois valores para a mesma nota.

Os nomes de propriedade JSON sensíveis a caixa que a especificação exige (`NroEmb`, `Tpsit`, `Tpnota`, `Tpsep`, `DhprevEmbarque`, `NOTAS_VENDA`, `PEDIDO`, `EMPRESA`, `ITEM_PEDIDO`, `ITEM_NOTA_VENDA`) estão todos nos `[JsonPropertyName]` dos DTOs, conferidos contra `docs/TEMPLATES-INTERFACES.md` e asseridos por testes de serialização.

A configuração cumpre o pedido explícito do usuário: `tipoCarga`/`tipoSeparacao`/`tipoSeparacao2` vêm de `OpcoesI50`, aplicados ao cabeçalho e replicados a cada item; CNPJs/armazém/depósito vêm de `OpcoesOperacionais`. Ambas registradas no `Program.cs` seguindo o padrão `OpcoesEntrada`/`OpcoesWms`, com as seções correspondentes no `appsettings.json` comentadas e sem segredos.

Datas usam `dd/MM/yyyy HH:mm:ss` com `CultureInfo.InvariantCulture`; decimais saem com ponto por serializarem como `decimal` sob opções invariantes (não por efeito do helper `FormatoWms.Decimal`, que é inerte). Build Debug/Release limpos e 59 testes verdes (30 pré-existentes + 29 novos) estão registrados na evidência de verificação.

As únicas lacunas são de higiene, não de comportamento: a marcação dos "a confirmar" diverge da letra da instrução (comentário em vez de `TODO`), e há dois helpers decimais ociosos em `FormatoWms`.

<details>
<summary>Issues (3)</summary>

1. **Marcação "a confirmar" vs TODO** — a especificação pediu campos 'a confirmar' marcados com `TODO`; o código usa comentários `// A CONFIRMAR`. Semanticamente equivalente e bem documentado, mas se a convenção `TODO` for um requisito de ferramenta (ex.: varredura de pendências), padronizar para `// TODO:` ou `// TODO (a confirmar):`.
2. **`FormatoWms.Decimal(decimal)` é no-op** — retorna o valor sem alteração e não é chamado por nenhum construtor; confunde quem lê, pois sugere que a política decimal está nesse helper quando na verdade vem do `System.Text.Json`. Remover ou documentar que a formatação decimal é responsabilidade do serializador.
3. **`FormatoWms.Decimal(decimal,int casas)` nunca usado** — helper de string com casas fixas sem nenhum ponto de uso. Remover enquanto não há campo que exija largura fixa, ou cobrir com teste quando passar a ser usado.

</details>

<details>
<summary>Details</summary>

## Formatação decimal e helpers mortos em FormatoWms

Os decimais saem com ponto, mas o comportamento vem do `System.Text.Json`, não do `FormatoWms`: os campos decimais nos DTOs são `decimal` (não string), e o serializador formata `decimal` de forma cultura-invariante por padrão (testes confirmam `"quantidadeProduto":12` e `"pesoLiquidoKg":275.64` sem vírgula). O helper `FormatoWms.Decimal(decimal)` apenas retorna o argumento (no-op) e não é chamado por nenhum construtor; o `FormatoWms.Decimal(decimal,int)` que produziria string com casas fixas também não tem uso. São helpers mortos — não afetam o payload, mas induzem a crer que a política decimal mora no `FormatoWms`, quando está no serializador. Remover ou documentar (confirmed). As datas, por contraste, de fato passam por `FormatoWms.Data` com `"dd/MM/yyyy HH:mm:ss"` invariante e sobrecargas `DateTimeOffset?`/`DateOnly?` que retornam `null` para omitir via ignore-null.

## Marcação dos campos "a confirmar"

Todos os campos de semântica ainda aberta estão sinalizados — `tipoAcondicionamento` (PC vs PP), `dataprev`/`DhprevEmbarque`, `qtdMandante` (U vs E), `Tpsit`/`tipoOperacaoNota` (IMB vs INE), conteúdo de `pedidos[]` (nNF vs xPed), `codigoempresa`/`siglaempresa`/`tpoperacao` da I10 — com comentários nos DTOs/construtores e tabela em `docs/TEMPLATES-INTERFACES.md`. A divergência é só de convenção: foi usado `// A CONFIRMAR`, não o literal `TODO` que a especificação pediu. Se a convenção `TODO` for esperada por alguma varredura de pendências, padronizar (likely).

## Cobertura de testes

Os 29 testes novos exercitam os três construtores contra o XML real `NFE...1612.XML` (offline): granularidade, `nroagrupseparacao` com zeros, override de transporte, derivação do `numeroPedido`, CNPJs/config no cabeçalho e itens, datas, decimais com ponto, omissão de nulos, amarração I50↔I60 e os nomes JSON exatos.

Não testado (fora de escopo, aceitável): `ConstrutorI60` quando `Volumes`/`ValorTotal` são nulos (o código trata com `?? 0m`, mas não há fixture com esses campos ausentes para travar o fallback); `ParseDecimalOuZero` com `QuantidadeVolumes` não-numérico (retorna 0, sem teste do caminho inválido). Caminhos de borda que um fixture adicional ou a Etapa 5 podem cobrir; não bloqueiam.

</details>

<details>
<summary>Arquivos alterados</summary>

- `src/KitWmsIntegrador.Core/ClienteWms/JsonWms.cs` (novo) — política de serialização compartilhada cliente/construtores.
- `src/KitWmsIntegrador.Core/ClienteWms/ClienteWmsHttp.cs` — passa a referenciar `JsonWms.Opcoes` (refatoração sem mudança de comportamento).
- `src/KitWmsIntegrador.Core/Configuracao/OpcoesOperacionais.cs` (novo) — CNPJs/depósito BRF, parametrizáveis.
- `src/KitWmsIntegrador.Core/Configuracao/OpcoesI50.cs` (novo) — tipoCarga/separação/acondicionamento/qtdMandante.
- `src/KitWmsIntegrador.Core/Construtores/FormatoWms.cs` (novo) — helpers de data/decimal/zeros (helpers decimais ociosos).
- `src/KitWmsIntegrador.Core/Construtores/Dtos/PayloadI50.cs`, `PayloadI60.cs`, `PayloadI10.cs` (novos) — DTOs com `[JsonPropertyName]` exatos.
- `src/KitWmsIntegrador.Core/Construtores/ConstrutorI50.cs`, `ConstrutorI60.cs`, `ConstrutorI10.cs` (novos) — implementações.
- `src/KitWmsIntegrador.Core/Construtores/IConstrutorI50.cs`, `IConstrutorI60.cs` (editados), `IConstrutorI10.cs` (novo) — contratos reais.
- `src/KitWmsIntegrador.Worker/Program.cs`, `appsettings.json` — DI + seções de config (sem segredos).
- `tests/KitWmsIntegrador.Tests/ConstrutorI50Tests.cs`, `ConstrutorI60Tests.cs`, `ConstrutorI10Tests.cs` (novos) — 29 testes offline.
- `docs/TEMPLATES-INTERFACES.md` — contrato de nomes/valores fixos (fonte de verdade).

Diff completo: `git diff 5e79eb2..HEAD` em `c:\wms-integrador`.

</details>
