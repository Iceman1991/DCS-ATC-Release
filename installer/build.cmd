@echo off
rem Builds dist\DCS-ATC-Setup.exe: self-contained app (no .NET prerequisite), bundles ffmpeg from PATH, Inno Setup.
cd /d "%~dp0.."
dotnet publish src\DcsAtc -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o tmp\sc || exit /b 1
if not exist tmp\ffmpeg mkdir tmp\ffmpeg
for %%f in (ffmpeg.exe) do copy /y "%%~$PATH:f" tmp\ffmpeg\ >nul || (echo ffmpeg nicht im PATH & exit /b 1)
"%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" /Q installer\DcsAtc.iss || exit /b 1
echo Fertig: dist\DCS-ATC-Setup.exe
