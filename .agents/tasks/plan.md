# Implementation Plan — KitWmsIntegrador (Esqueleto + Leitor/Parser de XML NF-e)

Escopo desta tarefa: APENAS esqueleto da solução .NET 8 + camada de leitura/parsing de
XML de NF-e com agrupamento por transporte. **100% offline** — nenhuma chamada HTTP,
nenhum envio ao WMS, nenhuma movimentação/deleção de arquivos em `C:\Envia xml`
(somente leitura), nenhum banco de dados. Pastas/interfaces placeholder para WMS,
construtores I50/I60, orquestrador, store e notificador são criadas vazias/TODO, sem
implementação de comunicação.

## Convenções de ambiente (valem para todos os itens)

- O SDK .NET 8 (8.0.427) está em `C:\Program Files\dotnet`. **Sempre** invoque o CLI
  por caminho absoluto `& "C:\Program Files\dotnet\dotnet.exe"` nos comandos (o PATH
  pode não resolver `dotnet` neste shell). Exemplos de verificação abaixo já usam esse
  caminho.
- Shell: PowerShell no Windows. Caminhos com espaço (`C:\Envia xml`) sempre entre aspas.
- Trabalhar na raiz do workspace `c:\wms-integrador`. O código da solution vive em
  `c:\wms-integrador\src`. Os dados de entrada ficam em `C:\Envia xml` e NUNCA são
  tocados além de leitura.
- A pasta de entrada é **configurável** (default `C:\Envia xml`) via appsettings e
  variável de ambiente; nunca hardcodar o caminho no código de domínio/parser.

## Decisões de design (registradas aqui, com motivo)

1. **Layout em `src/`**: solution `KitWmsIntegrador.sln` na raiz; projetos sob
   `src/`. Separa código da pasta `docs/` e dos dados. Mantém a raiz limpa para git.
2. **Worker Service (BackgroundService)** como projeto principal
   (`KitWmsIntegrador.Worker`) — alinhado à decisão de rodar sob NSSM no Windows Server
   (ver `docs/AMBIENTE-SERVIDOR.md`). Nesta fase o worker só varre a pasta e imprime o
   resumo; nada de rede.
3. **Biblioteca de classe `KitWmsIntegrador.Core`** com domínio + parser + agrupamento.
   Isolar a lógica numa lib torna-a testável pelo xUnit sem subir o worker, e deixa os
   placeholders (WMS/I50/I60/Store/Notificador) num único lugar coeso.
4. **Parser com `XmlReader` em streaming** (forward-only) em vez de `XDocument`/`XmlDocument`
   por arquivo-inteiro. Motivo: baixo consumo de memória é prioridade explícita do
   cliente; `XmlReader` lê sem materializar a árvore inteira. A varredura dos 1196
   arquivos processa **um arquivo por vez** (streaming incremental via `IEnumerable`
   com `yield return`), nunca carregando todos em memória.
5. **Namespace-agnostic**: o XML usa o namespace default
   `http://www.portalfiscal.inf.br/nfe`. O parser compara por `LocalName` dos elementos
   (ignorando namespace) para robustez — simples e suficiente para este layout 4.00.
6. **Transporte como `string`**: capturado por regex `Transporte:\s*(\d+)` sobre
   `infNFe/infAdic/infCpl`, preservando zeros à esquerda (ex.: `0130693549`). Confirmado
   em XML real. Quando ausente (emitente não-BRF) → nota marcada como "transporte não
   encontrado" (pendência), sem quebrar o processamento.
7. **Agrupamento em memória** via `Dictionary<string, Onda>` ao fim da varredura
   streaming (apenas metadados leves por nota ficam retidos, não o XML bruto). O volume
   (~1196 notas / ~398 transportes) é modesto; reter só o modelo de domínio é barato.
8. **Encoding**: os XMLs reais vêm com mojibake em alguns acentos (ex.: `JOSÃ‰`), mas
   isso não afeta os campos-chave (nNF, chave, transporte, números). O parser lê o
   arquivo como declarado (UTF-8); não tentamos "consertar" acentos nesta fase.

