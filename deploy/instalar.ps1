<#
.SYNOPSIS
    Instala/atualiza (idempotente) o servico NSSM Friozem-ImportacaoNotas no servidor.

.DESCRIPTION
    Roda NO SERVIDOR (RDP) ou via WinRM. Cria a estrutura de pastas, copia/expande a release para
    app\releases\<versao>, aponta app\current para ela, cria/atualiza o servico NSSM de forma
    idempotente (AppDirectory, executavel, stdout/stderr com rotacao, start auto-delayed, recuperacao
    apos falha), configura ACL restrita de config\ e, opcionalmente e com confirmacao, habilita o
    TLS 1.2 (Client) no registro. NAO inicia o servico ate os segredos existirem.

    IDEMPOTENTE: pode ser rodado varias vezes; so altera o que estiver diferente.

.PARAMETER RaizInstalacao
    Raiz do app (default C:\Friozem\importacao_notas).

.PARAMETER NomeServico
    Nome do servico NSSM (default Friozem-ImportacaoNotas).

.PARAMETER NssmPath
    Caminho do nssm.exe (default C:\Friozem\Gestao_Enderecos\tools\nssm.exe; fallback deploy\tools).

.PARAMETER ReleaseZip
    Zip de release gerado por publicar.ps1. Mutuamente exclusivo com -ReleaseDir.

.PARAMETER ReleaseDir
    Pasta de release ja expandida (ex. artifacts\<versao>\app). Mutuamente exclusivo com -ReleaseZip.

.PARAMETER Versao
    Nome da release (subpasta de app\releases). Default derivado da origem/data.

.PARAMETER ContaServico
    Conta sob a qual o servico roda (default 'NT AUTHORITY\SYSTEM'). Como a pasta de entrada e
    LOCAL (default C:\Friozem\kitbrf), LocalSystem basta. So seria necessaria conta de dominio se,
    hipoteticamente, a entrada passasse a ser um share em OUTRA maquina (ver docs\INSTALACAO-SERVIDOR.md).

.PARAMETER PastaEntrada
    Pasta LOCAL de entrada de XMLs (default C:\Friozem\kitbrf). Criada por este script; e o valor
    default de Pastas:Entrada. Para expor na rede, rode compartilhar-pasta.ps1 (ou use
    -CompartilharEntrada).

.PARAMETER CopiarScripts
    Se $true (default), copia todos os .ps1 de deploy\ (inclusive _comum.ps1) para a pasta
    PERMANENTE de scripts no servidor (ver -PastaScriptsDestino), de forma idempotente. Assim o
    servidor fica autossuficiente e o tecnico opera direto dele (RDP) a partir dessa pasta, sem
    depender de C:\temp nem de WinRM. Passe $false para desligar.

.PARAMETER PastaScriptsDestino
    Pasta PERMANENTE de destino dos scripts de deploy no servidor (default
    <RaizInstalacao>\tools\deploy, ex. C:\Friozem\importacao_notas\tools\deploy). Apenas essa
    subpasta de tools\ e gerenciada; outros conteudos de tools\ (ex. nssm.exe) nao sao tocados.

.PARAMETER CopiarManuais
    Se $true (default), copia os manuais (gerados por publicar.ps1) para a pasta PERMANENTE
    <RaizInstalacao>\manuais (ex. C:\Friozem\importacao_notas\manuais), para o tecnico te-los a mao
    por RDP. Idempotente (sobrescreve a cada execucao). Passe $false para desligar. A origem e
    resolvida nesta ordem: -ManuaisZip, -ManuaisDir, ou uma pasta/zip 'manuais' irma da release.

.PARAMETER ManuaisDir
    Pasta de manuais ja expandida (ex. artifacts\<versao>\manuais). Tem precedencia sobre a deteccao
    automatica, mas nao sobre -ManuaisZip.

.PARAMETER ManuaisZip
    Zip de manuais gerado por publicar.ps1 (ex. artifacts\<versao>\manuais.zip). Tem a maior
    precedencia na resolucao da origem dos manuais.

