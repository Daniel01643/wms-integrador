<#
.SYNOPSIS
    Modulo auxiliar (dot-sourced) da Etapa 9 — helpers compartilhados pelos scripts de deploy
    do servico Friozem-ImportacaoNotas (KitWmsIntegrador) sob NSSM.

.DESCRIPTION
    Funcoes comuns: localizar o nssm.exe, wrappers idempotentes de "nssm set", criacao de
    pastas, ACL restrita de config, derivacao de versao e logs sem segredo.

    USO: nao executar diretamente. Os demais scripts fazem:
        . (Join-Path $PSScriptRoot '_comum.ps1')

    EXECUCAO LOCAL (RDP no servidor) e REMOTA (WinRM/Invoke-Command) sao descritas em cada
    script que consome este modulo e no docs\INSTALACAO-SERVIDOR.md.

    Compativel com PowerShell 4.0/5.1 (Windows Server 2012 R2).
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Default conhecido do nssm.exe no servidor (reutiliza o da casa; override por parametro).
$script:NssmPathDefault = 'C:\Friozem\Gestao_Enderecos\tools\nssm.exe'

function Write-Passo {
    <#
    .SYNOPSIS Loga um passo informativo (NUNCA receber segredo como argumento).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Mensagem,
        [ValidateSet('Info', 'Ok', 'Alerta', 'Erro')] [string] $Nivel = 'Info'
    )
    $carimbo = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    switch ($Nivel) {
        'Ok'     { $cor = 'Green';  $tag = 'OK   ' }
        'Alerta' { $cor = 'Yellow'; $tag = 'ALERTA' }
        'Erro'   { $cor = 'Red';    $tag = 'ERRO ' }
        default  { $cor = 'Cyan';   $tag = 'INFO ' }
    }
    Write-Host ("[{0}] {1} {2}" -f $carimbo, $tag, $Mensagem) -ForegroundColor $cor
}

function Resolve-NssmPath {
    <#
    .SYNOPSIS Localiza o nssm.exe: parametro -> default conhecido -> deploy\tools\nssm.exe embarcado.
    .OUTPUTS Caminho absoluto do nssm.exe. Lanca se nao encontrar.
    #>
    [CmdletBinding()]
    param(
        [string] $NssmPath,
        [string] $RaizScripts = $PSScriptRoot
    )

    $candidatos = @()
    if ($NssmPath) { $candidatos += $NssmPath }
    $candidatos += $script:NssmPathDefault
    if ($RaizScripts) { $candidatos += (Join-Path $RaizScripts 'tools\nssm.exe') }

    foreach ($c in $candidatos) {
        if ($c -and (Test-Path -LiteralPath $c -PathType Leaf)) {
            return (Resolve-Path -LiteralPath $c).ProviderPath
        }
    }

    # Ultimo recurso: nssm no PATH.
    $noPath = Get-Command 'nssm.exe' -ErrorAction SilentlyContinue
    if ($noPath) { return $noPath.Source }

    throw ("nssm.exe nao encontrado. Procurado em: {0}. Informe -NssmPath ou coloque o executavel em deploy\tools\nssm.exe." -f ($candidatos -join '; '))
}

function Invoke-Nssm {
    <#
    .SYNOPSIS Executa o nssm.exe e retorna stdout+stderr; lanca em exit code != 0 (salvo -TolerarFalha).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $NssmPath,
        [Parameter(Mandatory)] [string[]] $Argumentos,
        [switch] $TolerarFalha
    )
    $saida = & $NssmPath @Argumentos 2>&1
    $code = $LASTEXITCODE
    if ($code -ne 0 -and -not $TolerarFalha) {
        throw ("nssm {0} falhou (exit {1}): {2}" -f ($Argumentos -join ' '), $code, ($saida -join ' '))
    }
    # nssm emite texto UTF-16; normaliza removendo nulos para comparacao.
    return ((($saida | Out-String) -replace "`0", '').Trim())
}

function Test-NssmServico {
    <#
    .SYNOPSIS Retorna $true se o servico existe no NSSM.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $NssmPath,
        [Parameter(Mandatory)] [string] $NomeServico
    )
    $saida = & $NssmPath 'status' $NomeServico 2>&1
    $code = $LASTEXITCODE
    # nssm status de servico inexistente retorna code != 0 com mensagem de erro.
    return ($code -eq 0)
}

