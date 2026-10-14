<#
  One entry point for Odin's Hand, using the scripts from the OdinsHand NuGet package (restore once with `dotnet restore`).

    .\odins.ps1 profile     create the isolated BepInEx dev profile (downloads BepInEx; once)
    .\odins.ps1 run         build, deploy, launch the game, run every case, print the report
    .\odins.ps1 run -Only startup -KeepOpen -TimeoutMinutes 20     (any Run-Harness.ps1 switch works)
    .\odins.ps1 deploy      build and copy the DLLs into the dev profile, without running
    .\odins.ps1 audit       list non-public game members your DLLs reference
#>
# Deliberately a plain parameter and nothing else (no [Parameter] attribute - that would make this an advanced function that
# rejects unknown flags): anything after the command stays in $args, and splatting $args (unlike splatting an
# array we built ourselves) forwards "-Name value" pairs as named parameters.
param([string]$Command = "run")
$ErrorActionPreference = "Stop"

$scripts = @{ profile = "New-DevProfile.ps1"; run = "Run-Harness.ps1"; deploy = "Deploy-Dev.ps1"; audit = "Audit-Access.ps1" }
if (-not $scripts.ContainsKey($Command)) { throw "Unknown command '$Command'. Use: $($scripts.Keys -join ', ')." }

$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE ".nuget\packages" }
$pkg = Get-ChildItem (Join-Path $packages "odinshand") -Directory -ErrorAction SilentlyContinue |
  Sort-Object { [version]($_.Name -replace '-.*$', '') } | Select-Object -Last 1
if (-not $pkg) { throw "The OdinsHand package is not restored. Run 'dotnet restore' in this folder first." }

& (Join-Path $pkg.FullName "tools\scripts\$($scripts[$Command])") -Project $PSScriptRoot @args
