<#
.SYNOPSIS
  Conversor Markdown -> HTML autocontido (identidade visual Friozem) para os manuais
  do KitWms Integrador. SEM pandoc, SEM internet, SEM dotnet. PowerShell 5.1.

.DESCRIPTION
  Gera docs/manuais/MANUAL-USUARIO.html e docs/manuais/MANUAL-TECNICO.html a partir
  dos respectivos arquivos .md, aplicando o template Friozem (paleta/cabecalho/rodape/
  tabelas do deck.html, com a logo embutida em base64). O HTML resultante nao referencia
  nenhum recurso externo (sem <link rel=stylesheet>, sem <script src>, sem url() externa).

  A logo base64 e extraida da linha 125 (indice 124) de docs/apresentacao/deck.html.
  O MANUAL-AGENTE-IA.md NAO e convertido (permanece so em Markdown).

  Os PDFs sao gerados fora deste script, via Microsoft Edge headless (ver README.md e FEAT-002).

.NOTES
  Regenerar:  powershell -ExecutionPolicy Bypass -File docs\manuais\gerar-manuais.ps1
#>

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Caminhos (resolvidos a partir da pasta do proprio script)
# ---------------------------------------------------------------------------
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$docsDir   = Split-Path -Parent $scriptDir
$repoDir   = Split-Path -Parent $docsDir
$deckPath  = Join-Path $docsDir 'apresentacao\deck.html'
$assetsDir = Join-Path $scriptDir 'assets'

# ---------------------------------------------------------------------------
# Versao exibida na capa: replica o criterio de deploy\_comum.ps1::Get-VersaoPadrao
# (yyyy.MM.dd-<hash git curto>). Fallback 'v1.0 - 2026-11' quando git nao responder.
# ---------------------------------------------------------------------------
function Get-VersaoManual {
    param([string]$RaizRepo)

    $hash = $null
    if ($RaizRepo -and (Test-Path -LiteralPath (Join-Path $RaizRepo '.git'))) {
        try {
            Push-Location $RaizRepo
            $hash = (& git rev-parse --short HEAD 2>$null)
        } catch {
            $hash = $null
        } finally {
            Pop-Location
        }
    }
    if ($hash) {
        return ("{0}-{1}" -f (Get-Date).ToString('yyyy.MM.dd'), $hash.Trim())
    }
    return 'v1.0 - 2026-11'
}

# ---------------------------------------------------------------------------
# Le um SVG de assets\<nome>.svg como UTF-8 e devolve o markup intacto.
# ---------------------------------------------------------------------------
function Read-AssetSvg {
    param([string]$Nome)
    $caminho = Join-Path $assetsDir ($Nome + '.svg')
    if (-not (Test-Path -LiteralPath $caminho)) {
        throw "SVG de diagrama nao encontrado: $caminho"
    }
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    return [System.IO.File]::ReadAllText($caminho, $utf8)
}

# ---------------------------------------------------------------------------
# Monta o bloco HTML da capa (logo base64 + titulo + subtitulo + tipo + data + versao).
# Identidade Friozem; a quebra de pagina depois da capa vem do CSS (.capa).
# ---------------------------------------------------------------------------
function Build-Capa {
    param(
        [string]$TipoManual,
        [string]$LogoBase64,
        [string]$Versao,
        [string]$DataGeracao
    )
    $logoSrc = 'data:image/png;base64,' + $LogoBase64
    $tipoEsc = ConvertTo-HtmlText $TipoManual
    $verEsc  = ConvertTo-HtmlText $Versao
    $dataEsc = ConvertTo-HtmlText $DataGeracao

    return @"
<section class="capa">
  <div class="capa-faixa"></div>
  <img class="capa-logo" src="$logoSrc" alt="Friozem" />
  <h1 class="capa-titulo">Integrador Notas Fiscais KIT &rarr; WMS</h1>
  <p class="capa-sub">Integra&ccedil;&atilde;o autom&aacute;tica de notas fiscais com o WMS</p>
  <p class="capa-tipo">$tipoEsc</p>
  <div class="capa-meta">
    <span>Gerado em: $dataEsc</span>
    <span>Vers&atilde;o: $verEsc</span>
  </div>
</section>
"@
}

