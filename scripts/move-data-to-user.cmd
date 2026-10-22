@echo off
rem Moves the local user's data from the top of an AI data directory into users\<local user id>\
rem (ADR-014). Run it once, with the Host and the desktop app stopped:
rem
rem   move-data-to-user.cmd "%LOCALAPPDATA%\AI"
rem
rem Each entry moves on its own and an entry already in place is skipped, so an interrupted run can
rem simply be repeated. Nothing is overwritten and no document is changed.
setlocal EnableExtensions

if "%~1"=="" (
    echo Usage: %~nx0 ^<data directory^>
    exit /b 2
)

set "ROOT=%~f1"
if not exist "%ROOT%\" (
    echo The data directory "%ROOT%" does not exist.
    exit /b 1
)

set "TARGET=%ROOT%\users\00000000000000000000000000000001"
if not exist "%TARGET%\" mkdir "%TARGET%" || exit /b 1

set "FAILED=0"
for %%E in (settings.json command-rules.json project-order.json credentials projects memory instructions resources assets usage skills) do call :move "%%E"

if "%FAILED%"=="1" (
    echo Some entries were not moved; see the messages above.
    exit /b 1
)
echo Done: "%TARGET%"
exit /b 0

:move
if not exist "%ROOT%\%~1" exit /b 0
if exist "%TARGET%\%~1" (
    echo Skipped %~1: "%TARGET%\%~1" already exists.
    set "FAILED=1"
    exit /b 0
)
move "%ROOT%\%~1" "%TARGET%\%~1" >nul || (
    echo Could not move %~1.
    set "FAILED=1"
    exit /b 0
)
echo Moved %~1
exit /b 0
