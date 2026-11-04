# Novo corpo de e-mail de fechamento com identidade visual Friozem

A mudança reescreve `CorpoEmailResumo.Montar` para produzir um corpo HTML com a cara da Friozem — cabeçalho da marca, cartões coloridos por status, tabela de problemas com "chips" e assinatura do sistema no rodapé — tudo em tabelas com CSS inline para o Outlook. Para a logo aparecer sem o bloqueio de download de imagem, ela é embutida por `cid:` em vez de `data:` URI, o que exigiu uma evolução pequena do transporte (`MensagemEmail` ganhou uma imagem inline opcional e `EnviadorEmailSmtp` passa a montar `AlternateView` + `LinkedResource`). O `FechadorPacote` lê os bytes da logo de `Relatorio:LogoPath` e os repassa; sem logo válida, cabeçalho e assinatura caem para o texto "Friozem".

Watch for: o diff traz um redesenho completo e não relacionado de `docs/apresentacao/deck.html` (277+/160-), fora do escopo desta tarefa (confirmed). Nenhum concern bloqueante no código do e-mail.

**Verdict**: APPROVED

## High-level view

A geração do corpo ficou isolada em `CorpoEmailResumo`, que continua puro (sem I/O) e devolve um `CorpoEmail(Html, ImagemEmbutida)`. O layout é todo em `<table>` com `style=` inline, sem `<style>`/`<link>`/`<script>` e sem flexbox/grid — a escolha certa para Outlook. As cores, o formato de horário local e a leitura do PNG vêm de `IdentidadeVisual`, e o "chip" de status reusa `RenderizadorHtml.ChaveStatus` (promovido a `internal`), sem duplicar lógica.

A logo entra por CID: `IEnviadorEmail` ganhou o record `ImagemInline` e um campo opcional em `MensagemEmail` (default `null`, então nenhum chamador posicional quebra); o enviador monta um `AlternateView` HTML com `LinkedResource` apenas quando há imagem, mantendo o caminho `Body`/`IsBodyHtml` intacto para o resto. O PDF segue como anexo normal.

O período de coleta é formatado em horário local via `IdentidadeVisual.Horario`, sem o sufixo `+00:00`. Quando tudo integra sem ressalvas, um bloco verde "sem pendências" substitui a tabela; caso contrário, a tabela lista só as linhas com problema, com zebra e limite de 20, mais a nota "ver todas no PDF anexo".

A assinatura do rodapé é de sistema: nome e descrição do programa, `mailto:sac@friozem.com.br`, `https://www.friozem.com.br` e o aviso "Mensagem automática, não responda este e-mail.", sem telefone, pessoa ou redes sociais. Todo texto dinâmico passa por `WebUtility.HtmlEncode`; os rótulos estáticos são constantes controladas.

A única pendência é organizacional, não técnica: um redesenho grande de `deck.html` viajou no mesmo commit do e-mail.

<details>
<summary>Issues (1)</summary>

1. **Redesenho de deck.html fora de escopo** — o commit mistura uma reescrita grande de `docs/apresentacao/deck.html` (277+/160-) com a tarefa do e-mail; separar em outro commit facilita revisão e revert. Não bloqueante.

</details>

<details>
<summary>Details</summary>

### Logo por CID em vez de data: URI

A evolução do transporte é mínima e compatível. `IEnviadorEmail.cs` ganhou `ImagemInline(string ContentId, byte[] Conteudo, string ContentType = "image/png")` e `MensagemEmail` ganhou `ImagemInline? ImagemEmbutida = null` ao final — campo opcional, então as chamadas posicionais existentes não quebram. Em `EnviadorEmailSmtp.MontarMensagem`, quando há imagem o corpo vira um `AlternateView` HTML com um `LinkedResource` cujo `ContentId`, `ContentType` e `ContentLink = cid:<id>` apontam para a logo; sem imagem, o caminho antigo (`Body`/`IsBodyHtml`) é preservado. O HTML do corpo nunca referencia um `cid:` que não exista, porque `ImagemEmbutida` só é preenchida quando `temLogo`, e o mesmo `temLogo` controla `<img src="cid:...">` vs. o texto "Friozem" (confirmed). Isso cumpre o requisito de a logo aparecer sem bloqueio de download e sem `data:` URI.

