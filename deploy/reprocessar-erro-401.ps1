<#
.SYNOPSIS
    Devolve a pasta de entrada os XMLs que foram para Erro por causa de HTTP 401 (falha de autenticacao
    do GetToken), para o servico integra-los de novo. NUNCA roda sozinho: so por decisao do operador.
.DESCRIPTION
    Roda NO SERVIDOR (RDP). Varre `<Entrada>\Erro\**\*.xml.motivo.txt` e considera apenas os sidecars cuja
    linha de mensagem (de `Mensagem:` ate `Origem:`) contem `401` como palavra inteira E cujo XML irmao
    existe. Cada XML e movido de volta para a pasta de origem:
      - sidecars novos trazem a linha `Origem:` (raiz | Puxada | PuxadaTSP): volta para onde estava;
      - sidecars antigos (sem `Origem:`, ex.: notas de 15/11/2026) voltam para -DestinoPadrao
        (`Puxada`). ATENCAO: notas que eram da PuxadaTSP so voltam corretas com `-DestinoPadrao PuxadaTSP`
        (rode em duas passadas, uma por destino, se houver os dois tipos).
    O sidecar processado e renomeado para `.motivo.txt.reprocessado-<aaaammdd_HHmmss>` (auditoria; a
    limpeza de retencao remove depois). E IDEMPOTENTE: sem sidecars de 401 pendentes, apenas informa e
    sai com 0. Nunca sobrescreve: se o nome ja existir no destino, grava com sufixo `_1`, `_2`...
    GUARDAS: nada fora de `<Entrada>`; recusa raiz de drive; caminhos resolvidos com GetFullPath; so mexe
    em arquivos dentro de `<Entrada>\Erro\`; ignora reparse points; destino so aceita nome simples de pasta.
    NAO imprime nem grava conteudo de notas: o log (`<Entrada>\Estado\logs\reprocessar-erro-401-<data>.log`)
    tem apenas nomes de arquivo, origem e contagens. Suporta `-WhatIf` (nada e movido nem gravado).
    Compativel com PowerShell 4.0/5.1 (Windows Server 2012 R2). Replicado para `<RaizInstalacao>\tools\deploy`
    pelo `instalar.ps1`.
.PARAMETER Entrada
    Pasta de Entrada (`Pastas:Entrada`). Default `C:\Friozem\kitbrf`.
.PARAMETER PastaErro
    Subpasta de erro dentro da Entrada. Default `Erro`.
.PARAMETER DestinoPadrao
    Destino dos sidecars sem `Origem:`: `Puxada` (default), `PuxadaTSP` ou `Raiz`.
.PARAMETER Padrao
    Texto procurado como palavra inteira na mensagem do sidecar. Default `401`.
.PARAMETER Force
    Pula a confirmacao interativa (sem efeito sob `-WhatIf`).
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\reprocessar-erro-401.ps1 -WhatIf
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\reprocessar-erro-401.ps1 -DestinoPadrao PuxadaTSP -Force
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Alias('Caminho')] [string] $Entrada = 'C:\Friozem\kitbrf',
    [string] $PastaErro = 'Erro',
    [ValidateSet('Puxada', 'PuxadaTSP', 'Raiz')] [string] $DestinoPadrao = 'Puxada',
    [string] $Padrao = '401',
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_comum.ps1')

if ($Force) { $ConfirmPreference = 'None' }

# --- Guardas de caminho ---------------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($Entrada)) { throw '-Entrada vazio.' }
if ([string]::IsNullOrWhiteSpace($Padrao)) { throw '-Padrao vazio.' }

$entradaFull = [IO.Path]::GetFullPath($Entrada)
if ([string]::IsNullOrEmpty([IO.Path]::GetDirectoryName($entradaFull))) {
    throw ("-Entrada nao pode ser a raiz de um drive: {0}" -f $entradaFull)
}

$sep = [IO.Path]::DirectorySeparatorChar
$raizErro = [IO.Path]::GetFullPath((Join-Path $entradaFull $PastaErro))
$prefixoErro = $raizErro.TrimEnd($sep) + $sep
$prefixoEntrada = $entradaFull.TrimEnd($sep) + $sep

if (-not (Test-Path -LiteralPath $raizErro -PathType Container)) {
    Write-Passo ("Pasta de erro inexistente: {0}. Nada a reprocessar." -f $raizErro) -Nivel Info
    return
}

function Test-ReparsePoint {
    param([Parameter(Mandatory)] [string] $Item)
    try {
        $attr = (Get-Item -LiteralPath $Item -Force).Attributes
        return (($attr -band [IO.FileAttributes]::ReparsePoint) -eq [IO.FileAttributes]::ReparsePoint)
    } catch {
        return $true
    }
}

if (Test-ReparsePoint -Item $raizErro) {
    Write-Passo ("{0} e um reparse point (junction/symlink); ignorado por seguranca." -f $raizErro) -Nivel Alerta
    return
}

# Texto da mensagem do sidecar: de 'Mensagem:' ate 'Origem:' (exclui Nota/Transporte/Data/Tentativas).
function Get-MensagemSidecar {
    param([Parameter(Mandatory)] [string[]] $Linhas)
    $msg = New-Object System.Collections.Generic.List[string]
    $dentro = $false
    foreach ($l in $Linhas) {
        if ($l -match '^Origem:') { break }
        if ($l -match '^Mensagem:') { $dentro = $true }
        if ($dentro) { $msg.Add($l) }
    }
    return ($msg -join "`n")
}

