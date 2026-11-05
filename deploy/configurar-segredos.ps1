<#
.SYNOPSIS
    Grava os segredos de PRODUCAO/VALIDACAO fora do git (WMS e SMTP), com ACL restrita.

.DESCRIPTION
    Roda NO SERVIDOR (RDP) ou via WinRM. NUNCA ecoa valores de segredo: as mensagens so confirmam
    "gravado" por nome de chave. Grava:
      - Credenciais do WMS: por padrao como variaveis de ambiente de MAQUINA (WMS__Usuario /
        WMS__Senha); alternativamente (-Escopo Arquivo) em config\appsettings.Production.json.
      - SMTP: sempre em config\email.settings.json (Host=smtp.friozem.com.br, Porta=587,
        UsarTls=false, Usuario/Senha/Remetente/Destinatarios, Habilitado=true).
    Aplica ACL para que apenas Administrators/SYSTEM e a conta do servico leiam config\.

    VIRADA Homolog -> Producao: rode de novo com -Fase Producao e os novos valores; depois reinicie
    o servico (servico.ps1 -Acao Restart). Nao reinstala.

.PARAMETER Fase
    Homolog (default) ou Producao — apenas rotula o que esta sendo gravado (nao muda o destino).

.PARAMETER RaizInstalacao
    Raiz do app (default C:\Friozem\importacao_notas).

.PARAMETER NomeServico
    Nome do servico (default Friozem-ImportacaoNotas) — usado so em mensagens.

.PARAMETER ContaServico
    Conta do servico para a ACL (default 'NT AUTHORITY\SYSTEM').

.PARAMETER Escopo
    Maquina (default) grava WMS como env de maquina; Arquivo grava no appsettings.Production.json.

.PARAMETER WmsUsuario / WmsSenha
    Credenciais do WMS (SecureString). Se omitidos, sao pedidos via Read-Host -AsSecureString.

.PARAMETER SmtpUsuario / SmtpSenha
    Credenciais do SMTP (SecureString). Se omitidos, sao pedidos via Read-Host -AsSecureString.

.PARAMETER SmtpRemetente / SmtpDestinatarios
    Remetente e destinatarios (separados por ';') do e-mail de fechamento.

.EXAMPLE
    # LOCAL (RDP), interativo:
    powershell -ExecutionPolicy Bypass -File .\configurar-segredos.ps1 -Fase Homolog

.EXAMPLE
    # REMOTO (WinRM): passe SecureString ja criados do lado de quem invoca.
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('Homolog', 'Producao')] [string] $Fase = 'Homolog',
    [string] $RaizInstalacao = 'C:\Friozem\importacao_notas',
    [string] $NomeServico = 'Friozem-ImportacaoNotas',
    [string] $ContaServico = 'NT AUTHORITY\SYSTEM',
    [ValidateSet('Maquina', 'Arquivo')] [string] $Escopo = 'Maquina',
    [securestring] $WmsUsuario,
    [securestring] $WmsSenha,
    [securestring] $SmtpUsuario,
    [securestring] $SmtpSenha,
    [string] $SmtpRemetente,
    [string] $SmtpDestinatarios
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_comum.ps1')

$pastaConfig = Join-Path $RaizInstalacao 'config'
[void](Confirm-Directory -Caminho $pastaConfig)

Write-Passo ("Fase: {0}  |  Escopo WMS: {1}  |  config\: {2}" -f $Fase, $Escopo, $pastaConfig)
Write-Passo 'Os valores NUNCA sao exibidos. Entradas vazias cancelam a gravacao daquele item.'