---

## Itens do plano

- [ ] 1. Criar a solution e os projetos vazios compiláveis.
      Criar `KitWmsIntegrador.sln` na raiz e três projetos: Worker Service principal,
      biblioteca Core e projeto de testes xUnit, todos referenciando .NET 8. Adicionar
      os projetos à solution e a referência Worker→Core e Tests→Core.
      Comandos (PowerShell, na raiz `c:\wms-integrador`):
      - `& "C:\Program Files\dotnet\dotnet.exe" new sln -n KitWmsIntegrador`
      - `& "C:\Program Files\dotnet\dotnet.exe" new worker -n KitWmsIntegrador.Worker -o src/KitWmsIntegrador.Worker -f net8.0`
      - `& "C:\Program Files\dotnet\dotnet.exe" new classlib -n KitWmsIntegrador.Core -o src/KitWmsIntegrador.Core -f net8.0`
      - `& "C:\Program Files\dotnet\dotnet.exe" new xunit -n KitWmsIntegrador.Tests -o tests/KitWmsIntegrador.Tests -f net8.0`
      - `& "C:\Program Files\dotnet\dotnet.exe" sln add src/KitWmsIntegrador.Worker src/KitWmsIntegrador.Core tests/KitWmsIntegrador.Tests`
      - `& "C:\Program Files\dotnet\dotnet.exe" add src/KitWmsIntegrador.Worker reference src/KitWmsIntegrador.Core`
      - `& "C:\Program Files\dotnet\dotnet.exe" add tests/KitWmsIntegrador.Tests reference src/KitWmsIntegrador.Core`
      Remover o arquivo de classe gerado por template `src/KitWmsIntegrador.Core/Class1.cs`.
      Files (criados pelos templates): `KitWmsIntegrador.sln`,
      `src/KitWmsIntegrador.Worker/KitWmsIntegrador.Worker.csproj`,
      `src/KitWmsIntegrador.Worker/Program.cs`,
      `src/KitWmsIntegrador.Worker/Worker.cs`,
      `src/KitWmsIntegrador.Worker/appsettings.json`,
      `src/KitWmsIntegrador.Core/KitWmsIntegrador.Core.csproj`,
      `tests/KitWmsIntegrador.Tests/KitWmsIntegrador.Tests.csproj`.
      Verify: `& "C:\Program Files\dotnet\dotnet.exe" build KitWmsIntegrador.sln` —
      compila sem erros (os 3 projetos).

- [ ] 2. Criar a estrutura de pastas por componente na Core, com placeholders/TODO.
      Criar as pastas e interfaces/classes vazias refletindo a arquitetura
      (`docs/ARQUITETURA.md` §2), SEM implementar comunicação. As pastas de domínio e
      leitura serão preenchidas nos itens seguintes; as demais ficam como placeholders
      documentados.
      Estrutura dentro de `src/KitWmsIntegrador.Core/`:
      - `Modelos/` (domínio — item 3)
      - `Leitura/` (reader + parser — itens 4 e 5)
      - `Agrupamento/` (ondas — item 6)
      - `ClienteWms/` com `IClienteWms.cs` (interface vazia + TODO "Fase futura: GetToken + POST I50/I60; nenhuma implementação nesta fase")
      - `Construtores/` com `IConstrutorI50.cs` e `IConstrutorI60.cs` (interfaces vazias + TODO)
      - `Orquestrador/` com `IOrquestrador.cs` (interface vazia + TODO)
      - `Estado/` com `IStoreEstado.cs` (interface vazia + TODO)
      - `Notificacao/` com `INotificador.cs` (interface vazia + TODO)
      Cada placeholder é uma interface no namespace `KitWmsIntegrador.Core.<Pasta>` com
      um comentário XML `/// TODO` explicando que a implementação é de fase futura e que
      NÃO há comunicação nesta fase.
      Files: `src/KitWmsIntegrador.Core/ClienteWms/IClienteWms.cs`,
      `src/KitWmsIntegrador.Core/Construtores/IConstrutorI50.cs`,
      `src/KitWmsIntegrador.Core/Construtores/IConstrutorI60.cs`,
      `src/KitWmsIntegrador.Core/Orquestrador/IOrquestrador.cs`,
      `src/KitWmsIntegrador.Core/Estado/IStoreEstado.cs`,
      `src/KitWmsIntegrador.Core/Notificacao/INotificador.cs`.
      Verify: `& "C:\Program Files\dotnet\dotnet.exe" build KitWmsIntegrador.sln` —
      compila sem erros.

