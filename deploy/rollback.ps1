<#
.SYNOPSIS
    Rollback: reaponta app\current para uma release anterior e reinicia o servico.

.DESCRIPTION
    Roda NO SERVIDOR (RDP) ou via WinRM. Se -Versao nao for informada, usa a release anterior
    (segunda mais recente em app\releases por data). Idempotente e com -WhatIf. Falha claro se
    nao houver release alvo.

.PARAMETER Versao
    Release alvo (subpasta de app\releases). Default = release anterior.

.PARAMETER RaizInstalacao / NomeServico / NssmPath
    Defaults da casa.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\rollback.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\rollback.ps1 -Versao 2026.01.15-1
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $Versao,
    [string] $RaizInstalacao = 'C:\Friozem\importacao_notas',
    [string] $NomeServico = 'Friozem-ImportacaoNotas',
    [string] $NssmPath = 'C:\Friozem\Gestao_Enderecos\tools\nssm.exe'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_comum.ps1')

$nssm = Resolve-NssmPath -NssmPath $NssmPath -RaizScripts $PSScriptRoot
$pastaApp      = Join-Path $RaizInstalacao 'app'
$pastaReleases = Join-Path $pastaApp 'releases'
$pastaCurrent  = Join-Path $pastaApp 'current'
$pastaConfig   = Join-Path $RaizInstalacao 'config'

if (-not (Test-Path -LiteralPath $pastaReleases)) {
    throw ("Nenhuma pasta de releases em {0}." -f $pastaReleases)
}

$releases = Get-ChildItem -LiteralPath $pastaReleases -Directory -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending

if (-not $releases -or $releases.Count -eq 0) {
    throw 'Nenhuma release encontrada para rollback.'
}

if ($Versao) {
    $alvo = $releases | Where-Object { $_.Name -eq $Versao } | Select-Object -First 1
    if (-not $alvo) { throw ("Release alvo '{0}' nao existe em app\releases." -f $Versao) }
} else {
    if ($releases.Count -lt 2) {
        throw 'Nao ha release anterior para rollback (apenas uma release instalada). Informe -Versao explicitamente.'
    }
    $alvo = $releases[1]   # segunda mais recente = anterior
}

Write-Passo ("Rollback para a release: {0}" -f $alvo.Name) -Nivel Alerta

if (Test-NssmServico -NssmPath $nssm -NomeServico $NomeServico) {
    if ($PSCmdlet.ShouldProcess($NomeServico, 'nssm stop (antes do rollback)')) {
        [void](Invoke-Nssm -NssmPath $nssm -Argumentos @('stop', $NomeServico) -TolerarFalha)
    }
}

Copy-ConfigParaRelease -PastaConfig $pastaConfig -PastaReleaseApp $alvo.FullName

if ($PSCmdlet.ShouldProcess($pastaCurrent, ("Reapontar current para {0}" -f $alvo.Name))) {
    if (Test-Path -LiteralPath $pastaCurrent) { Remove-Item -LiteralPath $pastaCurrent -Recurse -Force }
    [void](New-Item -ItemType Directory -Path $pastaCurrent -Force)
    Copy-Item -Path (Join-Path $alvo.FullName '*') -Destination $pastaCurrent -Recurse -Force
    Write-Passo ("current agora aponta para {0}." -f $alvo.Name) -Nivel Ok
}

if (Test-NssmServico -NssmPath $nssm -NomeServico $NomeServico) {
    if ($PSCmdlet.ShouldProcess($NomeServico, 'nssm start (apos rollback)')) {
        [void](Invoke-Nssm -NssmPath $nssm -Argumentos @('start', $NomeServico) -TolerarFalha)
        Write-Passo ("Servico {0} reiniciado na release {1}." -f $NomeServico, $alvo.Name) -Nivel Ok
    }
}

Write-Passo 'Rollback concluido.' -Nivel Ok