function Get-OrigemSidecar {
    param([Parameter(Mandatory)] [string[]] $Linhas)
    foreach ($l in $Linhas) {
        if ($l -match '^Origem:\s*(.+?)\s*$') { return $Matches[1] }
    }
    return $null
}

function Get-NomeUnico {
    param([Parameter(Mandatory)] [string] $Pasta, [Parameter(Mandatory)] [string] $Nome)
    $candidato = Join-Path $Pasta $Nome
    if (-not (Test-Path -LiteralPath $candidato)) { return $candidato }
    $baseNome = [IO.Path]::GetFileNameWithoutExtension($Nome)
    $ext = [IO.Path]::GetExtension($Nome)
    for ($i = 1; $i -le 1000; $i++) {
        $candidato = Join-Path $Pasta ("{0}_{1}{2}" -f $baseNome, $i, $ext)
        if (-not (Test-Path -LiteralPath $candidato)) { return $candidato }
    }
    throw ("Nao foi possivel obter nome unico para {0} em {1}" -f $Nome, $Pasta)
}

# --- Coleta ---------------------------------------------------------------------------------
Write-Passo ("Procurando sidecars com '{0}' sob {1} ..." -f $Padrao, $raizErro) -Nivel Info
$regex = '\b' + [regex]::Escape($Padrao) + '\b'
$destinoPadraoPasta = if ($DestinoPadrao -eq 'Raiz') { '' } else { $DestinoPadrao }

$sidecars = Get-ChildItem -LiteralPath $raizErro -Recurse -File -Force -Filter '*.xml.motivo.txt' -ErrorAction SilentlyContinue
$itens = @()
$ignorados = 0
foreach ($s in $sidecars) {
    $sidecarFull = [IO.Path]::GetFullPath($s.FullName)
    if (-not $sidecarFull.StartsWith($prefixoErro, [StringComparison]::OrdinalIgnoreCase)) { continue }
    if (Test-ReparsePoint -Item $sidecarFull) { $ignorados++; continue }

    $linhas = @(Get-Content -LiteralPath $sidecarFull -ErrorAction SilentlyContinue)
    if ($linhas.Count -eq 0) { continue }
    if ((Get-MensagemSidecar -Linhas $linhas) -notmatch $regex) { continue }

    $xml = $sidecarFull.Substring(0, $sidecarFull.Length - '.motivo.txt'.Length)
    if (-not (Test-Path -LiteralPath $xml -PathType Leaf) -or (Test-ReparsePoint -Item $xml)) { $ignorados++; continue }

    $origem = Get-OrigemSidecar -Linhas $linhas
    $semOrigem = $false
    if ([string]::IsNullOrWhiteSpace($origem)) {
        $semOrigem = $true
        $pastaDestino = $destinoPadraoPasta
    } elseif ($origem -ieq 'raiz') {
        $pastaDestino = ''
    } elseif (($origem -eq [IO.Path]::GetFileName($origem)) -and ($origem -ne '..') -and ($origem -ne '.')) {
        $pastaDestino = $origem
    } else {
        $ignorados++
        continue
    }

    $itens += New-Object psobject -Property @{
        Xml = $xml; Sidecar = $sidecarFull; PastaDestino = $pastaDestino; SemOrigem = $semOrigem
    }
}

if ($itens.Count -eq 0) {
    Write-Passo ("Nenhum XML em Erro com '{0}' pendente de reprocesso (ignorados: {1}). Nada a fazer." -f $Padrao, $ignorados) -Nivel Info
    return
}

