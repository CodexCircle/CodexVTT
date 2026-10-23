<#
.SYNOPSIS
  Costruisce l'installer e pubblica la release GitHub dal PC Windows, senza usare GitHub Actions.

.DESCRIPTION
  Build della UI, publish self-contained win-x64, runtime VC++ app-local (ADR-0005), pacchetto Velopack,
  SHA256SUMS e `gh release create` con le note di docs/releases/<versione>.md.
  La release contiene anche il feed di Velopack (releases.win.json, pacchetti completi e delta): è quello che
  l'app legge per aggiornarsi da sola (ADR-0008), quindi la release non va mai pubblicata a mano o a metà.
  La versione è quella di Directory.Build.props; il commit deve essere l'ultimo di main.
  Il repository è quello di src/CodexVtt.Core/ProjectInfo.cs (si può forzare con -Repo).

  Requisiti sul PC: Windows 11 x64, .NET SDK 10 con il workload maui-windows, Node 22, Visual Studio
  (per il runtime VC++), Git e GitHub CLI (`gh auth login` già fatto).

.EXAMPLE
  .\scripts\release-locale.ps1 -DryRun     # costruisce tutto ma non pubblica
  .\scripts\release-locale.ps1             # costruisce e pubblica la release
#>
[CmdletBinding()]
param(
    [switch]$DryRun,
    [string]$Repo
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Un solo posto per l'indirizzo del progetto: ProjectInfo.cs (lo usa anche l'app per cercare gli aggiornamenti).
if (-not $Repo) {
    $info = Get-Content src/CodexVtt.Core/ProjectInfo.cs -Raw
    if ($info -notmatch 'RepositoryUrl\s*=\s*"https://github\.com/([^"]+)"') { throw 'RepositoryUrl non trovato in ProjectInfo.cs' }
    $Repo = $Matches[1]
}

function Write-Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Invoke-Checked([scriptblock]$command) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "Comando fallito ($LASTEXITCODE): $command" }
}

Write-Step 'Controlli'
$version = (Select-Xml -Path Directory.Build.props -XPath '//Version').Node.InnerText
$notes = "docs/releases/$version.md"
if (-not (Test-Path $notes)) { throw "Mancano le note di rilascio: $notes" }
if (git status --porcelain) { throw 'Ci sono modifiche non committate: la release deve partire da un commit pulito.' }
Invoke-Checked { git fetch origin main --quiet }
$head = git rev-parse HEAD
if ($head -ne (git rev-parse origin/main)) { throw 'HEAD non è l''ultimo commit di origin/main: fai git checkout main && git pull.' }
if (-not $DryRun) {
    gh release view "v$version" --repo $Repo *> $null
    if ($LASTEXITCODE -eq 0) { throw "La release v$version esiste già." }
}
Write-Host "Versione $version, commit $head"

Write-Step 'Build UI per l''app'
Push-Location ui
Invoke-Checked { npm ci }
Invoke-Checked { npm run build:app }
Pop-Location

Write-Step 'Publish app (win-x64, self-contained)'
Remove-Item publish, installer -Recurse -Force -ErrorAction SilentlyContinue
Invoke-Checked { dotnet publish src/CodexVtt.App -c Release -r win-x64 -o publish }

Write-Step 'Runtime VC++ app-local (ADR-0005)'
$vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products '*' -property installationPath
$crt = Get-ChildItem "$vs\VC\Redist\MSVC" -Directory | Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64\Microsoft.VC143.CRT' } | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $crt) { throw 'Redistributable VC++ non trovato' }
Copy-Item "$crt\*.dll" publish/

Write-Step 'Installer Velopack'
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) { Invoke-Checked { dotnet tool install -g vpk --version 1.2.161 } }
# Le release precedenti servono a Velopack per creare i pacchetti delta (aggiornamenti più leggeri). Alla prima release non ce ne sono.
vpk download github --repoUrl "https://github.com/$Repo" --token (gh auth token) --outputDir installer 2>&1 | Write-Host
if ($LASTEXITCODE -ne 0) { Write-Host 'Nessuna release precedente: pacchetto completo senza delta.' -ForegroundColor Yellow }
Invoke-Checked { vpk pack --packId CodexVTT --packVersion $version --packDir publish --mainExe CodexVtt.App.exe --packTitle CodexVTT --releaseNotes $notes --outputDir installer }
# Installer non firmato (ADR-0005): si pubblicano i checksum per la verifica manuale.
$sums = Get-ChildItem installer -File | Get-FileHash -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash.ToLowerInvariant())  $(Split-Path $_.Path -Leaf)" }
# Terminazioni LF: sha256sum su Linux/macOS non legge i CRLF.
[IO.File]::WriteAllText((Join-Path $root 'installer/SHA256SUMS.txt'), (($sums -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
Get-Content installer/SHA256SUMS.txt

if ($DryRun) {
    Write-Host "`nDry run: installer in $root\installer, nessuna release pubblicata." -ForegroundColor Yellow
    return
}

Write-Step "Release v$version"
$files = Get-ChildItem installer -File | ForEach-Object FullName
$extra = @()
if ($version -like '*-*') { $extra += '--prerelease' }
Invoke-Checked { gh release create "v$version" @files --repo $Repo --target $head --title "CodexVTT $version" --notes-file $notes @extra }
Write-Host "`nRelease v$version pubblicata." -ForegroundColor Green