.PARAMETER CompartilharEntrada
    Se presente, chama compartilhar-pasta.ps1 para expor a PastaEntrada na rede via SMB (share
    'kitbrf', gravacao para 'Authenticated Users' por padrao).

.PARAMETER HabilitarTls12
    Se presente, habilita o TLS 1.2 Client no registro (COM confirmacao ShouldProcess). Reversivel.

.PARAMETER IniciarServico
    Se presente, inicia o servico ao final — somente se os segredos ja existirem.

.EXAMPLE
    # LOCAL (RDP):
    powershell -ExecutionPolicy Bypass -File .\instalar.ps1 -ReleaseZip C:\temp\KitWmsIntegrador-2026.01.15-1-win-x64.zip -Versao 2026.01.15-1

.EXAMPLE
    # REMOTO (WinRM): copie scripts + zip para o servidor e invoque la (ver docs).
    $cred = Get-Credential
    Invoke-Command -ComputerName 200.200.200.213 -Credential $cred -ScriptBlock {
        powershell -ExecutionPolicy Bypass -File C:\temp\deploy\instalar.ps1 -ReleaseZip C:\temp\rel.zip -Versao 2026.01.15-1
    }
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $RaizInstalacao = 'C:\Friozem\importacao_notas',
    [string] $NomeServico = 'Friozem-ImportacaoNotas',
    [string] $NssmPath = 'C:\Friozem\Gestao_Enderecos\tools\nssm.exe',
    [string] $ReleaseZip,
    [string] $ReleaseDir,
    [string] $Versao,
    [string] $ContaServico = 'NT AUTHORITY\SYSTEM',
    [string] $PastaEntrada = 'C:\Friozem\kitbrf',
    [bool]   $CopiarScripts = $true,
    [string] $PastaScriptsDestino,
    [bool]   $CopiarManuais = $true,
    [string] $ManuaisDir,
    [string] $ManuaisZip,
    [switch] $CompartilharEntrada,
    [switch] $HabilitarTls12,
    [switch] $IniciarServico
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_comum.ps1')

if ($ReleaseZip -and $ReleaseDir) { throw 'Informe apenas -ReleaseZip OU -ReleaseDir, nao ambos.' }
if (-not $ReleaseZip -and -not $ReleaseDir) { throw 'Informe -ReleaseZip ou -ReleaseDir com os binarios publicados.' }

$nssm = Resolve-NssmPath -NssmPath $NssmPath -RaizScripts $PSScriptRoot
Write-Passo ("NSSM           : {0}" -f $nssm)

if (-not $Versao) {
    if ($ReleaseDir) {
        $Versao = Split-Path -Leaf (Split-Path -Parent (Resolve-Path -LiteralPath $ReleaseDir))
    } else {
        $Versao = [IO.Path]::GetFileNameWithoutExtension($ReleaseZip)
    }
    if (-not $Versao) { $Versao = Get-VersaoPadrao }
}
Write-Passo ("Versao         : {0}" -f $Versao)

# --- 1. Estrutura de pastas ---
Write-Passo 'Criando/validando estrutura de pastas...'
$pastaApp       = Join-Path $RaizInstalacao 'app'
$pastaReleases  = Join-Path $pastaApp 'releases'
$pastaCurrent   = Join-Path $pastaApp 'current'
$pastaConfig    = Join-Path $RaizInstalacao 'config'
$pastaData      = Join-Path $RaizInstalacao 'data'
$pastaLogs      = Join-Path $RaizInstalacao 'logs'
$pastaBackups   = Join-Path $RaizInstalacao 'backups'
$pastaTools     = Join-Path $RaizInstalacao 'tools'
foreach ($p in @($RaizInstalacao, $pastaApp, $pastaReleases, $pastaConfig, $pastaData, $pastaLogs, $pastaBackups, $pastaTools)) {
    [void](Confirm-Directory -Caminho $p)
}

