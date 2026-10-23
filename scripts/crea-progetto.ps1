<#
.SYNOPSIS
  Crea su GitHub etichette, milestone e issue iniziali di CodexVTT (da docs/progetto/progetto.json).

.DESCRIPTION
  Si può rilanciare senza duplicare nulla: ciò che esiste già (per nome o titolo) viene saltato.
  I tag non si creano qui: `v<versione>` nasce con la release (`release-locale.ps1`), perché deve puntare
  al commit pubblicato.
  Richiede GitHub CLI autenticata (`gh auth login`) con permesso di scrittura sul repository.

.EXAMPLE
  .\scripts\crea-progetto.ps1 -Repo CodexCircle/CodexVTT -DryRun   # mostra cosa creerebbe
  .\scripts\crea-progetto.ps1 -Repo CodexCircle/CodexVTT
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Repo,
    [switch]$DryRun,
    [string]$Dati
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Dati) { $Dati = Join-Path $root 'docs/progetto/progetto.json' }
$data = Get-Content $Dati -Raw -Encoding UTF8 | ConvertFrom-Json

function Invoke-Gh { & gh @args; if ($LASTEXITCODE -ne 0) { throw "gh $($args -join ' ') fallito" } }

Write-Host "Repository: $Repo$(if ($DryRun) { ' (prova, nulla viene creato)' })" -ForegroundColor Cyan
if (-not $DryRun) { Invoke-Gh repo view $Repo --json name | Out-Null }

# --- etichette (gh label create --force aggiorna se esiste)
foreach ($l in $data.labels) {
    Write-Host "etichetta: $($l.name)"
    if (-not $DryRun) { Invoke-Gh label create $l.name --repo $Repo --color $l.color --description $l.description --force | Out-Null }
}

# --- milestone
$existingMilestones = @{}
if (-not $DryRun) {
    (gh api "repos/$Repo/milestones?state=all&per_page=100" | ConvertFrom-Json) | ForEach-Object { $existingMilestones[$_.title] = $_.number }
}
foreach ($m in $data.milestones) {
    if ($existingMilestones.ContainsKey($m.title)) { Write-Host "milestone esiste già: $($m.title)"; continue }
    Write-Host "milestone: $($m.title)"
    if (-not $DryRun) { Invoke-Gh api "repos/$Repo/milestones" -f "title=$($m.title)" -f "description=$($m.description)" | Out-Null }
}

# --- issue
$existingIssues = @{}
if (-not $DryRun) {
    (gh issue list --repo $Repo --state all --limit 500 --json title | ConvertFrom-Json) | ForEach-Object { $existingIssues[$_.title] = $true }
}
$created = 0
foreach ($i in $data.issues) {
    if ($existingIssues.ContainsKey($i.title)) { Write-Host "issue esiste già: $($i.title)"; continue }
    Write-Host "issue [$($i.milestone.Split(' ')[0])]: $($i.title)"
    $created++
    if ($DryRun) { continue }
    Invoke-Gh issue create --repo $Repo --title $i.title --body $i.body --label ($i.labels -join ',') --milestone $i.milestone | Out-Null
}
Write-Host "`nFatto: $($data.labels.Count) etichette, $($data.milestones.Count) milestone, $created issue nuove." -ForegroundColor Green
