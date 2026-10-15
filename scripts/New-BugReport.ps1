<#
  Packs the latest harness run into ONE small zip you can attach to a GitHub issue, so a bug can be understood without a back-and-forth.

  Inside: the report (report.json / report.txt, which include the versions, the loaded plugins and where the player was), the game
  log for that run, your odinshand.json and the harness config, and a short system summary. Usernames, machine name, Steam account
  ids, e-mail addresses, server addresses and server passwords are redacted. Screenshots are NOT included unless you ask.

  Nothing is uploaded. The zip is written to %LOCALAPPDATA%\OdinsHand\bug-reports (only the newest 3 are kept), and you attach it yourself.
  READ IT BEFORE YOU SHARE IT: redaction is a safety net, not a guarantee.

  Usage:  .\scripts\New-BugReport.ps1 [-Project <dir>] [-IncludeScreenshots] [-Note "what you were doing"]
#>
param([string]$Project = "", [switch]$IncludeScreenshots, [string]$Note = "", [int]$Keep = 3)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Config.ps1"
. "$PSScriptRoot\_History.ps1"
$p = Get-OdinsConfig $Project

$reportJson = Join-Path $p.ReportDir "report.json"
if (-not (Test-Path $reportJson)) { throw "No report at $($p.ReportDir). Run the harness first (it writes the report), then run this." }
$log = Join-Path $p.ProfileDir "BepInEx\LogOutput.log"

$stage = Join-Path ([IO.Path]::GetTempPath()) ("odins-bug-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory $stage | Out-Null
try {
  function Add-Redacted([string]$Source, [string]$Name, [int]$MaxMB = 20) {
    if (-not (Test-Path $Source)) { return }
    $text = if ((Get-Item $Source).Length -gt $MaxMB * 1MB) {
      "[Odin's Hand: this file was larger than $MaxMB MB, so only its last 200000 lines are included.]`r`n" + ((Get-Content $Source -Tail 200000) -join "`r`n")
    } else { Get-Content $Source -Raw }
    Set-Content -Path (Join-Path $stage $Name) -Value (Protect-OdinsText $text) -Encoding UTF8
  }

  Add-Redacted $reportJson "report.json"
  Add-Redacted (Join-Path $p.ReportDir "report.txt") "report.txt"
  Add-Redacted $log "LogOutput.log"
  Add-Redacted (Join-Path $p.ReportDir "bepinex-log-problems.txt") "bepinex-log-problems.txt"
  Add-Redacted (Join-Path $p.Root "odinshand.json") "odinshand.json"
  Add-Redacted (Join-Path $p.ProfileDir "BepInEx\config\$($p.Cfg.guid).cfg") "harness-config.cfg"

  $plugins = if (Test-Path (Join-Path $p.ProfileDir "BepInEx\plugins")) {
    (Get-ChildItem (Join-Path $p.ProfileDir "BepInEx\plugins") -Recurse -Filter *.dll | ForEach-Object { "{0}  {1}" -f $_.FullName.Substring((Join-Path $p.ProfileDir "BepInEx\plugins").Length + 1), $_.VersionInfo.FileVersion }) -join "`r`n"
  } else { "(no plugins folder)" }
  $dotnet = try { (& dotnet --version) } catch { "not found" }
  $system = @"
Project:        $($p.Name)
Created:        $(Get-Date -Format "yyyy-MM-dd HH:mm:ss zzz")
OS:             $([Environment]::OSVersion.VersionString)
PowerShell:     $($PSVersionTable.PSVersion)
dotnet SDK:     $dotnet
Game folder:    $($p.GameDir)
Note:           $Note

Plugin files in the dev profile (path, file version):
$plugins
"@
  Set-Content -Path (Join-Path $stage "system.txt") -Value (Protect-OdinsText $system) -Encoding UTF8

  if ($IncludeScreenshots) {
    $shots = Get-ChildItem $p.ReportDir -Filter *.png -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 20
    if ($shots) { New-Item -ItemType Directory (Join-Path $stage "screenshots") | Out-Null; $shots | Copy-Item -Destination (Join-Path $stage "screenshots") }
  }
  Set-Content -Path (Join-Path $stage "README.txt") -Encoding UTF8 -Value @"
Odin's Hand bug report bundle. Nothing here was uploaded; whoever gave you this zip attached it themselves.
Redacted: Windows user name and profile path, machine name, Steam account ids, e-mail addresses, server addresses and passwords.
Included: report.json/.txt (results, versions, loaded plugins, player locations), the game log for the run, odinshand.json,
the harness config, and system.txt. Screenshots: $(if ($IncludeScreenshots) { "included (up to 20)" } else { "not included" }).
"@

  $outDir = Join-Path (Get-OdinsDataDir) "bug-reports"
  New-Item -ItemType Directory -Force $outDir | Out-Null
  $zip = Join-Path $outDir ("{0}-{1}.zip" -f (Get-Date -Format "yyyyMMdd-HHmmss"), $p.Name)
  Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force
  $removed = Remove-OldFiles $outDir "*.zip" $Keep
}
finally { Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue }

$size = [math]::Round((Get-Item $zip).Length / 1KB, 1)
Write-Host "Bug report bundle: $zip  ($size KB)" -ForegroundColor Green
Write-Host "Contents:"; Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try { $archive.Entries | ForEach-Object { Write-Host ("   {0}  ({1} KB)" -f $_.FullName, [math]::Round($_.Length / 1KB, 1)) } }
finally { $archive.Dispose() }
Write-Host ""
Write-Host "Nothing was uploaded. Open the zip and read it before attaching it to an issue: usernames, paths and Steam ids are redacted, but check." -ForegroundColor Yellow
if ($removed.Count -gt 0) { Write-Host "(Removed $($removed.Count) older bundle(s); the newest $Keep are kept.)" }
