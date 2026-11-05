<#
.SYNOPSIS
    Diagnostico (SOMENTE LEITURA) do servidor para o servico Friozem-ImportacaoNotas.

.DESCRIPTION
    Verifica e imprime, SEM alterar nada: SO/versao, PowerShell, runtimes .NET, presenca do
    nssm.exe e servicos NSSM existentes, TLS 1.2 (Client) no SChannel, alcance ao WMS e ao SMTP,
    existencia da estrutura de pastas e ACL de config\.

    Nenhuma alteracao e feita. Sempre retorna exit 0 (e um diagnostico), destacando alertas.

.PARAMETER RaizInstalacao
    Raiz do app (default C:\Friozem\importacao_notas).

.PARAMETER NssmPath
    Caminho do nssm.exe (default C:\Friozem\Gestao_Enderecos\tools\nssm.exe, com fallbacks).

.PARAMETER WmsHost / WmsPort
    Host/porta do WMS a testar. Default do host de producao e porta 8888 (a usada hoje).

.PARAMETER SmtpHost / SmtpPort
    Host/porta do SMTP a testar. Default smtp.friozem.com.br:587.

.PARAMETER PastaEntrada
    Pasta LOCAL de entrada de XMLs (default C:\Friozem\kitbrf). Testa existencia e escrita e, se
    possivel, se o compartilhamento SMB 'kitbrf' existe.

.PARAMETER NomeShareEntrada
    Nome do compartilhamento SMB da pasta de entrada a verificar (default 'kitbrf').

.EXAMPLE
    # LOCAL (RDP no servidor):
    powershell -ExecutionPolicy Bypass -File .\diagnostico.ps1

.EXAMPLE
    # REMOTO (WinRM, de outra maquina; usuario SRVAPPALC02\administrator):
    $cred = Get-Credential
    Invoke-Command -ComputerName 200.200.200.213 -Credential $cred -FilePath .\diagnostico.ps1
#>