### Escape de HTML

Todo texto dinâmico — número de transporte, nota, mensagem de erro (`UltimoErroResumido`) e o período — passa por `Enc`, que é `WebUtility.HtmlEncode`. O teste `Mensagem_EhHtmlEncoded` valida `&lt;script&gt;`, `&amp;` e `&quot;`. O único texto não encodado dentro de um chip é `l.StatusDescricao`, mas essa propriedade é computada e só retorna um de quatro literais fixos ("Integrada", "Integrada — revisar produto", "Pendente", "Não integrada"), sem conteúdo do WMS — não é um vetor (confirmed). Os rótulos de cartão e a assinatura são constantes do próprio arquivo.

### Escopo preservado

`FechadorPacote.FecharAsync` muda apenas três linhas: lê a logo, passa à nova assinatura de `Montar` e repassa `corpo.ImagemEmbutida` ao `MensagemEmail`. Construtor, assunto, nome do PDF e decisão de envio ficam intactos. Orquestrador, cliente WMS, flag I60, `PoliticaEnvioEmail` e os renderizadores de relatório não foram tocados (confirmed via diff do commit).

### Cobertura de testes

Os testes pedidos foram adicionados e, segundo a nota de verificação, a suíte passou (264 aprovados, 9 ignorados de stress; 19/19 no filtro focado). Cobrem: contagens nos cartões e período sem `+00:00`; tabelas e CSS inline sem `<style>/<link>/<script>`; fallback sem logo (sem `cid:`, sem `data:`, texto "Friozem"); logo por CID com `ContentId=logo-friozem` e `image/png`; escape de `< > & "`; truncamento com nota do PDF; bloco verde "sem pendências"; assinatura com mailto/site/aviso; e, no enviador, o `AlternateView` + `LinkedResource` vs. o caminho `Body` sem imagem. Nenhum e-mail real é enviado nos testes.

A evidência de build/test está presente na nota de verificação, então não re-rodei as suítes.

### Mudança não relacionada embarcada

O commit `e94566a` inclui `docs/apresentacao/deck.html` com 277 inserções e 160 remoções — um redesenho completo do deck de apresentação (nova paleta, novo layout de slides), sem relação com o corpo do e-mail. Não causa dano e não afeta o build, mas infla o diff e dificulta um revert pontual. Recomenda-se separar em commit próprio (confirmed).

</details>

<details>
<summary>Arquivos alterados</summary>

- `src/KitWmsIntegrador.Core/Email/IEnviadorEmail.cs` — novo record `ImagemInline`; campo opcional `ImagemEmbutida` em `MensagemEmail`.
- `src/KitWmsIntegrador.Core/Email/EnviadorEmailSmtp.cs` — `AlternateView` + `LinkedResource` (CID) quando há imagem; caminho `Body` preservado sem imagem.
- `src/KitWmsIntegrador.Core/Email/CorpoEmailResumo.cs` — reescrita do corpo (cabeçalho, cartões, tabela com chips, bloco "sem pendências", assinatura); retorno `CorpoEmail`.
- `src/KitWmsIntegrador.Core/Pacote/FechadorPacote.cs` — lê a logo e repassa a imagem inline ao `MensagemEmail`.
- `tests/KitWmsIntegrador.Tests/CorpoEmailResumoTests.cs` — testes da nova estrutura (cartões, CID/fallback, escape, truncamento, "sem pendências", assinatura).
- `tests/KitWmsIntegrador.Tests/EnviadorEmailSmtpTests.cs` — teste do `AlternateView`/`LinkedResource` por CID.
- `tests/KitWmsIntegrador.Tests/EmailPorPacoteTests.cs`, `PacoteEndToEndTests.cs` — asserts ajustados à nova marcação.
- `docs/OPERACAO.md` — seção 4 descreve o novo corpo e a origem da logo (`Relatorio:LogoPath`).
- `docs/exemplos/email-pacote-exemplo.html` — exemplo gerado pelo código real.
- `docs/apresentacao/deck.html` — redesenho do deck, **não relacionado** a esta tarefa.

Diff completo: `git -C c:\wms-integrador diff HEAD~1`.

</details>
