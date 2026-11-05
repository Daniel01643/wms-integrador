<#
.SYNOPSIS
    Para e remove o servico NSSM Friozem-ImportacaoNotas e, opcionalmente, limpa as pastas.

.DESCRIPTION
    Roda NO SERVIDOR (RDP) ou via WinRM. Idempotente: servico inexistente nao falha. Com
    -LimparPastas (e confirmacao) apaga app\ e logs\, preservando data\ e backups\ quando
    -PreservarDados (padrao). NUNCA apaga o share de entrada (fora da raiz do app).

.PARAMETER NomeServico / NssmPath / RaizInstalacao
    Defaults da casa.

.PARAMETER LimparPastas
    Se presente, apaga pastas do app apos remover o servico (com confirmacao).

.PARAMETER PreservarDados
    Preserva data\ e backups\ ao limpar (default $true).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\remover.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\remover.ps1 -LimparPastas
#>

[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string] $NomeServico = 'Friozem-ImportacaoNotas',
    [string] $NssmPath = 'C:\Friozem\Gestao_Enderecos\tools\nssm.exe',
    [string] $RaizInstalacao = 'C:\Friozem\importacao_notas',
    [switch] $LimparPastas,
    [bool]   $PreservarDados = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_comum.ps1')

$nssm = Resolve-NssmPath -NssmPath $NssmPath -RaizScripts $PSScriptRoot

# --- 1. Parar e remover o servico ---
if (Test-NssmServico -NssmPath $nssm -NomeServico $NomeServico) {
    if ($PSCmdlet.ShouldProcess($NomeServico, 'nssm stop')) {
        [void](Invoke-Nssm -NssmPath $nssm -Argumentos @('stop', $NomeServico) -TolerarFalha)
    }
    if ($PSCmdlet.ShouldProcess($NomeServico, 'nssm remove confirm')) {
        [void](Invoke-Nssm -NssmPath $nssm -Argumentos @('remove', $NomeServico, 'confirm') -TolerarFalha)
        Write-Passo ("Servico {0} removido." -f $NomeServico) -Nivel Ok
    }
} else {
    Write-Passo ("Servico {0} nao existe (nada a remover)." -f $NomeServico) -Nivel Info
}

# --- 2. Limpeza opcional das pastas ---
if ($LimparPastas) {
    if (-not (Test-Path -LiteralPath $RaizInstalacao)) {
        Write-Passo ("Raiz {0} nao existe; nada a limpar." -f $RaizInstalacao) -Nivel Info
        return
    }

    $apagar = @('app', 'logs')
    $preservados = if ($PreservarDados) { @('data', 'backups', 'config') } else { @() }
    $preservadosTxt = if ($preservados) { $preservados -join ', ' } else { '(nada)' }

    Write-Passo ("Limpeza solicitada. Apagando: {0}. Preservando: {1}." -f ($apagar -join ', '), $preservadosTxt) -Nivel Alerta

    foreach ($sub in $apagar) {
        $p = Join-Path $RaizInstalacao $sub
        if (Test-Path -LiteralPath $p) {
            if ($PSCmdlet.ShouldProcess($p, 'Remover pasta')) {
                Remove-Item -LiteralPath $p -Recurse -Force
                Write-Passo ("Removido: {0}" -f $p) -Nivel Ok
            }
        }
    }

    if (-not $PreservarDados) {
        foreach ($sub in @('data', 'backups')) {
            $p = Join-Path $RaizInstalacao $sub
            if (Test-Path -LiteralPath $p) {
                if ($PSCmdlet.ShouldProcess($p, 'Remover pasta (PreservarDados=$false)')) {
                    Remove-Item -LiteralPath $p -Recurse -Force
                    Write-Passo ("Removido: {0}" -f $p) -Nivel Ok
                }
            }
        }
        Write-Passo 'config\ NAO e apagada automaticamente (contem segredos). Apague manualmente se desejar.' -Nivel Alerta
    }
} else {
    Write-Passo 'Pastas preservadas (sem -LimparPastas). Remova manualmente se desejar.' -Nivel Info
}

Write-Passo 'Remocao concluida.' -Nivel Ok
