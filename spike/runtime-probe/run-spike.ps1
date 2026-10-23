<#
.SYNOPSIS
  Spike ADR-0002: trascrizione Whisper locale e offline su Windows con Whisper.net.

.DESCRIPTION
  Fase online (solo se richiesta): scarica il modello GGML e il campione audio di riferimento.
  Fase offline: chiede di disattivare la rete, verifica che non ci sia connettività,
  esegue la trascrizione e un test di annullamento, salva i report JSON in .\results.

.EXAMPLE
  .\run-spike.ps1 -Model base
  .\run-spike.ps1 -Model small -Audio C:\audio\prova.mp3 -Language it

.EXAMPLE
  .\run-spike.ps1 -SkipDownload -AssumeOffline   # senza console interattiva: salta solo l'attesa dell'Invio, il controllo di connettività resta
#>
[CmdletBinding()]
param(
    [ValidateSet('tiny', 'base', 'small', 'medium', 'large-v3-turbo', 'large-v3')]
    [string]$Model = 'base',
    [string]$Audio,
    [string]$Language = 'auto',
    [string]$ModelsDir = (Join-Path $env:LOCALAPPDATA 'CodexVTT-spike\models'),
    [switch]$SkipDownload,
    [switch]$SkipOfflineCheck,
    [switch]$AssumeOffline
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$results = Join-Path $here 'results'
New-Item -ItemType Directory -Force -Path $ModelsDir, $results | Out-Null

function Write-Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }

Write-Step 'Prerequisiti'
$os = Get-CimInstance Win32_OperatingSystem
Write-Host "Windows: $($os.Caption) build $($os.BuildNumber) ($env:PROCESSOR_ARCHITECTURE)"
if ([int]$os.BuildNumber -lt 22000) {
    Write-Warning 'Whisper.net 1.9.x dichiara come requisito Windows 11 / Server 2022. Annotare il risultato nel report.'
}
$vc = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64' -ErrorAction SilentlyContinue
if ($vc -and $vc.Installed -eq 1) { Write-Host "VC++ Redistributable x64: $($vc.Version)" }
else { Write-Warning 'VC++ Redistributable x64 non trovato: il runtime nativo potrebbe non caricarsi.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK 10 non trovato (https://dot.net).' }
$ram = [math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
Write-Host "RAM: $ram GB - CPU: $((Get-CimInstance Win32_Processor | Select-Object -First 1).Name)"
Get-CimInstance Win32_VideoController | ForEach-Object { Write-Host "GPU: $($_.Name)" }

$modelFile = Join-Path $ModelsDir "ggml-$Model.bin"
$jfk = Join-Path $ModelsDir 'jfk.wav'

if (-not $SkipDownload) {
    Write-Step 'Fase online: download richiesto esplicitamente'
    if (-not (Test-Path $modelFile)) {
        $url = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-$Model.bin"
        Write-Host "Scarico $url"
        Invoke-WebRequest -Uri $url -OutFile "$modelFile.part" -UseBasicParsing
        Move-Item "$modelFile.part" $modelFile -Force
    }
    if (-not $Audio -and -not (Test-Path $jfk)) {
        Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/ggml-org/whisper.cpp/master/samples/jfk.wav' -OutFile $jfk -UseBasicParsing
    }
}

$sha = (Get-FileHash $modelFile -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Modello: $modelFile ($([math]::Round((Get-Item $modelFile).Length / 1MB)) MB) sha256=$sha"

Write-Step 'Build del probe'
dotnet build (Join-Path $here 'RuntimeProbe.csproj') -c Release -nologo | Out-Host
$probe = Join-Path $here 'bin\Release\net10.0\codexvtt-runtime-probe.exe'

if ($Audio) {
    $ffmpeg = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if (-not $ffmpeg) { throw 'Per audio diversi da WAV 16 kHz mono serve ffmpeg nel PATH (solo per lo spike).' }
    $wav = Join-Path $results 'input-16k.wav'
    & $ffmpeg.Source -loglevel error -y -i $Audio -ar 16000 -ac 1 -c:a pcm_s16le $wav
} else {
    $wav = $jfk
}

if (-not $SkipOfflineCheck) {
    Write-Step 'Fase offline'
    if (-not $AssumeOffline) { Read-Host 'Disattiva Wi-Fi/Ethernet (o la modalita aereo) e premi Invio' }
    $online = Test-NetConnection -ComputerName 'huggingface.co' -Port 443 -InformationLevel Quiet -WarningAction SilentlyContinue
    if ($online) { throw 'La rete risulta ancora attiva: il test offline non sarebbe valido.' }
    Write-Host 'Rete non raggiungibile: OK'
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
Write-Step 'Trascrizione'
& $probe --model $modelFile --audio $wav --sha256 $sha --language $Language --json (Join-Path $results "$stamp-$Model-transcribe.json")
Write-Step 'Test di annullamento (dopo 2 s)'
& $probe --model $modelFile --audio $wav --language $Language --cancel-after 2 --json (Join-Path $results "$stamp-$Model-cancel.json") | Out-Null

Write-Host "`nReport salvati in $results. Allegali alla issue #9 insieme al modello di CPU/GPU." -ForegroundColor Green
if (-not $Audio) {
    Write-Host 'Testo atteso (jfk.wav): "And so my fellow Americans, ask not what your country can do for you, ask what you can do for your country."'
}
