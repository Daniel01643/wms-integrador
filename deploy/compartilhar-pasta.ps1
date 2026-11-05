<#
.SYNOPSIS
    Cria/atualiza (idempotente) o compartilhamento SMB da pasta LOCAL de entrada de XMLs.

.DESCRIPTION
    Roda NO SERVIDOR (SRVAPPALC02), via RDP ou WinRM, como administrador.

    A pasta de ENTRADA e LOCAL do proprio servidor (default C:\Friozem\kitbrf). O servico a
    acessa por caminho LOCAL (LocalSystem basta). Este script apenas EXPOE essa pasta na rede
    (SMB) para que os usuarios depositem os XMLs de fora. Ele:

      1. Cria a pasta local se nao existir.
      2. Cria/atualiza o compartilhamento SMB (New-SmbShare/Set-SmbShare), verificando antes via
         Get-SmbShare para ser idempotente. Da ESCRITA (Change) ao principal gravador e Full aos
         Administradores. Fallback para 'net share' se o modulo SmbShare nao estiver disponivel.
      3. Ajusta a ACL NTFS da pasta para permitir escrita do mesmo principal (Modify), sem remover
         a heranca nem o acesso de Administradores/SYSTEM.

    IDEMPOTENTE: pode ser rodado varias vezes; so altera o que estiver diferente.
    Compativel com PowerShell 4.0/5.1 (Windows Server 2012 R2; modulo SmbShare presente).

    NAO configura o servico nem toca em segredos. Rode como passo separado de instalar.ps1
    (ou via instalar.ps1 -CompartilharEntrada).

.PARAMETER NomeShare
    Nome do compartilhamento SMB (default 'kitbrf').

.PARAMETER CaminhoLocal
    Pasta local a compartilhar (default 'C:\Friozem\kitbrf'). E a mesma de Pastas:Entrada.

.PARAMETER GravadoresShare
    Principal (usuario/grupo) que recebe ESCRITA no share (Change) e Modify na ACL NTFS.
    Default 'Authenticated Users' (validacao). Em producao, recomenda-se restringir a um grupo
    de dominio, ex. 'FRIOPART\<grupo>' (DECISAO EM ABERTO).

.EXAMPLE
    # LOCAL (RDP), validacao com Authenticated Users:
    powershell -ExecutionPolicy Bypass -File .\compartilhar-pasta.ps1

.EXAMPLE
    # Producao, restringindo a um grupo de dominio:
    .\compartilhar-pasta.ps1 -GravadoresShare 'FRIOPART\ImportacaoNotas'

.EXAMPLE
    # Previa sem alterar nada:
    .\compartilhar-pasta.ps1 -WhatIf
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $NomeShare = 'kitbrf',
    [string] $CaminhoLocal = 'C:\Friozem\kitbrf',
    [string] $GravadoresShare = 'Authenticated Users'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_comum.ps1')

# --- 1. Pasta local ---
Write-Passo ("Pasta local de entrada: {0}" -f $CaminhoLocal)
[void](Confirm-Directory -Caminho $CaminhoLocal)

# --- 2. Compartilhamento SMB ---
Write-Passo ("Compartilhamento SMB: {0} -> {1}" -f $NomeShare, $CaminhoLocal)
$temSmbCmdlets = [bool](Get-Command -Name 'New-SmbShare' -ErrorAction SilentlyContinue)

