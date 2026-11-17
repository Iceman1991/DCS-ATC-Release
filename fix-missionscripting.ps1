# Legacy entry point. Use the installer patcher for this DCS installation.
param([string]$Dcs = 'C:\Program Files\Eagle Dynamics\DCS World')
& (Join-Path $PSScriptRoot 'mod\DcsAtc-Patch.ps1') -Dcs $Dcs
