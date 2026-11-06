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
# Detecta pelo Gerenciador de Servicos do Windows (confiavel), nao pelo exit code do nssm
    # (que, sob WinRM/PS 4.0, nem sempre e capturado e levava a "Can't open service!").
    $svc = Get-Service -Name $NomeServico -ErrorAction SilentlyContinue
    return ($null -ne $svc)
}

function Set-NssmParam {
    <#
    .SYNOPSIS Define um parametro do servico via "nssm set" de forma IDEMPOTENTE
              (le o valor atual e so grava se diferente). Respeita -WhatIf do chamador.
    .PARAMETER SubParametro
        Sub-token opcional de parametros NSSM com multiplos argumentos (ex. 'AppExit Default' vira
        -Parametro 'AppExit' -SubParametro 'Default'). Quando informado, o get/set usa
        'nssm (get|set) <svc> <Parametro> <SubParametro> [<Valor>]' com tokens SEPARADOS, como a
        sintaxe do NSSM exige.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)] [string] $NssmPath,
        [Parameter(Mandatory)] [string] $NomeServico,
        [Parameter(Mandatory)] [string] $Parametro,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Valor,
        [string] $SubParametro
    )

    $argsGet = @('get', $NomeServico, $Parametro)
    $argsSet = @('set', $NomeServico, $Parametro)
    $rotulo  = $Parametro
    if ($SubParametro) {
        $argsGet += $SubParametro
        $argsSet += $SubParametro
        $rotulo   = "$Parametro $SubParametro"
    }
    $argsSet += $Valor

    $atual = ''
    try {
        $atual = (Invoke-Nssm -NssmPath $NssmPath -Argumentos $argsGet -TolerarFalha)
    } catch {
        $atual = ''
    }

    if ($atual -eq $Valor) {
        Write-Passo ("NSSM {0} ja = '{1}' (sem alteracao)" -f $rotulo, $Valor) -Nivel Info
        return
    }

    if ($PSCmdlet.ShouldProcess("$NomeServico/$rotulo", "nssm set -> '$Valor'")) {
        [void](Invoke-Nssm -NssmPath $NssmPath -Argumentos $argsSet)
        Write-Passo ("NSSM {0} definido = '{1}'" -f $rotulo, $Valor) -Nivel Ok
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

function Expand-ZipPortavel {
    <#
    .SYNOPSIS Expande um .zip de forma portavel (PowerShell 4.0+, sem Expand-Archive).
    .DESCRIPTION
        Usa [System.IO.Compression.ZipFile] do .NET (4.5+, presente no Server 2012 R2), pois
        Expand-Archive so existe no PowerShell 5.0+. IDEMPOTENTE: se o destino ja existir com
        conteudo, ele e limpo antes de extrair (equivale ao -Force de Expand-Archive), evitando a
        excecao de ExtractToDirectory quando um arquivo alvo ja existe. Preserva o layout do zip
        (arquivos na raiz do zip saem direto na raiz do destino). Respeita -WhatIf do chamador.
    .PARAMETER ZipPath  Caminho do .zip de origem.
    .PARAMETER Destino  Pasta de destino (criada se faltar; limpa se ja tiver conteudo).
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)] [string] $ZipPath,
        [Parameter(Mandatory)] [string] $Destino
    )

    if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) {
        throw ("Expand-ZipPortavel: zip inexistente: {0}" -f $ZipPath)
    }

    if (-not $PSCmdlet.ShouldProcess($Destino, ("Expandir zip '{0}'" -f $ZipPath))) {
        return
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    # Idempotente: recria o destino vazio antes de extrair (equivalente ao -Force).
    if (Test-Path -LiteralPath $Destino) {
        Remove-Item -LiteralPath $Destino -Recurse -Force
    }
    [void](New-Item -ItemType Directory -Path $Destino -Force)

    $destinoFull = (Resolve-Path -LiteralPath $Destino).ProviderPath
    [System.IO.Compression.ZipFile]::ExtractToDirectory($ZipPath, $destinoFull)
    Write-Passo ("Zip expandido: {0} -> {1}" -f $ZipPath, $destinoFull) -Nivel Ok
}