# --- 1b. Replicar os scripts de deploy para a pasta PERMANENTE do servidor (idempotente) ---
# Deixa o servidor autossuficiente: apos a producao, o tecnico abre o PowerShell como Admin NO
# SERVIDOR, vai ate esta pasta e opera (servico, publicar-release, rollback, configurar-segredos,
# diagnostico, compartilhar-pasta, remover) sem depender de C:\temp nem de WinRM da maquina de build.
# A fonte e a propria pasta onde este script esta rodando ($PSScriptRoot), entao _comum.ps1 vai junto.
if (-not $PastaScriptsDestino) { $PastaScriptsDestino = Join-Path $pastaTools 'deploy' }
if ($CopiarScripts) {
    Write-Passo ("Replicando scripts de deploy para a pasta permanente: {0}" -f $PastaScriptsDestino)
    Copy-ScriptsDeployParaServidor -PastaOrigem $PSScriptRoot -PastaDestino $PastaScriptsDestino -WhatIf:$WhatIfPreference
} else {
    Write-Passo 'Copia dos scripts de deploy desativada (-CopiarScripts $false).' -Nivel Info
}

# Pasta LOCAL de entrada de XMLs (fora da raiz do app). As subpastas Processados/Erro/Estado/
# telemetria sao criadas pelo proprio servico sob demanda. Esta e a Pastas:Entrada default.
Write-Passo ("Pasta de entrada (local): {0}" -f $PastaEntrada)
[void](Confirm-Directory -Caminho $PastaEntrada)

# --- 2. Expandir/copiar a release ---
$pastaReleaseAlvo = Join-Path $pastaReleases $Versao
[void](Confirm-Directory -Caminho $pastaReleaseAlvo)
if ($ReleaseZip) {
    if (-not (Test-Path -LiteralPath $ReleaseZip)) { throw ("ReleaseZip nao encontrado: {0}" -f $ReleaseZip) }
    Write-Passo ("Expandindo release {0} -> {1}" -f $ReleaseZip, $pastaReleaseAlvo)
    # Portavel para PowerShell 4.0 (Server 2012 R2): Expand-Archive so existe no PS 5.0+.
    Expand-ZipPortavel -ZipPath $ReleaseZip -Destino $pastaReleaseAlvo -WhatIf:$WhatIfPreference
} else {
    if (-not (Test-Path -LiteralPath $ReleaseDir)) { throw ("ReleaseDir nao encontrado: {0}" -f $ReleaseDir) }
    Write-Passo ("Copiando release {0} -> {1}" -f $ReleaseDir, $pastaReleaseAlvo)
    if ($PSCmdlet.ShouldProcess($pastaReleaseAlvo, 'Copiar release')) {
        Copy-Item -Path (Join-Path $ReleaseDir '*') -Destination $pastaReleaseAlvo -Recurse -Force
    }
}

$exe = Join-Path $pastaReleaseAlvo 'KitWmsIntegrador.Worker.exe'
if (-not $WhatIfPreference -and -not (Test-Path -LiteralPath $exe)) {
    throw ("Executavel nao encontrado apos expandir/copiar: {0}" -f $exe)
}

# --- 3. Copiar config persistente para a release (D4 do plano) ---
Copy-ConfigParaRelease -PastaConfig $pastaConfig -PastaReleaseApp $pastaReleaseAlvo

# --- 4. Apontar app\current para a release (copia idempotente) ---
Write-Passo 'Apontando app\current para a release...'
if ($PSCmdlet.ShouldProcess($pastaCurrent, 'Atualizar current')) {
    if (Test-Path -LiteralPath $pastaCurrent) {
        Remove-Item -LiteralPath $pastaCurrent -Recurse -Force
    }
    [void](New-Item -ItemType Directory -Path $pastaCurrent -Force)
    Copy-Item -Path (Join-Path $pastaReleaseAlvo '*') -Destination $pastaCurrent -Recurse -Force
    Write-Passo ("current atualizado a partir de {0}" -f $Versao) -Nivel Ok
}
$exeCurrent = Join-Path $pastaCurrent 'KitWmsIntegrador.Worker.exe'

