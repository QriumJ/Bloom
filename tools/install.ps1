# Installs Bloom from dist\app (run tools\package.ps1 first):
#   - into the setup's install (%LOCALAPPDATA%\BloomLauncher\current, made by a release's BloomLauncher-win-Setup.exe)
#     when there is one: the files are swapped in place, Velopack's own (sq.version) stay, so the next release still
#     updates it, and the setup's Start-menu shortcut stays as it is;
#   - otherwise into %LOCALAPPDATA%\Programs\Bloom with a Start menu "Bloom.lnk" (--show).
# A running copy quits first and starts again afterwards in the tray (--autostart, through explorer.exe and a short-lived
# shortcut so it doesn't inherit this shell's package identity); the app writes its own HKCU Run entry "Bloom".
# Works in Windows PowerShell 5.1 and PowerShell 7.
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$src = Join-Path $root 'dist\app'
$setupInstall = Join-Path $env:LOCALAPPDATA 'BloomLauncher\current'
$fromSetup = Test-Path -LiteralPath (Join-Path $setupInstall 'sq.version')
$dest = if ($fromSetup) { $setupInstall } else { Join-Path $env:LOCALAPPDATA 'Programs\Bloom' }
$exe = Join-Path $dest 'Bloom.exe'
$lnk = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Bloom.lnk'
$keep = @('sq.version') # Velopack's, in the setup's install

if (-not (Test-Path -LiteralPath (Join-Path $src 'Bloom.exe'))) { throw 'dist\app is empty: run tools\package.ps1 first' }

# 1. a running copy quits: its own --quit first, the PID only if it is still there after 5 s
$running = @(Get-CimInstance Win32_Process -Filter "Name='Bloom.exe'" | Where-Object { $_.ExecutablePath -eq $exe })
if ($running.Count -gt 0) {
    Start-Process -FilePath $exe -ArgumentList '--quit' -Wait
    foreach ($p in $running) { Wait-Process -Id $p.ProcessId -Timeout 5 -ErrorAction SilentlyContinue }
    foreach ($p in $running) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
    'stopped the running Bloom'
}

# 2. files (+ the Start menu shortcut outside the setup's install; --show: opening it from Start shows the launcher)
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item -Path (Join-Path $src '*') -Destination $dest -Recurse -Force
# files an older version shipped and this one doesn't (the app keeps no data here)
foreach ($f in @(Get-ChildItem -LiteralPath $dest -Recurse -File)) {
    $rel = $f.FullName.Substring($dest.Length).TrimStart('\')
    if ($keep -contains $rel) { continue }
    if (-not (Test-Path -LiteralPath (Join-Path $src $rel))) {
        Remove-Item -LiteralPath $f.FullName -Force
        "removed old $rel"
    }
}
$sh = New-Object -ComObject WScript.Shell
if (-not $fromSetup) {
    $s = $sh.CreateShortcut($lnk)
    $s.TargetPath = $exe
    $s.Arguments = '--show'
    $s.WorkingDirectory = $dest
    $s.IconLocation = "$exe,0"
    $s.Description = 'Bloom'
    $s.Save()
}
"installed: $dest"

# 3. back in the tray as it was (--autostart: a setup install started without arguments would open the launcher)
if ($running.Count -gt 0) {
    $start = Join-Path $env:TEMP 'Bloom-start.lnk'
    $s = $sh.CreateShortcut($start)
    $s.TargetPath = $exe
    $s.Arguments = '--autostart'
    $s.WorkingDirectory = $dest
    $s.Save()
    Start-Process -FilePath "$env:WINDIR\explorer.exe" -ArgumentList "`"$start`""
    Start-Sleep -Seconds 2
    Remove-Item -LiteralPath $start -ErrorAction SilentlyContinue
    'started the new copy'
}