function Set-NssmParam {
    <#
    .SYNOPSIS Define um parametro do servico via "nssm set" de forma IDEMPOTENTE
              (le o valor atual e so grava se diferente). Respeita -WhatIf do chamador.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)] [string] $NssmPath,
        [Parameter(Mandatory)] [string] $NomeServico,
        [Parameter(Mandatory)] [string] $Parametro,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Valor
    )

    $atual = ''
    try {
        $atual = (Invoke-Nssm -NssmPath $NssmPath -Argumentos @('get', $NomeServico, $Parametro) -TolerarFalha)
    } catch {
        $atual = ''
    }

    if ($atual -eq $Valor) {
        Write-Passo ("NSSM {0} ja = '{1}' (sem alteracao)" -f $Parametro, $Valor) -Nivel Info
        return
    }

    if ($PSCmdlet.ShouldProcess("$NomeServico/$Parametro", "nssm set -> '$Valor'")) {
        [void](Invoke-Nssm -NssmPath $NssmPath -Argumentos @('set', $NomeServico, $Parametro, $Valor))
        Write-Passo ("NSSM {0} definido = '{1}'" -f $Parametro, $Valor) -Nivel Ok
    }
}

function Confirm-Directory {
    <#
    .SYNOPSIS Garante que o diretorio exista (cria se faltar). Idempotente. Respeita -WhatIf.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param([Parameter(Mandatory)] [string] $Caminho)

    if (Test-Path -LiteralPath $Caminho -PathType Container) {
        return (Resolve-Path -LiteralPath $Caminho).ProviderPath
    }
    if ($PSCmdlet.ShouldProcess($Caminho, 'Criar diretorio')) {
        [void](New-Item -ItemType Directory -Path $Caminho -Force)
        Write-Passo ("Pasta criada: {0}" -f $Caminho) -Nivel Ok
    }
    return $Caminho
}

function Set-ConfigAcl {
    <#
    .SYNOPSIS Aplica ACL restrita a um caminho: somente Administrators (ou SYSTEM) e a conta do
              servico tem acesso; remove herança. Idempotente. Respeita -WhatIf.
    .PARAMETER ContaServico
        Conta sob a qual o servico roda (ex. 'NT AUTHORITY\SYSTEM' ou 'FRIOPART\svc_xxx').
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)] [string] $Caminho,
        [string] $ContaServico = 'NT AUTHORITY\SYSTEM'
    )

    if (-not (Test-Path -LiteralPath $Caminho)) {
        throw ("Set-ConfigAcl: caminho inexistente: {0}" -f $Caminho)
    }

    if (-not $PSCmdlet.ShouldProcess($Caminho, "Aplicar ACL restrita (Administrators, SYSTEM, $ContaServico)")) {
        return
    }

    $acl = New-Object System.Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)   # remove herança, nao copia regras herdadas

    $contas = @('BUILTIN\Administrators', 'NT AUTHORITY\SYSTEM')
    if ($ContaServico -and ($contas -notcontains $ContaServico)) { $contas += $ContaServico }

    $heranca = [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    $prop = [System.Security.AccessControl.PropagationFlags]::None
    foreach ($c in $contas) {
        try {
            $regra = New-Object System.Security.AccessControl.FileSystemAccessRule(
                $c, 'FullControl', $heranca, $prop, 'Allow')
            $acl.AddAccessRule($regra)
        } catch {
            Write-Passo ("Conta ignorada na ACL (nao resolvida): {0}" -f $c) -Nivel Alerta
        }
    }

    Set-Acl -LiteralPath $Caminho -AclObject $acl
    Write-Passo ("ACL restrita aplicada em: {0}" -f $Caminho) -Nivel Ok
}

function Get-VersaoPadrao {
    <#
    .SYNOPSIS Deriva uma versao: yyyy.MM.dd[-<hash git curto>] ou yyyy.MM.dd.HHmm sem git.
    #>
    [CmdletBinding()]
    param([string] $RaizRepo)

    $data = (Get-Date).ToString('yyyy.MM.dd')
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
    if ($hash) { return ("{0}-{1}" -f $data, $hash.Trim()) }
    return ("{0}.{1}" -f $data, (Get-Date).ToString('HHmm'))
}

function Copy-ConfigParaRelease {
    <#
    .SYNOPSIS Copia os JSON/txt de config\ para a pasta de uma release (ao lado do exe), para que o
              Worker (que procura config ao lado de si) os enxergue. Idempotente (sobrescreve).
              Mantem config\ como unico ponto de verdade; a copia na release e descartavel.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)] [string] $PastaConfig,
        [Parameter(Mandatory)] [string] $PastaReleaseApp
    )

    if (-not (Test-Path -LiteralPath $PastaConfig)) {
        Write-Passo ("config\ inexistente ({0}); nenhuma config copiada (ok no primeiro install)" -f $PastaConfig) -Nivel Alerta
        return
    }

    $arquivos = @('appsettings.Production.json', 'email.settings.json')
    foreach ($nome in $arquivos) {
        $origem = Join-Path $PastaConfig $nome
        if (Test-Path -LiteralPath $origem -PathType Leaf) {
            $destino = Join-Path $PastaReleaseApp $nome
            if ($PSCmdlet.ShouldProcess($destino, "Copiar config '$nome' para a release")) {
                Copy-Item -LiteralPath $origem -Destination $destino -Force
                Write-Passo ("Config copiada para a release: {0}" -f $nome) -Nivel Ok
            }
        }
    }
}
