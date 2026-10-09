# DCS-ATC: adds a line to <DCS>\Scripts\MissionScripting.lua that loads the mod's mission script (before os/io/lfs are sanitized).
# Needs admin rights (Program Files). DCS updates reset the file -> DCS-ATC then asks again by itself.
#   -Remove: remove the line again (uninstall)
param([string]$Dcs, [switch]$Remove)
$ErrorActionPreference = 'Stop'
$dirs = @($Dcs) + @('DCS World', 'DCS World OpenBeta' | ForEach-Object { (Get-ItemProperty "HKCU:\Software\Eagle Dynamics\$_" -EA 0).Path }) +
        @((Get-ItemProperty 'HKCU:\Software\DCS-ATC' -EA 0).ServerDcs)   # dedicated server (chosen in setup)
foreach ($d in $dirs | Where-Object { $_ } | Select-Object -Unique) {
    $f = Join-Path $d 'Scripts\MissionScripting.lua'
    if (-not (Test-Path $f)) { continue }
    $t = [IO.File]::ReadAllText($f)
    $clean = [regex]::Replace($t, '(?m)^.*DcsAtcMission.*\r?\n(\r?\n)?', '')
    if ($Remove) { $n = $clean }
    else {
        if ($t -match 'DcsAtcMission') { "$f : schon eingetragen / already patched"; continue }
        $line = "pcall(function() local f = lfs.writedir() .. 'Scripts/DcsAtc/DcsAtcMission.lua' if lfs.attributes(f) then dofile(f) end end)   -- DCS-ATC (Mod)"
        $n = ([regex]'(?m)^local function sanitizeModule').Replace($clean, "$line`n`nlocal function sanitizeModule", 1)
        if ($n -eq $clean) { "$f : unbekannter Aufbau, nicht geändert / unknown layout, not changed"; continue }
        Copy-Item $f "$f.vor-dcsatc" -Force
    }
    if ($n -ne $t) { [IO.File]::WriteAllText($f, $n); "$f : angepasst / patched" }
}
