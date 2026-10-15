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
    foreach ($f in "report.json", "report.txt", "bepinex-log-problems.txt") { if (Test-Path (Join-Path $ReportDir $f)) { Copy-Item (Join-Path $ReportDir $f) $stage } }
    if ($LogPath -and (Test-Path $LogPath)) { Copy-Item $LogPath (Join-Path $stage "LogOutput.log") }
    if (-not (Get-ChildItem $stage)) { return $null }
    $tag = "run"
    $rj = Join-Path $stage "report.json"
    if (Test-Path $rj) { try {
      $r = Get-Content $rj -Raw | ConvertFrom-Json
      if ($null -eq $r.passed) { $res = @($r.results); $pass = @($res | Where-Object { $_.outcome -eq "PASS" }).Count; $fail = @($res | Where-Object { $_.outcome -in "FAIL", "ERROR" }).Count }
      else { $pass = $r.passed; $fail = $r.failed + $r.errors }
      $tag = "{0}pass-{1}fail" -f $pass, $fail
    } catch { } }
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

# Reads a BepInEx LogOutput.log and returns its errors: the ones plugins and BepInEx wrote, and load-time problems (a plugin that would not
# load, missing dependencies). Errors that Unity forwards into the log (source "Unity Log") are marked FromUnity: the harness already counts
# those itself. Messages the harness recorded as EXPECTED (a case provoked them on purpose) are skipped.
function Get-BepInExLogProblems([string]$LogPath, [string[]]$ExpectedMessages = @()) {
  if (-not $LogPath -or -not (Test-Path $LogPath)) { return @() }
  $benign = @("Unable to start Unity log writer")                                       # a harmless BepInEx start-up message
  $loadWarning = 'missing dependenc|Could not load|Failed to load|wrong version|incompatib|Skipping'
  $lines = @(Get-Content $LogPath); $found = @()
  for ($i = 0; $i -lt $lines.Count; $i++) {
    $m = [regex]::Match($lines[$i], '^\[(Fatal|Error|Warning)\s*:\s*(.+?)\]\s?(.*)$')
    if (-not $m.Success) { continue }
    $level = $m.Groups[1].Value; $source = $m.Groups[2].Value.Trim(); $msg = $m.Groups[3].Value
    if ($benign | Where-Object { $msg -like "*$_*" }) { continue }
    if ($level -eq 'Warning' -and $msg -notmatch $loadWarning) { continue }
    if ($ExpectedMessages | Where-Object { $_ -and $msg.Contains($_) }) { continue }
    $extra = @(); for ($j = $i + 1; $j -lt [math]::Min($i + 8, $lines.Count) -and $lines[$j] -notmatch '^\['; $j++) { if ($lines[$j].Trim()) { $extra += $lines[$j].TrimEnd() } }
    $found += [pscustomobject]@{ Line = $i + 1; Level = $level; Source = $source; Message = $msg; Detail = ($extra -join "`n"); FromUnity = ($source -eq 'Unity Log') }
  }
  return $found
}

# The messages the harness marked as expected in report.json (first 80 characters of each), for Get-BepInExLogProblems to skip.
function Get-ExpectedLogMessages([string]$ReportJson) {
  if (-not (Test-Path $ReportJson)) { return @() }
  try {
    $r = Get-Content $ReportJson -Raw | ConvertFrom-Json
    return @($r.bepInExLog.errors | Where-Object { $_.expected } | ForEach-Object { $t = [string]$_.message; $t.Substring(0, [math]::Min(80, $t.Length)) })
  } catch { return @() }
}

# Prints the scan result and writes the full text next to the report. Returns the plugin/BepInEx errors (what failOnLogErrors counts).
function Write-BepInExLogSummary($Problems, [string]$ReportDir) {
  $own = @($Problems | Where-Object { $_.Level -ne 'Warning' -and -not $_.FromUnity })
  $load = @($Problems | Where-Object { $_.Level -eq 'Warning' })
  $unity = @($Problems | Where-Object { $_.FromUnity })
  if ($own.Count -eq 0 -and $load.Count -eq 0) {
    Write-Host "BepInEx log:  no errors from plugins or BepInEx." -ForegroundColor Green
  } else {
    Write-Host ("BepInEx log:  {0} error(s) from plugins/BepInEx, {1} load warning(s):" -f $own.Count, $load.Count) -ForegroundColor Yellow
    foreach ($p in @($own + $load) | Select-Object -First 15) {
      $t = $p.Message; if ($t.Length -gt 150) { $t = $t.Substring(0, 150) + "..." }
      Write-Host ("   line {0} [{1}] {2}: {3}" -f $p.Line, $p.Level, $p.Source, $t) -ForegroundColor Yellow
    }
    if (($own.Count + $load.Count) -gt 15) { Write-Host "   ... and $(($own.Count + $load.Count) - 15) more (all of them are in bepinex-log-problems.txt)" }
  }
  if ($unity.Count -gt 0) { Write-Host "              (+ $($unity.Count) error(s) forwarded from Unity; the harness counts those itself)" }
  if ($ReportDir -and (Test-Path $ReportDir)) {
    $text = if ($Problems.Count -eq 0) { "No errors or load warnings found in the BepInEx log." } else {
      ($Problems | ForEach-Object { "line {0} [{1}] {2}{3}: {4}{5}" -f $_.Line, $_.Level, $_.Source, $(if ($_.FromUnity) { " (from Unity)" } else { "" }), $_.Message, $(if ($_.Detail) { "`n    " + ($_.Detail -replace "`n", "`n    ") } else { "" }) }) -join "`n"
    }
    Set-Content -Path (Join-Path $ReportDir "bepinex-log-problems.txt") -Value $text -Encoding UTF8
  }
  return $own
}