if ($temSmbCmdlets) {
    $share = Get-SmbShare -Name $NomeShare -ErrorAction SilentlyContinue
    if ($null -eq $share) {
        if ($PSCmdlet.ShouldProcess($NomeShare, "New-SmbShare -Path $CaminhoLocal")) {
            [void](New-SmbShare -Name $NomeShare -Path $CaminhoLocal `
                -FullAccess 'BUILTIN\Administrators' `
                -ChangeAccess $GravadoresShare `
                -Description 'Entrada de XMLs do KitWmsIntegrador (deposito de notas).')
            Write-Passo ("Share {0} criado (Change: {1}; Full: Administrators)." -f $NomeShare, $GravadoresShare) -Nivel Ok
        }
    } else {
        Write-Passo ("Share {0} ja existe - atualizando caminho/permissoes." -f $NomeShare) -Nivel Info
        if ($share.Path -ne $CaminhoLocal) {
            Write-Passo ("ATENCAO: share {0} aponta para '{1}', diferente de '{2}'. Removendo e recriando." -f $NomeShare, $share.Path, $CaminhoLocal) -Nivel Alerta
            if ($PSCmdlet.ShouldProcess($NomeShare, "Remove-SmbShare + New-SmbShare -Path $CaminhoLocal")) {
                [void](Remove-SmbShare -Name $NomeShare -Force)
                [void](New-SmbShare -Name $NomeShare -Path $CaminhoLocal `
                    -FullAccess 'BUILTIN\Administrators' `
                    -ChangeAccess $GravadoresShare `
                    -Description 'Entrada de XMLs do KitWmsIntegrador (deposito de notas).')
                Write-Passo ("Share {0} recriado." -f $NomeShare) -Nivel Ok
            }
        } else {
            if ($PSCmdlet.ShouldProcess($NomeShare, "Set-SmbShare / Grant-SmbShareAccess ($GravadoresShare = Change)")) {
                [void](Set-SmbShare -Name $NomeShare -Force `
                    -Description 'Entrada de XMLs do KitWmsIntegrador (deposito de notas).')
                [void](Grant-SmbShareAccess -Name $NomeShare -AccountName 'BUILTIN\Administrators' -AccessRight Full -Force)
                [void](Grant-SmbShareAccess -Name $NomeShare -AccountName $GravadoresShare -AccessRight Change -Force)
                Write-Passo ("Permissoes do share {0} atualizadas (Change: {1}; Full: Administrators)." -f $NomeShare, $GravadoresShare) -Nivel Ok
            }
        }
    }
} else {
    # Fallback documentado: cmdlets SmbShare ausentes. No Server 2012 R2 eles existem; este ramo e
    # apenas uma rede de seguranca.
    Write-Passo 'Cmdlets SmbShare indisponiveis - usando fallback "net share".' -Nivel Alerta
    $existeNet = (& net.exe share $NomeShare 2>$null)
    if ($LASTEXITCODE -eq 0 -and $existeNet) {
        Write-Passo ("Share {0} ja existe (net share)." -f $NomeShare) -Nivel Info
    } elseif ($PSCmdlet.ShouldProcess($NomeShare, "net share (Change: $GravadoresShare; Full: Administrators)")) {
        [void](& net.exe share ("{0}={1}" -f $NomeShare, $CaminhoLocal) `
            ("/GRANT:{0},CHANGE" -f $GravadoresShare) '/GRANT:Administrators,FULL' `
            '/REMARK:Entrada de XMLs do KitWmsIntegrador')
        if ($LASTEXITCODE -ne 0) { throw ("net share falhou (exit {0})." -f $LASTEXITCODE) }
        Write-Passo ("Share {0} criado via net share." -f $NomeShare) -Nivel Ok
    }
}

# --- 3. ACL NTFS da pasta (escrita para o gravador, sem remover heranca) ---
Write-Passo ("Ajustando ACL NTFS de {0} ({1}: Modify)..." -f $CaminhoLocal, $GravadoresShare)
if ($PSCmdlet.ShouldProcess($CaminhoLocal, "Conceder Modify a '$GravadoresShare' (mantendo heranca)")) {
    $acl = Get-Acl -LiteralPath $CaminhoLocal
    $heranca = [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    $prop = [System.Security.AccessControl.PropagationFlags]::None
    $regra = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $GravadoresShare, 'Modify', $heranca, $prop, 'Allow')
    $acl.AddAccessRule($regra)
    Set-Acl -LiteralPath $CaminhoLocal -AclObject $acl
    Write-Passo ("ACL NTFS atualizada: {0} com Modify em {1}." -f $GravadoresShare, $CaminhoLocal) -Nivel Ok
}

Write-Passo 'Compartilhamento concluido.' -Nivel Ok
