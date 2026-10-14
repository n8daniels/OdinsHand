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


# BepInEx 5 "core" folder: downloaded once from the official GitHub release and cached under %LOCALAPPDATA%\OdinsHand.
function Get-BepInExCore([string]$Version = "5.4.23.2") {
  $cache = Join-Path $env:LOCALAPPDATA "OdinsHand\bepinex-$Version"
  $core = Join-Path $cache "BepInEx\core"
  if (-not (Test-Path "$core\BepInEx.Preloader.dll")) {
    $zip = Join-Path $env:TEMP "BepInEx_win_x64_$Version.zip"
    $url = "https://github.com/BepInEx/BepInEx/releases/download/v$Version/BepInEx_win_x64_$Version.zip"
    Write-Host "Downloading BepInEx $Version ..."
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    New-Item -ItemType Directory -Force $cache | Out-Null
    Expand-Archive -Path $zip -DestinationPath $cache -Force
    Remove-Item $zip -Force
  }
  return $core
}

# The optional "server" section of odinshand.json, with defaults, for the dedicated-server scripts.
function Get-OdinsServerConfig($p) {
  $c = $p.Cfg.server
  $root = if ($c -and $c.root) { $c.root } elseif ($env:VALHEIM_SERVER_ROOT) { $env:VALHEIM_SERVER_ROOT } else { $null }
  if (-not $root) { throw "No dedicated server location. Set server.root in odinshand.json or the VALHEIM_SERVER_ROOT environment variable (see New-ServerProfile.ps1)." }
  $files = [ordered]@{}
  if ($c -and $c.configFiles) { foreach ($prop in $c.configFiles.PSObject.Properties) { $files[$prop.Name] = $prop.Value } }
  return [pscustomobject]@{
    Root        = $root
    Exe         = Join-Path $root "server\valheim_server.exe"
    ProfileDir  = Join-Path $root ($p.Name + "ServerProfile")
    Name        = if ($c -and $c.name) { $c.name } else { "$($p.Name)-Test" }
    World       = if ($c -and $c.world) { $c.world } else { "$($p.Name)Server" }
    Port        = if ($c -and $c.port) { [int]$c.port } else { 2456 }
    Password    = if ($c -and $c.password) { $c.password } else { "harnesspw1" }
    Plugins     = if ($c -and $c.plugins) { @($c.plugins) } else { @() }
    ConfigFiles = $files
  }
}
