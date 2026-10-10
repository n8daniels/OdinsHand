<#
  Fails if a built DLL references any NON-public game member. The publicized reference lets such code compile, but the real
  assembly enforces access at runtime and the JIT rejects the whole method. Private members must go through HarmonyLib.AccessTools.
  The DLL list comes from "audit" in odinshand.json.
  Usage:  .\scripts\Audit-Access.ps1 [-Project <dir>]
#>
param([string]$Project = "")
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\_Config.ps1"
$p = Get-OdinsConfig $Project
$game = Join-Path $p.GameDir "valheim_Data\Managed\assembly_valheim.dll"
$bad = $false
foreach ($dll in $p.Cfg.audit) {
  Write-Host "== $dll"
  $out = dotnet run -c Release --project "$PSScriptRoot\tools\AccessAudit" -- (Join-Path $p.Root $dll) $game
  $out
  if ($out -cmatch "NON-PUBLIC members referenced") { $bad = $true }
}
if ($bad) { throw "Non-public game members are referenced." }