function ConvertFrom-Secure([securestring] $s) {
    if (-not $s) { return $null }
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

# --- Coleta segura (sem eco) ---
if (-not $WmsUsuario) { $WmsUsuario = Read-Host -AsSecureString -Prompt 'WMS Usuario (vazio = pular)' }
if (-not $WmsSenha)   { $WmsSenha   = Read-Host -AsSecureString -Prompt 'WMS Senha   (vazio = pular)' }
if (-not $SmtpUsuario){ $SmtpUsuario= Read-Host -AsSecureString -Prompt 'SMTP Usuario (vazio = pular)' }
if (-not $SmtpSenha)  { $SmtpSenha  = Read-Host -AsSecureString -Prompt 'SMTP Senha   (vazio = pular)' }
if (-not $SmtpRemetente)     { $SmtpRemetente     = Read-Host -Prompt 'SMTP Remetente (ex. kitwms@friozem.com.br; vazio = pular)' }
if (-not $SmtpDestinatarios) { $SmtpDestinatarios = Read-Host -Prompt 'Destinatarios do fechamento, separados por ; (vazio = pular)' }

$wmsUserTxt  = ConvertFrom-Secure $WmsUsuario
$wmsSenhaTxt = ConvertFrom-Secure $WmsSenha
$smtpUserTxt = ConvertFrom-Secure $SmtpUsuario
$smtpSenhaTxt= ConvertFrom-Secure $SmtpSenha

# --- 1. WMS ---
if ($wmsUserTxt -and $wmsSenhaTxt) {
    if ($Escopo -eq 'Maquina') {
        if ($PSCmdlet.ShouldProcess('WMS__Usuario / WMS__Senha (env de Maquina)', 'Gravar')) {
            [Environment]::SetEnvironmentVariable('WMS__Usuario', $wmsUserTxt, 'Machine')
            [Environment]::SetEnvironmentVariable('WMS__Senha', $wmsSenhaTxt, 'Machine')
            Write-Passo 'Credenciais do WMS gravadas como env de MAQUINA (WMS__Usuario / WMS__Senha).' -Nivel Ok
            Write-Passo 'O servico precisa ser REINICIADO para herdar a env (servico.ps1 -Acao Restart).' -Nivel Info
        }
    } else {
        $prodJson = Join-Path $pastaConfig 'appsettings.Production.json'
        if ($PSCmdlet.ShouldProcess($prodJson, 'Gravar Wms.Usuario/Wms.Senha em arquivo')) {
            $obj = if (Test-Path -LiteralPath $prodJson) {
                Get-Content -LiteralPath $prodJson -Raw | ConvertFrom-Json
            } else { [pscustomobject]@{} }
            if (-not ($obj.PSObject.Properties.Name -contains 'Wms')) {
                $obj | Add-Member -NotePropertyName 'Wms' -NotePropertyValue ([pscustomobject]@{}) -Force
            }
            $obj.Wms | Add-Member -NotePropertyName 'Usuario' -NotePropertyValue $wmsUserTxt -Force
            $obj.Wms | Add-Member -NotePropertyName 'Senha' -NotePropertyValue $wmsSenhaTxt -Force
            ($obj | ConvertTo-Json -Depth 20) | Set-Content -LiteralPath $prodJson -Encoding UTF8
            Write-Passo ("Credenciais do WMS gravadas em {0} (chaves Wms.Usuario/Wms.Senha)." -f $prodJson) -Nivel Ok
        }
    }
} else {
    Write-Passo 'WMS: usuario/senha nao informados — pulado.' -Nivel Info
}

# --- 2. SMTP (email.settings.json) ---
if ($smtpUserTxt -and $smtpSenhaTxt) {
    $emailJson = Join-Path $pastaConfig 'email.settings.json'
    if ($PSCmdlet.ShouldProcess($emailJson, 'Gravar bloco Email (SMTP 587 sem TLS)')) {
        $remetenteFinal = if ($SmtpRemetente) { $SmtpRemetente } else { $smtpUserTxt }
        $destinatariosFinal = if ($SmtpDestinatarios) { $SmtpDestinatarios } else { '' }
        $email = [ordered]@{
            Habilitado      = $true
            Host            = 'smtp.friozem.com.br'
            Porta           = 587
            UsarTls         = $false
            Usuario         = $smtpUserTxt
            Senha           = $smtpSenhaTxt
            Remetente       = $remetenteFinal
            RemetenteNome   = 'KitWms Integrador'
            Destinatarios   = $destinatariosFinal
            AssuntoPrefixo  = '[KitWms]'
            EnviarQuando    = 'Sempre'
            TimeoutSegundos = 60
        }
        $wrapper = [ordered]@{ Email = $email }
        ($wrapper | ConvertTo-Json -Depth 10) | Set-Content -LiteralPath $emailJson -Encoding UTF8
        Write-Passo ("SMTP gravado em {0} (Host/Porta/UsarTls + Usuario/Senha ocultos)." -f $emailJson) -Nivel Ok
    }
} else {
    Write-Passo 'SMTP: usuario/senha nao informados — pulado.' -Nivel Info
}

# --- 3. ACL restrita ---
Write-Passo 'Aplicando ACL restrita em config\...'
Set-ConfigAcl -Caminho $pastaConfig -ContaServico $ContaServico

# --- 4. Limpa texto claro da memoria desta sessao ---
$wmsUserTxt = $wmsSenhaTxt = $smtpUserTxt = $smtpSenhaTxt = $null
[GC]::Collect()

Write-Host ''
Write-Passo ("Segredos da fase {0} gravados. Virada para producao: rode de novo com -Fase Producao e reinicie o servico." -f $Fase) -Nivel Ok
Write-Passo 'Lembre: se WMS ficou em env de MAQUINA, o appsettings.Production.json NAO carrega as credenciais — o servico le da env.' -Nivel Info
