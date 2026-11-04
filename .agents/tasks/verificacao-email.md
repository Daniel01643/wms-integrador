# Verificação — melhoria do corpo do e-mail de fechamento

Branch `main`, sem worktree. Esta é a PRIMEIRA iteração (não existia `review-email.json`).

## Comandos executados e resultados

### Build (solução)

```
dotnet build c:\wms-integrador\KitWmsIntegrador.sln
```

Resultado: **Compilação com êxito — 0 Aviso(s), 0 Erro(s).**

### Testes (suíte inteira)

```
dotnet test c:\wms-integrador\KitWmsIntegrador.sln
```

Resultado: **Aprovado — Com falha: 0, Aprovado: 264, Ignorado: 9, Total: 273.**

- Os 9 ignorados são os testes de stress (`CenariosStress`), marcados como `[SKIP]` por padrão (como antes).
- Antes desta tarefa a suíte tinha 259 testes aprovados; foram adicionados testes novos ao corpo do e-mail
  e ao enviador (CID), subindo para 264 aprovados.

### Testes focados (durante o desenvolvimento)

```
dotnet test c:\wms-integrador\tests\KitWmsIntegrador.Tests\KitWmsIntegrador.Tests.csproj --filter "FullyQualifiedName~CorpoEmailResumo|FullyQualifiedName~EnviadorEmailSmtp|FullyQualifiedName~EmailPorPacote|FullyQualifiedName~PacoteEndToEnd"
```

Resultado: **Aprovado — 19/19.**

## Cobertura dos testes (o que cada caso valida)

`CorpoEmailResumoTests`:
- `Cartoes_MostramContagensPorStatus` — rótulos dos cartões por status e período em horário local **sem `+00:00`**.
- `UsaTabelasECssInline_SemStyleLinkScript` — usa `<table>` e `style=` inline; **sem** `<style>`/`<link>`/`<script>`.
- `SemLogo_NaoEmbuteImagemNemDataUri` — sem logo: `ImagemEmbutida == null`, sem `cid:` e **sem `data:` URI**; cai para texto "Friozem".
- `ComLogo_EmbuteImagemPorCid_SemDataUri` — com PNG: `ImagemEmbutida` com `ContentId = logo-friozem`, `src="cid:logo-friozem"`, sem `data:` URI.
- `Mensagem_EhHtmlEncoded` — escape de `<`, `>`, `&` e aspas (`&lt;`, `&amp;`, `&quot;`) na mensagem dinâmica.
- `Truncamento_IndicaRestantesComNotaDoPdf` — trunca em `maxLinhas` e mostra "e mais X, ver todas no PDF anexo".
- `SoIntegradas_MostraBlocoSemPendencias_SemTabelaDeProblemas` — 100% integrado mostra o bloco verde "Todas as notas integradas, sem pendências" e **não** a tabela de problemas.
- `Assinatura_DoSistema_ComContatoESite` — rodapé com `mailto:sac@friozem.com.br`, `https://www.friozem.com.br` e "Mensagem automática, não responda este e-mail.".

`EnviadorEmailSmtpTests`:
- `MontarMensagem_ComImagemEmbutida_MontaAlternateViewComLinkedResourceCid` — inspeciona o `MailMessage`
  montado por `MontarMensagem` (SEM enviar e-mail): 1 `AlternateView` HTML, com `LinkedResource` cujo
  `ContentId == "logo-friozem"` e `ContentType.MediaType == "image/png"`; o PDF continua em `Attachments`.
- `MontarMensagem_AplicaPrefixoDestinatariosEAnexo` — caminho sem imagem continua usando `Body`/`IsBodyHtml` e sem `AlternateViews`.

`EmailPorPacoteTests` e `PacoteEndToEndTests` — ajustados para a nova marcação (cartões e cabeçalho "fechamento do pacote"); nenhum e-mail real é enviado (usam o fake `GravadorEmail`).

## Observações

- Nenhum e-mail real foi enviado em nenhum teste.
- Constantes estáticas de UI (rótulos dos cartões, assinatura) são emitidas literais (preservam acentos);
  todo **texto dinâmico** (mensagem de erro, números, período) passa pelo helper `Enc` baseado em
  `WebUtility.HtmlEncode`, que encoda `< > & "` e também acentos como entidades numéricas — ver no exemplo
  gerado `docs/exemplos/email-pacote-exemplo.html`.
- O exemplo HTML foi gerado a partir do código real de `CorpoEmailResumo.Montar`.