# --- 4b. Copiar manuais para a pasta permanente (FORA de app\current) ---
# Os manuais (.md/.html/.pdf + README.md + assets\) sao empacotados por publicar.ps1 como pasta
# IRMA de app\ (artifacts\<versao>\manuais e manuais.zip), NUNCA dentro do zip de app. Aqui eles
# vao para <RaizInstalacao>\manuais, para o tecnico consulta-los por RDP. Idempotente.
if ($CopiarManuais) {
    Write-Passo 'Copiando manuais para a pasta permanente...'
    $pastaManuais = Join-Path $RaizInstalacao 'manuais'
    [void](Confirm-Directory -Caminho $pastaManuais)

    # Resolve a origem dos manuais: (1) -ManuaisZip; (2) -ManuaisDir; (3) deteccao a partir da release.
    $origemManuaisZip = $null
    $origemManuaisDir = $null
    if ($ManuaisZip) {
        $origemManuaisZip = $ManuaisZip
    } elseif ($ManuaisDir) {
        $origemManuaisDir = $ManuaisDir
    } elseif ($ReleaseDir) {
        # Pasta 'manuais' irma de -ReleaseDir (ex. artifacts\<versao>\app -> artifacts\<versao>\manuais).
        $candidatoDir = Join-Path (Split-Path -Parent $ReleaseDir) 'manuais'
        if (Test-Path -LiteralPath $candidatoDir -PathType Container) { $origemManuaisDir = $candidatoDir }
    } elseif ($ReleaseZip) {
        # manuais.zip (ou pasta manuais\) irmao de -ReleaseZip.
        $candidatoZip = Join-Path (Split-Path -Parent $ReleaseZip) 'manuais.zip'
        $candidatoDir = Join-Path (Split-Path -Parent $ReleaseZip) 'manuais'
        if (Test-Path -LiteralPath $candidatoZip -PathType Leaf) { $origemManuaisZip = $candidatoZip }
        elseif (Test-Path -LiteralPath $candidatoDir -PathType Container) { $origemManuaisDir = $candidatoDir }
    }

    if ($origemManuaisZip -and (Test-Path -LiteralPath $origemManuaisZip -PathType Leaf)) {
        Write-Passo ("Expandindo manuais {0} -> {1}" -f $origemManuaisZip, $pastaManuais)
        # Portavel para PowerShell 4.0 (Server 2012 R2). Sobrescreve o destino (idempotente).
        Expand-ZipPortavel -ZipPath $origemManuaisZip -Destino $pastaManuais -WhatIf:$WhatIfPreference
        Write-Passo ("Manuais copiados para: {0}" -f $pastaManuais) -Nivel Ok
    } elseif ($origemManuaisDir -and (Test-Path -LiteralPath $origemManuaisDir -PathType Container)) {
        Write-Passo ("Copiando manuais {0} -> {1}" -f $origemManuaisDir, $pastaManuais)
        if ($PSCmdlet.ShouldProcess($pastaManuais, 'Copiar manuais')) {
            Copy-Item -Path (Join-Path $origemManuaisDir '*') -Destination $pastaManuais -Recurse -Force
            Write-Passo ("Manuais copiados para: {0}" -f $pastaManuais) -Nivel Ok
        }
    } else {
        Write-Passo ('Origem dos manuais nao encontrada (-ManuaisZip/-ManuaisDir ou pasta irma da release); manuais NAO copiados.') -Nivel Alerta
    }
} else {
    Write-Passo 'Copia dos manuais desativada (-CopiarManuais $false).' -Nivel Info
}

