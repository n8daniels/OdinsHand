<#
  Builds the two NuGet packages (the library and the template) into .\artifacts.

  This has to run on a machine with Valheim installed: the library is compiled against the game's own assemblies, which cannot
  be redistributed and so are not available to a hosted CI runner.

  Nothing is published. To release, after checking the output:
      dotnet nuget push artifacts\OdinsHand.<version>.nupkg           -s https://api.nuget.org/v3/index.json -k <key>
      dotnet nuget push artifacts\OdinsHand.Templates.<version>.nupkg -s https://api.nuget.org/v3/index.json -k <key>
      git tag v<version>; git push --tags; gh release create v<version> artifacts\*.nupkg

  Usage:  .\tools\Pack-Release.ps1 [-SkipSmoke]
#>
param([switch]$SkipSmoke)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$version = ([xml](Get-Content "$root\Directory.Build.props")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$tplVersion = ([xml](Get-Content "$root\templates\OdinsHand.Templates.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$tplDefault = (Get-Content "$root\templates\odinshand-harness\.template.config\template.json" -Raw | ConvertFrom-Json).symbols.odinsHandVersion.defaultValue
if ($version -ne $tplVersion -or $version -ne $tplDefault) {
  throw "Versions disagree: Directory.Build.props=$version, Templates.csproj=$tplVersion, template default=$tplDefault. Make them match."
}

if (-not $SkipSmoke) {
  Write-Host "Running the Smoke harness in the real game first (use -SkipSmoke to skip)..." -ForegroundColor Cyan
  & "$root\scripts\Run-Harness.ps1" -Project $root | Out-Host
  if ($LASTEXITCODE -ne 0) { throw "The Smoke harness did not complete." }
  $report = Get-Content (Join-Path $env:TEMP "smoke-harness\report.json") -Raw | ConvertFrom-Json
  if (-not $report.ok) { throw "The Smoke harness reported $($report.failed) failure(s) and $($report.errors) error(s); not packing." }
  Write-Host "Smoke: $($report.passed) passed, $($report.skipped) skipped." -ForegroundColor Green
}

if (Test-Path "$root\artifacts") { Remove-Item "$root\artifacts" -Recurse -Force }
New-Item -ItemType Directory "$root\artifacts" | Out-Null
dotnet pack "$root\src\OdinsHand\OdinsHand.csproj" -c Release -o "$root\artifacts" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "pack (library) failed" }
dotnet pack "$root\templates\OdinsHand.Templates.csproj" -c Release -o "$root\artifacts" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "pack (template) failed" }

Get-ChildItem "$root\artifacts" | Select-Object Name, @{ n = "KB"; e = { [math]::Round($_.Length / 1KB, 1) } } | Format-Table -AutoSize
Write-Host "Packed $version. Nothing was published." -ForegroundColor Green
