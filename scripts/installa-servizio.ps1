<#
.SYNOPSIS
  Installa CodexVTT Server come servizio di Windows (parte all'avvio del PC, funziona anche con la pagina chiusa).
.DESCRIPTION
  Pubblica il server (con la UI), lo copia in una cartella e registra il servizio "CodexVTT".
  Richiede PowerShell come amministratore. Il servizio gira con l'account indicato (default: quello che esegue
  lo script), perché modelli, impostazioni e cronologia stanno nel profilo di quell'account.
.EXAMPLE
  .\scripts\installa-servizio.ps1 -Urls "http://0.0.0.0:5185" -Token "una-frase-lunga-e-segreta"
#>
[CmdletBinding()]
param(
    [string]$Destinazione = "$env:ProgramFiles\CodexVTT Server",
    [string]$Urls = 'http://127.0.0.1:5185',
    [string]$Token,
    [string]$Account = "$env:USERDOMAIN\$env:USERNAME",
    [switch]$Disinstalla
)

$ErrorActionPreference = 'Stop'
$nome = 'CodexVTT'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw 'Esegui PowerShell come amministratore.' }

if ($Disinstalla) {
    if (Get-Service $nome -ErrorAction SilentlyContinue) {
        Stop-Service $nome -ErrorAction SilentlyContinue
        sc.exe delete $nome | Out-Null
        Write-Host "Servizio $nome rimosso. I dati (modelli, cronologia) non sono stati toccati."
    }
    return
}

$repo = Split-Path -Parent $PSScriptRoot
if ($Urls -notmatch '^http://(127\.0\.0\.1|localhost|\[::1\])' -and -not $Token) {
    throw 'Per ascoltare su un indirizzo raggiungibile da altri PC serve -Token (altrimenti il server rifiuta tutto tranne localhost).'
}

Push-Location (Join-Path $repo 'ui')
try {
    npm ci
    npm run build:server
}
finally { Pop-Location }

if (Get-Service $nome -ErrorAction SilentlyContinue) {
    Stop-Service $nome -ErrorAction SilentlyContinue
}
dotnet publish (Join-Path $repo 'src\CodexVtt.Server\CodexVtt.Server.csproj') -c Release -r win-x64 --self-contained -o $Destinazione
if ($LASTEXITCODE -ne 0) { throw 'Pubblicazione non riuscita.' }

if (-not (Get-Service $nome -ErrorAction SilentlyContinue)) {
    $credenziali = Get-Credential -UserName $Account -Message "Password dell'account con cui gira il servizio (i dati stanno nel suo profilo)"
    New-Service -Name $nome -DisplayName 'CodexVTT Server' -BinaryPathName "`"$Destinazione\CodexVtt.Server.exe`"" `
        -StartupType Automatic -Credential $credenziali -Description 'Trascrizione locale con Whisper, interfaccia web.' | Out-Null
}
else {
    Write-Warning "Il servizio esiste già: aggiornati solo i file. Per cambiare l'account usa -Disinstalla e reinstalla."
}

# Indirizzo e token in variabili d'ambiente del servizio (non nella riga di comando, visibile a tutti).
# "Urls" (non ASPNETCORE_URLS): appsettings.json ha lo stesso nome e avrebbe la precedenza su ASPNETCORE_*.
$ambiente = @("Urls=$Urls")
if ($Token) { $ambiente += "CodexVtt__AccessToken=$Token" }
Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$nome" -Name Environment -Value $ambiente -Type MultiString
sc.exe failure $nome reset= 86400 actions= restart/5000/restart/30000// | Out-Null

Start-Service $nome
Write-Host "CodexVTT Server attivo su $Urls"
if ($Token) { Write-Host 'Prima apertura da un altro PC: <indirizzo>/?token=<il tuo token> (poi il browser lo ricorda).' }
