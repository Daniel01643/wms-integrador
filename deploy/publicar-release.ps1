<#
.SYNOPSIS
    Promove uma nova release: expande/copia, copia config, aponta app\current, reinicia o servico e
    mantem apenas as ultimas N releases.

.DESCRIPTION
    Roda NO SERVIDOR (RDP) ou via WinRM. Faz backup do current anterior em backups\ antes de virar.
    Idempotente e com -WhatIf. Preserva data\ e backups\. Nunca toca o share de entrada.

.PARAMETER ReleaseZip / ReleaseDir
    Origem da nova release (um dos dois).

.PARAMETER Versao
    Nome da release (subpasta de app\releases). Default derivado da origem/data.

.PARAMETER ManterUltimas
    Quantas releases manter em app\releases (default 5). As mais antigas excedentes sao removidas.

.PARAMETER RaizInstalacao / NomeServico / NssmPath
    Defaults da casa.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\publicar-release.ps1 -ReleaseZip C:\temp\rel.zip -Versao 2026.02.01-1
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $ReleaseZip,
    [string] $ReleaseDir,
    [string] $Versao,
    [int]    $ManterUltimas = 5,
    [string] $RaizInstalacao = 'C:\Friozem\importacao_notas',
    [string] $NomeServico = 'Friozem-ImportacaoNotas',
    [string] $NssmPath = 'C:\Friozem\Gestao_Enderecos\tools\nssm.exe'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_comum.ps1')

if ($ReleaseZip -and $ReleaseDir) { throw 'Informe apenas -ReleaseZip OU -ReleaseDir.' }
if (-not $ReleaseZip -and -not $ReleaseDir) { throw 'Informe -ReleaseZip ou -ReleaseDir.' }

$nssm = Resolve-NssmPath -NssmPath $NssmPath -RaizScripts $PSScriptRoot
$pastaApp      = Join-Path $RaizInstalacao 'app'
$pastaReleases = Join-Path $pastaApp 'releases'
$pastaCurrent  = Join-Path $pastaApp 'current'
$pastaConfig   = Join-Path $RaizInstalacao 'config'
$pastaBackups  = Join-Path $RaizInstalacao 'backups'
foreach ($p in @($pastaReleases, $pastaBackups)) { [void](Confirm-Directory -Caminho $p) }

if (-not $Versao) {
    if ($ReleaseDir) { $Versao = Split-Path -Leaf (Split-Path -Parent (Resolve-Path -LiteralPath $ReleaseDir)) }
    else { $Versao = [IO.Path]::GetFileNameWithoutExtension($ReleaseZip) }
    if (-not $Versao) { $Versao = Get-VersaoPadrao }
}
Write-Passo ("Promovendo release: {0}" -f $Versao)

# --- 1. Expandir/copiar nova release ---
$pastaReleaseAlvo = Join-Path $pastaReleases $Versao
[void](Confirm-Directory -Caminho $pastaReleaseAlvo)
if ($ReleaseZip) {
    if (-not (Test-Path -LiteralPath $ReleaseZip)) { throw ("ReleaseZip nao encontrado: {0}" -f $ReleaseZip) }
    if ($PSCmdlet.ShouldProcess($pastaReleaseAlvo, 'Expandir nova release')) {
        Expand-Archive -LiteralPath $ReleaseZip -DestinationPath $pastaReleaseAlvo -Force
    }
} else {
    if (-not (Test-Path -LiteralPath $ReleaseDir)) { throw ("ReleaseDir nao encontrado: {0}" -f $ReleaseDir) }
    if ($PSCmdlet.ShouldProcess($pastaReleaseAlvo, 'Copiar nova release')) {
        Copy-Item -Path (Join-Path $ReleaseDir '*') -Destination $pastaReleaseAlvo -Recurse -Force
    }
}

# --- 2. Backup do current anterior ---
if (Test-Path -LiteralPath $pastaCurrent) {
    $carimbo = (Get-Date).ToString('yyyyMMdd_HHmmss')
    $backupZip = Join-Path $pastaBackups ("current_{0}.zip" -f $carimbo)
    if ($PSCmdlet.ShouldProcess($backupZip, 'Backup do current anterior')) {
        Compress-Archive -Path (Join-Path $pastaCurrent '*') -DestinationPath $backupZip -Force
        Write-Passo ("Backup do current anterior: {0}" -f $backupZip) -Nivel Ok
    }
}

# --- 3. Parar servico, copiar config, virar current, reiniciar ---
if (Test-NssmServico -NssmPath $nssm -NomeServico $NomeServico) {
    if ($PSCmdlet.ShouldProcess($NomeServico, 'nssm stop (antes de virar current)')) {
        [void](Invoke-Nssm -NssmPath $nssm -Argumentos @('stop', $NomeServico) -TolerarFalha)
    }
}

Copy-ConfigParaRelease -PastaConfig $pastaConfig -PastaReleaseApp $pastaReleaseAlvo

if ($PSCmdlet.ShouldProcess($pastaCurrent, 'Atualizar current para a nova release')) {
    if (Test-Path -LiteralPath $pastaCurrent) { Remove-Item -LiteralPath $pastaCurrent -Recurse -Force }
    [void](New-Item -ItemType Directory -Path $pastaCurrent -Force)
    Copy-Item -Path (Join-Path $pastaReleaseAlvo '*') -Destination $pastaCurrent -Recurse -Force
    Write-Passo ("current agora aponta para {0}." -f $Versao) -Nivel Ok
}

if (Test-NssmServico -NssmPath $nssm -NomeServico $NomeServico) {
    if ($PSCmdlet.ShouldProcess($NomeServico, 'nssm start (apos virar current)')) {
        [void](Invoke-Nssm -NssmPath $nssm -Argumentos @('start', $NomeServico) -TolerarFalha)
        Write-Passo ("Servico {0} reiniciado na release {1}." -f $NomeServico, $Versao) -Nivel Ok
    }
}

# --- 4. Retencao: manter apenas as ultimas N releases ---
$releases = Get-ChildItem -LiteralPath $pastaReleases -Directory -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending
if ($releases.Count -gt $ManterUltimas) {
    $excedentes = $releases | Select-Object -Skip $ManterUltimas
    foreach ($r in $excedentes) {
        if ($PSCmdlet.ShouldProcess($r.FullName, 'Remover release antiga (retencao)')) {
            Remove-Item -LiteralPath $r.FullName -Recurse -Force
            Write-Passo ("Release antiga removida: {0}" -f $r.Name) -Nivel Info
        }
    }
}

Write-Passo ("Promocao concluida. Releases mantidas (ate {0}): verifique app\releases." -f $ManterUltimas) -Nivel Ok
