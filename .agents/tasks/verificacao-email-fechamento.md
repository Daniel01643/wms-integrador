# Verificação — destinatários do e-mail de FECHAMENTO configuráveis por TXT

Iteração: primeira (não havia `review-email-fechamento.json`). Implementado do zero seguindo
`plan-email-fechamento.md`.

## Comandos rodados (raiz `c:\wms-integrador`)

### Build
```
dotnet build KitWmsIntegrador.sln -warnaserror
```
Resultado: **0 Aviso(s), 0 Erro(s)** — compilação com êxito (Core + Worker + Tests).

### Testes
```
dotnet test tests/KitWmsIntegrador.Tests/KitWmsIntegrador.Tests.csproj --no-build
```
Resultado: **Com falha: 0, Aprovado: 323, Ignorado: 9, Total: 332.**

- Baseline anterior: 313 aprovados. Agora 323 = **313 + 10 novos**.
- Os 9 ignorados são os cenários de stress (`CenariosStress.*`), pulados por padrão — igual ao
  baseline (não contam no total aprovado).

## Testes novos (10)

`FonteDestinatariosFechamentoTests` (6 — unidade da fonte):
- `Txt_ComEmails_TemPrecedenciaSobreOpcoesEmail`
- `TxtAusente_UsaOpcoesEmailComoFallback`
- `TxtVazio_UsaOpcoesEmailComoFallback`
- `TxtAusente_EOpcoesEmailVazio_RetornaVazio`
- `Parsing_IgnoraComentariosEVazias_ComTrim`
- `Recarrega_AposEditarOArquivo`

`FechadorPacoteDestinatariosTests` (4 — integração via `AmbientePacote` + `GravadorEmail`, sem SMTP/WMS):
- `Txt_ComEmails_EmailVaiParaEles_PrecedeEmailDestinatarios` → requisito (i)
- `TxtAusente_FallbackParaEmailDestinatarios` → requisito (ii)
- `AmbosVazios_NaoEnviaMasConcluiOFechamento` → requisito (iii) (pacote volta a Ocioso, HTML+PDF gerados)
- `ReleAArquivoACadaEnvio_EdicaoEntreDoisPacotesRefleteNoSegundo` → requisito (v)
- Requisito (iv) parsing coberto em `Parsing_IgnoraComentariosEVazias_ComTrim` acima.

## Mudanças principais

- `LeitorListaEmails` (novo): parsing puro compartilhado (um e-mail/linha, `Trim`, ignora branco e
  `#`, relê a cada chamada; ausente/vazio/ilegível → lista vazia, sem decidir fallback).
- `FonteDestinatariosResumo`: passou a usar `LeitorListaEmails`; contrato público e fallback
  (`ti@friozem.com.br` + aviso) preservados. Os 4 testes do resumo continuam verdes.
- `FonteDestinatariosFechamento` (nova): espelha o resumo no parsing, fallback próprio
  txt → `Email:Destinatarios` → vazio (com aviso).
- `OpcoesEmail.CaminhoDestinatarios` (nova chave, default vazio = `<telemetria>\destinatarios-fechamento.txt`).
- `FechadorPacote`: resolve destinatários pela nova fonte e passa via `DestinatariosOverride`;
  lista vazia → loga e não envia (retorna `true`, relatório preservado). Nenhuma mudança na decisão
  do pacote/Orquestrador; I60 permanece desligada.
- `Program.cs`: `AddSingleton<FonteDestinatariosFechamento>()`.
- Infra de teste (`AmbientePacote`, `AmbienteStress`) ajustada para construir a nova fonte.
- Exemplo versionado `deploy/config-exemplo/destinatarios-fechamento.exemplo.txt`; `.gitignore`
  cobre o real `destinatarios-fechamento.txt`.
- `appsettings.json` e `deploy/config-exemplo/appsettings.Production.json`: nova chave + comentários.
- Docs `OPERACAO.md` (seção 4.1) e `INSTALACAO-SERVIDOR.md` atualizados (dois txts, precedência, fallback).
