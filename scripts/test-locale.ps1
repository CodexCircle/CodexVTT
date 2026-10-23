<#
.SYNOPSIS
  Esegue in locale tutti i controlli che prima faceva la CI (il repository non usa GitHub Actions).

.DESCRIPTION
  1. Test .NET dell'intera soluzione (unitari, architettura/isolamento di rete, server, aggiornamenti, integrazione)
  2. UI: typecheck, test e build per l'app e per il server
  3. Build dell'app Windows (richiede il workload MAUI)
  4. Con -Docker: costruisce l'immagine, avvia il container con un token casuale e controlla porta, token e /healthz
  5. Con -Feed: crea con `vpk` un feed Velopack di prova (0.1.0 → 0.2.0, con delta) e prova il servizio di aggiornamento
  Per la trascrizione reale imposta CODEXVTT_TEST_MODEL e CODEXVTT_TEST_AUDIO (vedi tests/CodexVtt.Integration.Tests).

.EXAMPLE
  .\scripts\test-locale.ps1                  # controlli di base
  .\scripts\test-locale.ps1 -Docker -Feed    # tutto
#>
[CmdletBinding()]
param(
    [switch]$Docker,
    [switch]$Feed,
    [switch]$SkipApp
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$failures = [System.Collections.Generic.List[string]]::new()

function Write-Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Step([string]$name, [scriptblock]$body) {
    Write-Step $name
    try {
        & $body
        if ($LASTEXITCODE -ne 0) { throw "codice di uscita $LASTEXITCODE" }
    }
    catch {
        $failures.Add("${name}: $_")
        Write-Host "FALLITO: $_" -ForegroundColor Red
    }
}

Step 'Test .NET (soluzione)' { dotnet test CodexVtt.slnx --nologo }

Push-Location ui
Step 'UI: dipendenze' { npm ci }
Step 'UI: typecheck e test' { npm run typecheck; if ($LASTEXITCODE -eq 0) { npm test } }
Step 'UI: build per il server' { npm run build:server }
if (-not $SkipApp) { Step 'UI: build per l''app' { npm run build:app } }
Pop-Location

if (-not $SkipApp) {
    Step 'Build app Windows' { dotnet build src/CodexVtt.App/CodexVtt.App.csproj --nologo }
}

if ($Docker) {
    Step 'Docker: immagine e container' {
        $token = [guid]::NewGuid().ToString('N')
        $name = "codexvtt-test-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
        docker build -t codexvtt-server:test . ; if ($LASTEXITCODE -ne 0) { throw 'build dell''immagine fallita' }
        docker run -d --name $name -p 127.0.0.1:5186:5185 -e "CodexVtt__AccessToken=$token" codexvtt-server:test | Out-Null
        try {
            $ok = $false
            for ($i = 0; $i -lt 30 -and -not $ok; $i++) {
                Start-Sleep 1
                $ok = (curl.exe -s -o NUL -w '%{http_code}' --max-time 2 http://127.0.0.1:5186/healthz) -eq '200'
            }
            if (-not $ok) { throw '/healthz non risponde' }
            $senza = curl.exe -s -o NUL -w '%{http_code}' http://127.0.0.1:5186/
            if ($senza -ne '401') { throw "senza token atteso 401, ricevuto $senza" }
            $con = curl.exe -s -o NUL -w '%{http_code}' -b "codexvtt_access=$token" http://127.0.0.1:5186/
            if ($con -ne '200') { throw "con token atteso 200, ricevuto $con" }
            Write-Host 'Container ok: /healthz 200, senza token 401, con token 200'
            $global:LASTEXITCODE = 0
        }
        finally {
            docker rm -f $name | Out-Null
        }
    }
}

if ($Feed) {
    Step 'Aggiornamenti: feed Velopack reale' {
        if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) { dotnet tool install -g vpk --version 1.2.161 }
        $work = Join-Path ([IO.Path]::GetTempPath()) "codexvtt-feed-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
        New-Item -ItemType Directory $work, "$work\app" | Out-Null
        try {
            Set-Content "$work\app\Tiny.csproj" '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><AssemblyName>Tiny</AssemblyName></PropertyGroup><ItemGroup><PackageReference Include="Velopack" Version="1.2.161" /></ItemGroup></Project>'
            Set-Content "$work\app\Program.cs" 'Velopack.VelopackApp.Build().Run();'
            foreach ($v in '0.1.0', '0.2.0') {
                dotnet publish "$work\app" -c Release -r win-x64 --self-contained -p:Version=$v -o "$work\pub$v" | Out-Null
                Set-Content "$work\n$v.md" "# Novità $v"
                vpk pack --packId CodexVTT --packVersion $v --packDir "$work\pub$v" --mainExe Tiny.exe --packTitle CodexVTT --releaseNotes "$work\n$v.md" --outputDir "$work\feed" | Out-Null
                if ($LASTEXITCODE -ne 0) { throw "vpk pack $v fallito" }
            }
            $env:CODEXVTT_TEST_FEED = "$work\feed"
            dotnet test tests/CodexVtt.Updates.Tests --nologo --filter RealFeedTests
        }
        finally {
            $env:CODEXVTT_TEST_FEED = $null
            Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host 'Controlli falliti:' -ForegroundColor Red
    $failures | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
    exit 1
}
Write-Host 'Tutti i controlli sono passati.' -ForegroundColor Green