[CmdletBinding()]
param(
    [string] $RaizInstalacao = 'C:\Friozem\importacao_notas',
    [string] $NssmPath = 'C:\Friozem\Gestao_Enderecos\tools\nssm.exe',
    [string] $WmsHost = 'friozem.assistsoftware.com.br',
    [int]    $WmsPort = 8888,
    [string] $SmtpHost = 'smtp.friozem.com.br',
    [int]    $SmtpPort = 587,
    [string] $PastaEntrada = 'C:\Friozem\kitbrf',
    [string] $NomeShareEntrada = 'kitbrf'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$alertas = New-Object System.Collections.Generic.List[string]
function Add-Alerta([string] $m) { $script:alertas.Add($m); Write-Host ("  [ALERTA] {0}" -f $m) -ForegroundColor Yellow }
function Secao([string] $t) { Write-Host ''; Write-Host ("=== {0} ===" -f $t) -ForegroundColor Cyan }

Write-Host ''
Write-Host '############################################################' -ForegroundColor White
Write-Host '#  DIAGNOSTICO (somente leitura) — Friozem-ImportacaoNotas' -ForegroundColor White
Write-Host '############################################################' -ForegroundColor White

# --- SO / PowerShell ---
Secao 'Sistema operacional e PowerShell'
Write-Host ("  Computador : {0}" -f $env:COMPUTERNAME)
Write-Host ("  SO         : {0}" -f [Environment]::OSVersion.VersionString)
Write-Host ("  PowerShell : {0}" -f $PSVersionTable.PSVersion)

# --- .NET runtimes ---
Secao '.NET runtimes instalados'
$dotnet = Get-Command 'dotnet' -ErrorAction SilentlyContinue
if ($dotnet) {
    $runtimes = (& dotnet --list-runtimes 2>$null)
    if ($runtimes) {
        $runtimes | ForEach-Object { Write-Host ("  {0}" -f $_) }
        $temNet8 = ($runtimes | Where-Object { $_ -match 'Microsoft\.NETCore\.App 8\.' })
        if ($temNet8) {
            Write-Host '  .NET 8 runtime presente.' -ForegroundColor Green
        } else {
            Add-Alerta 'Runtime .NET 8 NAO encontrado (ok se o deploy for self-contained).'
        }
    } else {
        Add-Alerta 'dotnet encontrado mas nao listou runtimes.'
    }
} else {
    Add-Alerta 'dotnet nao esta no PATH (ok se o deploy for self-contained — o pacote nao depende do dotnet).'
}

# --- NSSM ---
Secao 'NSSM'
$nssmResolvido = $null
foreach ($c in @($NssmPath, (Join-Path $PSScriptRoot 'tools\nssm.exe'))) {
    if ($c -and (Test-Path -LiteralPath $c -PathType Leaf)) { $nssmResolvido = $c; break }
}
if ($nssmResolvido) {
    Write-Host ("  nssm.exe   : {0}" -f $nssmResolvido) -ForegroundColor Green
} else {
    Add-Alerta ("nssm.exe nao encontrado em '{0}' nem em deploy\tools\." -f $NssmPath)
}

Write-Host '  Servicos NSSM existentes:'
try {
    $svcsNssm = Get-CimInstance Win32_Service -ErrorAction Stop | Where-Object { $_.PathName -match 'nssm' }
    if ($svcsNssm) {
        $svcsNssm | Select-Object Name, State, StartMode | Format-Table -AutoSize | Out-String | Write-Host
    } else {
        Write-Host '    (nenhum)'
    }
} catch {
    Add-Alerta ("Nao foi possivel consultar servicos (Win32_Service): {0}" -f $_.Exception.Message)
}

# --- TLS 1.2 (SChannel, Client) ---
Secao 'TLS 1.2 (SChannel / Client)'
$tlsKey = 'HKLM:\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Client'
$tls = Get-ItemProperty -Path $tlsKey -ErrorAction SilentlyContinue
$enabled = if ($tls) { $tls.Enabled } else { $null }
$disabledByDefault = if ($tls) { $tls.DisabledByDefault } else { $null }
$enabledTxt = if ($null -eq $enabled) { '(vazio)' } else { $enabled }
$disabledTxt = if ($null -eq $disabledByDefault) { '(vazio)' } else { $disabledByDefault }
Write-Host ("  Enabled            : {0}" -f $enabledTxt)
Write-Host ("  DisabledByDefault  : {0}" -f $disabledTxt)
if ($null -eq $enabled -or $enabled -ne 1 -or ($null -ne $disabledByDefault -and $disabledByDefault -ne 0)) {
    Add-Alerta 'TLS 1.2 Client NAO esta explicitamente habilitado no registro. Se o handshake com o WMS falhar, habilite conforme docs\INSTALACAO-SERVIDOR.md (instalar.ps1 -HabilitarTls12).'
}

# --- Alcance de rede ---
Secao 'Alcance de rede'
function Test-Porta([string] $computador, [int] $porta) {
    if (Get-Command 'Test-NetConnection' -ErrorAction SilentlyContinue) {
        return [bool](Test-NetConnection -ComputerName $computador -Port $porta -WarningAction SilentlyContinue -InformationLevel Quiet)
    }
    # Fallback para PS sem Test-NetConnection.
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $iar = $tcp.BeginConnect($computador, $porta, $null, $null)
        $ok = $iar.AsyncWaitHandle.WaitOne(5000, $false)
        if ($ok -and $tcp.Connected) { $tcp.EndConnect($iar); $tcp.Close(); return $true }
        $tcp.Close(); return $false
    } catch { return $false }
}
$wmsOk = Test-Porta $WmsHost $WmsPort
$corWms = if ($wmsOk) { 'Green' } else { 'Yellow' }
Write-Host ("  WMS {0}:{1} : {2}" -f $WmsHost, $WmsPort, $wmsOk) -ForegroundColor $corWms
if (-not $wmsOk) { Add-Alerta ("WMS {0}:{1} inalcancavel." -f $WmsHost, $WmsPort) }
$smtpOk = Test-Porta $SmtpHost $SmtpPort
$corSmtp = if ($smtpOk) { 'Green' } else { 'Yellow' }
Write-Host ("  SMTP {0}:{1} : {2}" -f $SmtpHost, $SmtpPort, $smtpOk) -ForegroundColor $corSmtp
if (-not $smtpOk) { Add-Alerta ("SMTP {0}:{1} inalcancavel." -f $SmtpHost, $SmtpPort) }

