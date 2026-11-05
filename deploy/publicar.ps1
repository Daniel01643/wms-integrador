<#
.SYNOPSIS
    Publica o Worker (self-contained, win-x64) em staging versionado e gera o zip de release.

.DESCRIPTION
    Roda na MAQUINA DE BUILD (nao no servidor). Faz `dotnet publish` do Worker em Release,
    win-x64, self-contained (runtime embutido — blinda contra mudanca de runtime no servidor).
    Gera a pasta artifacts\<versao>\app e o zip artifacts\<versao>\KitWmsIntegrador-<versao>-win-x64.zip.

    PublishSingleFile NAO e o padrao (D2 do plano): o Core depende de PdfSharpCore/ImageSharp, cuja
    resolucao de recursos pode falhar sob extracao single-file no 2012 R2. Use -SingleFile por sua
    conta e risco. Trimming/ReadyToRun NAO sao usados (quebram reflection de DI/Options).

    Framework-dependent tambem seria viavel (o servidor ja tem .NET 8), mas o pacote usa
    self-contained por seguranca. Ver docs\INSTALACAO-SERVIDOR.md.

.PARAMETER Versao
    Versao da release (default derivada: yyyy.MM.dd-<hash git> ou yyyy.MM.dd.HHmm).

.PARAMETER Configuracao
    Configuracao de build (default Release).

.PARAMETER Runtime
    RID de runtime (default win-x64).

.PARAMETER SelfContained
    Publicar com runtime embutido (default $true).

.PARAMETER SingleFile
    Publicar como arquivo unico (default $false — ver ressalva ImageSharp).

.PARAMETER SaidaStaging
    Pasta base de staging (default <repo>\artifacts). O artefato vai para <SaidaStaging>\<versao>.

.PARAMETER Zip
    Gerar o zip da release (default $true).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\publicar.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\publicar.ps1 -Versao 2026.01.15-1 -SingleFile
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $Versao,
    [string] $Configuracao = 'Release',
    [string] $Runtime = 'win-x64',
    [bool]   $SelfContained = $true,
    [switch] $SingleFile,
    [string] $SaidaStaging,
    [bool]   $Zip = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_comum.ps1')

# Raiz do repositorio = pasta pai de deploy\.
$raizRepo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
$csproj = Join-Path $raizRepo 'src\KitWmsIntegrador.Worker\KitWmsIntegrador.Worker.csproj'
if (-not (Test-Path -LiteralPath $csproj)) {
    throw ("Worker.csproj nao encontrado em {0}" -f $csproj)
}

if (-not $Versao) { $Versao = Get-VersaoPadrao -RaizRepo $raizRepo }
if (-not $SaidaStaging) { $SaidaStaging = Join-Path $raizRepo 'artifacts' }

$pastaVersao = Join-Path $SaidaStaging $Versao
$pastaApp = Join-Path $pastaVersao 'app'

Write-Passo ("Versao        : {0}" -f $Versao)
Write-Passo ("Runtime       : {0} (self-contained={1}, single-file={2})" -f $Runtime, $SelfContained, [bool]$SingleFile)
Write-Passo ("Saida staging : {0}" -f $pastaApp)

# Idempotente: limpa a saida desta versao antes de republicar.
if (Test-Path -LiteralPath $pastaVersao) {
    if ($PSCmdlet.ShouldProcess($pastaVersao, 'Remover saida anterior da versao')) {
        Remove-Item -LiteralPath $pastaVersao -Recurse -Force
    }
}
[void](Confirm-Directory -Caminho $pastaApp)

# Monta argumentos do dotnet publish.
$args = @(
    'publish', $csproj,
    '-c', $Configuracao,
    '-r', $Runtime,
    "--self-contained", $SelfContained.ToString().ToLowerInvariant(),
    '-o', $pastaApp,
    "/p:PublishSingleFile=$([bool]$SingleFile)",
    '--nologo'
)

Write-Passo ("Executando: dotnet {0}" -f ($args -join ' '))
if ($PSCmdlet.ShouldProcess($pastaApp, 'dotnet publish')) {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw ("dotnet publish falhou (exit {0})." -f $LASTEXITCODE) }

    $exe = Join-Path $pastaApp 'KitWmsIntegrador.Worker.exe'
    if (-not (Test-Path -LiteralPath $exe)) {
        throw ("Publish concluiu mas {0} nao foi gerado." -f $exe)
    }
    Write-Passo ("Executavel gerado: {0}" -f $exe) -Nivel Ok

    $tamPasta = (Get-ChildItem -LiteralPath $pastaApp -Recurse -File | Measure-Object -Property Length -Sum).Sum
    Write-Passo ("Tamanho da pasta publicada: {0:N1} MB ({1} arquivos)" -f ($tamPasta / 1MB), (Get-ChildItem -LiteralPath $pastaApp -Recurse -File).Count) -Nivel Ok

    if ($Zip) {
        $zipPath = Join-Path $pastaVersao ("KitWmsIntegrador-{0}-{1}.zip" -f $Versao, $Runtime)
        if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
        Write-Passo ("Compactando release -> {0}" -f $zipPath)
        # Helper portavel (System.IO.Compression): mesmo layout de Compress-Archive -Path *,
        # i.e. conteudo de app\ na raiz do zip. Casa com Expand-ZipPortavel no servidor (PS 4.0).
        Compress-ZipPortavel -Origem $pastaApp -ZipPath $zipPath
        $tamZip = (Get-Item -LiteralPath $zipPath).Length
        Write-Passo ("Zip gerado: {0} ({1:N1} MB)" -f $zipPath, ($tamZip / 1MB)) -Nivel Ok
    }

    Write-Host ''
    Write-Passo ("Publish OK. Artefato em: {0}" -f $pastaVersao) -Nivel Ok
    Write-Host ''
    Write-Host 'Proximo passo: copie o zip para o servidor e rode instalar.ps1 (ver docs\INSTALACAO-SERVIDOR.md).'
}
