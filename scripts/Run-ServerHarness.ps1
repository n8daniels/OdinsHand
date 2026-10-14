<#
  Multiplayer verification with ONE client: starts a dedicated server (your mod, with server config that may differ from the client's),
  launches the dev client with the harness, joins the server automatically, runs your cases, then stops the server and scans ITS logs
  for errors too. A second human player is not needed, but things that need two people (seeing each other's effects) remain manual.

  Needs the dedicated server installed and a "server" section in odinshand.json; see the top of New-ServerProfile.ps1.

  Usage:  .\scripts\Run-ServerHarness.ps1 [-Project <dir>] [-Only <case filter>] [-TimeoutMinutes 25] [-NoDeploy]
#>
param([string]$Project = "", [string]$Only = "", [int]$TimeoutMinutes = 25, [switch]$NoDeploy)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Config.ps1"
$p = Get-OdinsConfig $Project
$s = Get-OdinsServerConfig $p
$serverLog = Join-Path $s.ProfileDir "server.log"
$serverBepLog = Join-Path $s.ProfileDir "BepInEx\LogOutput.log"

if (Get-Process -Name valheim_server, valheim -ErrorAction SilentlyContinue) { throw "A Valheim process is already running. Close it first (client and server)." }

if (-not $NoDeploy) { & "$PSScriptRoot\Deploy-Dev.ps1" -Project $p.Root }     # builds, and puts the mod + harness on the client
& "$PSScriptRoot\New-ServerProfile.ps1" -Project $p.Root
& (Join-Path $s.ProfileDir "Start-$($p.Name)Server.ps1")

Write-Host "Waiting for the server to come up..."
$deadline = (Get-Date).AddMinutes(4); $up = $false
while ((Get-Date) -lt $deadline) {
  Start-Sleep 4
  if ((Test-Path $serverLog) -and (Select-String -Path $serverLog -Pattern "Game server connected" -Quiet -ErrorAction SilentlyContinue)) { $up = $true; break }
  if (-not (Get-Process -Name valheim_server -ErrorAction SilentlyContinue)) { break }
}
if (-not $up) {
  Get-Process -Name valheim_server -ErrorAction SilentlyContinue | Stop-Process -Force
  throw "The server did not report 'Game server connected'. See $serverLog and $serverBepLog"
}
Write-Host "Server is up." -ForegroundColor Green

$exit = 0
try {
  & "$PSScriptRoot\Run-Harness.ps1" -Project $p.Root -NoDeploy -Only $Only -Server "127.0.0.1:$($s.Port)" -ServerPassword $s.Password -TimeoutMinutes $TimeoutMinutes
  $exit = $LASTEXITCODE
}
finally {
  Start-Sleep 3
  Get-Process -Name valheim_server -ErrorAction SilentlyContinue | Stop-Process -Force
}

Write-Host ""
Write-Host "== Server-side log check ==" -ForegroundColor Cyan
if (Test-Path $serverBepLog) {
  $bad = Get-Content $serverBepLog | Select-String "\[Error|Exception"
  if ($bad) { Write-Warning "Server BepInEx log has errors:"; $bad | Select-Object -First 15 | ForEach-Object { $_.Line } } else { Write-Host "No errors or exceptions in the server's BepInEx log." -ForegroundColor Green }
}
if (Test-Path $serverLog) {
  $ex = Get-Content $serverLog | Select-String "Exception|NullReference" | Select-Object -First 10
  if ($ex) { Write-Warning "Server game log mentions exceptions:"; $ex | ForEach-Object { $_.Line } } else { Write-Host "No exceptions in the server's game log." -ForegroundColor Green }
}
if ($exit) { exit $exit }
