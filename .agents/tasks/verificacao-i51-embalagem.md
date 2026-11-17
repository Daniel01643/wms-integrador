# Verificação: I51 com quantidade em embalagens (17/11/2026)

## Mudança
- `I51:CampoQuantidadeSeparada` = `Embalagem` (padrão) | `Produto` (comportamento antigo), validada em `OpcoesI51.Validar()`, presente em `appsettings.json` e `deploy\config-exemplo\appsettings.Production.json` (comentário com o caso 5006566).
- `ParserI51` lê `qtdeEmbalagemSep` e a soma de `detalhes[].qtdeCaixaSep` (ambos opcionais, nunca invalidam o documento).
- `SelecionadorQuantidadeI51`: `qtdeEmbalagemSep` → soma `qtdeCaixaSep` → `qtdeProdutoSep` (fallback com Observacao na nota e aviso no log).
- `CasadorDocumentoNota`: resolve a quantidade antes de `CalculadorAjuste`; a comparação original × separada e a I60 usam embalagens.
- Órfãos: guardam a quantidade já escolhida em `QtdeSeparada`; arquivos antigos (unidades) são lidos como estão.
- Relatório diário da I60: seção "Ajustes de quantidade" mostra o campo usado (HTML/e-mail e PDF).

## Testes novos (18)
Produto com conversão (emb 18, prod 180, nota 18 → separada 18, sem ajuste, I60 com 18); separação parcial (nota 11, emb 9 → ajuste 11→9); `qtdeEmbalagemSep` ausente/vazia/inválida → soma dos detalhes; sem emb e sem detalhes → produto + Observacao; opção `Produto` → 180/ajuste; órfão; parser; validação da opção; relatório mostra o campo.

## Execução
- `dotnet build c:\wms-integrador\KitWmsIntegrador.sln -c Release`: 0 avisos, 0 erros.
- `dotnet test c:\wms-integrador\KitWmsIntegrador.sln -c Release`: 1009 aprovados, 9 ignorados, 0 falhas (baseline 991 / 9).
- Documentação: `docs\ESCOPO-I51-I60.md`, `docs\OPERACAO.md` (seção 10), `docs\manuais\MANUAL-TECNICO.md`; HTML por `gerar-manuais.ps1` e PDF por Edge headless (`--no-pdf-header-footer --print-to-pdf`).