function Compress-ZipPortavel {
    <#
    .SYNOPSIS Compacta uma pasta em .zip de forma portavel (PowerShell 4.0+, sem Compress-Archive).
    .DESCRIPTION
        Usa [System.IO.Compression.ZipFile] do .NET (4.5+, presente no Server 2012 R2), pois
        Compress-Archive so existe no PowerShell 5.0+. Mantem o MESMO layout do pacote de hoje:
        o CONTEUDO de -Origem vai para a RAIZ do zip (nao dentro de uma subpasta), de modo que a
        expansao coloque os binarios direto em app\releases\<versao>\. Se o zip ja existir, e
        removido antes (CreateFromDirectory falha se o destino existir). Respeita -WhatIf.
    .PARAMETER Origem   Pasta cujo conteudo sera compactado (vai para a raiz do zip).
    .PARAMETER ZipPath  Caminho do .zip a gerar.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)] [string] $Origem,
        [Parameter(Mandatory)] [string] $ZipPath
    )

    if (-not (Test-Path -LiteralPath $Origem -PathType Container)) {
        throw ("Compress-ZipPortavel: pasta de origem inexistente: {0}" -f $Origem)
    }

    if (-not $PSCmdlet.ShouldProcess($ZipPath, ("Compactar pasta '{0}'" -f $Origem))) {
        return
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -LiteralPath $ZipPath -Force
    }

    $origemFull = (Resolve-Path -LiteralPath $Origem).ProviderPath
    # includeBaseDirectory=$false -> o CONTEUDO da pasta vai para a raiz do zip (mesmo layout de
    # 'Compress-Archive -Path (Join-Path $pasta *)').
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $origemFull, $ZipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    Write-Passo ("Zip gerado: {0}" -f $ZipPath) -Nivel Ok
}

function Copy-ScriptsDeployParaServidor {
    <#
    .SYNOPSIS Copia (idempotente) todos os .ps1 de deploy\ para uma pasta PERMANENTE no servidor,
              para que os scripts de operacao vivam junto da instalacao (RDP, sem depender de C:\temp).
    .DESCRIPTION
        Fonte = a pasta onde o script chamador esta sendo executado ($PSScriptRoot). Copia todos os
        .ps1 (inclusive _comum.ps1 e este conjunto) para -PastaDestino, SOBRESCREVENDO a cada
        execucao (mantem a versao mais nova dos scripts junto da release). Como os demais scripts
        fazem dot-source de _comum.ps1 por $PSScriptRoot, rodando de -PastaDestino eles continuam
        achando o _comum.ps1 ao lado. NAO apaga outros conteudos de tools\ (so gerencia esta
        subpasta). Se a fonte e o destino forem a MESMA pasta (ja rodando de tools\deploy), nao faz
        nada. Respeita -WhatIf do chamador.
    .PARAMETER PastaOrigem  Pasta de origem dos .ps1 (tipicamente $PSScriptRoot do chamador).
    .PARAMETER PastaDestino Pasta PERMANENTE de destino no servidor (ex. ...\tools\deploy).
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)] [string] $PastaOrigem,
        [Parameter(Mandatory)] [string] $PastaDestino
    )

    if (-not (Test-Path -LiteralPath $PastaOrigem -PathType Container)) {
        throw ("Copy-ScriptsDeployParaServidor: pasta de origem inexistente: {0}" -f $PastaOrigem)
    }

    [void](Confirm-Directory -Caminho $PastaDestino)

    $origemFull  = (Resolve-Path -LiteralPath $PastaOrigem).ProviderPath
    $destinoFull = (Resolve-Path -LiteralPath $PastaDestino).ProviderPath

    # Se ja estamos rodando de dentro do destino, nao ha o que copiar.
    if ($origemFull.TrimEnd('\') -ieq $destinoFull.TrimEnd('\')) {
        Write-Passo ("Scripts de deploy ja estao na pasta permanente: {0} (sem copia)." -f $destinoFull) -Nivel Info
        return
    }

    $scripts = Get-ChildItem -LiteralPath $origemFull -Filter '*.ps1' -File -ErrorAction SilentlyContinue
    if (-not $scripts) {
        Write-Passo ("Nenhum .ps1 encontrado em {0}; nada copiado." -f $origemFull) -Nivel Alerta
        return
    }

    foreach ($s in $scripts) {
        $destino = Join-Path $destinoFull $s.Name
        if ($PSCmdlet.ShouldProcess($destino, ("Copiar script de deploy '{0}'" -f $s.Name))) {
            Copy-Item -LiteralPath $s.FullName -Destination $destino -Force
        }
    }
    Write-Passo ("Scripts de deploy copiados (idempotente) para: {0}" -f $destinoFull) -Nivel Ok
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
