# dist\manifest.json: version and the big files the update setup leaves out (path below the app folder, size, SHA-256).
# The list is the block "#ifndef Small" in [Files] of DcsAtc.iss, so both setups and the manifest cannot drift apart.
# The app (Update.cs) takes the small download only if all of these files are already installed unchanged.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$iss = Get-Content "$PSScriptRoot\DcsAtc.iss" -Encoding UTF8
$ver = ($iss | Select-String '#define AppVer "(.+)"').Matches[0].Groups[1].Value
$files = @()
$in = $false
foreach ($line in $iss) {
    if ($line -eq '#ifndef Small') { $in = $true; continue }
    if ($line -eq '#endif') { $in = $false }
    if (-not $in) { continue }
    if ($line -notmatch '^Source: "\{#Src\}\\([^"]+)"; DestDir: "\{app\}\\([^"]+)"; Flags: ([\w ]+)$') { throw "manifest.ps1: Zeile nicht lesbar: $line" }
    $src, $dest, $rec = "$repo\$($Matches[1])", $Matches[2], ($Matches[3] -match 'recursesubdirs')
    $base = (Resolve-Path (Split-Path $src)).Path
    foreach ($f in Get-ChildItem $src -File -Recurse:$rec) {
        $files += [ordered]@{
            path   = ("$dest\" + $f.FullName.Substring($base.Length + 1)) -replace '\\', '/'
            size   = $f.Length
            sha256 = (Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLower()
        }
    }
}
if ($files.Count -lt 10) { throw "manifest.ps1: nur $($files.Count) Dateien gefunden" }
[IO.File]::WriteAllText("$repo\dist\manifest.json", ([ordered]@{ version = $ver; files = $files } | ConvertTo-Json -Depth 3))   # UTF-8 without BOM
"manifest.json: $ver, $($files.Count) Dateien, $([int](($files | ForEach-Object { $_.size } | Measure-Object -Sum).Sum / 1MB)) MB"
