<#
  Builds the solution (Release) and copies the DLLs listed under "plugins" in odinshand.json into the dev profile.
  Usage:  .\scripts\Deploy-Dev.ps1 [-Project <dir>] [-SkipTests]
#>
param([string]$Project = "", [switch]$SkipTests)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Config.ps1"
$p = Get-OdinsConfig $Project
$plugins = Join-Path $p.ProfileDir "BepInEx\plugins\$($p.Name)"
if (-not (Test-Path (Join-Path $p.ProfileDir "BepInEx"))) { throw "No dev profile at $($p.ProfileDir). Run .\scripts\New-DevProfile.ps1 -CoreSource <...> first." }

if ($p.Cfg.tests -and -not $SkipTests) {
  dotnet test (Join-Path $p.Root $p.Cfg.tests) --nologo -v q
  if ($LASTEXITCODE -ne 0) { throw "Unit tests failed" }
}
dotnet build (Join-Path $p.Root $p.Cfg.solution) -c Release --nologo -v q "-p:ValheimDir=$($p.GameDir)"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

New-Item -ItemType Directory -Force $plugins | Out-Null
foreach ($dll in $p.Cfg.plugins) {
  $src = Join-Path $p.Root $dll
  if (-not (Test-Path $src)) { throw "Build output missing: $src" }
  Copy-Item $src $plugins -Force
  $pdb = [IO.Path]::ChangeExtension($src, ".pdb"); if (Test-Path $pdb) { Copy-Item $pdb $plugins -Force }
}
Write-Host "Deployed -> $plugins" -ForegroundColor Green
