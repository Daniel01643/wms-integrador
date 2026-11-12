# Bloco "Tempo de integração" (nível B), cabeçalho/logo e remoção de "Notas por transporte:"

Oito commits locais em `main` (02dd237..25456ff) medem o tempo do pacote com `IClock` (espera da janela, leitura, integração no WMS, ondas) e alimentam um único `BlocoTempo` que renderiza HTML, PDF e e-mail com os mesmos números. A linha "Notas por transporte:" saiu do resumo do HTML/PDF (o e-mail não a tinha). O cabeçalho ganhou faixa #17479E, logo negativa embutida (PNG como recurso do Core, fallback quando `LogoPath` está vazio) e título "Relatório de Integração". A telemetria ganhou campos opcionais, retrocompatíveis. Watch for: nenhum ponto bloqueante; o tempo de gerar o relatório fica só na telemetria (por desenho, correto), e pacotes retomados omitem espera/leitura (confirmed, documentado).

**Verdict**: APPROVED

## High-level view

O tempo é medido na origem (Orquestrador, ProcessadorLote, GerenciadorPacote) e consolidado em um `BlocoTempo`; HTML, PDF e e-mail só formatam o mesmo objeto, então os números não divergem. Dado ausente omite a linha ou o bloco. A tabela por onda só aparece com TSP e substitui os chips de ondas.

O tempo de gerar o relatório não entra no relatório nem no e-mail, evitando número inventado ou circular; fica em `TempoRelatorioMs` na telemetria. O e-mail usa tabelas e CSS inline, com texto dinâmico passando por `Enc`.

A logo vem de recurso embutido (sem pacote novo), com cadeia LogoPath válido, embutida, texto. A assinatura do e-mail e o PDF semanal, de fundo branco, usam selo azul para a logo branca aparecer.

<details>
<summary>Issues (1)</summary>

1. **Evidência de build/testes não reexecutada** (informativo, não bloqueante) — build Debug/Release 0 avisos e 654 testes verdes vêm do relatório do coder e das mensagens de commit, que concordam entre si; nada a corrigir.

</details>

<details>
<summary>Details</summary>

## Verificações pedidas (confirmed por inspeção do diff 02dd237^..HEAD)

Nenhum arquivo `.xml`/`.zip` versionado, nem `CONTROLE-ESFORCO.md`, `docs/controle/*`, `docs/diario/*` ou `email.settings*.json` tocados. Nenhum `PackageReference` adicionado: o único delta em csproj é um `EmbeddedResource` da logo. Nenhum `R$`/`US$` nas linhas adicionadas de `src` e `docs`. Os manuais não ganharam menção a devolução/I40. O `CorpoEmailResumo` declara layout em tabelas com CSS inline, sem `<style>`/`<link>`/`<script>`/flexbox (apenas o comentário cita essas tags), tem `<head>` só com meta charset, e os rótulos do tempo passam por `Enc`. `TempoRelatorio` não é referenciado em `BlocoTempo` nem em `RenderizadorHtml`, então o tempo de geração não aparece no relatório. As cinco imagens em `.agents/tasks` (HTML TSP e PUXADA, e-mail TSP, PDF p1) correspondem à conferência visual descrita no relatório do coder. Os testes novos (`FormatadorDuracaoTests`, `BlocoTempoTests`, `MedicaoTempoPacoteTests`, `TelemetriaTempoTests`, `LogoEmbutidoTests`, `RelatorioCabecalhoTempoTests`, `CorpoEmailTempoTests`, 7 testes só no e-mail) cobrem formato pt-BR, omissão de dados ausentes, retrocompatibilidade, logo/fallback, título e consistência entre canais.

## Limitações conhecidas

Em pacote retomado, espera e leitura ficam sem valor e as linhas são omitidas; integração e ondas vêm do diário e seguem corretas. "Duração total" termina no início do fechamento. O cartão "Transportes" mostra 0 em pacote só TSP (comportamento anterior, fora do escopo).

</details>

<details>
<summary>Mapa de arquivos</summary>

Core: Relatorio (FormatadorDuracao, MedicaoTempo, BlocoTempo, renderizadores, IdentidadeVisual, recurso PNG), Pacote (ProcessadorLote, GerenciadorPacote, DiarioPacote), Orquestrador, Email (corpos), Monitoramento (telemetria). Config: comentários de appsettings. Docs e manuais regenerados. Testes novos e ajustados. Diff completo: `git diff 02dd237^ HEAD`.

</details>
