# DCS-ATC: adds a line to <DCS>\Scripts\MissionScripting.lua that loads the mod's mission script (before os/io/lfs are sanitized).
# Needs admin rights (Program Files). DCS updates reset the file -> DCS-ATC then asks again by itself.
#   -Remove: remove the line again (uninstall)
#   -Pause: wait for Enter at the end (start menu shortcut, shows the result)
#   No write access (started without admin rights): starts itself again with admin prompt (UAC).
param([string]$Dcs, [switch]$Remove, [switch]$Pause, [switch]$Elevated)
$ErrorActionPreference = 'Stop'
# folders: parameter, install folder reported by the hook (Steam, other drive), registry, dedicated server
$dirs = @($Dcs) + @(Get-Content "$env:TEMP\DcsAtc-DcsDir.txt" -EA 0) + @('DCS World', 'DCS World OpenBeta', 'DCS World Server' | ForEach-Object { (Get-ItemProperty "HKCU:\Software\Eagle Dynamics\$_" -EA 0).Path }) +
        @((Get-ItemProperty 'HKCU:\Software\DCS-ATC' -EA 0).ServerDcs)   # dedicated server (chosen in setup)
try {
foreach ($d in $dirs | Where-Object { $_ } | ForEach-Object { $_.Trim().TrimEnd('\') } | Select-Object -Unique) {
    $f = Join-Path $d 'Scripts\MissionScripting.lua'
    if (-not (Test-Path $f)) { continue }
    $t = [IO.File]::ReadAllText($f)
    $clean = [regex]::Replace($t, '(?m)^.*DcsAtcMission.*\r?\n(\r?\n)?', '')
    if ($Remove) { $n = $clean }
    else {
        if ($t -match 'DcsAtcMission') { "$f : schon eingetragen / already patched"; continue }
        $line = "pcall(function() local f = lfs.writedir() .. 'Scripts/DcsAtc/DcsAtcMission.lua' if lfs.attributes(f) then dofile(f) end end)   -- DCS-ATC (Mod)"
        $n = ([regex]'(?m)^local function sanitizeModule').Replace($clean, "$line`n`nlocal function sanitizeModule", 1)
        # sanitizing commented out (server with LotAtc etc.): os/io/lfs stay available, line at the end
        if ($n -eq $clean -and $clean -notmatch '(?m)^\s*sanitizeModule\(') { $n = $clean.TrimEnd() + "`r`n`r`n$line`r`n" }
        if ($n -eq $clean) { "$f : unbekannter Aufbau, nicht geändert / unknown layout, not changed"; continue }
        Copy-Item $f "$f.vor-dcsatc" -Force
    }
    if ($n -ne $t) { [IO.File]::WriteAllText($f, $n); "$f : angepasst / patched" }
}
} catch {
    if ($Elevated) { "Fehler / error: $_" }
    else {
        $a = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Elevated" + $(if ($Dcs) { " -Dcs `"$Dcs`"" }) + $(if ($Remove) { ' -Remove' }) + $(if ($Pause) { ' -Pause' })
        Start-Process powershell -Verb RunAs -Wait -ArgumentList $a
        exit
    }
}
if ($Pause) { Read-Host 'Enter' }