- [ ] 3. Criar o modelo de domínio (records) em `Modelos/`.
      Criar os tipos imutáveis que representam a nota e seus itens, com os campos
      confirmados no `docs/MAPEAMENTO-DADOS.md` e no XML real. Usar `record` C# e
      propriedades `init`. Um único item do plano cria estes tipos relacionados:
      - `NotaFiscal` (record): `ChaveNFe` (string), `Numero` (string, = nNF),
        `Serie` (string), `Modelo` (string), `DataEmissao` (DateTimeOffset?),
        `EmitenteCnpj` (string), `EmitenteNome` (string),
        `DestinatarioCnpj` (string), `DestinatarioNome` (string),
        `Endereco` (EnderecoDestinatario?), `Itens` (IReadOnlyList<ItemNota>),
        `ValorTotal` (decimal?, = total/ICMSTot/vNF),
        `Volumes` (VolumeTransporte?), `TransportadoraNome` (string?),
        `TransportadoraCnpj` (string?),
        `NumeroTransporte` (string?, null quando não encontrado),
        `ArquivoOrigem` (string, caminho do XML).
      - `ItemNota` (record): `CodigoProduto` (cProd), `Descricao` (xProd),
        `Quantidade` (decimal, qCom), `Unidade` (uCom), `ValorProduto` (decimal, vProd),
        `Cfop` (string), `Lote` (string?, rastro/nLote), `Validade` (DateOnly?, rastro/dVal).
      - `EnderecoDestinatario` (record): `Logradouro` (xLgr), `Numero` (nro),
        `Bairro` (xBairro), `Municipio` (xMun), `Uf` (UF), `Cep` (CEP).
      - `VolumeTransporte` (record): `QuantidadeVolumes` (string?, qVol),
        `PesoLiquido` (decimal?, pesoL), `PesoBruto` (decimal?, pesoB).
      Namespace `KitWmsIntegrador.Core.Modelos`.
      Files: `src/KitWmsIntegrador.Core/Modelos/NotaFiscal.cs`,
      `src/KitWmsIntegrador.Core/Modelos/ItemNota.cs`,
      `src/KitWmsIntegrador.Core/Modelos/EnderecoDestinatario.cs`,
      `src/KitWmsIntegrador.Core/Modelos/VolumeTransporte.cs`.
      Verify: `& "C:\Program Files\dotnet\dotnet.exe" build KitWmsIntegrador.sln` —
      compila sem erros.

- [ ] 4. Implementar a extração do número do transporte (regex) como utilitário isolado.
      Criar `ExtratorTransporte` em `Leitura/` com um método estático
      `string? Extrair(string? infCpl)` que aplica a regex `Transporte:\s*(\d+)`
      (compilada, `RegexOptions.Compiled`) sobre o texto de `infCpl` e retorna o grupo
      1 como string **preservando zeros à esquerda**; retorna `null` quando o padrão não
      casa ou a entrada é nula/vazia. Isolar aqui permite testá-lo unitariamente sem XML.
      Files: `src/KitWmsIntegrador.Core/Leitura/ExtratorTransporte.cs`.
      Verify: `& "C:\Program Files\dotnet\dotnet.exe" build KitWmsIntegrador.sln` —
      compila. (Testes dedicados vêm no item 8.)