# ---------------------------------------------------------------------------
# 1) Extrair a logo base64 do deck (linha 125 = indice 124; com fallback)
# ---------------------------------------------------------------------------
function Get-LogoBase64 {
    param([string]$DeckHtmlPath)

    if (-not (Test-Path -LiteralPath $DeckHtmlPath)) {
        throw "deck.html nao encontrado em: $DeckHtmlPath"
    }
    $deck = Get-Content -LiteralPath $DeckHtmlPath
    $regex = 'data:image/png;base64,([A-Za-z0-9+/=]+)'

    # Caminho feliz: linha 125 (indice 124)
    if ($deck.Count -ge 125) {
        $m = [regex]::Match($deck[124], $regex)
        if ($m.Success) { return $m.Groups[1].Value }
    }
    # Fallback: primeira linha que casar a regex
    foreach ($line in $deck) {
        $m = [regex]::Match($line, $regex)
        if ($m.Success) { return $m.Groups[1].Value }
    }
    throw "Nao foi possivel extrair a logo base64 do deck: $DeckHtmlPath"
}

# ---------------------------------------------------------------------------
# Escape de HTML para texto comum
# ---------------------------------------------------------------------------
function ConvertTo-HtmlText {
    param([string]$Text)
    $Text = $Text -replace '&', '&amp;'
    $Text = $Text -replace '<', '&lt;'
    $Text = $Text -replace '>', '&gt;'
    return $Text
}

