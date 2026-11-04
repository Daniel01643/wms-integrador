# Plano de implementação — Melhorar o corpo do e-mail de fechamento de pacote

Projeto: KitWmsIntegrador (C#/.NET 8). Solução `c:\wms-integrador\KitWmsIntegrador.sln`.
Branch `main`, sem worktree. Base verde confirmada (`dotnet build` = 0 avisos / 0 erros no commit `95c0098`).

Escopo: APENAS a apresentação do corpo do e-mail de fechamento (`CorpoEmailResumo`) e, por causa da
decisão de logo via CID, uma evolução pequena de `MensagemEmail` + `EnviadorEmailSmtp` para carregar uma
imagem embutida. NÃO tocar em Orquestrador, cliente WMS, flag I60, decisão de pacote nem no conteúdo do
relatório HTML/PDF (`RenderizadorHtml`/`RenderizadorPdf`).

## Caminhos de arquivo reais (verificados nesta exploração)

- `src\KitWmsIntegrador.Core\Email\CorpoEmailResumo.cs` — gera o corpo hoje (classe estática, `Montar(...)`).
- `src\KitWmsIntegrador.Core\Email\IEnviadorEmail.cs` — contém os records `AnexoEmail`, **`MensagemEmail`**
  (o arquivo `MensagemEmail.cs` citado no pedido NÃO existe; o record vive aqui), `ResultadoEnvioEmail` e a interface.
- `src\KitWmsIntegrador.Core\Email\EnviadorEmailSmtp.cs` — monta a `MailMessage` em `MontarMensagem` (interno, testável).
- `src\KitWmsIntegrador.Core\Email\PoliticaEnvioEmail.cs` — decisão de envio (NÃO alterar).
- `src\KitWmsIntegrador.Core\Configuracao\OpcoesEmail.cs` — opções de e-mail (o pedido citou `Email\OpcoesEmail.cs`; o real é em `Configuracao\`).
- `src\KitWmsIntegrador.Core\Configuracao\OpcoesRelatorio.cs` — `LogoPath` (fonte da logo).
- `src\KitWmsIntegrador.Core\Relatorio\IdentidadeVisual.cs` — paleta Friozem + `Horario()` + `LerLogoPng()` + `Plural()`. É `internal`, mas Email está no MESMO assembly (Core), então pode usar direto.
- `src\KitWmsIntegrador.Core\Relatorio\RenderizadorHtml.cs` — referência de cartões/cores do relatório. **Não reutilizável direto** no e-mail: usa `<style>` no head e flexbox (Outlook ignora). Serve de modelo visual, não de código.
- `src\KitWmsIntegrador.Core\Relatorio\ModeloRelatorio.cs` — `ModeloRelatorio.Montar(linhas).Resumo` (contagens) — JÁ usado por `CorpoEmailResumo`.
- `src\KitWmsIntegrador.Core\Relatorio\LinhaRelatorioErro.cs` — `StatusDescricao`, `UltimoErroResumido` (usa `ResumoMensagem`), `EstaIntegrada`, `IntegradaRevisarProduto`, `StatusEfetivo`.
- `src\KitWmsIntegrador.Core\Relatorio\ResumoMensagem.cs` — `Resumir(...)` (já reusado via `LinhaRelatorioErro.UltimoErroResumido`).
- `src\KitWmsIntegrador.Core\Pacote\FechadorPacote.cs` — único chamador de `CorpoEmailResumo.Montar` (linha ~88); também monta assunto e anexa PDF.
- `tests\KitWmsIntegrador.Tests\CorpoEmailResumoTests.cs`, `EnviadorEmailSmtpTests.cs`, `EmailPorPacoteTests.cs` — testes existentes a manter/ajustar.
- `tests\KitWmsIntegrador.Tests\Infra\AmbientePacote.cs` — `GravadorEmail` (fake que só guarda `MensagemEmail`); `GerenciadorPacote`/`FechadorPacote` montados com `OpcoesRelatorio { PastaSaida = Relatorios }` (LogoPath vazio por padrão).
- `docs\OPERACAO.md` seção 4 (E-mail).

Comando de build: `dotnet build c:\wms-integrador\KitWmsIntegrador.sln`.
Comando de teste (toda a suíte): `dotnet test c:\wms-integrador\KitWmsIntegrador.sln`.
Teste de um só arquivo/classe: `dotnet test c:\wms-integrador\tests\KitWmsIntegrador.Tests\KitWmsIntegrador.Tests.csproj --filter "FullyQualifiedName~CorpoEmailResumo"`.

## Decisões de projeto (sem design aprovado; registradas aqui)

1. **Logo por CID (LinkedResource), não base64.** O requisito-alvo é a logo aparecer sem o aviso "o Outlook
   impediu o download de imagens"; o Outlook costuma bloquear `data:` URIs mas renderiza imagens embutidas por
   `cid:`. Implico evoluir o transporte: `MensagemEmail` ganha uma imagem inline opcional e `EnviadorEmailSmtp`
   passa a montar um `AlternateView` HTML com um `LinkedResource` (`ContentId`, `ContentType=image/png`,
   `TransferEncoding=Base64`, `ContentDisposition.Inline=true`). O PDF continua como `Attachment` normal.
2. **Fallback sem quebrar.** Quando `Relatorio:LogoPath` estiver vazio/inválido, `IdentidadeVisual.LerLogoPng`
   devolve `null`; nesse caso NÃO há CID e o cabeçalho/assinatura caem para o texto "Friozem" na cor da marca —
   mesmo comportamento do relatório. O corpo nunca referencia um `cid:` que não exista.
3. **`CorpoEmailResumo` recebe os bytes do logo, não o caminho.** Manter o `CorpoEmailResumo` sem I/O (hoje é
   puro e testável). Quem lê o PNG é `FechadorPacote`, reusando `IdentidadeVisual.LerLogoPng(_opcoesRelatorio.LogoPath)`
   (mesma fonte que o relatório). Assim o e-mail e o relatório embutem exatamente a mesma imagem.
4. **O corpo retorna HTML + a imagem a embutir juntos.** `Montar` passa a devolver um pequeno resultado
   (`CorpoEmail` com `Html` e `ImagemInline?`) para `FechadorPacote` repassar ao `MensagemEmail`. O CID é uma
   constante conhecida (ex.: `logo-friozem`) definida uma vez e usada pelo corpo e pelo enviador.
5. **Renderização própria para Outlook, cores reaproveitadas.** Como `RenderizadorHtml` usa `<style>`/flexbox,
   o e-mail terá seu próprio layout em TABELAS com CSS inline (`style=` em cada elemento), reaproveitando as
   constantes de cor de `IdentidadeVisual` (Azul, AzulClaro, Verde, Ambar, Vermelho, Cinza, Texto) e os helpers
   `IdentidadeVisual.Horario`, `Plural` e `ResumoMensagem`/`LinhaRelatorioErro.UltimoErroResumido`. Cartões = células
   de tabela coloridas por status (não flexbox).
6. **Decisão em aberto (duplicata WMS) — só documentar.** Acrescentar um comentário em `CorpoEmailResumo`
   registrando que a resposta real de duplicata do homolog ("...já existe com Id[...]. Nao permitida acao de I...")
   chega classificada como **Pendente**, e que o corpo apenas a exibe de forma legível (via `UltimoErroResumido`),
   sem mexer no classificador. Nada de lógica nova.

## Itens (ordenados por dependência; cada item deixa a solução compilável)

- [ ] 1. Evoluir o transporte para suportar uma imagem embutida por CID.
      Em `IEnviadorEmail.cs`, criar o record `ImagemInline(string ContentId, byte[] Conteudo, string ContentType = "image/png")`
      e adicionar ao record `MensagemEmail` um parâmetro OPCIONAL `ImagemInline? ImagemEmbutida = null` ao final
      (mantém compatível toda chamada posicional existente). Documentar com doc-comments.
      Files: `src\KitWmsIntegrador.Core\Email\IEnviadorEmail.cs`
      Verify: `dotnet build c:\wms-integrador\KitWmsIntegrador.sln` — 0 erros, 0 avisos (nenhum chamador quebra porque o novo campo tem default).

- [ ] 2. Fazer o `EnviadorEmailSmtp` montar a imagem inline como `AlternateView` + `LinkedResource` quando houver.
      Em `MontarMensagem`, se `mensagem.ImagemEmbutida` for não nula: criar um `AlternateView.CreateAlternateViewFromString(CorpoHtml, Encoding.UTF8, MediaTypeNames.Text.Html)`, adicionar um `LinkedResource` com `ContentId = ImagemEmbutida.ContentId`, `ContentType = new ContentType(ImagemEmbutida.ContentType)`, `TransferEncoding = Base64`, `ContentLink`/`ContentDisposition.Inline = true`, e anexar via `msg.AlternateViews.Add(...)` em vez de `msg.Body`. Sem imagem, manter o caminho atual (`Body`+`IsBodyHtml`). PDF continua em `msg.Attachments`. Nunca logar segredo (já respeitado).
      Files: `src\KitWmsIntegrador.Core\Email\EnviadorEmailSmtp.cs`
      Verify: `dotnet test ...Tests.csproj --filter "FullyQualifiedName~EnviadorEmailSmtp"` — testes existentes passam; novo teste do item 7 valida o AlternateView.

- [ ] 3. Definir o CID compartilhado e o resultado do corpo.
      Em `CorpoEmailResumo.cs`, adicionar a constante `public const string CidLogo = "logo-friozem";` e o record
      de retorno `public sealed record CorpoEmail(string Html, ImagemInline? ImagemEmbutida);`. (Preparação para os itens 4–6; ainda sem mudar a assinatura de `Montar`.)
      Files: `src\KitWmsIntegrador.Core\Email\CorpoEmailResumo.cs`
      Verify: `dotnet build c:\wms-integrador\KitWmsIntegrador.sln` — 0 erros, 0 avisos.

- [ ] 4. Reescrever a geração do corpo com identidade visual, cartões e tabela legível (tudo tabela + CSS inline).
      Trocar o `Montar(...)` para devolver `CorpoEmail` e aceitar `byte[]? logoPng` (bytes já lidos; nunca faz I/O).
      Montar com `<table>` de largura fixa (ex. 640px) centralizada:
      (a) **Cabeçalho**: faixa na cor `IdentidadeVisual.Azul`; à esquerda `<img src="cid:logo-friozem">` quando `logoPng != null`, senão o texto "Friozem" em branco/azul; título "KitWms Integrador — fechamento do pacote".
      (b) **Período de coleta** (horário LOCAL, sem `+00:00`) usando `IdentidadeVisual.Horario(...)` para primeira/última gravação e horário de fechamento — manter as duas formas (com `periodo` e sem).
      (c) **Cartões por status** como células de tabela coloridas (borda-topo + número grande + rótulo), reaproveitando as cores: Total (azul/cinza), Integradas (Verde), Integradas — revisar produto (Verde com marca), Pendentes (Ambar), Não integradas (Vermelho), XMLs inválidos (Vermelho), Transportes. Números vêm de `ModeloRelatorio.Montar(linhas).Resumo` + `xmlsInvalidos`.
      (d) **Tabela de problemas**: só as linhas com problema (`!EstaIntegrada || IntegradaRevisarProduto`), limite `maxLinhas` (default 20), cabeçalho azul, zebra (linhas alternadas com fundo claro), coluna Status com "chip" colorido conforme o status; mensagem via `l.UltimoErroResumido`. Nota "…e mais X, ver PDF anexo" quando exceder.
      (e) Quando NÃO há problemas, bloco verde "Todas as notas integradas, sem pendências" (sem tabela vazia).
      (f) Comentário registrando a decisão em aberto da duplicata (item 6 das decisões).
      Todo texto dinâmico continua HTML-encoded (reusar o helper `Enc` existente, baseado em `WebUtility.HtmlEncode`).
      O record devolvido traz `ImagemEmbutida = logoPng is null ? null : new ImagemInline(CidLogo, logoPng)`.
      Files: `src\KitWmsIntegrador.Core\Email\CorpoEmailResumo.cs`
      Verify: `dotnet test ...Tests.csproj --filter "FullyQualifiedName~CorpoEmailResumo"` — após ajustar os testes (item 8) todos passam.

- [ ] 5. Adicionar a assinatura do PROGRAMA no rodapé (bloco discreto, dentro do mesmo HTML do corpo).
      Ainda em `CorpoEmailResumo`, após o conteúdo, inserir um rodapé com separador acima (borda/linha fina),
      fonte menor e cor suave (`IdentidadeVisual.Texto`/cinza): a MESMA logo por `cid:logo-friozem` (ou texto
      "Friozem" no fallback); "KitWms Integrador"; "Integração automática de notas fiscais com o WMS";
      "A serviço da Friozem Logística — Corporativo"; contato `sac@friozem.com.br` como `mailto:`; site
      `www.friozem.com.br` como link `https://www.friozem.com.br`; e a linha pequena "Mensagem automática, não
      responda este e-mail.". SEM telefone, SEM nome de pessoa, SEM redes sociais. Conteúdo fixo (constantes no arquivo).
      Files: `src\KitWmsIntegrador.Core\Email\CorpoEmailResumo.cs`
      Verify: `dotnet test ...Tests.csproj --filter "FullyQualifiedName~CorpoEmailResumo"` — teste do item 8 confirma a assinatura, o mailto e o aviso.

- [ ] 6. Ligar o `FechadorPacote` à nova assinatura: ler a logo e repassar a imagem inline ao `MensagemEmail`.
      Em `FecharAsync`, substituir `var corpo = CorpoEmailResumo.Montar(linhas, xmlsInvalidos, inicio, fim, periodo: periodo);`
      por: ler `var logo = IdentidadeVisual.LerLogoPng(_opcoesRelatorio.LogoPath);` e
      `var corpo = CorpoEmailResumo.Montar(linhas, xmlsInvalidos, inicio, fim, logo, periodo: periodo);`
      Construir `new MensagemEmail(assunto, corpo.Html, new[] { new AnexoEmail(nomePdf, pdf) }, corpo.ImagemEmbutida)`.
      NÃO alterar construtor do `FechadorPacote`, assunto, nem a lógica de nome/decisão de envio.
      Files: `src\KitWmsIntegrador.Core\Pacote\FechadorPacote.cs`
      Verify: `dotnet test ...Tests.csproj --filter "FullyQualifiedName~EmailPorPacote"` — os testes de pacote (`PoliticaSempre...`, etc.) continuam passando, incluindo `Total de notas` esperado (ver ajuste no item 8).

- [ ] 7. Teste do enviador para a imagem embutida (CID).
      Em `EnviadorEmailSmtpTests.cs`, adicionar teste que monta `MensagemEmail` com `ImagemEmbutida` preenchida,
      chama `EnviadorEmailSmtp.MontarMensagem`, e verifica: `AlternateViews` contém 1 view HTML; a view tem um
      `LinkedResource` com `ContentId == "logo-friozem"` e `ContentType.MediaType == "image/png"`; o PDF segue em
      `Attachments`. Manter o teste atual (sem imagem) validando o caminho `Body`/`IsBodyHtml`.
      Files: `tests\KitWmsIntegrador.Tests\EnviadorEmailSmtpTests.cs`
      Verify: `dotnet test ...Tests.csproj --filter "FullyQualifiedName~EnviadorEmailSmtp"` — todos passam.

- [ ] 8. Atualizar/expandir os testes do corpo para a nova estrutura.
      Em `CorpoEmailResumoTests.cs`: ajustar os asserts que casam o texto antigo (`"Total de notas: 3"`,
      `"Integradas: 1"`, `"e mais 3, ver PDF"`, `"<td>001</td>"`, `DoesNotContain("<table")`) para a nova marcação
      — como `Montar` passa a devolver `CorpoEmail`, usar `.Html`. Cobrir: contagens nos cartões; HTML-encode de
      `<script>` na mensagem (mantém); truncamento com nota "ver todas no PDF anexo"; bloco verde "Todas as notas
      integradas, sem pendências" quando 100% integrado; ausência da imagem inline quando `logoPng == null`;
      presença de `src="cid:logo-friozem"` e `ImagemEmbutida != null` quando `logoPng` é um PNG válido (usar bytes
      com a assinatura PNG); assinatura do rodapé contém `mailto:sac@friozem.com.br`, `https://www.friozem.com.br`
      e "Mensagem automática, não responda este e-mail."; período em horário local sem `+00:00`.
      Files: `tests\KitWmsIntegrador.Tests\CorpoEmailResumoTests.cs`
      Verify: `dotnet test ...Tests.csproj --filter "FullyQualifiedName~CorpoEmailResumo"` — todos passam.

- [ ] 9. Atualizar a documentação (seção 4 do OPERACAO.md).
      Acrescentar 1–2 frases: o corpo agora traz identidade visual Friozem, cartões por status e assinatura do
      sistema; a logo é embutida por CID (vem de `Relatorio:LogoPath`; sem logo válida, cai para texto "Friozem");
      contato de dúvidas `sac@friozem.com.br`. Sem mudar as chaves de configuração.
      Files: `docs\OPERACAO.md`
      Verify: revisão de leitura — a seção 4 descreve o novo corpo e a origem da logo (`Relatorio:LogoPath`).

- [ ] 10. Verificação final de integração.
      Rodar a suíte inteira e o build.
      Files: (nenhum)
      Verify: `dotnet build c:\wms-integrador\KitWmsIntegrador.sln` = 0 erros / 0 avisos **e** `dotnet test c:\wms-integrador\KitWmsIntegrador.sln` = todos os testes verdes.

## Lacunas / suposições

- A logo do e-mail depende de `Relatorio:LogoPath` apontar para um PNG válido; hoje o default é vazio, então em
  produção sem logo configurada o corpo usa o texto "Friozem" (igual ao relatório). Isso é intencional (nunca quebrar).
- O pedido cita `Email\MensagemEmail.cs` e `Email\OpcoesEmail.cs`; os reais são `Email\IEnviadorEmail.cs` (record)
  e `Configuracao\OpcoesEmail.cs`. O plano usa os caminhos reais.
- O assunto do e-mail permanece como está (requisito 5).
- Decisão da duplicata do WMS: apenas registrada em comentário; nenhum classificador é alterado.

## Commit

Commit pequeno ao final, adicionando apenas os arquivos nomeados acima (nunca `git add .`). Revisor sempre
como último passo do loop. Sem push.