- [ ] 5. Implementar o leitor/parser de XML em streaming.
      Criar `LeitorNotaFiscalXml` em `Leitura/` com:
      - `NotaFiscal Parse(Stream xml, string arquivoOrigem)` — parse de um stream usando
        `XmlReader` (settings: `IgnoreWhitespace=true`, `IgnoreComments=true`,
        `DtdProcessing=DtdProcessing.Prohibit` por segurança, `XmlResolver=null`).
      - `NotaFiscal ParseArquivo(string caminho)` — abre `FileStream` em modo leitura
        compartilhada (`FileShare.Read`, nunca escrita) e delega ao `Parse`.
      Regras de extração (comparar por `LocalName`, ignorando namespace), conforme
      `docs/MAPEAMENTO-DADOS.md` §1 e confirmado no XML real:
      - Chave: atributo `Id` de `infNFe` com prefixo `NFe` removido; fallback
        `protNFe/infProt/chNFe`.
      - `ide`: `nNF`→Numero, `serie`→Serie, `mod`→Modelo, `dhEmi`→DataEmissao
        (`DateTimeOffset.Parse`, cultura invariante).
      - `emit`: `CNPJ`, `xNome`. `dest`: `CNPJ`, `xNome`; `enderDest`:
        `xLgr/nro/xBairro/xMun/UF/CEP`.
      - Cada `det/prod` → um `ItemNota` (pode haver N): `cProd/xProd/qCom/uCom/vProd/CFOP`;
        `rastro/nLote`→Lote, `rastro/dVal`→Validade. Decimais com
        `decimal.Parse(..., CultureInfo.InvariantCulture)` (o XML usa ponto decimal).
        Ignorar conteúdo de `imposto` (fora de escopo).
      - `total/ICMSTot/vNF`→ValorTotal. `transp/vol`: `qVol/pesoL/pesoB`.
        `transp/transporta`: `xNome/CNPJ`.
      - `infAdic/infCpl` → passar a `ExtratorTransporte.Extrair` → `NumeroTransporte`
        (null quando ausente).
      Importante: ler os `det` em sequência durante o streaming (não exige segunda
      passada). Manter o parser tolerante a campos ausentes (usar null/listas vazias sem
      lançar exceção, exceto XML malformado).
      Files: `src/KitWmsIntegrador.Core/Leitura/LeitorNotaFiscalXml.cs`.
      Verify: `& "C:\Program Files\dotnet\dotnet.exe" build KitWmsIntegrador.sln` —
      compila. (Testes contra XMLs reais no item 8.)

- [ ] 6. Implementar o varredor da pasta (streaming) e o agrupamento por transporte.
      Criar dois tipos em `Agrupamento/`:
      - `VarredorPasta` em `Leitura/` (ou `Agrupamento/`): método
        `IEnumerable<NotaFiscal> Varrer(string pasta)` que enumera `*.XML` (case-insensitive)
        e faz `yield return` de cada `NotaFiscal` parseada — um arquivo por vez, sem
        carregar todos em memória. Expor também contagem de arquivos e lista de erros de
        parse por arquivo (coletar exceções por arquivo em vez de abortar a varredura).
      - `Onda` (record): `NumeroTransporte` (string), `Notas` (IReadOnlyList<NotaFiscal>).
        Regra de negócio modelada (NÃO enviar): cada nota = 1 DocCarga (I50); o
        `NumeroTransporte` é o `nroagrupseparacao` que agrupa os DocCargas numa onda.
      - `AgrupadorTransporte`: método
        `ResultadoAgrupamento Agrupar(IEnumerable<NotaFiscal> notas)` que agrupa por
        `NumeroTransporte` em `Dictionary<string, List<NotaFiscal>>`, separando as notas
        sem transporte numa lista de pendências. Retornar um `ResultadoAgrupamento`
        (record) com `Ondas` (IReadOnlyList<Onda>), `SemTransporte`
        (IReadOnlyList<NotaFiscal>), `TotalNotas` (int).
      Namespace `KitWmsIntegrador.Core.Agrupamento` (e `.Leitura` para o varredor).
      Files: `src/KitWmsIntegrador.Core/Leitura/VarredorPasta.cs`,
      `src/KitWmsIntegrador.Core/Agrupamento/Onda.cs`,
      `src/KitWmsIntegrador.Core/Agrupamento/AgrupadorTransporte.cs`,
      `src/KitWmsIntegrador.Core/Agrupamento/ResultadoAgrupamento.cs`.
      Verify: `& "C:\Program Files\dotnet\dotnet.exe" build KitWmsIntegrador.sln` —
      compila.

