# Shared by the other scripts: loads odinshand.json from the project being tested.
# Project = the folder that holds odinshand.json (default: the folder you run the script from).
function Get-OdinsConfig([string]$Project) {
  if (-not $Project) { $Project = (Get-Location).Path }
  $file = Join-Path $Project "odinshand.json"
  if (-not (Test-Path $file)) { throw "No odinshand.json in '$Project'. Run the script from your project folder or pass -Project. See README.md." }
  $c = Get-Content $file -Raw | ConvertFrom-Json
  foreach ($req in "name", "guid", "reportName", "logTag", "solution", "plugins") {
    if (-not $c.$req) { throw "odinshand.json is missing '$req'." }
  }
  $root = (Resolve-Path $Project).Path
  $game = if ($c.gameDir) { $c.gameDir } elseif ($env:VALHEIM_DIR) { $env:VALHEIM_DIR } else { "C:\Program Files (x86)\Steam\steamapps\common\Valheim" }
  $profile = if ($c.profileDir) { $c.profileDir } else { Join-Path (Split-Path $root -Parent) ($c.name + "Dev-profile") }
  return [pscustomobject]@{
    Root       = $root
    Cfg        = $c
    Name       = $c.name
    GameDir    = $game
    ProfileDir = $profile
    ReportDir  = Join-Path $env:TEMP ($c.reportName + "-harness")
  }
}
