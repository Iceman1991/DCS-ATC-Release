@echo off
rem All regression tests: selftest, approach test, multiplayer test, mission and crew Lua tests. Exit 1 on any failure, log in tmp\tests.log.
rem Every fixed bug gets a check in one of these suites; installer\build.cmd runs this first.
cd /d "%~dp0.."
if not exist tmp mkdir tmp
set EXE=src\DcsAtc\bin\Release\net8.0-windows\DcsAtc.exe
set LUAE=%ProgramFiles%\Eagle Dynamics\DCS World\bin\luae.exe
dotnet build src\DcsAtc -c Release -v q -nologo > tmp\tests.log || (echo Build fehlgeschlagen & type tmp\tests.log & exit /b 1)
call :run selftest %EXE% --selftest || exit /b 1
call :run anflugtest %EXE% --anflugtest || exit /b 1
call :run mptest %EXE% --mptest * * || exit /b 1
if not exist "%LUAE%" (echo luae.exe fehlt, Lua-Tests uebersprungen & goto ok)
call :run missiontest "%LUAE%" tests\missiontest.lua mod\Scripts\DcsAtc\DcsAtcMission.lua || exit /b 1
call :run crewtest "%LUAE%" tests\crewtest.lua mission\KutaisiTraffic.lua || exit /b 1
:ok
echo Alle Testsuiten OK
exit /b 0

:run
set NAME=%1
shift
%1 %2 %3 %4 %5 > tmp\%NAME%.log 2>&1
if errorlevel 1 (echo FEHLER %NAME%: & findstr /b /c:"FAIL" /c:"FEHL" tmp\%NAME%.log & echo Log: tmp\%NAME%.log & exit /b 1)
echo OK %NAME%
exit /b 0