- [ ] 7. Configuração (appsettings + env) e fiação do Worker para varrer e imprimir resumo.
      - Criar classe de opções `OpcoesEntrada` em
        `src/KitWmsIntegrador.Worker/Configuracao/OpcoesEntrada.cs` com
        `PastaEntrada` (string, default `C:\Envia xml`).
      - Em `appsettings.json` adicionar seção `Entrada: { "PastaEntrada": "C:\\Envia xml" }`.
      - Em `Program.cs`: registrar `OpcoesEntrada` via
        `builder.Configuration.GetSection("Entrada")` com binding, e habilitar variáveis
        de ambiente (o host genérico já adiciona env vars; documentar o prefixo/override
        `Entrada__PastaEntrada`). Registrar `LeitorNotaFiscalXml`, `VarredorPasta`,
        `AgrupadorTransporte` na DI.
      - Em `Worker.cs`: no `ExecuteAsync`, resolver a pasta de `OpcoesEntrada`, rodar a
        varredura + agrupamento e **imprimir no log/console** o resumo:
        total de XMLs lidos, total de ondas/transportes distintos, distribuição de
        tamanhos de onda (quantas notas por transporte — ex.: min/max/média e contagem
        por tamanho), e quantos XMLs ficaram sem transporte. Em seguida chamar
        `StopApplication` (execução única nesta fase; watcher contínuo é fase futura).
        NÃO fazer qualquer chamada de rede; NÃO mover/alterar arquivos.
      Files: `src/KitWmsIntegrador.Worker/Configuracao/OpcoesEntrada.cs`,
      `src/KitWmsIntegrador.Worker/appsettings.json` (editar),
      `src/KitWmsIntegrador.Worker/Program.cs` (editar),
      `src/KitWmsIntegrador.Worker/Worker.cs` (editar).
      Verify: `& "C:\Program Files\dotnet\dotnet.exe" build KitWmsIntegrador.sln` —
      compila sem erros.

- [ ] 8. Testes xUnit contra XMLs reais e contra o extrator de transporte.
      Adicionar os testes no projeto `tests/KitWmsIntegrador.Tests`. Copiar 2 XMLs reais
      para `tests/KitWmsIntegrador.Tests/Recursos/` e marcá-los como
      `CopyToOutputDirectory=PreserveNewest` no `.csproj` (não ler de `C:\Envia xml`
      diretamente no teste, para o teste ser self-contained e não depender da pasta de
      dados). Os dois arquivos:
      - `NFE35251201838723009850550060156658071604491612.XML` (nNF=15665807,
        Transporte=0130693549, 2 itens) — valores confirmados no XML real.
      - `NFE35251201838723009850550060156663211093034056.XML` (nNF=15666321,
        Transporte=0130693967) — segundo XML real confirmado.
      Comando para copiar (PowerShell): usar `Copy-Item "C:\Envia xml\<arquivo>"
      "tests/KitWmsIntegrador.Tests/Recursos/<arquivo>"` (somente leitura da origem).
      Testes a escrever:
      - `ExtratorTransporteTests`: casa `"Transporte: 0130693549"`→`"0130693549"`
        (preserva zeros à esquerda); texto sem o padrão → null; entrada null → null.
      - `LeitorNotaFiscalXmlTests`: parse o XML 1 e afirma `Numero=="15665807"`,
        `ChaveNFe=="35251201838723009850550060156658071604491612"`,
        `NumeroTransporte=="0130693549"`, `Itens.Count==2`, e o primeiro item
        `CodigoProduto=="175894"`/`Quantidade==12.0000m`/`Cfop=="5102"`. Parse do XML 2
        e afirma `Numero=="15666321"` e `NumeroTransporte=="0130693967"`.
      - `AgrupadorTransporteTests`: dadas notas sintéticas (ou os 2 XMLs reais) com
        mesmo/diferentes transportes e uma sem transporte, afirma contagem de ondas,
        tamanho das ondas e lista de `SemTransporte`.
      Files: `tests/KitWmsIntegrador.Tests/KitWmsIntegrador.Tests.csproj` (editar p/ incluir Recursos),
      `tests/KitWmsIntegrador.Tests/Recursos/NFE35251201838723009850550060156658071604491612.XML`,
      `tests/KitWmsIntegrador.Tests/Recursos/NFE35251201838723009850550060156663211093034056.XML`,
      `tests/KitWmsIntegrador.Tests/ExtratorTransporteTests.cs`,
      `tests/KitWmsIntegrador.Tests/LeitorNotaFiscalXmlTests.cs`,
      `tests/KitWmsIntegrador.Tests/AgrupadorTransporteTests.cs`.
      Verify: `& "C:\Program Files\dotnet\dotnet.exe" test KitWmsIntegrador.sln` —
      todos os testes passam.