# ---------------------------------------------------------------------------
# Inline: codigo `...`, negrito **...**, links [txt](url). Escapa &,<,>.
# A ordem importa: 1) protege o codigo inline, 2) escapa, 3) negrito/links,
# 4) restaura o codigo inline (ja escapado).
# ---------------------------------------------------------------------------
function Convert-Inline {
    param([string]$Text)

    $codeStore = New-Object System.Collections.ArrayList
    # 1) Extrair spans de codigo inline e trocar por placeholders
    $pattern = '`([^`]+)`'
    $Text = [regex]::Replace($Text, $pattern, {
        param($match)
        $escaped = ConvertTo-HtmlText $match.Groups[1].Value
        $idx = $codeStore.Add("<code>$escaped</code>")
        return [char]0 + "CODE$idx" + [char]0
    })

    # 2) Escapar o restante do texto
    $Text = ConvertTo-HtmlText $Text

    # 3) Links [texto](url)  -> depois do escape, os ( ) e [ ] continuam literais
    $Text = [regex]::Replace($Text, '\[([^\]]+)\]\(([^)]+)\)', {
        param($match)
        $label = $match.Groups[1].Value
        $url   = $match.Groups[2].Value
        return "<a href=`"$url`">$label</a>"
    })

    # 4) Negrito **...**
    $Text = [regex]::Replace($Text, '\*\*([^*]+)\*\*', '<strong>$1</strong>')

    # 5) Restaurar o codigo inline
    $Text = [regex]::Replace($Text, [char]0 + 'CODE(\d+)' + [char]0, {
        param($match)
        $i = [int]$match.Groups[1].Value
        return $codeStore[$i]
    })

    return $Text
}

# ---------------------------------------------------------------------------
# Celula de tabela (ignora as barras de borda externas)
# ---------------------------------------------------------------------------
function Split-TableRow {
    param([string]$Line)
    $s = $Line.Trim()
    if ($s.StartsWith('|')) { $s = $s.Substring(1) }
    if ($s.EndsWith('|'))   { $s = $s.Substring(0, $s.Length - 1) }
    # split em '|' sem escape (os manuais nao usam '\|')
    return $s -split '\|'
}

function Test-TableSeparator {
    param([string]$Line)
    $s = $Line.Trim()
    if (-not ($s -match '\|')) { return $false }
    return ($s -match '^\|?[\s:-]*-[\s:|-]*\|?$')
}

# ---------------------------------------------------------------------------
# Conversor do subconjunto de Markdown usado nos manuais
# ---------------------------------------------------------------------------
function Convert-MarkdownToHtmlBody {
    param([string[]]$Lines)

    $sb = New-Object System.Text.StringBuilder
    $i = 0
    $n = $Lines.Count

    $listStack = New-Object System.Collections.Stack  # guarda 'ul' ou 'ol'

    function Close-Lists {
        param([System.Text.StringBuilder]$Sb, [System.Collections.Stack]$Stack)
        while ($Stack.Count -gt 0) {
            $t = $Stack.Pop()
            [void]$Sb.AppendLine("</$t>")
        }
    }

    while ($i -lt $n) {
        $line = $Lines[$i]
        $trim = $line.TrimEnd()

        # --- Bloco de codigo cercado ``` ---
        if ($trim -match '^\s*```') {
            Close-Lists $sb $listStack
            # nao interpreta markdown dentro; preserva conteudo; escapa HTML
            $i++
            $codeLines = New-Object System.Collections.ArrayList
            while ($i -lt $n -and ($Lines[$i].TrimEnd() -notmatch '^\s*```')) {
                [void]$codeLines.Add((ConvertTo-HtmlText $Lines[$i]))
                $i++
            }
            $i++  # pula a cerca de fechamento
            [void]$sb.AppendLine('<pre><code>' + ($codeLines -join "`n") + '</code></pre>')
            continue
        }

        # --- Linha em branco: fecha listas ---
        if ($trim -match '^\s*$') {
            Close-Lists $sb $listStack
            $i++
            continue
        }

        # --- Regua horizontal / separador de secao '---' ---
        if ($trim -match '^---+\s*$') {
            Close-Lists $sb $listStack
            [void]$sb.AppendLine('<hr />')
            $i++
            continue
        }

        # --- Titulos # ## ### ---
        if ($trim -match '^(#{1,6})\s+(.*)$') {
            Close-Lists $sb $listStack
            $level = $Matches[1].Length
            $content = Convert-Inline $Matches[2].Trim()
            [void]$sb.AppendLine("<h$level>$content</h$level>")
            $i++
            continue
        }

        # --- Tabela GFM: linha com '|' seguida de separador '---' ---
        if (($trim -match '\|') -and (($i + 1) -lt $n) -and (Test-TableSeparator $Lines[$i + 1])) {
            Close-Lists $sb $listStack
            $headerCells = Split-TableRow $trim
            $i += 2  # pula cabecalho + separador
            [void]$sb.AppendLine('<table>')
            [void]$sb.AppendLine('<thead><tr>')
            foreach ($c in $headerCells) {
                [void]$sb.AppendLine('<th>' + (Convert-Inline $c.Trim()) + '</th>')
            }
            [void]$sb.AppendLine('</tr></thead>')
            [void]$sb.AppendLine('<tbody>')
            while ($i -lt $n -and ($Lines[$i].Trim() -match '\|') -and ($Lines[$i].Trim() -ne '')) {
                $cells = Split-TableRow $Lines[$i]
                [void]$sb.AppendLine('<tr>')
                foreach ($c in $cells) {
                    [void]$sb.AppendLine('<td>' + (Convert-Inline $c.Trim()) + '</td>')
                }
                [void]$sb.AppendLine('</tr>')
                $i++
            }
            [void]$sb.AppendLine('</tbody></table>')
            continue
        }

        # --- Citacao '>' (consome bloco contiguo) ---
        if ($trim -match '^\s*>\s?') {
            Close-Lists $sb $listStack
            $quoteLines = New-Object System.Collections.ArrayList
            while ($i -lt $n -and ($Lines[$i].TrimEnd() -match '^\s*>\s?')) {
                $q = $Lines[$i] -replace '^\s*>\s?', ''
                if ($q.Trim() -eq '') {
                    # linha de quote vazia vira quebra de paragrafo dentro da citacao
                    [void]$quoteLines.Add('')
                } else {
                    [void]$quoteLines.Add((Convert-Inline $q.Trim()))
                }
                $i++
            }
            $joined = ($quoteLines -join ' ').Trim()
            [void]$sb.AppendLine("<blockquote><p>$joined</p></blockquote>")
            continue
        }

        # --- Lista nao ordenada '- ' ---
        if ($trim -match '^(\s*)-\s+(.*)$') {
            if ($listStack.Count -eq 0 -or $listStack.Peek() -ne 'ul') {
                Close-Lists $sb $listStack
                [void]$sb.AppendLine('<ul>')
                $listStack.Push('ul')
            }
            $item = Convert-Inline $Matches[2].Trim()
            # continuacao: linhas indentadas que nao iniciam novo item/bloco
            $i++
            while ($i -lt $n) {
                $cont = $Lines[$i]
                if ($cont.Trim() -eq '') { break }
                if ($cont -match '^\s*[-*]\s+') { break }
                if ($cont -match '^\s*\d+\.\s+') { break }
                if ($cont.TrimEnd() -match '^\s*```') { break }
                if ($cont -match '^#{1,6}\s+') { break }
                $item += ' ' + (Convert-Inline $cont.Trim())
                $i++
            }
            [void]$sb.AppendLine("<li>$item</li>")
            continue
        }

        # --- Lista ordenada '1. ' ---
        if ($trim -match '^(\s*)\d+\.\s+(.*)$') {
            if ($listStack.Count -eq 0 -or $listStack.Peek() -ne 'ol') {
                Close-Lists $sb $listStack
                [void]$sb.AppendLine('<ol>')
                $listStack.Push('ol')
            }
            $item = Convert-Inline $Matches[2].Trim()
            $i++
            while ($i -lt $n) {
                $cont = $Lines[$i]
                if ($cont.Trim() -eq '') { break }
                if ($cont -match '^\s*[-*]\s+') { break }
                if ($cont -match '^\s*\d+\.\s+') { break }
                if ($cont.TrimEnd() -match '^\s*```') { break }
                if ($cont -match '^#{1,6}\s+') { break }
                $item += ' ' + (Convert-Inline $cont.Trim())
                $i++
            }
            [void]$sb.AppendLine("<li>$item</li>")
            continue
        }

        # --- Paragrafo (consome linhas contiguas de texto) ---
        Close-Lists $sb $listStack
        $paraLines = New-Object System.Collections.ArrayList
        while ($i -lt $n) {
            $p = $Lines[$i]
            if ($p.Trim() -eq '') { break }
            if ($p -match '^#{1,6}\s+') { break }
            if ($p.TrimEnd() -match '^\s*```') { break }
            if ($p -match '^\s*>\s?') { break }
            if ($p -match '^\s*[-*]\s+') { break }
            if ($p -match '^\s*\d+\.\s+') { break }
            if ($p.TrimEnd() -match '^---+\s*$') { break }
            if (($p.Trim() -match '\|') -and (($i + 1) -lt $n) -and (Test-TableSeparator $Lines[$i + 1])) { break }
            [void]$paraLines.Add((Convert-Inline $p.Trim()))
            $i++
        }
        if ($paraLines.Count -gt 0) {
            [void]$sb.AppendLine('<p>' + ($paraLines -join ' ') + '</p>')
        }
    }

    Close-Lists $sb $listStack
    return $sb.ToString()
}

# ---------------------------------------------------------------------------
# Template Friozem (CSS embutido no <head>, nada externo) + @page A4 c/ pagina
# ---------------------------------------------------------------------------
function Build-HtmlDocument {
    param(
        [string]$Title,
        [string]$BodyHtml,
        [string]$LogoBase64
    )

    $css = @'
    * { margin: 0; padding: 0; box-sizing: border-box; }
    html, body { background: #ffffff; font-family: Arial, Helvetica, sans-serif; color: #333333; font-size: 15px; line-height: 1.5; }
    .page { max-width: 900px; margin: 0 auto; padding: 0 0 48px; }

    /* Cabecalho: faixa azul, titulo a esquerda + logo a direita */
    .hd { background: #17479E; display: flex; align-items: center; justify-content: space-between; padding: 22px 40px; }
    .hd h1 { color: #ffffff; font-size: 26px; font-weight: 700; line-height: 1.2; margin: 0; border: 0; }
    .hd img { width: 176px; height: auto; display: block; }
    /* Faixa de acento ciano sob o cabecalho */
    .bar { width: 96px; height: 5px; background: #54B6E7; margin: 0 0 0 40px; }

    .bd { padding: 20px 40px 0; }

    h1 { color: #17479E; font-size: 24px; margin: 26px 0 12px; line-height: 1.2; }
    h2 { color: #17479E; font-size: 20px; margin: 24px 0 10px; line-height: 1.25; border-bottom: 2px solid #e1e7f0; padding-bottom: 4px; }
    h3 { color: #17479E; font-size: 16.5px; margin: 18px 0 8px; }
    h4, h5, h6 { color: #17479E; font-size: 15px; margin: 14px 0 6px; }

    p { margin: 8px 0; }
    ul, ol { margin: 8px 0 8px 26px; }
    li { margin: 4px 0; }
    a { color: #17479E; text-decoration: underline; }

    hr { border: 0; border-top: 1px solid #e1e7f0; margin: 18px 0; }

    /* Faixa/callout de destaque ciano */
    blockquote { background: #eaf5fc; border-left: 6px solid #54B6E7; border-radius: 8px; padding: 12px 18px; margin: 14px 0; }
    blockquote p { margin: 0; }
    blockquote strong { color: #17479E; }

    /* Blocos de codigo/comando: fundo suave + monoespacada */
    code { font-family: Consolas, "Courier New", monospace; font-size: 13.5px; background: #eef2f8; color: #17479E; padding: 1px 5px; border-radius: 4px; }
    pre { background: #f1f5fb; border: 1px solid #e1e7f0; border-left: 4px solid #54B6E7; border-radius: 8px; padding: 14px 16px; margin: 12px 0; overflow-x: auto; }
    pre code { background: transparent; color: #243b53; padding: 0; border-radius: 0; font-size: 13px; line-height: 1.5; display: block; white-space: pre; }

    /* Tabelas: cabecalho azul + zebra */
    table { width: 100%; border-collapse: collapse; margin: 14px 0; font-size: 14px; }
    th, td { padding: 9px 12px; text-align: left; border-bottom: 1px solid #e1e7f0; vertical-align: top; }
    thead th { background: #17479E; color: #ffffff; border-bottom: none; font-weight: 700; }
    tbody tr:nth-child(even) { background: #f6f8fc; }

    /* Rodape */
    .ft { margin: 36px 40px 0; padding-top: 12px; border-top: 3px solid #009A83; color: #555555; font-size: 12.5px; display: flex; justify-content: space-between; }

    /* Capa ilustrada (identidade Friozem, sobria) */
    .capa { position: relative; text-align: center; padding: 70px 40px 60px; margin: 0 0 24px; }
    .capa-faixa { height: 10px; background: #17479E; border-bottom: 4px solid #54B6E7; margin: -0px -40px 48px; }
    .capa-logo { width: 240px; height: auto; display: block; margin: 0 auto 44px; }
    .capa-titulo { color: #17479E; font-size: 34px; font-weight: 700; line-height: 1.2; margin: 0 0 14px; border: 0; }
    .capa-sub { color: #54B6E7; font-size: 18px; font-weight: 700; margin: 0 0 40px; }
    .capa-tipo { display: inline-block; color: #ffffff; background: #17479E; font-size: 17px; font-weight: 700; padding: 8px 26px; border-radius: 22px; margin: 0 0 56px; }
    .capa-meta { display: flex; justify-content: center; gap: 48px; color: #555555; font-size: 13.5px; border-top: 3px solid #009A83; padding-top: 16px; max-width: 520px; margin: 0 auto; }

    /* Diagramas SVG inline (nao cortar, largura total da area de conteudo) */
    figure.diagrama { max-width: 100%; margin: 18px 0; }
    figure.diagrama svg { width: 100%; height: auto; display: block; }

    @media print {
      .capa { page-break-after: always; min-height: 90vh; }
      figure.diagrama { page-break-inside: avoid; }
      @page {
        size: A4;
        margin: 18mm 16mm 20mm 16mm;
        @bottom-right { content: counter(page); color: #555555; font-family: Arial, Helvetica, sans-serif; font-size: 10px; }
      }
      html, body { font-size: 12px; }
      .page { max-width: none; }
      .hd { padding: 14px 0; }
      .bd { padding: 10px 0 0; }
      .bar { margin-left: 0; }
      .ft { margin: 24px 0 0; }
      pre, table, blockquote { page-break-inside: avoid; }
      h1, h2, h3 { page-break-after: avoid; }
      a { color: #17479E; }
    }
'@

    $titleEsc = ConvertTo-HtmlText $Title
    $logoSrc = 'data:image/png;base64,' + $LogoBase64

    $doc = @"
<!DOCTYPE html>
<html lang="pt-BR">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>$titleEsc</title>
<style>
$css
</style>
</head>
<body>
<div class="page">
  <header class="hd">
    <h1>$titleEsc</h1>
    <img src="$logoSrc" alt="Friozem" />
  </header>
  <div class="bar"></div>
  <main class="bd">
$BodyHtml
  </main>
  <footer class="ft">
    <span>Friozem | Integrador WMS</span>
  </footer>
</div>
</body>
</html>
"@
    return $doc
}

# ---------------------------------------------------------------------------
# Conversao de um manual (.md -> .html)
# ---------------------------------------------------------------------------
function Convert-Manual {
    param(
        [string]$MarkdownPath,
        [string]$HtmlPath,
        [string]$Title,
        [string]$LogoBase64,
        [string]$Versao,
        [string]$DataGeracao
    )

    if (-not (Test-Path -LiteralPath $MarkdownPath)) {
        throw "Markdown nao encontrado: $MarkdownPath"
    }
    # Ler como UTF-8 (os .md sao UTF-8; o default do PS 5.1 e a codepage ANSI).
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    $mdText = [System.IO.File]::ReadAllText($MarkdownPath, $utf8)
    $mdLines = $mdText -split "`r?`n"

    # Remove o primeiro titulo H1 do corpo (ja vai no cabecalho), para nao duplicar.
    $firstH1 = $true
    $body = New-Object System.Collections.ArrayList
    foreach ($l in $mdLines) {
        if ($firstH1 -and ($l -match '^#\s+')) { $firstH1 = $false; continue }
        [void]$body.Add($l)
    }

    # --- Resolver os marcadores de CAPA e DIAGRAMA ANTES de converter o Markdown ---
    # Tecnica: substituir cada marcador por um placeholder de linha unica que o
    # conversor repassa intacto (como paragrafo), guardando o HTML cru num store;
    # depois da conversao, reinjetar o HTML cru no lugar do placeholder.
    $htmlStore = New-Object System.Collections.ArrayList
    $NUL = [char]0

    $mapaDiagramas = @{
        'fluxo-usuario'       = 'fluxo-usuario'
        'arquitetura-tecnico' = 'arquitetura-tecnico'
        'ciclo-pacote'        = 'ciclo-pacote'
    }

    $bodyLines = New-Object System.Collections.ArrayList
    foreach ($l in $body) {
        $t = $l.Trim()
        $m = [regex]::Match($t, '^<!--\s*(CAPA|DIAGRAMA)\s*:\s*([a-z0-9\-]+)\s*-->$')
        if ($m.Success) {
            $tipo = $m.Groups[1].Value
            $nome = $m.Groups[2].Value
            if ($tipo -eq 'CAPA') {
                $tipoManual = if ($nome -eq 'tecnico') { 'Manual Tecnico' } else { 'Manual do Usuario' }
                $html = Build-Capa -TipoManual $tipoManual -LogoBase64 $LogoBase64 -Versao $Versao -DataGeracao $DataGeracao
            } else {
                if (-not $mapaDiagramas.ContainsKey($nome)) {
                    throw "Marcador de diagrama desconhecido: $nome (em $MarkdownPath)"
                }
                $svg = Read-AssetSvg -Nome $mapaDiagramas[$nome]
                $html = "<figure class=`"diagrama`">$svg</figure>"
            }
            $idx = $htmlStore.Add($html)
            [void]$bodyLines.Add($NUL + "HTMLBLOCK$idx" + $NUL)
        } else {
            [void]$bodyLines.Add($l)
        }
    }

    $bodyHtml = Convert-MarkdownToHtmlBody -Lines $bodyLines.ToArray()

    # Reinjetar o HTML cru (capa/diagramas) no lugar dos placeholders. O conversor
    # envolve a linha do placeholder num <p>...</p>; removemos esse wrapper para que
    # o <section>/<figure> fique como bloco de nivel superior (HTML valido).
    $nulEsc = [regex]::Escape($NUL)
    $bodyHtml = [regex]::Replace($bodyHtml, '<p>\s*' + $nulEsc + 'HTMLBLOCK(\d+)' + $nulEsc + '\s*</p>', {
        param($match)
        $i = [int]$match.Groups[1].Value
        return $htmlStore[$i]
    })
    # Rede de seguranca: se por algum motivo nao estiver em <p>, reinjeta mesmo assim.
    $bodyHtml = [regex]::Replace($bodyHtml, $nulEsc + 'HTMLBLOCK(\d+)' + $nulEsc, {
        param($match)
        $i = [int]$match.Groups[1].Value
        return $htmlStore[$i]
    })

    $doc = Build-HtmlDocument -Title $Title -BodyHtml $bodyHtml -LogoBase64 $LogoBase64

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($HtmlPath, $doc, $utf8NoBom)
    Write-Host "Gerado: $HtmlPath"
}

# ---------------------------------------------------------------------------
# Execucao
# ---------------------------------------------------------------------------
$logo = Get-LogoBase64 -DeckHtmlPath $deckPath
Write-Host ("Logo base64 extraida ({0} chars)." -f $logo.Length)

$versao      = Get-VersaoManual -RaizRepo $repoDir
$dataGeracao = (Get-Date).ToString('yyyy-MM-dd')
Write-Host ("Versao da capa: {0} | Data de geracao: {1}" -f $versao, $dataGeracao)

Convert-Manual `
    -MarkdownPath (Join-Path $scriptDir 'MANUAL-USUARIO.md') `
    -HtmlPath     (Join-Path $scriptDir 'MANUAL-USUARIO.html') `
    -Title        'Manual do Usuario' `
    -LogoBase64   $logo `
    -Versao       $versao `
    -DataGeracao  $dataGeracao

Convert-Manual `
    -MarkdownPath (Join-Path $scriptDir 'MANUAL-TECNICO.md') `
    -HtmlPath     (Join-Path $scriptDir 'MANUAL-TECNICO.html') `
    -Title        'Manual Tecnico (Nivel 1)' `
    -LogoBase64   $logo `
    -Versao       $versao `
    -DataGeracao  $dataGeracao

Write-Host "Concluido. Para os PDFs, use o Microsoft Edge headless (ver README.md)."