$antigos = @($itens | Where-Object { $_.SemOrigem }).Count
Write-Passo ("{0} XML(s) em Erro por '{1}' (ignorados: {2})." -f $itens.Count, $Padrao, $ignorados) -Nivel Alerta
if ($antigos -gt 0) {
    Write-Passo ("{0} sidecar(s) antigo(s) sem 'Origem:' voltarao para '{1}'. Notas da PuxadaTSP exigem -DestinoPadrao PuxadaTSP." -f $antigos, $DestinoPadrao) -Nivel Alerta
}

if (-not $WhatIfPreference -and -not $Force) {
    $resposta = Read-Host ("Mover {0} XML(s) de volta para a entrada? (s/n)" -f $itens.Count)
    if ($resposta -notmatch '^[sS]') {
        Write-Passo 'Cancelado pelo operador. Nada foi movido.' -Nivel Info
        return
    }
}

# --- Log (so fora de -WhatIf; nunca com conteudo de nota) ----------------------------------
$arquivoLog = $null
if (-not $WhatIfPreference) {
    $pastaLog = Join-Path $entradaFull 'Estado\logs'
    New-Item -ItemType Directory -Path $pastaLog -Force | Out-Null
    $arquivoLog = Join-Path $pastaLog ("reprocessar-erro-401-{0}.log" -f (Get-Date).ToString('yyyyMMdd'))
}
function Write-LogLinha {
    param([string] $Texto)
    if ($arquivoLog) {
        Add-Content -LiteralPath $arquivoLog -Value ("{0} {1}" -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'), $Texto)
    }
}

# --- Movimentacao ---------------------------------------------------------------------------
$movidos = 0
$falhas = 0
$carimbo = (Get-Date).ToString('yyyyMMdd_HHmmss')
foreach ($it in $itens) {
    $pastaFinal = if ([string]::IsNullOrEmpty($it.PastaDestino)) { $entradaFull } else { Join-Path $entradaFull $it.PastaDestino }
    $pastaFinalFull = [IO.Path]::GetFullPath($pastaFinal)
    if (-not ($pastaFinalFull.TrimEnd($sep) + $sep).StartsWith($prefixoEntrada, [StringComparison]::OrdinalIgnoreCase)) {
        Write-Passo ("Destino fora da Entrada, ignorado: {0}" -f $it.PastaDestino) -Nivel Alerta
        $falhas++
        continue
    }

    $nome = [IO.Path]::GetFileName($it.Xml)
    if ($PSCmdlet.ShouldProcess($it.Xml, ("Mover para {0}" -f $pastaFinalFull))) {
        try {
            if (-not (Test-Path -LiteralPath $pastaFinalFull -PathType Container)) {
                New-Item -ItemType Directory -Path $pastaFinalFull -Force | Out-Null
            }
            $final = Get-NomeUnico -Pasta $pastaFinalFull -Nome $nome
            Move-Item -LiteralPath $it.Xml -Destination $final
            Move-Item -LiteralPath $it.Sidecar -Destination ($it.Sidecar + '.reprocessado-' + $carimbo)
            $movidos++
            $destRotulo = if ([string]::IsNullOrEmpty($it.PastaDestino)) { 'raiz' } else { $it.PastaDestino }
            Write-LogLinha ("MOVIDO {0} -> {1} (destino {2}{3})" -f $nome, [IO.Path]::GetFileName($final), $destRotulo, $(if ($it.SemOrigem) { ', origem desconhecida' } else { '' }))
        } catch {
            $falhas++
            Write-Passo ("Falha ao mover {0}: {1}" -f $nome, $_.Exception.Message) -Nivel Erro
            Write-LogLinha ("FALHA {0}: {1}" -f $nome, $_.Exception.Message)
        }
    }
}

if ($WhatIfPreference) {
    Write-Passo ("(WhatIf) {0} XML(s) seriam devolvidos a entrada; nada foi alterado." -f $itens.Count) -Nivel Info
} else {
    Write-LogLinha ("RESUMO encontrados={0} movidos={1} falhas={2} ignorados={3}" -f $itens.Count, $movidos, $falhas, $ignorados)
    Write-Passo ("Reprocesso: encontrados={0} movidos={1} falhas={2} ignorados={3}. Log: {4}" -f $itens.Count, $movidos, $falhas, $ignorados, $arquivoLog) -Nivel Ok
}
if ($falhas -gt 0) { exit 1 }