- [ ] 9. Executar a varredura real e reportar os números obtidos.
      Rodar o Worker apontado para `C:\Envia xml` e capturar o resumo impresso.
      Comando (PowerShell, na raiz):
      - `& "C:\Program Files\dotnet\dotnet.exe" run --project src/KitWmsIntegrador.Worker`
        (a pasta default já é `C:\Envia xml`; alternativamente sobrescrever via env
        `$env:Entrada__PastaEntrada="C:\Envia xml"` antes do run).
      Confirmar que o resumo sai com: total de XMLs lidos, total de transportes/ondas
      distintos, distribuição de tamanho das ondas e quantos XMLs sem transporte.
      Números de referência esperados: ~1196 XMLs, ~398 transportes distintos, ondas de
      até 10 notas, 0 sem transporte para o conjunto BRF atual. **Registrar os números
      efetivamente obtidos** (no output do step e, se desejado, num breve comentário no
      diário/doc). Confirmar que nenhum arquivo em `C:\Envia xml` foi movido/alterado
      (apenas leitura) e que nenhuma chamada de rede ocorreu.
      Files: nenhum arquivo de código alterado (passo de execução/verificação).
      Verify: o comando `run` imprime o resumo; os números batem na ordem de grandeza
      esperada (≈1196 lidos, ≈398 ondas, 0 sem transporte).

- [ ] 10. Higienização final e sanidade da solution.
      Garantir que não há `using`/implementações de rede, que os placeholders
      (ClienteWms/Construtores/Orquestrador/Estado/Notificacao) continuam vazios com TODO,
      e que a solution compila limpa. Adicionar um `.gitignore` de .NET na raiz
      (`bin/`, `obj/`, etc.) para não versionar artefatos. Remover quaisquer arquivos
      temporários criados durante a verificação.
      Files: `.gitignore` (criar na raiz).
      Verify: `& "C:\Program Files\dotnet\dotnet.exe" build KitWmsIntegrador.sln` e
      `& "C:\Program Files\dotnet\dotnet.exe" test KitWmsIntegrador.sln` — build e todos
      os testes passam; nenhuma referência a HttpClient/rede no código.

---

## Gaps e premissas assumidas

- **Nome exato dos XMLs de teste**: ambos confirmados existentes em `C:\Envia xml`.
  Assumido copiá-los para `Recursos/` para tornar os testes independentes da pasta de
  dados. Se, por política, o revisor preferir ler direto de `C:\Envia xml`, o teste
  pode ler por caminho configurável, mas a cópia é mais robusta e offline.
- **Encoding/mojibake**: campos-chave não afetados; não há tratamento especial nesta
  fase (ver decisão 8).
- **Execução única do Worker**: nesta fase o Worker roda a varredura e encerra
  (`StopApplication`). O modo watcher contínuo é fase futura (ver `docs/ARQUITETURA.md` §6).
- **Fonte única = XML**: conforme `docs/MAPEAMENTO-DADOS.md` §3b, a planilha é
  dispensada para agrupamento nesta fase; nenhum leitor de `.xls` é implementado aqui.
