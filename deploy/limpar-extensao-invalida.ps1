<#
.SYNOPSIS
    Limpeza FORCADA sob demanda das pastas de erro `ExtensaoInvalida` dentro da Entrada do
    KitWmsIntegrador (os arquivos NAO-XML recusados na deteccao).

.DESCRIPTION
    Roda NO SERVIDOR (RDP) ou via WinRM. Remove o conteudo e a propria pasta de cada
    `Erro\<pacote_...>\ExtensaoInvalida` encontrada sob a Entrada informada, sem esperar o ciclo
    automatico de limpeza de 30 dias. E IDEMPOTENTE (rodar de novo sem itens apenas informa que nao
    ha nada a remover) e suporta `-WhatIf` (lista o que faria e NAO apaga nada).

    GUARDAS DE SEGURANCA (so apaga DENTRO de <Entrada>\Erro, nunca fora):
      - recusa `-Caminho` que seja raiz de drive;
      - resolve caminhos absolutos com [IO.Path]::GetFullPath antes de comparar;
      - so remove uma pasta se o caminho completo COMECA com (<Entrada>\Erro + separador) E o nome
        da pasta folha e exatamente -NomeCategoria (comparacao case-insensitive);
      - nunca segue reparse points (junctions/symlinks): pastas com o atributo ReparsePoint sao
        ignoradas com alerta;
      - se <Entrada>\Erro nao existir, nada a fazer (sai sem erro).

    Alem do `ShouldProcess` nativo, exibe um resumo do que sera apagado e, quando NAO em `-WhatIf`
    e sem `-Force`, pede confirmacao interativa (s/n).

    Este script e replicado automaticamente para `<RaizInstalacao>\tools\deploy` pelo
    `instalar.ps1` (via `Copy-ScriptsDeployParaServidor`, que copia todos os `.ps1` de `deploy\`),
    entao opere-o do servidor a partir dessa pasta (RDP).

    Compativel com PowerShell 4.0/5.1 (Windows Server 2012 R2).

.PARAMETER Caminho
    Pasta de Entrada (`Pastas:Entrada`). Default `C:\Friozem\kitbrf` (producao). A pasta de erro
    e resolvida como <Caminho>\<NomePastaErro>.

.PARAMETER NomePastaErro
    Nome da subpasta de erro dentro da Entrada. Default `Erro`.

.PARAMETER NomeCategoria
    Nome da pasta-folha de categoria a limpar. Default `ExtensaoInvalida`.

.PARAMETER Force
    Pula a confirmacao interativa (nao tem efeito sob `-WhatIf`).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\limpar-extensao-invalida.ps1 -WhatIf
    Mostra quais pastas ExtensaoInvalida seriam limpas, sem apagar nada.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\limpar-extensao-invalida.ps1
    Lista o que sera apagado e pede confirmacao (s/n) antes de remover.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\limpar-extensao-invalida.ps1 -Caminho D:\dados\kitbrf -Force
    Limpa sem perguntar, numa Entrada alternativa.
#>

[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string] $Caminho = 'C:\Friozem\kitbrf',
    [string] $NomePastaErro = 'Erro',
    [string] $NomeCategoria = 'ExtensaoInvalida',
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_comum.ps1')

# -Force pula tanto a confirmacao interativa custom quanto o prompt nativo do ShouldProcess
# (ConfirmImpact High). Sem -Force (e fora de -WhatIf) o operador confirma com s/n.
if ($Force) { $ConfirmPreference = 'None' }

# --- Guardas de caminho ---------------------------------------------------------------------

if ([string]::IsNullOrWhiteSpace($Caminho)) {
    throw '-Caminho vazio.'
}

# Resolve o caminho absoluto da Entrada (mesmo que ainda nao exista fisicamente).
$entradaFull = [IO.Path]::GetFullPath($Caminho)

# Recusa raiz de drive (ex. 'C:\'): o diretorio pai de uma raiz e nulo/vazio.
$paiEntrada = [IO.Path]::GetDirectoryName($entradaFull)
if ([string]::IsNullOrEmpty($paiEntrada)) {
    throw ("-Caminho nao pode ser a raiz de um drive: {0}" -f $entradaFull)
}

$raizErro = [IO.Path]::GetFullPath((Join-Path $entradaFull $NomePastaErro))

# Prefixo exigido: <Entrada>\Erro + separador (garante que so toquemos ITENS DENTRO de Erro).
$sep = [IO.Path]::DirectorySeparatorChar
$prefixoErro = $raizErro.TrimEnd($sep) + $sep

if (-not (Test-Path -LiteralPath $raizErro -PathType Container)) {
    Write-Passo ("Pasta de erro inexistente: {0}. Nada a remover." -f $raizErro) -Nivel Info
    return
}

function Test-ReparsePoint {
    <#
    .SYNOPSIS Retorna $true se o item e um reparse point (junction/symlink) — a NAO seguir.
    #>
    param([Parameter(Mandatory)] [string] $Item)
    try {
        $attr = (Get-Item -LiteralPath $Item -Force).Attributes
        return (($attr -band [IO.FileAttributes]::ReparsePoint) -eq [IO.FileAttributes]::ReparsePoint)
    } catch {
        # Em caso de duvida, tratar como reparse point (nao mexer).
        return $true
    }
}

# --- Coleta das pastas candidatas ----------------------------------------------------------

Write-Passo ("Procurando pastas '{0}' sob {1} ..." -f $NomeCategoria, $raizErro) -Nivel Info

# Nao seguimos a raiz de Erro se ela mesma for um reparse point.
if (Test-ReparsePoint -Item $raizErro) {
    Write-Passo ("{0} e um reparse point (junction/symlink); ignorado por seguranca." -f $raizErro) -Nivel Alerta
    return
}

$todas = Get-ChildItem -LiteralPath $raizErro -Directory -Recurse -Force -ErrorAction SilentlyContinue

$candidatas = @()
foreach ($d in $todas) {
    # (c) nome da folha exatamente == categoria (case-insensitive).
    if ($d.Name -ine $NomeCategoria) { continue }

    $alvoFull = [IO.Path]::GetFullPath($d.FullName)

    # (c) caminho completo COMECA com (<Entrada>\Erro + separador).
    if (-not $alvoFull.StartsWith($prefixoErro, [StringComparison]::OrdinalIgnoreCase)) {
        Write-Passo ("Ignorado (fora de {0}): {1}" -f $raizErro, $alvoFull) -Nivel Alerta
        continue
    }

    # (d) nao seguir reparse points.
    if (Test-ReparsePoint -Item $alvoFull) {
        Write-Passo ("Ignorado (reparse point): {0}" -f $alvoFull) -Nivel Alerta
        continue
    }

    $candidatas += $alvoFull
}

if ($candidatas.Count -eq 0) {
    Write-Passo ("Nenhuma pasta '{0}' encontrada sob {1}. Nada a remover." -f $NomeCategoria, $raizErro) -Nivel Info
    return
}

# --- Resumo do que sera apagado ------------------------------------------------------------

$totalArquivos = 0
Write-Passo ("{0} pasta(s) '{1}' encontrada(s):" -f $candidatas.Count, $NomeCategoria) -Nivel Alerta
foreach ($p in $candidatas) {
    $qtd = (Get-ChildItem -LiteralPath $p -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object).Count
    $totalArquivos += $qtd
    Write-Host ("    - {0}  ({1} arquivo(s))" -f $p, $qtd)
}
Write-Passo ("Total: {0} arquivo(s) em {1} pasta(s)." -f $totalArquivos, $candidatas.Count) -Nivel Alerta

# --- Confirmacao interativa (fora de -WhatIf e sem -Force) ----------------------------------

if (-not $WhatIfPreference -and -not $Force) {
    $resposta = Read-Host 'Confirma a remocao dessas pastas ExtensaoInvalida? (s/n)'
    if ($resposta -notmatch '^[sS]') {
        Write-Passo 'Cancelado pelo operador. Nada foi removido.' -Nivel Info
        return
    }
}

# --- Remocao ------------------------------------------------------------------------------

$removidas = 0
foreach ($p in $candidatas) {
    if ($PSCmdlet.ShouldProcess($p, 'Remover conteudo de ExtensaoInvalida')) {
        Remove-Item -LiteralPath $p -Recurse -Force
        Write-Passo ("Removido: {0}" -f $p) -Nivel Ok
        $removidas++
    }
}

if ($WhatIfPreference) {
    Write-Passo ("(WhatIf) {0} pasta(s) seriam removidas ({1} arquivo(s))." -f $candidatas.Count, $totalArquivos) -Nivel Info
} else {
    Write-Passo ("Limpeza concluida: {0} pasta(s) removida(s)." -f $removidas) -Nivel Ok
}
