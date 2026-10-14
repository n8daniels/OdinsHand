<#
  One command: deploy -> launch the game from the dev profile -> auto-load a disposable test character and world ->
  run every harness case -> quit -> print the report.

  The harness plugin's config is written for you ([AutoStart] + [Harness]); the character and world are local, named
  deliberately, and created on demand, so they cannot collide with real saves.

  Usage:  .\scripts\Run-Harness.ps1 [-Project <dir>] [-Only <case filter>] [-TimeoutMinutes 12] [-NoDeploy] [-KeepOpen]
                                    [-Server host:port -ServerPassword pw]
  Exit code: 0 = ran to the end (read the pass/fail line), 2 = timed out / game exited early.
#>
param(
  [string]$Project = "",
  [string]$Only = "",
  [int]$TimeoutMinutes = 12,
  [switch]$NoDeploy,
  [switch]$KeepOpen,
  [string]$Server = "",
  [string]$ServerPassword = ""
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Config.ps1"
$p = Get-OdinsConfig $Project
$bep = Join-Path $p.ProfileDir "BepInEx"
if (-not (Test-Path $bep)) { throw "No dev profile at $($p.ProfileDir). Run .\scripts\New-DevProfile.ps1 -CoreSource <...> first." }
if (-not $NoDeploy) { & "$PSScriptRoot\Deploy-Dev.ps1" -Project $p.Root }

$running = Get-Process -Name valheim -ErrorAction SilentlyContinue
if ($running) { throw "Valheim is already running (PID $($running.Id)). Close it first so the run starts from a clean menu." }

$character = if ($p.Cfg.character) { $p.Cfg.character } else { "OdinsTester" }
$world = if ($p.Cfg.world) { $p.Cfg.world } else { "OdinsHandTest" }
$quit = if ($KeepOpen) { "false" } else { "true" }
$worldLine = if ($Server) { "World = " } else { "World = $world" }
$cfg = @"
[AutoStart]
Character = $character
$worldLine
Server = $Server
ServerPassword = $ServerPassword
QuitWhenDone = $quit

[Harness]
RunOnSpawn = true
OnlyCases = $Only
CaptureScreenshots = true
"@
Set-Content -Path (Join-Path $bep "config\$($p.Cfg.guid).cfg") -Value $cfg -Encoding UTF8

if (Test-Path $p.ReportDir) { Remove-Item $p.ReportDir -Recurse -Force }
& (Join-Path $p.ProfileDir "Launch-$($p.Name)Dev.ps1")
Start-Sleep 5

$log = Join-Path $bep "LogOutput.log"
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
Write-Host "Waiting for the run (timeout $TimeoutMinutes min)..."
$done = $false
while ((Get-Date) -lt $deadline) {
  Start-Sleep 5
  if ((Test-Path $log) -and (Select-String -Path $log -Pattern "\[harness\] \d+ passed" -Quiet -ErrorAction SilentlyContinue)) { $done = $true; break }
  if (-not (Get-Process -Name valheim -ErrorAction SilentlyContinue)) { break }
}
Start-Sleep 4

if (-not (Test-Path $log)) { throw "No BepInEx log was produced - BepInEx did not load. Check the profile." }
$lines = Get-Content $log
$start = ($lines | Select-String "RESULTS" | Select-Object -Last 1).LineNumber
Write-Host ""
$tag = [regex]::Escape($p.Cfg.logTag)
if ($start) { $lines[($start - 1)..($lines.Count - 1)] | ForEach-Object { $_ -replace "^\[Info   :$tag\] ", '' } }
else {
  Write-Host "The harness did not reach its results. Relevant log lines:" -ForegroundColor Yellow
  $lines | Select-String "autostart|harness|Error|Exception" | Select-Object -Last 40 | ForEach-Object { $_.Line }
}
Write-Host ""
Write-Host "Report:      $($p.ReportDir)\report.json"
Write-Host "Screenshots: $($p.ReportDir)"

# Keep a small history: this run's report and log as one zip, pruned to the newest few ("keepRuns" in odinshand.json, default 5).
# Never allowed to fail the run.
try {
  . "$PSScriptRoot\_History.ps1"
  $keepRuns = if ($p.Cfg.keepRuns) { [math]::Max(1, [math]::Min(50, [int]$p.Cfg.keepRuns)) } else { 5 }
  $saved = Save-RunHistory -Name $p.Name -ReportDir $p.ReportDir -LogPath $log -Keep $keepRuns
  if ($saved) { Write-Host "History:     $saved  (newest $keepRuns kept)" }
} catch { Write-Warning "Could not save run history: $($_.Exception.Message)" }
if (-not $done) { Write-Warning "The run did not finish before the timeout / the game exited early."; exit 2 }