# --- 5. Criar/atualizar o servico NSSM (idempotente) ---
Write-Passo 'Configurando servico NSSM...'
$existe = Test-NssmServico -NssmPath $nssm -NomeServico $NomeServico
if (-not $existe) {
    if ($PSCmdlet.ShouldProcess($NomeServico, 'nssm install')) {
        [void](Invoke-Nssm -NssmPath $nssm -Argumentos @('install', $NomeServico, $exeCurrent))
        Write-Passo ("Servico {0} criado." -f $NomeServico) -Nivel Ok
    }
} else {
    Write-Passo ("Servico {0} ja existe - atualizando parametros." -f $NomeServico) -Nivel Info
}

$logStdout = Join-Path $pastaLogs 'worker.out.log'
$logStderr = Join-Path $pastaLogs 'worker.err.log'

$params = [ordered]@{
    'Application'          = $exeCurrent
    'AppDirectory'         = $pastaCurrent
    'AppStdout'            = $logStdout
    'AppStderr'            = $logStderr
    'AppRotateFiles'       = '1'
    'AppRotateOnline'      = '1'
    'AppRotateSeconds'     = '86400'       # rotaciona diariamente
    'AppRotateBytes'       = '10485760'    # ou a cada 10 MB
    'AppStdoutCreationDisposition' = '4'   # OPEN_ALWAYS (append)
    'AppStderrCreationDisposition' = '4'
    'Start'                = 'SERVICE_DELAYED_AUTO_START'
    'AppRestartDelay'      = '15000'       # 15 s entre reinicios
    'DisplayName'          = $NomeServico
    'Description'          = 'KitWmsIntegrador - importacao automatica de NF-e para o WMS (Friozem).'
    'ObjectName'           = $ContaServico
}
foreach ($k in $params.Keys) {
    # ObjectName com conta de dominio exige senha; SYSTEM/LocalService nao. So define se nao exigir senha.
    if ($k -eq 'ObjectName' -and $ContaServico -notin @('NT AUTHORITY\SYSTEM', 'LocalSystem', 'NT AUTHORITY\LocalService', 'NT AUTHORITY\NetworkService')) {
        Write-Passo ("ObjectName='{0}' requer senha - configure manualmente (nssm set {1} ObjectName <conta> <senha>) ou via docs." -f $ContaServico, $NomeServico) -Nivel Alerta
        continue
    }
    Set-NssmParam -NssmPath $nssm -NomeServico $NomeServico -Parametro $k -Valor ([string]$params[$k])
}

# Recuperacao apos falha: 'AppExit Default Restart' exige 'AppExit' e 'Default' como tokens
# SEPARADOS (sintaxe do NSSM). Aplicado fora do laco via -SubParametro para nao virar um unico
# argumento com espaco. Confirmar depois com: nssm get <servico> AppExit Default -> 'Restart'.
Set-NssmParam -NssmPath $nssm -NomeServico $NomeServico -Parametro 'AppExit' -SubParametro 'Default' -Valor 'Restart'

# --- 6. ACL restrita de config\ ---
Write-Passo 'Aplicando ACL restrita em config\...'
Set-ConfigAcl -Caminho $pastaConfig -ContaServico $ContaServico

# --- 6b. Compartilhar a pasta de entrada na rede (opcional) ---
if ($CompartilharEntrada) {
    Write-Passo 'Compartilhamento da pasta de entrada solicitado (-CompartilharEntrada).' -Nivel Info
    $scriptShare = Join-Path $PSScriptRoot 'compartilhar-pasta.ps1'
    if (-not (Test-Path -LiteralPath $scriptShare)) {
        Write-Passo ("compartilhar-pasta.ps1 nao encontrado em {0}; pule ou rode-o manualmente." -f $PSScriptRoot) -Nivel Alerta
    } else {
        & $scriptShare -CaminhoLocal $PastaEntrada -WhatIf:$WhatIfPreference
    }
} else {
    Write-Passo 'Pasta de entrada NAO compartilhada (rode compartilhar-pasta.ps1 como passo separado, ou use -CompartilharEntrada).' -Nivel Info
}

