@echo off
rem Starts DCS ATC (including SRS server and client), then DCS. Usage: DCS-mit-ATC.cmd "<path to the DCS exe>"
tasklist /FI "IMAGENAME eq DcsAtc.exe" | find /I "DcsAtc.exe" >nul || start "DCS ATC" /min /D "%LOCALAPPDATA%\Programs\DCS-ATC" "%LOCALAPPDATA%\Programs\DCS-ATC\DcsAtc.exe" --auto
set "DCS=%~1"
if "%DCS%"=="" set "DCS=C:\Program Files\Eagle Dynamics\DCS World\bin\DCS_updater.exe"
start "" /D "%~dp1" "%DCS%"
