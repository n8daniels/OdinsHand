# Shared by Run-Harness.ps1 and New-BugReport.ps1: a small, self-pruning run history, and redaction for anything that leaves the machine.

function Get-OdinsDataDir { Join-Path $env:LOCALAPPDATA "OdinsHand" }

# Keep only the newest $Keep files matching $Filter in $Dir (names start with a sortable timestamp). Returns what it removed.
function Remove-OldFiles([string]$Dir, [string]$Filter, [int]$Keep) {
  if (-not (Test-Path $Dir)) { return @() }
  $old = @(Get-ChildItem $Dir -Filter $Filter -File | Sort-Object Name -Descending | Select-Object -Skip $Keep)
  $old | Remove-Item -Force
  return $old
}

# Archives the finished run (report + the game log) as ONE small zip and prunes to the newest $Keep. The logs compress roughly 10:1,
# and the screenshots are deliberately not kept: they are what makes a run big, and the next run overwrites them anyway.
function Save-RunHistory([string]$Name, [string]$ReportDir, [string]$LogPath, [int]$Keep = 5, [string]$Root = (Get-OdinsDataDir)) {
  $dir = Join-Path $Root "runs\$Name"
  New-Item -ItemType Directory -Force $dir | Out-Null
  $stage = Join-Path ([IO.Path]::GetTempPath()) ("odins-stage-" + [guid]::NewGuid().ToString("N"))
  New-Item -ItemType Directory $stage | Out-Null
  try {
    foreach ($f in "report.json", "report.txt") { if (Test-Path (Join-Path $ReportDir $f)) { Copy-Item (Join-Path $ReportDir $f) $stage } }
    if ($LogPath -and (Test-Path $LogPath)) { Copy-Item $LogPath (Join-Path $stage "LogOutput.log") }
    if (-not (Get-ChildItem $stage)) { return $null }
    $tag = "run"
    $rj = Join-Path $stage "report.json"
    if (Test-Path $rj) { try { $r = Get-Content $rj -Raw | ConvertFrom-Json; $tag = "{0}pass-{1}fail" -f $r.passed, ($r.failed + $r.errors) } catch { } }
    $zip = Join-Path $dir ((Get-Date -Format "yyyyMMdd-HHmmss") + "_" + $tag + ".zip")
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force
    [void](Remove-OldFiles $dir "*.zip" $Keep)
    return $zip
  }
  finally { Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue }
}

# Removes what identifies the person or their machine from text that is about to be shared. This is a safety net, not a guarantee:
# the user is told to read the bundle before attaching it.
function Protect-OdinsText([string]$Text) {
  if (-not $Text) { return $Text }
  $t = $Text
  $profileDir = [regex]::Escape($env:USERPROFILE); $profileFwd = [regex]::Escape(($env:USERPROFILE -replace '\\', '/'))
  $t = [regex]::Replace($t, $profileDir, '%USERPROFILE%', 'IgnoreCase')
  $t = [regex]::Replace($t, $profileFwd, '%USERPROFILE%', 'IgnoreCase')
  $t = [regex]::Replace($t, [regex]::Escape(($env:USERPROFILE -replace '\\', '\\')), '%USERPROFILE%', 'IgnoreCase')
  if ($env:USERNAME) { $t = [regex]::Replace($t, '(?<![A-Za-z0-9])' + [regex]::Escape($env:USERNAME) + '(?![A-Za-z0-9])', '<user>', 'IgnoreCase') }
  if ($env:COMPUTERNAME) { $t = [regex]::Replace($t, '(?<![A-Za-z0-9])' + [regex]::Escape($env:COMPUTERNAME) + '(?![A-Za-z0-9])', '<machine>', 'IgnoreCase') }
  $t = [regex]::Replace($t, '(?<=userdata[\\/])\d+', '<steamid>')                       # Steam account folder
  $t = [regex]::Replace($t, '\b7656119\d{10}\b', '<steamid64>')                          # SteamID64
  $t = [regex]::Replace($t, '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}', '<email>')
  # Server addresses and passwords, but not version numbers like 5.4.23.2: only addresses that follow a server/join/connect word.
  $t = [regex]::Replace($t, '(?i)((?:server|join(?:ing)?|connect(?:ed|ing)?)\W{0,16})\d{1,3}(?:\.\d{1,3}){3}(?::\d+)?', '$1<ip>')
  $t = [regex]::Replace($t, '(?im)^(\s*ServerPassword\s*=).*$', '$1 <redacted>')
  $t = [regex]::Replace($t, '(?im)^(\s*Server\s*=).*$', '$1 <redacted>')
  return $t
}