# --- 7. TLS 1.2 (opcional, com confirmacao) ---
if ($HabilitarTls12) {
    Write-Passo 'Habilitacao de TLS 1.2 Client solicitada (-HabilitarTls12).' -Nivel Alerta
    $tlsBase = 'HKLM:\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Client'
    if ($PSCmdlet.ShouldProcess($tlsBase, 'Habilitar TLS 1.2 Client (Enabled=1, DisabledByDefault=0)')) {
        if (-not (Test-Path -LiteralPath $tlsBase)) { [void](New-Item -Path $tlsBase -Force) }
        New-ItemProperty -Path $tlsBase -Name 'Enabled' -Value 1 -PropertyType DWord -Force | Out-Null
        New-ItemProperty -Path $tlsBase -Name 'DisabledByDefault' -Value 0 -PropertyType DWord -Force | Out-Null
        Write-Passo 'TLS 1.2 Client habilitado no SChannel. REVERSIVEL (ver docs). O SChannel pode exigir REINICIO do servidor.' -Nivel Ok

        # Chaves do .NET Framework (v4.0.30319): fazem processos .NET (como o Worker) negociarem
        # TLS 1.2 SEM depender do reboot do SChannel. Foram DECISIVAS em producao (2012 R2): sem
        # elas o GetToken HTTPS de producao falha com "Could not create SSL/TLS secure channel".
        foreach ($netKey in @(
            'HKLM:\SOFTWARE\Microsoft\.NETFramework\v4.0.30319',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\.NETFramework\v4.0.30319')) {
            if (-not (Test-Path -LiteralPath $netKey)) { [void](New-Item -Path $netKey -Force) }
            New-ItemProperty -Path $netKey -Name 'SchUseStrongCrypto' -Value 1 -PropertyType DWord -Force | Out-Null
            New-ItemProperty -Path $netKey -Name 'SystemDefaultTlsVersions' -Value 1 -PropertyType DWord -Force | Out-Null
        }
        Write-Passo 'Chaves .NET de TLS 1.2 (SchUseStrongCrypto, SystemDefaultTlsVersions) gravadas. Basta REINICIAR O SERVICO para o Worker usar TLS 1.2 (nao exige reboot da maquina).' -Nivel Ok
    }
} else {
    Write-Passo 'TLS 1.2 nao alterado. Se o handshake com o WMS falhar, rode novamente com -HabilitarTls12 (ver docs).' -Nivel Info
}

# --- 8. Inicio condicionado aos segredos ---
function Test-SegredosPresentes {
    $wmsUser = [Environment]::GetEnvironmentVariable('WMS__Usuario', 'Machine')
    $wmsSenha = [Environment]::GetEnvironmentVariable('WMS__Senha', 'Machine')
    $prodJson = Join-Path $pastaConfig 'appsettings.Production.json'
    $emailJson = Join-Path $pastaConfig 'email.settings.json'
    $temWmsEnv = ($wmsUser -and $wmsSenha)
    $temWmsArquivo = (Test-Path -LiteralPath $prodJson)
    return (($temWmsEnv -or $temWmsArquivo) -and (Test-Path -LiteralPath $emailJson))
}

if ($IniciarServico) {
    if (Test-SegredosPresentes) {
        if ($PSCmdlet.ShouldProcess($NomeServico, 'nssm start')) {
            [void](Invoke-Nssm -NssmPath $nssm -Argumentos @('start', $NomeServico) -TolerarFalha)
            Write-Passo ("Servico {0} iniciado." -f $NomeServico) -Nivel Ok
        }
    } else {
        Write-Passo 'Segredos ausentes (WMS e/ou email.settings.json). NAO iniciando o servico. Rode configurar-segredos.ps1 e depois servico.ps1 -Acao Start.' -Nivel Alerta
    }
} else {
    Write-Passo 'Instalacao concluida. Servico NAO iniciado (esperado). Rode configurar-segredos.ps1 e depois servico.ps1 -Acao Start.' -Nivel Ok
}
