<#
.SYNOPSIS
    Controla o servico Friozem-ImportacaoNotas: Start / Stop / Restart / Status.

.DESCRIPTION
    Roda NO SERVIDOR (RDP) ou via WinRM. Idempotente: parar um servico ja parado nao falha; Status
    sempre retorna. Usa o nssm para start/stop/restart e combina nssm status + Get-Service no Status.

.PARAMETER Acao
    Start | Stop | Restart | Status (obrigatorio).

.PARAMETER NomeServico
    Nome do servico (default Friozem-ImportacaoNotas).

.PARAMETER NssmPath
    Caminho do nssm.exe (default conhecido; fallback deploy\tools).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\servico.ps1 -Acao Status
.EXAMPLE
    $cred = Get-Credential
    Invoke-Command -ComputerName 200.200.200.213 -Credential $cred -ScriptBlock {
        powershell -ExecutionPolicy Bypass -File C:\temp\deploy\servico.ps1 -Acao Restart
    }
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [ValidateSet('Start', 'Stop', 'Restart', 'Status')] [string] $Acao,
    [string] $NomeServico = 'Friozem-ImportacaoNotas',
    [string] $NssmPath = 'C:\Friozem\Gestao_Enderecos\tools\nssm.exe'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_comum.ps1')

$nssm = Resolve-NssmPath -NssmPath $NssmPath -RaizScripts $PSScriptRoot

if (-not (Test-NssmServico -NssmPath $nssm -NomeServico $NomeServico)) {
    if ($Acao -eq 'Status') {
        Write-Passo ("Servico {0} NAO existe (nao instalado)." -f $NomeServico) -Nivel Alerta
        exit 0
    }
    throw ("Servico {0} nao existe. Rode instalar.ps1 primeiro." -f $NomeServico)
}

switch ($Acao) {
    'Status' {
        $st = Invoke-Nssm -NssmPath $nssm -Argumentos @('status', $NomeServico) -TolerarFalha
        Write-Passo ("NSSM status : {0}" -f $st)
        $svc = Get-Service -Name $NomeServico -ErrorAction SilentlyContinue
        if ($svc) {
            Write-Passo ("Get-Service : {0} / StartType={1}" -f $svc.Status, $svc.StartType)
        }
    }
    'Start' {
        if ($PSCmdlet.ShouldProcess($NomeServico, 'nssm start')) {
            $r = Invoke-Nssm -NssmPath $nssm -Argumentos @('start', $NomeServico) -TolerarFalha
            Write-Passo ("Start: {0}" -f $r) -Nivel Ok
        }
    }
    'Stop' {
        if ($PSCmdlet.ShouldProcess($NomeServico, 'nssm stop')) {
            $r = Invoke-Nssm -NssmPath $nssm -Argumentos @('stop', $NomeServico) -TolerarFalha
            Write-Passo ("Stop: {0}" -f $r) -Nivel Ok
        }
    }
    'Restart' {
        if ($PSCmdlet.ShouldProcess($NomeServico, 'nssm restart')) {
            $r = Invoke-Nssm -NssmPath $nssm -Argumentos @('restart', $NomeServico) -TolerarFalha
            Write-Passo ("Restart: {0}" -f $r) -Nivel Ok
        }
    }
}
