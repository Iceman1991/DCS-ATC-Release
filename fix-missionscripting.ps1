# Comments out sanitizeModule('os'/'io'/'lfs') in MissionScripting.lua (DCS updates reset the file).
# Needs admin rights (Program Files). Without these three modules there is no F10 ATC menu and no airfield list.
param([string]$Dcs = 'C:\Program Files\Eagle Dynamics\DCS World')
$f = "$Dcs\Scripts\MissionScripting.lua"
$t = [IO.File]::ReadAllText($f)
$n = [regex]::Replace($t, "(?m)^(\s*)sanitizeModule\('(os|io|lfs)'\)", '$1--sanitizeModule(''$2'')')
if ($n -ne $t) {
    Copy-Item $f "$f.bak-update" -Force
    [IO.File]::WriteAllText($f, $n)   # UTF-8 without BOM
    "MissionScripting.lua angepasst."
} else { "MissionScripting.lua war schon angepasst." }
