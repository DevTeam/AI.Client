@echo off
powershell.exe -NoLogo -NoProfile -NonInteractive -File "%~dp0tests.ps1"
exit /b %ERRORLEVEL%
