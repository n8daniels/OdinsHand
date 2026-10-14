<#
  Removes the disposable test characters that harness runs leave behind, without touching anything else. (Worlds are not
  handled: world saves are folders as well as files and are left alone.)

  Safe by construction:
    * DRY RUN unless you pass -Apply. It prints exactly which files it would move and which characters it is keeping.
    * Names come only from odinshand.json ("character") of the projects you point it at, plus any
      you pass with -Name. Exact, case-insensitive match on the save's file name. There are no wildcards and no "anything with
      'test' in it", so a real character cannot be matched by accident.
    * Nothing is deleted. Files are MOVED to a dated folder under %LOCALAPPDATA%\OdinsHand\removed-saves, with a manifest, so any
      of it can be put back by moving it home again.
    * Refuses to run while Valheim is running, and warns about anything written in the last few minutes.
    * After -Apply it checks that every file it was not supposed to touch is byte-for-byte unchanged.

  Characters live in two places and a name can exist in both: the local folder (characters_local) and Steam cloud
  (Steam\userdata\<id>\892970\remote\characters). Both are searched. Steam may sync a removal to the cloud, which is the same as
  deleting the character in the game's own menu; the backup is your way back.

  Usage:
    .\scripts\Clean-TestSaves.ps1 -Project C:\path\ModA, C:\path\ModB            # dry run
    .\scripts\Clean-TestSaves.ps1 -Project C:\path\ModA, C:\path\ModB -Apply     # do it
    .\scripts\Clean-TestSaves.ps1 -Name OldTester -Except DALTester -Apply
#>
param(
  [string[]]$Project = @(),
  [string[]]$Name = @(),
  [string[]]$Except = @(),
  [switch]$Apply,
  [int]$RecentMinutes = 15,
  # Overrides, mainly so the script can be tested against a fake save folder.
  [string]$LocalSaves = "$env:USERPROFILE\AppData\LocalLow\IronGate\Valheim",
  [string[]]$CloudRoots = $null,
  [string]$BackupRoot = "$env:LOCALAPPDATA\OdinsHand\removed-saves",
  [switch]$SkipRunningCheck
)
$ErrorActionPreference = "Stop"

# ---- which names are test saves --------------------------------------------------------------------------------------------
$characters = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
$source = @{}
foreach ($dir in $Project) {
  $file = Join-Path (Resolve-Path $dir).Path "odinshand.json"
  if (-not (Test-Path $file)) { throw "No odinshand.json in '$dir'." }
  $j = Get-Content $file -Raw | ConvertFrom-Json
  $c = if ($j.character) { $j.character } else { "OdinsTester" }
  [void]$characters.Add($c); $source["char:$c"] = "$($j.name) (odinshand.json)"
}
foreach ($n in $Name) { if ($n.Trim()) { [void]$characters.Add($n.Trim()); if (-not $source["char:$($n.Trim())"]) { $source["char:$($n.Trim())"] = "-Name" } } }
foreach ($e in $Except) { [void]$characters.Remove($e) }
if ($characters.Count -eq 0) { throw "Nothing to do: pass -Project <dir with odinshand.json> and/or -Name <character>." }
foreach ($n in @($characters)) { if ($n -match '[\\/\*\?]' -or $n.Length -lt 3) { throw "Refusing suspicious name '$n' (wildcards, path characters, or shorter than 3 characters)." } }

# ---- where saves live ------------------------------------------------------------------------------------------------------
if ($null -eq $CloudRoots) {
  $ud = "${env:ProgramFiles(x86)}\Steam\userdata"
  $CloudRoots = if (Test-Path $ud) { @(Get-ChildItem $ud -Directory | ForEach-Object { Join-Path $_.FullName "892970\remote" } | Where-Object { Test-Path $_ }) } else { @() }
}
$places = @()
$places += [pscustomobject]@{ Label = "local";  CharDir = (Join-Path $LocalSaves "characters_local") }
foreach ($r in $CloudRoots) { $places += [pscustomobject]@{ Label = "cloud:" + (Split-Path (Split-Path (Split-Path $r -Parent) -Parent) -Leaf); CharDir = (Join-Path $r "characters") } }

