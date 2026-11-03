@echo off
powershell.exe -NoLogo -NoProfile -NonInteractive -File "%~dp0writer.ps1"
exit /b %ERRORLEVEL%
