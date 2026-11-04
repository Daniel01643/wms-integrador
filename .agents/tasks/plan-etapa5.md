# Plano de Implementação — Etapa 5

Três mudanças no KitWmsIntegrador (.NET 8, C#). Base: build verde, **59 testes passando**
(confirmado com `dotnet test KitWmsIntegrador.sln -c Debug`). Executável do dotnet:
`dotnet` resolve no PATH (versão 8.0.427); caso não resolva, usar
`C:\Program Files\dotnet\dotnet.exe`.

## Nomes reais verificados (lidos nos arquivos)

- `NotaFiscal` (Core/Modelos/NotaFiscal.cs): `ChaveNFe` (string), `Numero` (string),
  `Serie` (string), `Protocolo` (string?), `ValorTotal` (decimal?), `Volumes`
  (`VolumeTransporte?`), `Itens` (`IReadOnlyList<ItemNota>`), `NumeroTransporte` (string?).
  **NÃO existe** campo de infCpl exposto hoje (só `NumeroTransporte` derivado dele).
- `VolumeTransporte` (Core/Modelos/VolumeTransporte.cs): `QuantidadeVolumes` (**string?**),
  `PesoLiquido` (decimal?), `PesoBruto` (decimal?).
- `ItemNota` (Core/Modelos/ItemNota.cs): `Cfop` (string), `ValorUnitario` (decimal),
  `Quantidade` (decimal).
- `PedidoI50` (Core/Construtores/Dtos/PayloadI50.cs): `Atributo1`, `Atributo2`,
  `Atributo3`, `Observacao` — todas `string` default `string.Empty`, com
  `[JsonPropertyName("atributo1"/"atributo2"/"atributo3"/"Observacao")]`.
- `LeitorNotaFiscalXml` (Core/Leitura/LeitorNotaFiscalXml.cs): já lê `infCpl` numa variável
  local `string? infCpl` em `case "infAdic"` via `LerInfCpl`, e o usa só para
  `ExtratorTransporte.Extrair(infCpl)`. O texto bruto NÃO é exposto hoje.
- `FormatoWms` (Core/Construtores/FormatoWms.cs): classe estática com helpers invariantes
  (`Data`, `Decimal`, `ZerosEsquerda`, `Documento`, `Cep`). É onde entram os novos helpers.
- `JsonWms.Opcoes` (Core/ClienteWms/JsonWms.cs): `JsonSerializerDefaults.Web` +
  `WhenWritingNull`.
- DI do Worker: `src/KitWmsIntegrador.Worker/Program.cs` registra opções via
  `AddOptions<T>().Bind(config.GetSection(T.Secao))`. `OpcoesI50`/`OpcoesOperacionais`
  já registradas. appsettings em `src/KitWmsIntegrador.Worker/appsettings.json`.
- Testes: xUnit; recursos XML em `tests/KitWmsIntegrador.Tests/Recursos/`, copiados para
  o output (`<None Include="Recursos\**\*.XML" CopyToOutputDirectory=PreserveNewest>`).
  XML de referência: `NFE35251201838723009850550060156658071604491612.XML`
  (nNF 15665807, chave 44 díg, 2 itens, CFOP 1º item `5102`, ValorTotal 12644.40,
  PesoBruto 302.675, PesoLiquido 275.640, QuantidadeVolumes "36", Protocolo presente,
  infCpl longo começando "VD: 00842983 ... Transporte: 0130693549 ...").

## Decisões de design (tomadas aqui, grão fino)

- **Helper de atributos**: criar métodos estáticos em `FormatoWms` (coeso com os demais
  helpers de formatação, já referenciado pelos dois construtores). `Truncar(string?, int)`
  genérico; `Atributo3(decimal? valorTotal, decimal? pesoBruto, decimal? pesoLiquido,
  string? qtdVolumes)` monta o texto visual pt-BR. Rótulos visuais usam vírgula decimal
  (`CultureInfo("pt-BR")`) — isolado NESTES textos de exibição; os campos numéricos reais
  do payload continuam com ponto invariante (inalterados).
- **infCpl**: adicionar `InfoComplementar` (string?) em `NotaFiscal` e preencher no parser
  reutilizando a variável `infCpl` já lida (zero custo extra de parsing).
- **Flag I60**: `OpcoesI60` com `Habilitada=false` default, seção "I60", registrada no DI.
  Nada da I60 existente é tocado (construtor/DTO/testes intactos). A flag é só sinalização
  para a Etapa 5 futura (Orquestrador), documentada em comentário.
- **Relatório**: `IGeradorRelatorio` + `GeradorRelatorio` em `Core/Relatorio`. Record
  `LinhaRelatorioErro(string NumeroNota, string NumeroTransporte, DateTimeOffset HorarioErro,
  string Mensagem)`. HTML via `StringBuilder` (sem dependência). PDF via **PdfSharpCore
  1.3.67** (MIT, exata). `OpcoesRelatorio` com `PastaSaida` configurável (default subpasta
  "relatorios"). Nome de arquivo `erros_<yyyy-MM-dd_HHmm>.<ext>`.
  Observação: PdfSharpCore traz `SixLabors.ImageSharp` como dependência transitiva (tem
  CVEs conhecidas); o relatório é só texto/tabela, não usa imagens — registrar a ressalva,
  sem ação de imagem.

---

## MUDANÇA 1 — Enriquecer a I50 com dados fiscais da I60

- [ ] 1. Expor o infCpl bruto no domínio. Adicionar `public string? InfoComplementar { get; init; }`
      em `NotaFiscal`; no parser, atribuir `InfoComplementar = infCpl` no `return new NotaFiscal{...}`
      (a variável `infCpl` já é lida em `case "infAdic"`).
      Files: src/KitWmsIntegrador.Core/Modelos/NotaFiscal.cs,
      src/KitWmsIntegrador.Core/Leitura/LeitorNotaFiscalXml.cs
      Verify: `dotnet build KitWmsIntegrador.sln -c Debug` compila; os 59 testes continuam
      passando (`dotnet test`).

- [ ] 2. Adicionar helpers de formatação em `FormatoWms`: `Truncar(string? valor, int max)`
      (preserva início, retorna "" se null) e `Atributo3(decimal? valorTotal,
      decimal? pesoBruto, decimal? pesoLiquido, string? qtdVolumes)` que monta
      `"Vlr: 12.644,40 | PB: 302,675 | PL: 275,640 | Vol: 36"` em pt-BR (vírgula decimal),
      omitindo rótulos cujo valor é null. Também um helper opcional `Atributo1`/`Atributo2`
      se útil, ou montar inline no construtor.
      Files: src/KitWmsIntegrador.Core/Construtores/FormatoWms.cs
      Verify: cobertos pelos testes do passo 4; `dotnet build` compila.

- [ ] 3. Preencher `Atributo1/2/3` e `Observacao` no `ConstrutorI50.Construir`:
      atributo1 = `$"CH:{nota.ChaveNFe} PROT:{nota.Protocolo}"` (truncado a 200);
      atributo2 = `$"NF:{nota.Numero} S:{nota.Serie} CFOP:{cfop1ºitem}"` (trunc 200);
      atributo3 = `FormatoWms.Atributo3(...)` (trunc 200);
      Observacao = `FormatoWms.Truncar(nota.InfoComplementar, 255)`.
      CFOP do 1º item via `nota.Itens.Count > 0 ? nota.Itens[0].Cfop : ""`.
      Files: src/KitWmsIntegrador.Core/Construtores/ConstrutorI50.cs
      Verify: passo 4.

- [ ] 4. Testes: em `ConstrutorI50Tests` validar atributo1 contém chave+protocolo,
      atributo2 contém NF/Série/CFOP 5102, atributo3 contém "Vlr:"/"Vol: 36" com vírgula
      decimal, Observacao não-vazia e <= 255; truncamento (string longa → 200/255). Em
      `LeitorNotaFiscalXmlTests` validar `InfoComplementar` contém "Transporte: 0130693549".
      Novos testes de `FormatoWms` (Truncar/Atributo3) em arquivo próprio
      `FormatoWmsTests.cs` (valor null → rótulo omitido; vírgula decimal).
      Files: tests/KitWmsIntegrador.Tests/ConstrutorI50Tests.cs,
      tests/KitWmsIntegrador.Tests/LeitorNotaFiscalXmlTests.cs,
      tests/KitWmsIntegrador.Tests/FormatoWmsTests.cs
      Verify: `dotnet test KitWmsIntegrador.sln -c Debug` — todos passam (59 + novos).

## MUDANÇA 2 — Flag para desligar a I60 (sem remover)

- [ ] 5. Criar `OpcoesI60` em Core/Configuracao com `const string Secao = "I60"` e
      `public bool Habilitada { get; set; } = false;` (XML-doc explicando: I60 desligada
      por ora, I50 carrega os dados fiscais; reativável; o fluxo de envio da Etapa 5 deve
      respeitar esta flag). NÃO tocar em ConstrutorI60/PayloadI60/testes da I60.
      Files: src/KitWmsIntegrador.Core/Configuracao/OpcoesI60.cs
      Verify: `dotnet build` compila.

- [ ] 6. Registrar no DI do Worker (`AddOptions<OpcoesI60>().Bind(GetSection(OpcoesI60.Secao))`)
      e adicionar seção "I60" no appsettings.json com `_comentario` explicando que está
      desligada por ora e é reativável, e `"Habilitada": false`.
      Files: src/KitWmsIntegrador.Worker/Program.cs,
      src/KitWmsIntegrador.Worker/appsettings.json
      Verify: `dotnet build` compila; teste do passo 7.

- [ ] 7. Teste de binding/default: `OpcoesI60Tests` confirmando `Habilitada` default false
      e que bind de `{"Habilitada": true}` resulta true (via
      `ConfigurationBuilder.AddInMemoryCollection`).
      Files: tests/KitWmsIntegrador.Tests/OpcoesI60Tests.cs
      Verify: `dotnet test` — passa.

## MUDANÇA 3 — Gerador de relatório HTML + PDF

- [ ] 8. Adicionar `PackageReference Include="PdfSharpCore" Version="1.3.67"` ao Core.csproj.
      Files: src/KitWmsIntegrador.Core/KitWmsIntegrador.Core.csproj
      Verify: `dotnet restore` + `dotnet build` resolvem o pacote.

- [ ] 9. Criar modelo e opções: record `LinhaRelatorioErro(string NumeroNota,
      string NumeroTransporte, DateTimeOffset HorarioErro, string Mensagem)` e
      `OpcoesRelatorio { const Secao="Relatorio"; string PastaSaida = "relatorios"; }`.
      Files: src/KitWmsIntegrador.Core/Relatorio/LinhaRelatorioErro.cs,
      src/KitWmsIntegrador.Core/Configuracao/OpcoesRelatorio.cs
      Verify: `dotnet build` compila.

- [ ] 10. Criar `IGeradorRelatorio` com `string GerarHtml(IReadOnlyList<LinhaRelatorioErro>,
      DateTimeOffset geradoEm)`, `byte[] GerarPdf(IReadOnlyList<LinhaRelatorioErro>,
      DateTimeOffset geradoEm)`, e `GerarArquivos(...)` que escreve os dois arquivos em
      `PastaSaida` com nome `erros_<yyyy-MM-dd_HHmm>.<ext>` e retorna os caminhos.
      Implementar `GeradorRelatorio`: HTML via StringBuilder (título, data de geração,
      `<table>` com cabeçalho 4 colunas + linhas, horário formatado com fuso); PDF via
      PdfSharpCore (`PdfDocument`, `XGraphics`, `XFont`, desenho manual de cabeçalho/linhas).
      Horário formatado p/ exibição (ex.: `dd/MM/yyyy HH:mm:ss zzz`).
      Files: src/KitWmsIntegrador.Core/Relatorio/IGeradorRelatorio.cs,
      src/KitWmsIntegrador.Core/Relatorio/GeradorRelatorio.cs
      Verify: passo 12.

- [ ] 11. Registrar no DI: `AddOptions<OpcoesRelatorio>().Bind(...)` e
      `AddSingleton<IGeradorRelatorio, GeradorRelatorio>()`; seção "Relatorio" no appsettings
      com `_comentario` e `PastaSaida`.
      Files: src/KitWmsIntegrador.Worker/Program.cs,
      src/KitWmsIntegrador.Worker/appsettings.json
      Verify: `dotnet build` compila.

- [ ] 12. Testes `GeradorRelatorioTests` com lista de 2-3 linhas de exemplo: HTML contém
      número da nota, transporte, mensagem, horário formatado e tags `<table>`/`<tr>`/`<td>`;
      PDF (`GerarPdf`) retorna bytes começando com `%PDF` e não-vazio; `GerarArquivos` cria
      ambos os arquivos com o padrão de nome no diretório temporário e limpa depois.
      Files: tests/KitWmsIntegrador.Tests/GeradorRelatorioTests.cs
      Verify: `dotnet test KitWmsIntegrador.sln -c Debug` — todos passam.

## Verificação final

- [ ] 13. Rodar `dotnet build KitWmsIntegrador.sln -c Debug` e
      `dotnet test KitWmsIntegrador.sln -c Debug`; confirmar 0 falhas e reportar o total de
      testes (59 existentes + novos). Nenhuma chamada HTTP; Orquestrador/e-mail/movimentação
      de arquivos NÃO implementados. Sem segredos commitados.

## Restrições (do pedido)

- Manter os 59 testes passando; adicionar testes para as 3 mudanças.
- Sem chamadas HTTP; sem Orquestrador (Etapa 5), sem e-mail, sem mover arquivos.
- Não remover nada da I60 — apenas a flag.
- Não commitar segredos.
