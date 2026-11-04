# Plano de Implementação — Etapa 6: Orquestrador de Integração (reativo)

Projeto: KitWmsIntegrador (.NET 8, C#). Solução: `c:\wms-integrador\KitWmsIntegrador.sln`.
Baseline verificado: build verde, **75 testes passando** (`dotnet test` em Debug).
Comando dotnet: usar `dotnet` (resolve no PATH após `$env:Path += ';C:\Program Files\dotnet'`)
ou o caminho absoluto `"C:\Program Files\dotnet\dotnet.exe"`.

Comando de build/test padrão (usado em toda verificação abaixo):
```
dotnet test c:\wms-integrador\KitWmsIntegrador.sln -c Debug --nologo
```
Para rodar uma classe de teste isolada:
```
dotnet test c:\wms-integrador\KitWmsIntegrador.sln -c Debug --nologo --filter "FullyQualifiedName~<NomeDaClasse>"
```

---

## Decisões de design (registradas aqui, 1-2 frases cada)

1. **NCM/EAN no parser (DECISÃO):** o XML de exemplo expõe `<NCM>` e `<cEAN>` diretamente
   dentro de `prod` (confirmado: `NCM=19059090`, `cEAN=17891515645813`). Estender o parser
   é trivial (o `switch` de `LerProd` já trata irmãos), então **vamos adicionar `Ncm` e `Ean`
   a `ItemNota` e dois `case` em `LerProd`**. Isso gera um I30 mais completo (classificacaoncm
   e eancliente preenchidos) em vez de deixá-los vazios. É mudança aditiva, não quebra nada.

2. **`InterfaceWms.I30` (DECISÃO):** o enum `InterfaceWms` hoje só tem I50/I60/I10. Adicionar
   `I30` + mapeamento `"WEBAPI-I30-Json"` em `InterfaceWmsExtensions.ParaIdentificador`. Sem
   isso o cliente não consegue enviar o I30. Mudança aditiva (o `switch` continua exaustivo).

3. **I30 usa query string, não envelope header/body (DECISÃO):** `TESTE-COMUNICACAO.md` e
   `ClienteWmsHttp` provam que a query string com os 5 parâmetros funciona para todas as
   interfaces enviadas ao vivo (inclusive I30, idDoc 331063/331064). O `ClienteWmsHttp` já
   monta a query a partir do `InterfaceWms`. Portanto o `PayloadI30` é o `{ "PRODUTO": [...] }`
   puro (sem envelope header/body) — igual aos demais payloads — e o cliente cuida do resto.
   O template mostra envelope, mas o contrato validado ao vivo é query string; seguimos o que
   foi provado.

4. **Classificação por texto (DECISÃO):** não há código de erro estruturado (confirmado no
   brief e em `TESTE-COMUNICACAO.md`). O `ClassificadorRetorno` casa os **textos reais**
   observados (mapeamento abaixo), case-insensitive, via `Contains`. A ordem de verificação
   importa: sucesso → empresa → produto → pendência → (resto) rejeitado.

5. **`codempresa` do I30 (DECISÃO):** usar `OpcoesOperacionais.CodigoDepositante` (`"3509308"`),
   valor confirmado ao vivo (v2 do E2E passou com `codempresa="3509308"`). Não hardcoded.

6. **`codclassificacao` do I30 (DECISÃO):** fixo `"2"` — valor confirmado ao vivo (v2 passou;
   `"DEF"` falhava). É um default do `PayloadI30` (documentado), não vem de config nesta etapa.

7. **I60 respeita a flag (DECISÃO):** o Orquestrador só envia I60 após sucesso da I50 **se
   `OpcoesI60.Habilitada == true`**. Hoje a flag é `false` (default), então na prática a I60
   não é enviada. O código I60 permanece intacto. Não remover.

8. **Caso "empresa não tem erro estruturado" (confirmado pelo usuário, msg 12):** não existe
   contagem especial para empresa — a regra geral de **até 3 tentativas por nota** cobre tudo.
   I10 e I30 são pré-condições disparadas pela mensagem; cada reenvio da I50 conta 1 tentativa.

9. **Pendência e não-integrada são resultados aceitáveis (confirmado, msg 12/5):** o objetivo
   é agilidade, não 100%. Nunca travar o lote nem entrar em loop: pendência → registra e segue;
   3 tentativas sem sucesso → não-integrada, registra motivo + contagem.

---

## Mapeamento dos textos do WMS → ClassificadorRetorno

Fonte: `docs/TESTE-COMUNICACAO.md` (textos reais observados ao vivo). Casamento
case-insensitive por `Contains` sobre `RetornoWms.Mensagem` (+ `Sucesso` para o caso de sucesso).

| Método | Casa quando a mensagem/sucesso contém (texto real) | Origem (TESTE-COMUNICACAO.md) |
|--------|----------------------------------------------------|-------------------------------|
| `EhSucesso` | `Sucesso == "S"` (item ou resposta) **OU** mensagem contém `"Registrado com sucesso"` | "I50 DocCarga → sucesso S", "Registrado com sucesso", "sucesso=S" |
| `EhEmpresaNaoLocalizada` | `"Empresa não localizada"` **OU** `"Destinataria - Empresa não localizada"` | "Empresa não localizada", "Destinataria - Empresa não localizada, sigla não informada." |
| `EhProdutoNaoCadastrado` | `"não ativo ou faltando ou não cadastrado"` (trecho âncora, cobre `Produto Cod\EAN: "175886" ... não ativo ou faltando ou não cadastrado!`) | `Produto Cod\EAN: "175886" da empresa "3509308-..." não ativo ou faltando ou não cadastrado!` |
| `EhPendencia` | `"pendente na interface"` (cobre `Foi importado e esta pendente na interface`) | "Foi importado e esta pendente na interface", achado "Pendência ≠ falha" |

Notas de robustez para o coder:
- Comparar com `StringComparison.OrdinalIgnoreCase`.
- Normalizar acentos NÃO é necessário (os textos reais trazem acento), mas o casamento de
  "empresa não localizada" deve tolerar o acento exatamente como no texto real.
- `EhSucesso` verifica tanto `RespostaWms.Sucesso`/`RetornoWms.Sucesso == "S"` quanto o texto,
  porque o sucesso real veio como `sucesso="S"` (v2) e às vezes com mensagem "Registrado com
  sucesso".
- "já existe" (I10: `já existe no cadastro`; I30: `já existe`) **NÃO** é tratado pelo
  classificador de I50 — é tratado pelo Orquestrador como **sucesso da pré-condição** (helper
  `PreCondicaoOk` no ClassificadorRetorno, ver abaixo).

---

## Arquivos a criar / alterar

### FEAT-001 — Camada de blocos de construção (parser, I30, classificador, resultado, relatório)

1. **ALTERAR** `c:\wms-integrador\src\KitWmsIntegrador.Core\Modelos\ItemNota.cs`
   - Adicionar `public string Ncm { get; init; } = string.Empty;` (prod/NCM)
   - Adicionar `public string Ean { get; init; } = string.Empty;` (prod/cEAN)

2. **ALTERAR** `c:\wms-integrador\src\KitWmsIntegrador.Core\Leitura\LeitorNotaFiscalXml.cs`
   - Em `LerProd`, adicionar ao `switch`:
     - `case "NCM": ncm = r.ReadElementContentAsString(); return true;`
     - `case "cEAN": ean = r.ReadElementContentAsString(); return true;`
   - Declarar `string ncm = string.Empty, ean = string.Empty;` e atribuir no `new ItemNota { ... Ncm = ncm, Ean = ean }`.

3. **ALTERAR** `c:\wms-integrador\src\KitWmsIntegrador.Core\ClienteWms\InterfaceWms.cs`
   - Adicionar valor `I30` ao enum.
   - Adicionar `InterfaceWms.I30 => "WEBAPI-I30-Json"` em `ParaIdentificador`.

4. **CRIAR** `c:\wms-integrador\src\KitWmsIntegrador.Core\Construtores\Dtos\PayloadI30.cs`
   - `public sealed record PayloadI30 { [JsonPropertyName("PRODUTO")] public IReadOnlyList<ProdutoI30> Produto { get; init; } = Array.Empty<ProdutoI30>(); }`
   - `public sealed record ProdutoI30 { ... }` com `[JsonPropertyName]` EXATOS do template
     (seção I30 de `TEMPLATES-INTERFACES.md`). Campos-chave e defaults confirmados:
     - `codtpprod1 = "2"` (fixo)
     - `codigo` (cProd SEM zeros à esquerda — `item.CodigoProduto.TrimStart('0')`)
     - `descricao` (xProd)
     - `codempresa` (da config: `CodigoDepositante`)
     - `codunidade` (uCom)
     - `codcliente` (cProd COM zeros — `item.CodigoProduto`)
     - `eancliente` (cEAN se houver, senão "")
     - `classificacaoncm` (NCM se houver, senão "")
     - `codclassificacao = "2"` (fixo, confirmado ao vivo)
     - `codtipologia = "SEC"` (default)
     - `tpacao = "I"`, `codigotributario = "040"`, e demais campos com os defaults do template
       (`ctrunifiscal="E"`, `ctrunifisica="E"`, `tipovalidade="N"`, `temppadrao="25.00"`,
       `qtdcxcamada="1"`, `qtdpadraopallet="1"`, `psindividual="1.000"`, etc.).
     - `cnpjArmazem`, `cnpjProprietaria`, `deposito` (da config operacional, com máscara nos CNPJs).

5. **CRIAR** `c:\wms-integrador\src\KitWmsIntegrador.Core\Construtores\IConstrutorI30.cs`
   ```csharp
   public interface IConstrutorI30
   {
       // Monta UM PayloadI30 com TODOS os itens da nota (o WMS reporta um produto
       // por vez; enviamos todos — os que já existem retornam "já existe" = OK).
       PayloadI30 Construir(NotaFiscal nota);
       string ConstruirJson(NotaFiscal nota);
   }
   ```

6. **CRIAR** `c:\wms-integrador\src\KitWmsIntegrador.Core\Construtores\ConstrutorI30.cs`
   - `public sealed class ConstrutorI30 : IConstrutorI30`
   - Injeta `IOptions<OpcoesOperacionais>` (CodigoDepositante, CnpjArmazem, CnpjDepositante, Deposito).
   - `Construir` mapeia `nota.Itens` → lista de `ProdutoI30` (um por item), aplicando
     `FormatoWms.Documento` nos CNPJs e `TrimStart('0')` em `codigo`.
   - `ConstruirJson` serializa com `JsonWms.Opcoes` (igual aos demais construtores).

7. **CRIAR** `c:\wms-integrador\src\KitWmsIntegrador.Core\Orquestrador\ClassificadorRetorno.cs`
   - Peça testável isolada, SEM estado, SEM rede. Pode ser `public sealed class
     ClassificadorRetorno` (ou `static`; preferir instância para DI/testabilidade). Métodos:
   ```csharp
   bool EhSucesso(RespostaWms resposta);        // sucesso="S" em resposta/retornos ou "Registrado com sucesso"
   bool EhEmpresaNaoLocalizada(RespostaWms r);  // "Empresa não localizada"
   bool EhProdutoNaoCadastrado(RespostaWms r);  // "não ativo ou faltando ou não cadastrado"
   bool EhPendencia(RespostaWms r);             // "pendente na interface"
   bool PreCondicaoOk(RespostaWms r);           // "já existe" (I10/I30) = OK; ou EhSucesso
   string PrimeiraMensagem(RespostaWms r);      // helper p/ relatório: 1ª mensagem não-vazia
   ```
   - Cada método varre `resposta.Retornos` e também olha `resposta.Sucesso`. Casamento
     case-insensitive por `Contains` com os textos reais da tabela acima.

8. **CRIAR** `c:\wms-integrador\src\KitWmsIntegrador.Core\Orquestrador\EstadoIntegracao.cs`
   - `public enum EstadoIntegracao { Integrada, Pendente, NaoIntegrada }`

9. **CRIAR** `c:\wms-integrador\src\KitWmsIntegrador.Core\Orquestrador\ResultadoIntegracaoNota.cs`
   - `public sealed record ResultadoIntegracaoNota` com:
     - `string NumeroNota`
     - `string NumeroTransporte`
     - `bool Integrada`
     - `int Tentativas`
     - `EstadoIntegracao Estado`
     - `string UltimaMensagem`
     - `IReadOnlyList<string> Historico` (mensagens/transações observadas; default vazio)

10. **ALTERAR** `c:\wms-integrador\src\KitWmsIntegrador.Core\Relatorio\LinhaRelatorioErro.cs`
    - Adicionar dois parâmetros ao record posicional, COM DEFAULT para não quebrar os
      call-sites existentes nos testes:
      `bool Integrada = false, int Tentativas = 0` (após `Mensagem`).

11. **ALTERAR** `c:\wms-integrador\src\KitWmsIntegrador.Core\Relatorio\GeradorRelatorio.cs`
    - Adicionar ao array `Cabecalhos`: `"Integrada"` e `"Tentativas"`.
    - HTML: emitir `<td>` para `Integrada` ("S"/"N") e `Tentativas`.
    - PDF: adicionar 2 colunas ao array `valores` e 2 posições a `colunasX` (ajustar offsets
      para caber: reduzir largura da coluna Mensagem ou ampliar a página; manter legível).

Verificação FEAT-001:
```
dotnet test c:\wms-integrador\KitWmsIntegrador.sln -c Debug --nologo
```
Esperado: build verde; os 75 testes existentes continuam passando + os novos testes da FEAT-001
(parser NCM/EAN, ConstrutorI30, ClassificadorRetorno, GeradorRelatorio com 2 colunas) passam.

### FEAT-002 — Orquestrador reativo + DI

12. **SUBSTITUIR** `c:\wms-integrador\src\KitWmsIntegrador.Core\Orquestrador\IOrquestrador.cs`
    (hoje placeholder vazio) por:
    ```csharp
    public interface IOrquestrador
    {
        Task<IReadOnlyList<ResultadoIntegracaoNota>> IntegrarAsync(
            Onda onda, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ResultadoIntegracaoNota>> IntegrarAsync(
            IEnumerable<NotaFiscal> notas, string numeroTransporte,
            CancellationToken cancellationToken = default);
    }
    ```

13. **CRIAR** `c:\wms-integrador\src\KitWmsIntegrador.Core\Orquestrador\Orquestrador.cs`
    - `public sealed class Orquestrador : IOrquestrador`.
    - Injeta: `IClienteWms`, `IConstrutorI50`, `IConstrutorI10`, `IConstrutorI30`,
      `IConstrutorI60`, `ClassificadorRetorno`, `IOptions<OpcoesI60>`, `IClock`,
      `IGeradorRelatorio` (opcional/nullable — ver brief item 4), `ILogger<Orquestrador>`.
    - Constante `const int MaxTentativas = 3;`.
    - **Algoritmo por nota** (ver seção abaixo). Devolve `ResultadoIntegracaoNota` por nota.
    - NÃO faz e-mail, NÃO move arquivos, NÃO chama HTTP real (usa `IClienteWms`).

14. **ALTERAR** `c:\wms-integrador\src\KitWmsIntegrador.Worker\Program.cs`
    - Registrar no DI:
      `builder.Services.AddSingleton<ClassificadorRetorno>();`
      `builder.Services.AddSingleton<IConstrutorI30, ConstrutorI30>();`
      `builder.Services.AddSingleton<IOrquestrador, Orquestrador>();`
    - (IGeradorRelatorio, IClienteWms, IClock, construtores I50/I60/I10, OpcoesI60 já estão
      registrados.) Não alterar o `Worker.cs` (o acionamento do orquestrador é fase futura).

Verificação FEAT-002:
```
dotnet test c:\wms-integrador\KitWmsIntegrador.sln -c Debug --nologo
```
Esperado: build verde (Core + Worker + Tests compilam); todos os testes passam, incluindo os
novos `OrquestradorTests` cobrindo os cenários obrigatórios abaixo.

---

## Algoritmo por nota (até 3 tentativas) — a implementar em `Orquestrador`

Para cada nota da onda (o `numeroTransporte` da onda alimenta a I50; se a nota não tem
transporte, isso é o Caso B — tratar como não-integrada/pendência e registrar, sem lançar):

```
tentativas = 0
while tentativas < 3:
    tentativas++
    resp = cliente.EnviarAsync(I50, i50.Construir(nota, transporte))
    if classificador.EhSucesso(resp):
        if OpcoesI60.Habilitada: cliente.EnviarAsync(I60, i60.Construir(nota))  // hoje flag=false → não envia
        => Integrada (Estado=Integrada, Tentativas=tentativas); break
    if classificador.EhEmpresaNaoLocalizada(resp):
        respI10 = cliente.EnviarAsync(I10, i10.Construir(nota))
        // "já existe no cadastro" = OK (PreCondicaoOk); segue para reenviar a I50 (novo loop = nova tentativa)
        continue
    if classificador.EhProdutoNaoCadastrado(resp):
        respI30 = cliente.EnviarAsync(I30, i30.Construir(nota))  // TODOS os itens de uma vez
        // registrar PENDÊNCIA de revisão (produto provisório) no resultado/relatório
        // "já existe" por item = OK; segue para reenviar a I50 (novo loop = nova tentativa)
        continue
    if classificador.EhPendencia(resp):
        => Pendente (Estado=Pendente); registra; break   // não trava o lote, não faz loop
    else:
        // qualquer outro erro: registra e segue (break com Estado=NaoIntegrada se esgotou sentido)
        => registra; break
// se saiu do while por esgotar 3 tentativas sem sucesso:
=> NaoIntegrada (Estado=NaoIntegrada, Tentativas=3, UltimaMensagem=motivo)
```

Regras-chave (confirmadas pelo usuário):
- Produto: envia I30 de **TODOS os itens** (o WMS só mostra um por vez; já-existentes = OK).
- "já existe" (empresa no I10, produto no I30) = sucesso da pré-condição (`PreCondicaoOk`).
- Empresa não tem contagem especial; a regra de 3 tentativas cobre tudo.
- Objetivo é agilidade: falhar rápido, registrar bem, **nunca travar o lote nem fazer loop**.
- Pendência e não-integrada são resultados aceitáveis e bem registrados (Integrada=false,
  Tentativas preenchido, motivo na UltimaMensagem). O relatório inclui TODAS as notas com
  problema (pendentes + não-integradas) com coluna Integrada (S/N) e Tentativas.

---

## Lista completa de testes a escrever

### Testes da FEAT-001 (blocos de construção)

**`LeitorNotaFiscalXmlTests` (ALTERAR/adicionar casos):**
- `Parse_Item_ExtraiNcmEEan` — lê o XML de recurso e confirma `item.Ncm == "19059090"` e
  `item.Ean == "17891515645813"` para o 1º item.

**`ConstrutorI30Tests` (NOVO):**
- `Construir_UmItemPorProdutoDaNota` — nota com 2 itens → `PayloadI30.Produto.Count == 2`.
- `Construir_Codigo_SemZerosAEsquerda_Codcliente_ComZeros` — valida `codigo` (trim) vs `codcliente`.
- `Construir_CodempresaEClassificacao_ConfirmadosAoVivo` — `codempresa == "3509308"`,
  `codclassificacao == "2"`.
- `Construir_NcmEEan_VemDoItem` — `classificacaoncm` e `eancliente` preenchidos do XML.
- `Construir_CodtipologiaEDefaults_SeguemTemplate` — `codtipologia == "SEC"`, `tpacao == "I"`,
  `codtpprod1 == "2"`.
- `Construir_CnpjsOperacionais_VemDaConfigComMascara` — cnpjArmazem/cnpjProprietaria/deposito.
- `ConstruirJson_GeraNomesExatosDoContrato` — contém `"PRODUTO"`, `"codigo"`,
  `"classificacaoncm"`, `"codclassificacao":"2"`, `"codempresa":"3509308"`.

**`InterfaceWmsTests` (NOVO ou dentro de outro):**
- `I30_MapeiaParaWebapiI30Json` — `InterfaceWms.I30.ParaIdentificador() == "WEBAPI-I30-Json"`.

**`ClassificadorRetornoTests` (NOVO) — usar os JSONs/textos reais:**
- `EhSucesso_QuandoSucessoS` — resposta com `sucesso="S"`.
- `EhSucesso_QuandoMensagemRegistradoComSucesso`.
- `EhEmpresaNaoLocalizada_TextoReal` — "Destinataria - Empresa não localizada, sigla não informada."
- `EhProdutoNaoCadastrado_TextoReal` — `... "175886" ... não ativo ou faltando ou não cadastrado!`.
- `EhPendencia_TextoReal` — "Foi importado e esta pendente na interface...".
- `PreCondicaoOk_JaExiste_I10` — "já existe no cadastro. Utilizar o comando de alteracao(U)."
- `PreCondicaoOk_JaExiste_I30` — mensagem "já existe".
- `Classificacao_Rejeitado_EstruturaMalFormada` — "Mensagem EDI com estrutura mal-formada."
  não casa nenhum dos positivos.

**`GeradorRelatorioTests` (ALTERAR):**
- `GerarHtml_ContemColunasIntegradaETentativas` — cabeçalhos "Integrada"/"Tentativas" + valores
  ("S"/"N" e número) presentes no HTML.
- `GerarPdf_ComColunasNovas_GeraPdfValido` — header "%PDF" e bytes não vazios com linhas que
  incluem as 2 colunas.
- (manter os testes existentes passando — os defaults novos em `LinhaRelatorioErro` garantem isso.)

### Testes da FEAT-002 (orquestrador) — `OrquestradorTests` (NOVO)

Usar um `IClienteWms` fake/stub programável (sequência de `RespostaWms` por `InterfaceWms`), um
`ClassificadorRetorno` real, construtores reais (ou fakes) e `OpcoesI60` configurável. Cenários
OBRIGATÓRIOS do brief:

1. `I50Sucesso_NotaIntegrada_SemI60_QuandoFlagDesligada` — I50 sucesso na 1ª; `OpcoesI60.Habilitada=false`
   → nenhum envio I60; `Estado==Integrada`, `Integrada==true`, `Tentativas==1`.
2. `I50Sucesso_ComFlagI60Ligada_EnviaI60` — `Habilitada=true` → após I50 sucesso, envia I60; nota integrada.
3. `EmpresaNaoLocalizada_EnviaI10_DepoisReenviaI50_Integra` — 1ª I50 "empresa não localizada";
   I10 "já existe" (OK); 2ª I50 sucesso → Integrada, `Tentativas==2`. Verifica ordem de chamadas (I50, I10, I50).
4. `ProdutoNaoCadastrado_EnviaI30DeTodosOsItens_DepoisReenviaI50_Integra` — 1ª I50 "produto não
   cadastrado"; I30 enviado UMA vez com TODOS os itens; 2ª I50 sucesso → Integrada, `Tentativas==2`.
   Verifica que o PayloadI30 enviado tem `Produto.Count == nota.Itens.Count`.
5. `ProdutoNaoCadastrado_RegistraPendenciaDeRevisao` — ao enviar I30, o resultado/relatório
   registra a pendência de revisão (produto provisório) — mesmo que a nota acabe integrada.
6. `Pendencia_NaInterface_TrataComoPendente_NaoTravaLote` — I50 retorna "pendente na interface"
   → `Estado==Pendente`, `Integrada==false`, segue para a próxima nota (sem loop).
7. `TresTentativasSemSucesso_NaoIntegrada_RegistraMotivoEContagem` — I50 falha 3x (ex.: produto
   persiste) → `Estado==NaoIntegrada`, `Integrada==false`, `Tentativas==3`, `UltimaMensagem` com motivo.
8. `OutroErro_RegistraESegue` — mensagem desconhecida → registra, não trava, não entra em loop.
9. `LoteComMultiplasNotas_UmaFalhaNaoTravaAsOutras` — onda com 2+ notas; uma falha/pendência não
   impede a outra de integrar; retorna um `ResultadoIntegracaoNota` por nota.
10. `NaoEnviaHttpReal` — garantido pelo fake de `IClienteWms` (nenhuma rede).
11. (opcional) `CasoB_NotaSemTransporte_NaoIntegradaRegistrada` — nota sem transporte não lança;
    vira não-integrada/pendência registrada.

Infra de teste sugerida: um `FakeClienteWms : IClienteWms` em `tests/.../Infra/` que recebe uma
fila/func de respostas por `InterfaceWms` e grava os envios (interface + payload) para asserts.

---

## Observações / lacunas (assunções razoáveis, não bloqueiam)

- A I60 continua com erro de conferência de quantidade em aberto ao vivo (ver TESTE-COMUNICACAO.md),
  mas como a flag está desligada o Orquestrador não a envia; isso não bloqueia a Etapa 6.
  O cenário 2 (flag ligada) testa apenas que o **envio é disparado**, não o sucesso do WMS.
- `codclassificacao="2"` e `codempresa="3509308"` são os valores que passaram ao vivo; ficam como
  defaults/config conforme as decisões 5 e 6.
- Não implementar e-mail (Etapa 7), movimentação de arquivos nem watcher contínuo.
```