# --- Estrutura de pastas ---
Secao 'Estrutura de pastas'
$subs = @('', 'app', 'app\releases', 'app\current', 'config', 'data', 'logs', 'backups', 'tools')
foreach ($s in $subs) {
    $p = if ($s) { Join-Path $RaizInstalacao $s } else { $RaizInstalacao }
    $existe = Test-Path -LiteralPath $p
    $rotulo = if ($s) { $s } else { '(raiz)' }
    $corPasta = if ($existe) { 'Green' } else { 'Gray' }
    Write-Host ("  {0,-16} {1} : {2}" -f $rotulo, $existe, $p) -ForegroundColor $corPasta
}
if (-not (Test-Path -LiteralPath $RaizInstalacao)) {
    Write-Host '  (A estrutura ainda nao existe — sera criada por instalar.ps1.)' -ForegroundColor Gray
}

# --- ACL de config\ ---
Secao 'ACL de config\'
$pastaConfig = Join-Path $RaizInstalacao 'config'
if (Test-Path -LiteralPath $pastaConfig) {
    try {
        $acl = Get-Acl -LiteralPath $pastaConfig
        Write-Host ("  Owner : {0}" -f $acl.Owner)
        $acl.Access | ForEach-Object {
            Write-Host ("    {0,-40} {1} {2}" -f $_.IdentityReference, $_.FileSystemRights, $_.AccessControlType)
        }
    } catch {
        Add-Alerta ("Nao foi possivel ler ACL de config\: {0}" -f $_.Exception.Message)
    }
} else {
    Write-Host '  config\ ainda nao existe.' -ForegroundColor Gray
}

# --- Pasta de entrada (LOCAL) e compartilhamento SMB ---
Secao 'Pasta de entrada (local) e compartilhamento SMB'
$entradaExiste = Test-Path -LiteralPath $PastaEntrada
$corEntrada = if ($entradaExiste) { 'Green' } else { 'Yellow' }
Write-Host ("  Pasta local {0} : {1}" -f $PastaEntrada, $entradaExiste) -ForegroundColor $corEntrada
if ($entradaExiste) {
    $marcador = Join-Path $PastaEntrada ('.diag_write_test_{0}.tmp' -f ([guid]::NewGuid().ToString('N')))
    try {
        Set-Content -LiteralPath $marcador -Value 'diag' -ErrorAction Stop
        Remove-Item -LiteralPath $marcador -Force -ErrorAction SilentlyContinue
        Write-Host '  Escrita na pasta : OK' -ForegroundColor Green
    } catch {
        Add-Alerta ("Sem permissao de ESCRITA na pasta de entrada {0} (o servico, LocalSystem, precisa gravar)." -f $PastaEntrada)
    }
} else {
    Write-Host '  (A pasta de entrada ainda nao existe — sera criada por instalar.ps1.)' -ForegroundColor Gray
}

# Compartilhamento SMB (opcional; so verifica se os cmdlets existem).
if (Get-Command 'Get-SmbShare' -ErrorAction SilentlyContinue) {
    $share = Get-SmbShare -Name $NomeShareEntrada -ErrorAction SilentlyContinue
    if ($share) {
        Write-Host ("  Share SMB '{0}' : presente -> {1}" -f $NomeShareEntrada, $share.Path) -ForegroundColor Green
        if ($share.Path -ne $PastaEntrada) {
            Add-Alerta ("Share '{0}' aponta para '{1}', diferente da pasta de entrada '{2}'." -f $NomeShareEntrada, $share.Path, $PastaEntrada)
        }
    } else {
        Write-Host ("  Share SMB '{0}' : ausente (rode compartilhar-pasta.ps1 para expor na rede)." -f $NomeShareEntrada) -ForegroundColor Gray
    }
} else {
    Write-Host '  (Cmdlets SmbShare indisponiveis — verificacao do share pulada.)' -ForegroundColor Gray
}

# --- Resumo ---
Secao 'Resumo'
if ($alertas.Count -eq 0) {
    Write-Host '  Nenhum alerta. Ambiente aparenta pronto.' -ForegroundColor Green
} else {
    Write-Host ("  {0} alerta(s):" -f $alertas.Count) -ForegroundColor Yellow
    $alertas | ForEach-Object { Write-Host ("   - {0}" -f $_) -ForegroundColor Yellow }
}

exit 0
