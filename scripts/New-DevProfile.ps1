<#
  Creates an isolated BepInEx dev profile for the project in odinshand.json:
      <parent of project>\<name>Dev-profile\BepInEx   (BepInEx 5.4.x core [+ Jotunn if odinshand.json asks for it])

  It does NOT touch Thunderstore Mod Manager / r2modman / Gale and does NOT change the game folder. valheim.exe is started
  directly with Doorstop arguments that point at this profile (so a stale or missing BepInEx in the game folder is irrelevant).

  You need a BepInEx 5.4.2x "core" folder (the one containing BepInEx.Preloader.dll) from any working install or the
  BepInEx release zip (BepInEx_win_x64_5.4.23.x.zip -> BepInEx\core). Pass it with -CoreSource.

  Usage:  .\scripts\New-DevProfile.ps1 -CoreSource <...\BepInEx\core> [-Project <dir>]
#>
param([string]$Project = "", [Parameter(Mandatory)][string]$CoreSource)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Config.ps1"
$p = Get-OdinsConfig $Project
if (-not (Test-Path "$CoreSource\BepInEx.Preloader.dll")) { throw "No BepInEx core at '$CoreSource' (expected BepInEx.Preloader.dll)." }

$bep = Join-Path $p.ProfileDir "BepInEx"
New-Item -ItemType Directory -Force "$bep\core", "$bep\plugins\$($p.Name)", "$bep\config" | Out-Null
Copy-Item "$CoreSource\*" "$bep\core" -Force

if ($p.Cfg.jotunn) {
  $jot = "$env:USERPROFILE\.nuget\packages\jotunnlib\$($p.Cfg.jotunn)\lib\net462\Jotunn.dll"
  if (-not (Test-Path $jot)) { throw "JotunnLib $($p.Cfg.jotunn) is not in the NuGet cache. Run 'dotnet restore' on the solution first." }
  New-Item -ItemType Directory -Force "$bep\plugins\Jotunn" | Out-Null
  Copy-Item $jot "$bep\plugins\Jotunn\Jotunn.dll" -Force
}

# -console is required: without it F5 / devcommands do nothing.
$launcher = @"
`$ErrorActionPreference = 'Stop'
`$game      = '$($p.GameDir)'
`$preloader = Join-Path `$PSScriptRoot 'BepInEx\core\BepInEx.Preloader.dll'
`$exe       = Join-Path `$game 'valheim.exe'
foreach (`$f in @(`$exe, `$preloader)) { if (-not (Test-Path `$f)) { throw "Missing: `$f" } }
`$log = Join-Path `$PSScriptRoot 'BepInEx\LogOutput.log'
if (Test-Path `$log) { Remove-Item `$log -Force }
if (-not (Get-Process -Name steam -ErrorAction SilentlyContinue)) { Write-Warning 'Steam does not appear to be running.' }
`$arguments = @('-console', '--doorstop-enabled', 'true', '--doorstop-target-assembly', "``"`$preloader``"") -join ' '
Write-Host 'Launching Valheim with the $($p.Name) dev profile...'
Start-Process -FilePath `$exe -WorkingDirectory `$game -ArgumentList `$arguments
"@
Set-Content -Path (Join-Path $p.ProfileDir "Launch-$($p.Name)Dev.ps1") -Value $launcher -Encoding UTF8
Write-Host "Dev profile ready: $($p.ProfileDir)" -ForegroundColor Green
Write-Host "Next: .\scripts\Run-Harness.ps1"