if (-not $SkipRunningCheck -and (Get-Process -Name valheim -ErrorAction SilentlyContinue)) { throw "Valheim is running. Close it first: it rewrites the character on exit." }

# ---- plan ------------------------------------------------------------------------------------------------------------------
function Hash($p) { (Get-FileHash $p -Algorithm SHA256).Hash }
$plan = @(); $all = @()
foreach ($pl in $places) {
  if (Test-Path $pl.CharDir) {
    foreach ($f in Get-ChildItem $pl.CharDir -File) {
      $base = $f.Name -replace '(_backup_.*)?\.fch(\.old|\.new)?$', ''
      $isChar = $f.Name -match '\.fch(\.old|\.new)?$'
      if ($isChar -and $characters.Contains($base)) { $plan += [pscustomobject]@{ Kind = "character"; Name = $base; Where = $pl.Label; File = $f } }
      else { $all += $f }
    }
  }
}

"{0}" -f $(if ($Apply) { "APPLY: moving test saves to a backup." } else { "DRY RUN: nothing will be changed. Add -Apply to do it." })
""
"Test names (from configs): " + ((@($characters | Sort-Object) | ForEach-Object { "$_ [$($source["char:$_"])]" }) -join "; ")
""
if ($plan.Count -eq 0) { "No matching save files found."; return }
$plan | Sort-Object Kind, Name, Where | Select-Object Kind, Name, Where,
  @{ n = "File"; e = { $_.File.Name } },
  @{ n = "KB"; e = { [math]::Round($_.File.Length / 1KB, 1) } },
  @{ n = "LastWrite"; e = { $_.File.LastWriteTime.ToString("yyyy-MM-dd HH:mm") } } | Format-Table -AutoSize | Out-String -Width 170

$keptChars = @($all | Where-Object { $_.Name -match '\.fch$' -and $_.Name -notmatch '_backup_' } | ForEach-Object { $_.BaseName } | Sort-Object -Unique)
"KEEPING these characters (not in the list above): " + ($keptChars -join ", ")
""
$recent = @($plan | Where-Object { $_.File.LastWriteTime -gt (Get-Date).AddMinutes(-$RecentMinutes) })
if ($recent.Count -gt 0) {
  Write-Warning ("{0} file(s) were written in the last {1} minutes: {2}. A game may still be using them." -f $recent.Count, $RecentMinutes, (($recent | ForEach-Object { $_.File.Name }) -join ", "))
  if ($Apply) { throw "Refusing to move recently written saves. Wait, close the game, or raise -RecentMinutes." }
}
if (-not $Apply) { return }

# ---- apply: move, never delete ---------------------------------------------------------------------------------------------
$before = @{}; foreach ($f in $all) { $before[$f.FullName] = Hash $f.FullName }
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$dest = Join-Path $BackupRoot $stamp
$manifest = @()
foreach ($item in $plan) {
  $sub = Join-Path $dest (($item.Where -replace '[:\\/]', '_') + "\" + $item.Kind)
  New-Item -ItemType Directory -Force $sub | Out-Null
  $target = Join-Path $sub $item.File.Name
  $h = Hash $item.File.FullName
  Move-Item -LiteralPath $item.File.FullName -Destination $target
  if ((Hash $target) -ne $h) { throw "Backup copy of $($item.File.Name) does not match the original. Stopping." }
  $manifest += [pscustomobject]@{ kind = $item.Kind; name = $item.Name; where = $item.Where; file = $item.File.Name; originalPath = $item.File.FullName; backupPath = $target; sha256 = $h }
}
$manifest | ConvertTo-Json | Set-Content (Join-Path $dest "manifest.json")

# ---- prove nothing else changed --------------------------------------------------------------------------------------------
$changed = @()
foreach ($path in $before.Keys) { if (-not (Test-Path $path) -or (Hash $path) -ne $before[$path]) { $changed += $path } }
if ($changed.Count -gt 0) { throw "UNEXPECTED: $($changed.Count) file(s) that should be untouched changed: $($changed -join '; '). Backup is in $dest" }
"Moved $($plan.Count) file(s) to $dest"
"Verified: all $($before.Count) other save file(s) are byte-for-byte unchanged."
"To restore, move the files back to their originalPath (listed in $dest\manifest.json)."
