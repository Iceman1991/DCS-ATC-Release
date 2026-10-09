@echo off
rem Copies the DCS-ATC hook to Saved Games\DCS\Scripts\Hooks (once, no admin rights needed)
set H=%USERPROFILE%\Saved Games\DCS\Scripts\Hooks
if not exist "%H%" mkdir "%H%"
copy /Y "%~dp0DcsAtcHook.lua" "%H%\" >nul && echo Hook installiert: %H%\DcsAtcHook.lua
echo DCS neu starten. Danach vor jedem Flug DcsAtc.exe starten.
pause
